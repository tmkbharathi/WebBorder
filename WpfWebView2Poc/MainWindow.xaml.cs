using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace WpfWebView2Poc
{
    public partial class MainWindow : Window
    {
        private bool _syncingAngle;
        private bool _syncingPreviewSize;
        private bool _audioEnabled = true;
        private LibVLC? _libVlc;
        private MediaPlayer? _vlcPlayer;
        private MediaPlayer? _audioPlayer;
        private readonly object _vlcFrameLock = new();
        private readonly MediaPlayer.LibVLCVideoLockCb _vlcLockCallback;
        private readonly MediaPlayer.LibVLCVideoUnlockCb _vlcUnlockCallback;
        private readonly MediaPlayer.LibVLCVideoDisplayCb _vlcDisplayCallback;
        private readonly MediaPlayer.LibVLCVideoFormatCb _vlcFormatCallback;
        private readonly MediaPlayer.LibVLCVideoCleanupCb _vlcCleanupCallback;
        private CancellationTokenSource? _playbackCancellation;
        private WriteableBitmap? _frameBitmap;
        private WriteableBitmap? _vlcFrameBitmap;
        private byte[]? _vlcFrameBuffer;
        private IntPtr _vlcVideoMemory;
        private int _vlcVideoWidth;
        private int _vlcVideoHeight;
        private int _vlcVideoPitch;
        private int _vlcFrameDispatchPending;
        private int _videoWidth;
        private int _videoHeight;
        private int _previewWidth;
        private int _previewHeight;
        private string? _selectedVideoPath;
        private string? _overlayImagePath;

        public MainWindow()
        {
            InitializeComponent();
            AudioToggle.Checked += AudioToggle_Changed;
            AudioToggle.Unchecked += AudioToggle_Changed;
            _vlcLockCallback = LockVlcVideo;
            _vlcUnlockCallback = UnlockVlcVideo;
            _vlcDisplayCallback = DisplayVlcVideo;
            _vlcFormatCallback = ConfigureVlcVideo;
            _vlcCleanupCallback = CleanupVlcVideo;
            try
            {
                Core.Initialize();
                _libVlc = new LibVLC("--no-video-title-show");
                _vlcPlayer = new MediaPlayer(_libVlc);
                _audioPlayer = new MediaPlayer(_libVlc);
                _vlcPlayer.SetVideoFormatCallbacks(_vlcFormatCallback, _vlcCleanupCallback);
                _vlcPlayer.SetVideoCallbacks(_vlcLockCallback, _vlcUnlockCallback, _vlcDisplayCallback);
                _vlcPlayer.Playing += (_, _) => QueueVlcStatusUpdate("Playing");
                _vlcPlayer.Paused += (_, _) => QueueVlcStatusUpdate("Paused");
                _vlcPlayer.Stopped += (_, _) => QueueVlcStatusUpdate("Stopped");
                _vlcPlayer.EndReached += (_, _) => QueueVlcStatusUpdate("End of video");
                _vlcPlayer.EncounteredError += (_, _) => QueueVlcStatusUpdate("VLC playback error");
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "VLC unavailable";
                VlcStatusText.ToolTip = ex.Message;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "Ayyappa.mp4");
            if (File.Exists(samplePath))
                await LoadVideoAsync(samplePath);
        }

        private async void ChooseVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a video",
                Filter = "Video files|*.mp4;*.wmv;*.avi;*.mov;*.mkv;*.m4v;*.webm|All files|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            await LoadVideoAsync(dialog.FileName);
        }

        private async Task LoadVideoAsync(string videoPath)
        {
            VideoNameText.Text = Path.GetFileName(videoPath);
            _selectedVideoPath = videoPath;
            EmptyStateText.Text = "Starting FFmpeg decoder…";
            EmptyStateText.Visibility = Visibility.Visible;
            RotatingFrame.Visibility = Visibility.Collapsed;
            StopPlayback();
            StartVlcVideo();

            var cancellation = new CancellationTokenSource();
            _playbackCancellation = cancellation;

            try
            {
                await PlayVideoAsync(videoPath, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // A new file was selected or the window is closing.
            }
            catch (Exception ex)
            {
                EmptyStateText.Text = "Could not play this video.";
                MessageBox.Show(this, ex.Message, "FFmpeg playback error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChooseOverlayImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose an image to overlay on both previews",
                Filter = "PNG images|*.png|Image files|*.png;*.bmp;*.jpg;*.jpeg;*.webp|All files|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            _overlayImagePath = dialog.FileName;
            OverlayImageText.Text = Path.GetFileName(dialog.FileName);
            UpdateVlcLogo();
        }

        private void AudioToggle_Changed(object sender, RoutedEventArgs e)
        {
            _audioEnabled = AudioToggle.IsChecked == true;
            if (!_audioEnabled)
            {
                StopAudio();
                return;
            }

            if (_selectedVideoPath is not null)
                StartAudio(_selectedVideoPath);
            else
            {
                AudioStatusText.Text = "Audio ready";
                AudioStatusText.ToolTip = "Audio will play when a video is selected.";
            }
        }

        private void StartVlcVideo()
        {
            if (_vlcPlayer is null || _libVlc is null || _selectedVideoPath is null)
                return;

            try
            {
                _vlcPlayer.Stop();
                VlcVideoFrame.Source = null;
                using var media = new Media(_libVlc, new Uri(_selectedVideoPath));
                media.AddOption(":no-audio");
                media.AddOption(":avcodec-hw=none");
                if (!_vlcPlayer.Play(media))
                    throw new InvalidOperationException("VLC did not start playback.");

                UpdateVlcLogo();
                UpdateVlcStatus();
                UpdateVlcLayout();
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "VLC playback error";
                VlcStatusText.ToolTip = ex.Message;
            }
        }

        private void UpdateVlcLogo()
        {
            if (!string.IsNullOrWhiteSpace(_overlayImagePath) && File.Exists(_overlayImagePath))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(_overlayImagePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                VlcOverlayImage.Source = image;
                FfmpegOverlayImage.Source = image;
                VlcOverlayImage.Visibility = Visibility.Visible;
                FfmpegOverlayImage.Visibility = Visibility.Visible;
            }
            else
            {
                VlcOverlayImage.Source = null;
                FfmpegOverlayImage.Source = null;
                VlcOverlayImage.Visibility = Visibility.Collapsed;
                FfmpegOverlayImage.Visibility = Visibility.Collapsed;
            }
        }

        private void StartAudio(string videoPath)
        {
            if (_audioPlayer is null || _libVlc is null)
                return;
            if (!_audioEnabled)
            {
                StopAudio();
                return;
            }

            try
            {
                _audioPlayer.Stop();
                using var media = new Media(_libVlc, new Uri(videoPath));
                media.AddOption(":no-video");
                if (!_audioPlayer.Play(media))
                    throw new InvalidOperationException("VLC audio playback did not start.");
                AudioStatusText.Text = "Audio playing";
                AudioStatusText.ToolTip = "Audio is played in-process by LibVLC.";
            }
            catch (Exception ex)
            {
                AudioStatusText.Text = "Audio unavailable";
                AudioStatusText.ToolTip = ex.Message;
            }
        }

        private void StopAudio()
        {
            _audioPlayer?.Stop();
            AudioStatusText.Text = _audioEnabled ? "Audio stopped" : "Audio off";
            AudioStatusText.ToolTip = _audioEnabled
                ? "Audio playback is stopped. It will resume when the video is selected again."
                : "Audio playback is disabled.";
        }

        private async Task PlayVideoAsync(string videoPath, CancellationToken cancellationToken)
        {
            using var decoder = await Task.Run(() => new FfmpegVideoDecoder(videoPath), cancellationToken);
            (_videoWidth, _videoHeight) = (decoder.Width, decoder.Height);
            _previewWidth = _videoWidth;
            _previewHeight = _videoHeight;
            int stride = checked(_videoWidth * 4);
            int frameBytes = checked(stride * _videoHeight);
            _frameBitmap = new WriteableBitmap(_videoWidth, _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            byte[] frame = new byte[frameBytes];

            await Dispatcher.InvokeAsync(() =>
            {
                VideoFrame.Source = _frameBitmap;
                _syncingPreviewSize = true;
                WidthInput.Text = _previewWidth.ToString();
                HeightInput.Text = _previewHeight.ToString();
                _syncingPreviewSize = false;
                EmptyStateText.Visibility = Visibility.Collapsed;
                RotatingFrame.Visibility = Visibility.Visible;
                UpdateFrameLayout();
                UpdateVlcLayout();
            });

            StartAudio(videoPath);
            while (!cancellationToken.IsCancellationRequested)
            {
                bool hasFrame = await Task.Run(() => decoder.TryReadFrame(frame), cancellationToken);
                if (!hasFrame)
                    break;

                await Dispatcher.InvokeAsync(() =>
                {
                    _frameBitmap?.WritePixels(new Int32Rect(0, 0, _videoWidth, _videoHeight), frame, stride, 0);
                }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
                await Task.Delay(decoder.FrameInterval, cancellationToken);
            }
        }

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsInitialized || _syncingAngle)
                return;

            _syncingAngle = true;
            RotationInput.Text = Math.Round(RotationSlider.Value).ToString();
            _syncingAngle = false;
            VideoRotation.Angle = RotationSlider.Value;
            VlcRotation.Angle = RotationSlider.Value;
            UpdateVlcStatus();
            UpdateFrameLayout();
            UpdateVlcLayout();
        }

        private void RotationInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized || _syncingAngle || !int.TryParse(RotationInput.Text, out int degrees))
                return;

            degrees = Math.Clamp(degrees, 0, 360);
            _syncingAngle = true;
            RotationSlider.Value = degrees;
            if (RotationInput.Text != degrees.ToString())
                RotationInput.Text = degrees.ToString();
            _syncingAngle = false;
            VideoRotation.Angle = degrees;
            VlcRotation.Angle = degrees;
            UpdateVlcStatus();
            UpdateFrameLayout();
            UpdateVlcLayout();
        }

        private void ApplyRotation_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(RotationInput.Text, out int degrees))
                degrees = (int)Math.Round(RotationSlider.Value);

            degrees = Math.Clamp(degrees, 0, 360);
            _syncingAngle = true;
            RotationSlider.Value = degrees;
            RotationInput.Text = degrees.ToString();
            _syncingAngle = false;
            VideoRotation.Angle = degrees;
            VlcRotation.Angle = degrees;
            UpdateFrameLayout();
            UpdateVlcLayout();
            UpdateVlcStatus();
        }

        private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFrameLayout();

        private void VlcSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVlcLayout();

        private void UpdateVlcStatus()
        {
            if (_selectedVideoPath is not null)
            {
                VlcStatusText.Text = $"Playing · {VlcRotation.Angle:0}° WPF-rendered VLC frame";
                VlcStatusText.ToolTip = _vlcVideoWidth > 0 && _vlcVideoHeight > 0
                    ? $"VLC decoded frame: {_vlcVideoWidth} × {_vlcVideoHeight}"
                    : "Waiting for the first VLC frame.";
            }
        }

        private void QueueVlcStatusUpdate(string state)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (state == "Playing")
                    UpdateVlcStatus();
                else if (_selectedVideoPath is not null)
                    VlcStatusText.Text = state;
            }));
        }

        private uint ConfigureVlcVideo(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            if (width is 0 or > 16384 || height is 0 or > 16384)
                return 0;

            try
            {
                uint pitch = checked(width * 4);
                int bufferSize = checked((int)(pitch * height));
                byte[] pixelFormat = System.Text.Encoding.ASCII.GetBytes("RV32");
                Marshal.Copy(pixelFormat, 0, chroma, pixelFormat.Length);

                lock (_vlcFrameLock)
                {
                    if (_vlcVideoMemory != IntPtr.Zero)
                        Marshal.FreeHGlobal(_vlcVideoMemory);

                    _vlcVideoMemory = Marshal.AllocHGlobal(bufferSize);
                    _vlcVideoWidth = (int)width;
                    _vlcVideoHeight = (int)height;
                    _vlcVideoPitch = (int)pitch;
                }

                pitches = pitch;
                lines = height;
                return 1;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private IntPtr LockVlcVideo(IntPtr opaque, IntPtr planes)
        {
            lock (_vlcFrameLock)
                Marshal.WriteIntPtr(planes, _vlcVideoMemory);
            return IntPtr.Zero;
        }

        private void UnlockVlcVideo(IntPtr opaque, IntPtr picture, IntPtr planes) { }

        private void DisplayVlcVideo(IntPtr opaque, IntPtr picture)
        {
            lock (_vlcFrameLock)
            {
                if (_vlcVideoMemory == IntPtr.Zero || _vlcVideoWidth <= 0 || _vlcVideoHeight <= 0)
                    return;

                int bufferLength = checked(_vlcVideoPitch * _vlcVideoHeight);
                if (_vlcFrameBuffer is null || _vlcFrameBuffer.Length != bufferLength)
                    _vlcFrameBuffer = new byte[bufferLength];
                Marshal.Copy(_vlcVideoMemory, _vlcFrameBuffer, 0, bufferLength);
            }

            if (Interlocked.Exchange(ref _vlcFrameDispatchPending, 1) != 0)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(UpdateVlcFrameOnUi), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _vlcFrameDispatchPending, 0);
            }
        }

        private void UpdateVlcFrameOnUi()
        {
            try
            {
                lock (_vlcFrameLock)
                {
                    if (_vlcFrameBuffer is null || _vlcVideoWidth <= 0 || _vlcVideoHeight <= 0)
                        return;

                    if (_vlcFrameBitmap is null || _vlcFrameBitmap.PixelWidth != _vlcVideoWidth || _vlcFrameBitmap.PixelHeight != _vlcVideoHeight)
                    {
                        _vlcFrameBitmap = new WriteableBitmap(_vlcVideoWidth, _vlcVideoHeight, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
                        VlcVideoFrame.Source = _vlcFrameBitmap;
                        UpdateVlcLayout();
                    }

                    _vlcFrameBitmap.WritePixels(new Int32Rect(0, 0, _vlcVideoWidth, _vlcVideoHeight), _vlcFrameBuffer, _vlcVideoPitch, 0);
                }
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "VLC frame display error";
                VlcStatusText.ToolTip = ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _vlcFrameDispatchPending, 0);
            }
        }

        private void CleanupVlcVideo(ref IntPtr opaque)
        {
            lock (_vlcFrameLock)
            {
                if (_vlcVideoMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_vlcVideoMemory);
                    _vlcVideoMemory = IntPtr.Zero;
                }

                _vlcVideoWidth = 0;
                _vlcVideoHeight = 0;
                _vlcVideoPitch = 0;
                _vlcFrameBuffer = null;
            }
        }

        private void UpdateVlcLayout()
        {
            if (_previewWidth <= 0 || _previewHeight <= 0 || _vlcVideoWidth <= 0 || _vlcVideoHeight <= 0 ||
                VlcSurface.ActualWidth <= 0 || VlcSurface.ActualHeight <= 0)
                return;

            // Resize only the WPF image element. LibVLC continues decoding at its native frame
            // dimensions and is never stopped or reconfigured by preview-size edits. Uniform
            // stretch preserves the complete decoded frame if VLC reports a different aspect.
            double diagonal = Math.Sqrt((double)_previewWidth * _previewWidth + (double)_previewHeight * _previewHeight);
            double scale = Math.Min(VlcSurface.ActualWidth / diagonal, VlcSurface.ActualHeight / diagonal);
            if (scale > 0 && double.IsFinite(scale))
            {
                VlcVideoContainer.Width = _previewWidth * scale;
                VlcVideoContainer.Height = _previewHeight * scale;
            }
        }

        private void PreviewSizeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized || _syncingPreviewSize ||
                !int.TryParse(WidthInput.Text, out int width) ||
                !int.TryParse(HeightInput.Text, out int height) ||
                width is < 1 or > 16384 || height is < 1 or > 16384)
                return;

            _previewWidth = width;
            _previewHeight = height;
            UpdateFrameLayout();
            UpdateVlcLayout();
        }

        private void UpdateFrameLayout()
        {
            if (_videoWidth <= 0 || _videoHeight <= 0 || _previewWidth <= 0 || _previewHeight <= 0 ||
                VideoSurface.ActualWidth <= 0 || VideoSurface.ActualHeight <= 0)
                return;

            // Fit the frame's diagonal once, so the full video remains inside the pane at every
            // angle without recalculating its scale as the angle changes. This avoids the
            // visible shrinking and growing caused by per-angle rotated-bounds fitting.
            double diagonal = Math.Sqrt((double)_previewWidth * _previewWidth + (double)_previewHeight * _previewHeight);
            double scale = Math.Min(VideoSurface.ActualWidth / diagonal, VideoSurface.ActualHeight / diagonal);
            VideoFrame.Width = _previewWidth * scale;
            VideoFrame.Height = _previewHeight * scale;
            RotatingFrame.Width = VideoFrame.Width;
            RotatingFrame.Height = VideoFrame.Height;
        }

        private void StopPlayback()
        {
            _playbackCancellation?.Cancel();
            _playbackCancellation?.Dispose();
            _playbackCancellation = null;
            StopAudio();
            _frameBitmap = null;
            VideoFrame.Source = null;
            RotatingFrame.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopPlayback();
            if (_vlcPlayer is not null)
            {
                _vlcPlayer.Stop();
                _vlcPlayer.Dispose();
                _vlcPlayer = null;
            }
            _audioPlayer?.Dispose();
            _audioPlayer = null;
            _libVlc?.Dispose();
            _libVlc = null;
            base.OnClosed(e);
        }
    }
}
