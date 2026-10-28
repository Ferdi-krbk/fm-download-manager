# Moving from early 3.x builds to stable 1.0

The stable release uses version 1.0.0. App, Core, extension and installer versions
must agree. CI rejects a release tag that differs from the project version.

## In-app transition

Installed 3.7.2 clients compare version numbers numerically. The reserved GitHub
release tag `v3.8.1` is an update compatibility marker; its payload installs the
actual stable application **1.0.0**. It is not a new 3.x application release.
The release body declares `FM_STABLE_TARGET=1.0.0` and `FM_STABLE_BUILD=6`. Refreshed stable clients read
that target and build number, so they receive refreshed 1.0.0 builds without
repeatedly offering the same installer. Build metadata is embedded in App and
Core; the displayed application version remains 1.0.0. The 3.8.1 marker also
reaches initial stable builds whose parser only recognized 3.8.0.

Old clients discover the compatibility release through their existing startup
check (unless update checks are disabled) or the tray's Check for updates action.
Their existing confirmation is still required. After accepting it, the app downloads
the bridge, exits, installs 1.0.0 silently and reopens FM. No browser download or
installer wizard is required. Installed old code cannot be made to bypass its own
confirmation remotely. Windows publisher/security prompts may still appear.

The bridge embeds the exact official installer and checks its SHA-256 before
launching it. It preserves the registered installation directory, uses the same
per-user installer AppId, and does not delete settings or download history.
Its `--verify-only` mode verifies the embedded payload without installing it.
The checksum validates the embedded payload; it does not replace code signing.
Build 4 additionally verifies the outer package against an RSA-PSS signed manifest
before launch. Previously installed clients lack that check until upgraded.

Keep `v3.8.1` as the API's Latest release for old clients. For subsequent stable
updates, refresh its payload and stable-target marker together. The official
human-facing installer and release notes remain under `v1.0.0`. The compatibility
tag is excluded from the normal tag-build workflow because the application
version remains 1.0.0.

On startup, legacy download history cookies are migrated to Windows DPAPI
encryption for the current user. Completed/cancelled jobs do not retain cookies
in history. A protected recovery copy is stored alongside downloads.json. History
still contains download URLs and filenames; avoid sharing it without reviewing it.

The extension may read its private token.json internally, but this file is no
longer exposed to web pages or copied from the developer's folder into published
packages. Browser setup creates the per-user pairing file after installation.
Reload the installed unpacked extension after updating so the new manifest takes
effect. The pairing code remains available in Browser setup.

For a forced refresh of the existing official tag, CI builds and verifies the
artifacts, while the maintainer publishes them with an authenticated release
upload. The default integration token cannot always edit a release created by
another actor. The workflow does not silently report that edit as successful.

## Validation

```powershell
dotnet build -c Release
dotnet run -c Release --project tests/McDm.Tests
foreach ($test in Get-ChildItem tests -Filter 'test_*.js') {
    node $test.FullName
    if ($LASTEXITCODE -ne 0) { throw $test.Name }
}
```

External-site extraction is opt-in using FM_LIVE_MEDIA_URL (and optionally
FM_LIVE_MEDIA_REFERER); the regular suites use deterministic local fixtures.
The scanner process tests exercise timeout, cancellation and output handling
without invoking a real antivirus or downloading malware.

Bundled tools are pinned in scripts/tools-lock.json. Update version, URL and the
publisher's SHA-256 together; missing or mismatched hashes fail the build.
Winget manifests are generated only after the matching installer exists.
