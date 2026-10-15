using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        private static readonly Regex VideoSizePattern = new(@"\b(\d{2,5})x(\d{2,5})\b", RegexOptions.Compiled);
        private bool _syncingAngle;
        private Process? _ffmpegProcess;
        private Process? _ffplayProcess;
        private LibVLC? _libVlc;
        private MediaPlayer? _vlcPlayer;
        private CancellationTokenSource? _playbackCancellation;
        private WriteableBitmap? _frameBitmap;
        private int _videoWidth;
        private int _videoHeight;
        private string? _ffmpegPath;
        private string? _selectedVideoPath;
        private string? _overlayImagePath;
        private bool _vlcViewLoaded;

        public MainWindow()
        {
            InitializeComponent();
            _ffmpegPath = FindFfmpeg();
            try
            {
                Core.Initialize();
                _libVlc = new LibVLC("--no-video-title-show");
                _vlcPlayer = new MediaPlayer(_libVlc);
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "VLC unavailable";
                VlcStatusText.ToolTip = ex.Message;
            }
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

            if (!EnsureFfmpegSelected())
                return;

            VideoNameText.Text = Path.GetFileName(dialog.FileName);
            _selectedVideoPath = dialog.FileName;
            EmptyStateText.Text = "Starting FFmpeg…";
            EmptyStateText.Visibility = Visibility.Visible;
            RotatingFrame.Visibility = Visibility.Collapsed;
            StopPlayback();
            StartVlcVideo();

            var cancellation = new CancellationTokenSource();
            _playbackCancellation = cancellation;

            try
            {
                await PlayVideoAsync(dialog.FileName, cancellation.Token);
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
                Title = "Choose an image to overlay on the VLC video",
                Filter = "PNG images|*.png|Image files|*.png;*.bmp;*.jpg;*.jpeg;*.webp|All files|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            _overlayImagePath = dialog.FileName;
            OverlayImageText.Text = Path.GetFileName(dialog.FileName);
            UpdateVlcLogo();
        }

        private void VlcVideoView_Loaded(object sender, RoutedEventArgs e)
        {
            _vlcViewLoaded = true;
            if (_vlcPlayer is null)
            {
                VlcStatusText.Text = "VLC unavailable";
                return;
            }

            VlcVideoView.MediaPlayer = _vlcPlayer;
            if (_selectedVideoPath is not null)
                StartVlcVideo();
        }

        private void StartVlcVideo()
        {
            if (_vlcPlayer is null || _libVlc is null || !_vlcViewLoaded || _selectedVideoPath is null)
                return;

            try
            {
                _vlcPlayer.Stop();
                using var media = new Media(_libVlc, new Uri(_selectedVideoPath));
                media.AddOption(":no-audio");
                if (!_vlcPlayer.Play(media))
                    throw new InvalidOperationException("VLC did not start playback.");

                VlcStatusText.Text = "Playing · no rotation";
                UpdateVlcLogo();
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "VLC playback error";
                VlcStatusText.ToolTip = ex.Message;
            }
        }

        private void UpdateVlcLogo()
        {
            if (_vlcPlayer is null)
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(_overlayImagePath) && File.Exists(_overlayImagePath))
                {
                    _vlcPlayer.SetLogoString(VideoLogoOption.File, _overlayImagePath);
                    _vlcPlayer.SetLogoInt(VideoLogoOption.Position, 4); // top-left
                    _vlcPlayer.SetLogoInt(VideoLogoOption.X, 18);
                    _vlcPlayer.SetLogoInt(VideoLogoOption.Y, 18);
                    _vlcPlayer.SetLogoInt(VideoLogoOption.Opacity, 255);
                    _vlcPlayer.SetLogoInt(VideoLogoOption.Enable, 1);
                    VlcStatusText.Text = "Playing · image overlay";
                }
                else
                {
                    _vlcPlayer.SetLogoInt(VideoLogoOption.Enable, 0);
                    if (_selectedVideoPath is not null)
                        VlcStatusText.Text = "Playing · no rotation";
                }
            }
            catch (Exception ex)
            {
                VlcStatusText.Text = "Overlay unavailable";
                VlcStatusText.ToolTip = ex.Message;
            }
        }

        private void ChooseFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select the FFmpeg executable",
                Filter = "FFmpeg executable|ffmpeg.exe|Executable files|*.exe|All files|*.*",
                FileName = "ffmpeg.exe"
            };

            if (dialog.ShowDialog(this) == true)
            {
                _ffmpegPath = dialog.FileName;
                VideoNameText.ToolTip = $"Using {_ffmpegPath}";
            }
        }

        private bool EnsureFfmpegSelected()
        {
            if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
                return true;

            MessageBox.Show(this,
                "FFmpeg was not found on PATH or beside the app. Select ffmpeg.exe with the FFmpeg… button, then choose the video again.",
                "FFmpeg is required", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void StartAudio(string videoPath)
        {
            string? ffplayPath = FindFfplay();
            if (ffplayPath is null)
            {
                AudioStatusText.Text = "Audio unavailable";
                AudioStatusText.ToolTip = "ffplay.exe was not found beside ffmpeg.exe or on PATH.";
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffplayPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-nodisp");
            startInfo.ArgumentList.Add("-loop");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(videoPath);
            startInfo.ArgumentList.Add("-vn");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (ReferenceEquals(_ffplayProcess, process))
                            AudioStatusText.ToolTip = args.Data;
                    });
            };
            process.Exited += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_ffplayProcess, process))
                        AudioStatusText.Text = "Audio stopped";
                });
            };

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("FFplay could not be started.");

                _ffplayProcess = process;
                process.BeginErrorReadLine();
                AudioStatusText.Text = "Audio playing";
                AudioStatusText.ToolTip = "Audio is played by FFplay.";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                process.Dispose();
                AudioStatusText.Text = "Audio unavailable";
                AudioStatusText.ToolTip = ex.Message;
            }
        }

        private async Task PlayVideoAsync(string videoPath, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-nostdin");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("info");
            startInfo.ArgumentList.Add("-re");
            startInfo.ArgumentList.Add("-hwaccel");
            startInfo.ArgumentList.Add("auto");
            startInfo.ArgumentList.Add("-noautorotate");
            startInfo.ArgumentList.Add("-stream_loop");
            startInfo.ArgumentList.Add("-1");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(videoPath);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:v:0");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("format=bgra");
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("bgra");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("rawvideo");
            startInfo.ArgumentList.Add("pipe:1");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var dimensions = new TaskCompletionSource<(int Width, int Height)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is null)
                    return;

                lock (stderr)
                {
                    stderr.AppendLine(args.Data);
                    if (stderr.Length > 12000)
                        stderr.Remove(0, stderr.Length - 12000);
                }

                var match = VideoSizePattern.Match(args.Data);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int width) && int.TryParse(match.Groups[2].Value, out int height))
                {
                    if (width > 0 && height > 0 && width <= 16384 && height <= 16384)
                        dimensions.TrySetResult((width, height));
                }
            };

            process.Exited += (_, _) =>
            {
                if (!dimensions.Task.IsCompleted)
                {
                    lock (stderr)
                        dimensions.TrySetException(new InvalidOperationException(GetFfmpegError(stderr)));
                }
            };

            if (!process.Start())
                throw new InvalidOperationException("FFmpeg could not be started.");

            _ffmpegProcess = process;
            process.BeginErrorReadLine();
            StartAudio(videoPath);

            using var registration = cancellationToken.Register(() => TryKill(process));
            var completed = await Task.WhenAny(dimensions.Task, process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (completed != dimensions.Task)
                throw new InvalidOperationException(GetFfmpegError(stderr));

            (_videoWidth, _videoHeight) = await dimensions.Task;
            int stride = checked(_videoWidth * 4);
            int frameBytes = checked(stride * _videoHeight);
            _frameBitmap = new WriteableBitmap(_videoWidth, _videoHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

            await Dispatcher.InvokeAsync(() =>
            {
                VideoFrame.Source = _frameBitmap;
                EmptyStateText.Visibility = Visibility.Collapsed;
                RotatingFrame.Visibility = Visibility.Visible;
                UpdateFrameLayout();
            });

            var frame = new byte[frameBytes];
            Stream output = process.StandardOutput.BaseStream;
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = 0;
                while (read < frame.Length)
                {
                    int count = await output.ReadAsync(frame.AsMemory(read, frame.Length - read), cancellationToken);
                    if (count == 0)
                        break;
                    read += count;
                }

                if (read != frame.Length)
                    break;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_frameBitmap is not null)
                        _frameBitmap.WritePixels(new Int32Rect(0, 0, _videoWidth, _videoHeight), frame, stride, 0);
                }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(GetFfmpegError(stderr));
        }

        private static string GetFfmpegError(StringBuilder stderr)
        {
            string output;
            lock (stderr)
                output = stderr.ToString().Trim();

            if (string.IsNullOrWhiteSpace(output))
                return "FFmpeg stopped before it could decode a video frame. Check that the selected executable is a valid FFmpeg build.";

            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int start = Math.Max(0, lines.Length - 8);
            return string.Join(Environment.NewLine, lines, start, lines.Length - start);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string? FindFfmpeg()
        {
            foreach (string relativePath in new[] { "ffmpeg.exe", Path.Combine("ffmpeg", "ffmpeg.exe"), Path.Combine("Resources", "ffmpeg.exe") })
            {
                string localPath = Path.Combine(AppContext.BaseDirectory, relativePath);
                if (File.Exists(localPath))
                    return localPath;
            }

            try
            {
                string? bundledPath = Directory.GetFiles(AppContext.BaseDirectory, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (bundledPath is not null)
                    return bundledPath;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException) { }
            }

            return null;
        }

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsInitialized || _syncingAngle)
                return;

            _syncingAngle = true;
            RotationInput.Text = Math.Round(RotationSlider.Value).ToString();
            _syncingAngle = false;
            VideoRotation.Angle = RotationSlider.Value;
            UpdateFrameLayout();
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
            UpdateFrameLayout();
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
            UpdateFrameLayout();
        }

        private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFrameLayout();

        private void UpdateFrameLayout()
        {
            if (_videoWidth <= 0 || _videoHeight <= 0 || VideoSurface.ActualWidth <= 0 || VideoSurface.ActualHeight <= 0)
                return;

            double radians = VideoRotation.Angle * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            double rotatedWidth = _videoWidth * cos + _videoHeight * sin;
            double rotatedHeight = _videoWidth * sin + _videoHeight * cos;
            double scale = Math.Min(VideoSurface.ActualWidth / rotatedWidth, VideoSurface.ActualHeight / rotatedHeight);
            VideoFrame.Width = _videoWidth * scale;
            VideoFrame.Height = _videoHeight * scale;
            RotatingFrame.Width = VideoFrame.Width;
            RotatingFrame.Height = VideoFrame.Height;
        }

        private void StopPlayback()
        {
            _playbackCancellation?.Cancel();
            _playbackCancellation?.Dispose();
            _playbackCancellation = null;

            if (_ffmpegProcess is not null)
            {
                TryKill(_ffmpegProcess);
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }

            if (_ffplayProcess is not null)
            {
                TryKill(_ffplayProcess);
                _ffplayProcess.Dispose();
                _ffplayProcess = null;
            }

            _frameBitmap = null;
            VideoFrame.Source = null;
            RotatingFrame.Visibility = Visibility.Collapsed;
        }

        private string? FindFfplay()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PATH")))
            {
                foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string candidate = Path.Combine(directory.Trim(), "ffplay.exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }

            if (!string.IsNullOrWhiteSpace(_ffmpegPath))
            {
                string? directory = Path.GetDirectoryName(_ffmpegPath);
                if (directory is not null)
                {
                    string siblingPath = Path.Combine(directory, "ffplay.exe");
                    if (File.Exists(siblingPath))
                        return siblingPath;
                }
            }

            try
            {
                return Directory.GetFiles(AppContext.BaseDirectory, "ffplay.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return null;
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
            VlcVideoView.Dispose();
            _libVlc?.Dispose();
            _libVlc = null;
            base.OnClosed(e);
        }
    }
}
