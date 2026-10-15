# FFmpeg vs VLC Rotation Comparison

A WPF comparison app with two synchronized video sources. The included `Samples/Ayyappa.mp4` starts automatically. The left pane decodes the video with bundled FFmpeg and rotates it from 0° to 360° using the slider or number field. The controls stay synchronized, and the full frame rotates at a constant size around its center. Live width and height fields adjust the preview dimensions independently. **Apply rotation** applies the typed angle.

The right pane decodes the same video through LibVLC's software frame callbacks, then displays and rotates those decoded frames in WPF. Both previews follow the rotation and size controls. Choose an optional image to overlay on the VLC preview. VLC audio is disabled to avoid playing the same sound twice; FFplay provides the shared audio playback. Both video previews loop. The VLC callback path uses CPU copies of decoded frames, so performance depends on video resolution and frame rate.

FFmpeg, FFplay, LibVLC, and VLC plugins are included through NuGet packages. If no FFmpeg executable is found at runtime, use **FFmpeg…** to select `ffmpeg.exe` (the matching `ffplay.exe` must be beside it for audio). Review the included FFmpeg and VLC license notices before redistributing the application.
