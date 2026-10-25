# Notice

This is an independent Windows implementation (C# / .NET 8) of the ideas and the browser-bridge contract of
[mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager) (MIT License, macOS, Swift).
No Swift code is reused. The browser extension builds on the upstream WebExtension and includes Windows-specific modifications.
Keep the original MIT copyright notice in any redistribution of reused parts.

Bundled / downloaded binaries keep their own licenses:
- yt-dlp: Unlicense (public domain software, https://github.com/yt-dlp/yt-dlp)
- Inter 4.1: SIL Open Font License 1.1 (https://github.com/rsms/inter). License copies are included with the application and extension fonts; see assets/FONT-SOURCES.md.
- FFmpeg: GPL / LGPL (https://ffmpeg.org). The automated installer / fetch script downloads release builds (gyan.dev essentials builds are licensed under GPL v3). If distributing builds under LGPL requirements, replace with an LGPL build of FFmpeg or provide corresponding source code as required by GPL.
