using FFmpeg.AutoGen;
using System.IO;
using System.Runtime.InteropServices;

namespace WpfWebView2Poc;

internal sealed unsafe class FfmpegVideoDecoder : IDisposable
{
    private _AVFormatContext* _format;
    private _AVCodecContext* _codecContext;
    private _AVPacket* _packet;
    private _AVFrame* _frame;
    private _SwsContext* _scaler;
    private readonly int _streamIndex;
    private readonly int _width;
    private readonly int _height;

    public int Width => _width;
    public int Height => _height;
    public TimeSpan FrameInterval { get; }

    public FfmpegVideoDecoder(string path)
    {
        ffmpeg.RootPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "win-x64");
        _AVCodec* decoder = null;
        _AVFormatContext* format = null;
        ThrowIfError(ffmpeg.avformat_open_input(&format, path, null, null), "open video");
        _format = format;
        ThrowIfError(ffmpeg.avformat_find_stream_info(_format, null), "read video information");
        _streamIndex = ffmpeg.av_find_best_stream(_format, _AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
        ThrowIfError(_streamIndex, "find video stream");
        _codecContext = ffmpeg.avcodec_alloc_context3(decoder);
        if (_codecContext == null)
            throw new InvalidOperationException("Could not allocate the FFmpeg decoder.");
        ThrowIfError(ffmpeg.avcodec_parameters_to_context(_codecContext, _format->streams[_streamIndex]->codecpar), "read video codec parameters");
        ThrowIfError(ffmpeg.avcodec_open2(_codecContext, decoder, null), "open video decoder");
        _width = _codecContext->width;
        _height = _codecContext->height;
        double frameRate = ffmpeg.av_q2d(_format->streams[_streamIndex]->avg_frame_rate);
        FrameInterval = double.IsFinite(frameRate) && frameRate is >= 1 and <= 240
            ? TimeSpan.FromSeconds(1d / frameRate)
            : TimeSpan.FromMilliseconds(33.333);
        if (_width <= 0 || _height <= 0 || _width > 16384 || _height > 16384)
            throw new InvalidOperationException("The video has unsupported dimensions.");
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        if (_packet == null || _frame == null)
            throw new InvalidOperationException("Could not allocate FFmpeg video frames.");
    }

    public bool TryReadFrame(byte[] destination)
    {
        int required = checked(_width * _height * 4);
        if (destination.Length < required)
            throw new ArgumentException("The destination buffer is too small.", nameof(destination));

        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (result == 0)
            {
                ConvertFrame(destination);
                ffmpeg.av_frame_unref(_frame);
                return true;
            }
            if (result != -11 && result != ffmpeg.AVERROR_EOF)
                ThrowIfError(result, "decode video frame");

            if (result == ffmpeg.AVERROR_EOF)
                LoopToStart();

            while (true)
            {
                ffmpeg.av_packet_unref(_packet);
                result = ffmpeg.av_read_frame(_format, _packet);
                if (result == ffmpeg.AVERROR_EOF)
                {
                    LoopToStart();
                    break;
                }
                ThrowIfError(result, "read video packet");
                if (_packet->stream_index != _streamIndex)
                    continue;

                result = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                ffmpeg.av_packet_unref(_packet);
                if (result != -11)
                    ThrowIfError(result, "submit video packet");
                break;
            }
        }
    }

    private void ConvertFrame(byte[] destination)
    {
        _scaler = ffmpeg.sws_getCachedContext(_scaler, _frame->width, _frame->height, (_AVPixelFormat)_frame->format,
            _width, _height, _AVPixelFormat.AV_PIX_FMT_BGRA, (int)_SwsFlags.SWS_BILINEAR, null, null, null);
        if (_scaler == null)
            throw new InvalidOperationException("FFmpeg could not convert the decoded frame to BGRA.");

        fixed (byte* output = destination)
        {
            byte** sourceData = stackalloc byte*[4];
            int* sourceStride = stackalloc int[4];
            byte** destinationData = stackalloc byte*[4];
            int* destinationStride = stackalloc int[4];
            for (int i = 0; i < 4; i++)
            {
                sourceData[i] = _frame->data[i];
                sourceStride[i] = _frame->linesize[i];
                destinationData[i] = null;
                destinationStride[i] = 0;
            }
            destinationData[0] = output;
            destinationStride[0] = checked(_width * 4);
            int rows = ffmpeg.sws_scale(_scaler, sourceData, sourceStride, 0, _height, destinationData, destinationStride);
            if (rows != _height)
                throw new InvalidOperationException("FFmpeg returned an incomplete converted frame.");
        }
    }

    private void LoopToStart()
    {
        int result = ffmpeg.av_seek_frame(_format, _streamIndex, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ThrowIfError(result, "rewind video");
        ffmpeg.avcodec_flush_buffers(_codecContext);
    }

    private static void ThrowIfError(int result, string operation)
    {
        if (result < 0)
        {
            byte* error = stackalloc byte[1024];
            ffmpeg.av_strerror(result, error, 1024);
            throw new InvalidOperationException($"FFmpeg failed to {operation}: {Marshal.PtrToStringAnsi((IntPtr)error)}");
        }
    }

    public void Dispose()
    {
        if (_scaler != null) ffmpeg.sws_freeContext(_scaler);
        if (_frame != null) { _AVFrame* frame = _frame; ffmpeg.av_frame_free(&frame); _frame = null; }
        if (_packet != null) { _AVPacket* packet = _packet; ffmpeg.av_packet_free(&packet); _packet = null; }
        if (_codecContext != null) { _AVCodecContext* context = _codecContext; ffmpeg.avcodec_free_context(&context); _codecContext = null; }
        if (_format != null) { _AVFormatContext* format = _format; ffmpeg.avformat_close_input(&format); _format = null; }
    }
}
