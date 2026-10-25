# FM Download Manager 1.0.0

The first official stable release. Earlier numbered builds are retained as
historical alpha/beta prereleases; their tags and downloadable files remain intact.

## Interface refresh

- Rebuilt desktop workspace, compact extension popup and small fm. video overlay.
- Bundled Inter Regular/Medium/SemiBold fonts; no separate font installation.
- Light/dark modes, preset and custom accent colors across the interface.
- Monitor-aware window bounds, compact selectable cards and a thin scrollbar.
- Expandable multipart archive groups with actual aggregate progress and batch selection.
- Select-all and context-aware action buttons; separate Add links and Clipboard actions.
- Selected details remain accessible in smaller windows; approximate media time left is marked ~.
- Valid server-capped byte ranges are supported without weakening start/identity validation.
- Python crash diagnostics retain the underlying error instead of only a generic banner.
- Smooth pixel scrolling, staggered card entrance, hover lift, removal transitions,
  selection details and interpolated progress. Windows/browser reduced-motion settings are respected.
- Unified file categories and clearer audio/subtitle classification.
- External audio downloads report their own progress and clear stale transfer speeds.

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

3.7.2 users can accept the in-app update without downloading files from GitHub.
A separate compatibility entry (machine tag v3.8.0) delivers the 1.0.0 installer,
installs after the existing confirmation and reopens FM. The refreshed client reads
the actual stable target and avoids repeat prompts. Settings/history stay in place.
The old client cannot be changed remotely to skip its own confirmation.

Unsigned Windows builds may show a publisher warning. Antivirus scanning is
optional and does not guarantee that a downloaded file is harmless.
