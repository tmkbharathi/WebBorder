# FFmpeg vs VLC Rotation Comparison

A WPF comparison app with two synchronized video sources. The left pane decodes the chosen video with bundled FFmpeg and rotates it from 0° to 360° using the slider or number field. The controls stay synchronized, the video fits inside the pane at diagonal angles, and **Apply rotation** applies the typed angle. The preview starts at 0°.

The right pane plays the same source video through LibVLC, without rotation. Choose an optional PNG image to show it as a VLC logo overlay. VLC audio is disabled to avoid playing the same sound twice; FFplay provides the shared audio playback. Both video previews loop.

FFmpeg, FFplay, LibVLC, and VLC plugins are included through NuGet packages. If no FFmpeg executable is found at runtime, use **FFmpeg…** to select `ffmpeg.exe` (the matching `ffplay.exe` must be beside it for audio). Review the included FFmpeg and VLC license notices before redistributing the application.
