# Moving from early 3.x builds to stable 1.0

The stable release uses version 1.0.0. App, Core, extension and installer versions
must agree. CI rejects a release tag that differs from the project version.

An already installed 3.x executable still has its original numeric update
comparison. Changing the comparison in the new source cannot update an executable
that the user has not installed yet. Users of those builds must run the 1.0.0
installer once, or first install a separate legacy bridge release containing the
new comparison. Do not describe this initial transition as an automatic update.

After 1.0.0 is installed, the new comparator treats 3.5–3.7 as the pre-stable
epoch, preventing a stable installation from being offered one of those old builds.
The per-user data folder and installer AppId remain unchanged.

On startup, legacy download history cookies are migrated to Windows DPAPI
encryption for the current user. Completed/cancelled jobs do not retain cookies
in history. A protected recovery copy is stored alongside downloads.json. History
still contains download URLs and filenames; avoid sharing it without reviewing it.

The extension may read its private token.json internally, but this file is no
longer exposed to web pages or copied from the developer's folder into published
packages. Browser setup creates the per-user pairing file after installation.
Reload the installed unpacked extension after updating so the new manifest takes
effect. The pairing code remains available in Browser setup.

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
