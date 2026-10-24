# 1.0.0 validation

Validated on Windows on 24 October 2026. Automated tests are not a guarantee that
every host, browser version or antivirus product will behave identically.

## Core and extension regressions

The regression suite checks content hashes, interrupted non-range transfers,
pause/resume and restart recovery, entity validators, invalid checkpoints, retry
deadlines, cookie scoping, protected history, scanner process cleanup and torrent
registration/output paths. The JavaScript suites exercise the actual extension
scripts with local browser API fixtures.

```powershell
dotnet run -c Release --project tests/McDm.Tests
foreach ($test in Get-ChildItem tests -Filter 'test_*.js') {
    node $test.FullName
    if ($LASTEXITCODE -ne 0) { throw $test.Name }
}
```

## Real tools and transfers

`tests/McDm.Acceptance` passed the following checks:

| Check | Result |
| --- | --- |
| HTTP text/MP4 downloads | Downloaded bytes match source SHA-256. |
| Pause/resume | A 24 MiB file resumed through 8 connections and matched its source hash. |
| Real antivirus | The locally installed Kaspersky scanner scanned the downloaded clean fixtures and returned code 0. |
| HLS media | Actual yt-dlp and FFmpeg produced an MP4 with video, audio and embedded subtitles, verified by ffprobe. |
| Public HTTPS | The dotnet/runtime license file downloaded from raw.githubusercontent.com with expected content. |
| Production UI | Current WPF controls rendered successfully in light and dark themes. |

The tests use an isolated directory and deliberately harmless generated media and
transfer fixtures. Personal download history is not loaded. The antivirus check
is opt-in using `MCDM_TEST_AV`; CI reports it as skipped when no scanner is supplied.
No Windows protection settings are changed.

```powershell
dotnet run -c Release --project tests/McDm.Acceptance
```

The committed UI images are created from the production XAML views while real
test transfers are running. Download rows, progress, speed and connection counts
come from the download engine, not seeded mock objects. The views are rendered in
memory, not screenshots of a personal desktop.

## Not verified interactively

Personal Chrome and Brave window automation was stopped because the desktop tool
could not reliably determine the active URL. No browser safety control was bypassed.
Interactive extension installation, hover behavior on third-party websites and a
full browser-to-desktop handoff still need a manual check on the target browser.
The automated extension/bridge contract tests passed; those do not replace this check.

The antivirus results concern clean test files and process behavior, not malware
detection efficacy. Website extractors and authenticated media depend on upstream
services; DRM-protected sources are not promised to work.

## Release artifacts

The self-contained x64 build excludes the developer's pairing secret. External
tools use pinned, verified SHA-256 values, including the JavaScript runtime. The
Winget manifest is generated from the finished installer. Installer hashes are
provided alongside release assets.
