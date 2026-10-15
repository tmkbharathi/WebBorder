# FFmpeg vs VLC Rotation Comparison

A WPF comparison app with two synchronized video sources. The included `Samples/Ayyappa.mp4` starts automatically. The left pane decodes the video in-process through FFmpeg's native libraries (FFmpeg.AutoGen) and rotates the frame from 0° to 360° using the slider or number field. The controls stay synchronized, and the full frame rotates at a constant size around its center. Live width and height fields adjust the preview dimensions independently. **Apply rotation** applies the typed angle.

The right pane decodes the same video through LibVLC's software frame callbacks, then displays and rotates those decoded frames in WPF. Both previews follow the rotation and size controls. Choose an optional image to overlay on both previews. The Audio checkbox starts or stops audio through LibVLC. Both video previews loop. The VLC callback path uses CPU copies of decoded frames, so performance depends on video resolution and frame rate.

FFmpeg's native libraries, LibVLC, and VLC plugins are included through NuGet packages. The app does not launch `ffmpeg.exe` or `ffplay.exe`. Review the included FFmpeg and VLC license notices before redistributing the application.
