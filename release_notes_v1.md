# FM Download Manager 1.0.0

The first official stable release. Earlier numbered builds are retained as
historical alpha/beta prereleases; their tags and downloadable files remain intact.

## Included

- Windows x64 desktop application with light/dark themes and English/Turkish UI.
- Parallel HTTP transfers, pause/resume, queue controls, scheduling and speed limits.
- yt-dlp/FFmpeg media handling, subtitle conversion/muxing and BitTorrent support.
- Companion Chromium extension, distributed as an unpacked extension.

## Reliability and security

- Interrupted non-range downloads restart cleanly instead of duplicating bytes.
- Resume checkpoints and entity validators are checked; transfer requests use If-Range.
- Retry deadlines are enforced and shutdown waits for workers to stop.
- Cookies are scoped to their destination. Pending credentials use Windows DPAPI;
  terminal download history omits them. Pairing credentials are not web-accessible.
- HTTPS certificate validation is enabled. Tool downloads and in-app tool updates
  are verified using SHA-256 before replacing installed binaries.
- Scanner output is drained, timed-out processes are terminated, and incomplete
  scans are distinguished from successful scans.
- Torrent registrations are released correctly and completed paths reflect the
  actual downloaded content.

## Validation

Core regressions, all extension test files, and Windows acceptance checks passed.
Actual media tools, hash-checked transfers, pause/resume and the installed antivirus
were exercised. The repository includes current UI captures and detailed validation
notes. Interactive personal-browser checks remain a manual follow-up because the
desktop automation tool could not verify the active Chrome/Brave URL.

## Install and migrate

Download `FMDownloadManager-Setup-1.0.0.exe`. The runtime and media tools are included.
Install the optional extension from the installed app's `extension` folder, then
check pairing in Browser setup. Reload an existing unpacked extension after updating.

Existing 3.x users need to run this installer once: their old updater considers
1.0.0 numerically older. Settings and download history remain in the existing
per-user data location. See the migration guide before moving between versions.

Unsigned Windows builds may show a publisher warning. Antivirus scanning is
optional and does not guarantee that a downloaded file is harmless.
