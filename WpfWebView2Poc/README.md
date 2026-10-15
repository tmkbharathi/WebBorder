# FFmpeg vs VLC Rotation Comparison

A WPF comparison app with two synchronized video sources. The included `Samples/Ayyappa.mp4` starts automatically. The left pane decodes the video with bundled FFmpeg and rotates it from 0° to 360° using the slider or number field. The controls stay synchronized, and the full frame rotates at a constant size around its center. **Apply rotation** applies the typed angle.

The right pane plays the same source video through LibVLC, without rotation. Choose an optional PNG image to show it as a VLC logo overlay. VLC audio is disabled to avoid playing the same sound twice; FFplay provides the shared audio playback. Both video previews loop.

FFmpeg, FFplay, LibVLC, and VLC plugins are included through NuGet packages. If no FFmpeg executable is found at runtime, use **FFmpeg…** to select `ffmpeg.exe` (the matching `ffplay.exe` must be beside it for audio). Review the included FFmpeg and VLC license notices before redistributing the application.
