# 1.0.0 validation

Validated on Windows on 28 October 2026. Automated tests are not a guarantee that
every host, browser version or antivirus product will behave identically.

## Download and scanner fixes: stable build 6

All 91 Core regressions, nine JavaScript suites and the isolated browser UI check
passed. Native UI checks passed, including intermediate animation frames and final
positions. The off-screen acceptance host uses software composition and bounded
completion waits to avoid occluded GPU timing affecting the fixture; production
rendering settings are unchanged.

New regressions cover ignored Range requests, interrupted single-stream retries,
invalid responses, browser download cancellation/continuation, scanner priority and
threat/policy handling. Installed Kaspersky console and its signed registered AMSI
provider both completed scans of harmless generated files. Bitdefender, Avast and
ESET priority is covered by fixtures; those products were not installed here.
Update checks cover builds 4 and 5 discovering build 6, no build-6 self-update loop
and no downgrade from a newer build.

## Antivirus refresh: stable build 5

The 76 Core tests and native UI checks passed after fixing repeated WMI product
name lookup. Eight actual queries on the installed Kaspersky system retained the
same registered names, and the real Detect again button passed four consecutive
refreshes. Regression tests cover per-instance WMI lookup and worker-thread
refresh. Update tests require build 4 to discover build 5 and build 5 not to offer
itself again. The user also reported a successful actual 3.7.2 to 1.0.0 upgrade;
that report is separate from the automated upgrade/parser tests.

## Security refresh: stable build 4 validation

- All 74 Core regressions passed, including seven update/origin/TLS security checks.
- The actual bundled yt-dlp rejected a generated, untrusted HTTPS video certificate;
  an isolated loopback transport control confirmed that the fixture itself worked.
- A valid signed update downloaded byte-exactly; wrong keys, modified manifests,
  changed/truncated/oversized packages, stale builds, untrusted asset hosts,
  HTTP redirects and missing signatures were rejected without launching an exe.
- Windows internet-origin marks were verified without changing file contents;
  restricted-zone marks and unrelated files in destination folders were preserved.
- All eight standalone JavaScript suites and the isolated real-browser extension
  UI check passed. External service extraction is explicitly opt-in; mandatory HLS
  transfer/muxing tests use actual local media and actual yt-dlp/FFmpeg.
- Native acceptance passed actual HTTP hashes, multipart ZIP reconstruction,
  pause/resume, HLS video/audio/subtitles, public HTTPS and installed Kaspersky
  scanning. Tests used separate fixtures, not personal downloads.
- The NuGet vulnerability query reported no known vulnerable application packages.
  This is an advisory-database check, not proof that all dependencies are secure.

A fresh Windows installation and a full upgrade of a separately installed 3.7.2
binary were not exercised on another machine in this run. The compatibility
version/parser tests and embedded installer verification do not replace that test.
Bitdefender and other antivirus products not installed here remain unverified.

Release CI additionally gates publication on a silent installer smoke test in its
disposable Windows runner: installed/published binary hashes, bundled tools,
absence of a packaged pairing secret, real app startup and an authenticated
bridge response. It uninstalls the fixture afterwards. A hosted Windows runner
has developer dependencies preinstalled; this is not a fresh Windows 10/11 PC.

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
