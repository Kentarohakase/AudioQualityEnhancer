# Third-Party Notices

AudioQualityEnhancer uses external command-line tools for audio analysis and processing.

## FFmpeg and FFprobe

AudioQualityEnhancer can call `ffmpeg.exe` and `ffprobe.exe` as external programs. These tools are part of the FFmpeg project and are not part of the AudioQualityEnhancer source code.

Project:

- FFmpeg: https://ffmpeg.org/
- Source code: https://ffmpeg.org/download.html
- Git mirror: https://github.com/FFmpeg/FFmpeg
- License and legal information: https://ffmpeg.org/legal.html

FFmpeg is licensed under the GNU Lesser General Public License (LGPL) version 2.1 or later. Depending on the build configuration and enabled components, a distributed FFmpeg binary may be covered by the GNU General Public License (GPL) instead. The license terms of FFmpeg and FFprobe apply to those tools.

AudioQualityEnhancer release packages are provided in two variants:

- Packages without FFmpeg do not include `ffmpeg.exe` or `ffprobe.exe`. Users can install FFmpeg separately or place the tools in the `Tools` folder.
- Packages with FFmpeg include `ffmpeg.exe` and `ffprobe.exe` in the `Tools` folder for convenience. Version details for the bundled binaries are included in `Tools/FFMPEG_VERSION.txt`.

## yt-dlp

AudioQualityEnhancer can call `yt-dlp.exe` as an external program to download the audio of a user-provided URL. yt-dlp is not part of the AudioQualityEnhancer source code.

Project:

- yt-dlp: https://github.com/yt-dlp/yt-dlp
- License: The Unlicense (public domain) - https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE

Packages with FFmpeg include `yt-dlp.exe` in the `Tools` folder, and the application keeps it up to date in a writable per-user folder. Downloading content can be subject to the terms of service of the source website and to copyright law; using this feature responsibly is the user's responsibility.

This notice is provided for transparency about third-party software used by AudioQualityEnhancer.
