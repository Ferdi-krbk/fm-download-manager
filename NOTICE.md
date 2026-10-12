# Notice

This is an independent Windows implementation (C# / .NET 8) of the ideas and the browser-bridge contract of
[mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager) (MIT License, macOS, Swift).
No Swift code is reused; the WebExtension in that repository is intended to be reused unchanged.
Keep the original MIT copyright notice in any redistribution of reused parts.

Bundled / downloaded binaries keep their own licenses:
- yt-dlp: Unlicense (public domain software, https://github.com/yt-dlp/yt-dlp)
- FFmpeg: GPL / LGPL (https://ffmpeg.org). The automated installer / fetch script downloads release builds (gyan.dev essentials builds are licensed under GPL v3). If distributing builds under LGPL requirements, replace with an LGPL build of FFmpeg or provide corresponding source code as required by GPL.
