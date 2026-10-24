# Contributing

## Report a bug

Open an issue with the app version, Windows version, steps to reproduce, expected
result and actual result. A small public test URL is useful when the problem is
host-specific. Never include cookies, pairing codes, account credentials or
private/signed URLs. Check screenshots for personal download history before posting.

## Propose a change

Describe the user workflow and the behavior the change should provide. Keep pull
requests focused; avoid combining formatting changes with engine or protocol changes.
Preserve the English/Turkish UI and upstream license notices.

## Validate

On Windows with .NET 8 SDK and Node.js:

```powershell
dotnet build -c Release
dotnet run -c Release --project tests/McDm.Tests
foreach ($test in Get-ChildItem tests -Filter 'test_*.js') {
    node $test.FullName
    if ($LASTEXITCODE -ne 0) { throw $test.Name }
}
```

Use deterministic local fixtures for automated tests. Add a regression test for a
correctness or security fix; do not rely only on a string appearing in source code.
Test changes to transfers with content hashes, not only status or file size.

`tests/McDm.Acceptance` uses actual media tools and local HTTP transfers, renders
the production WPF controls and writes isolated output to `dist/acceptance`.
It requires the binaries fetched by `scripts/fetch-tools.ps1`.
Do not present simulated data or stale UI captures as current screenshots.

## Releases

The first stable release is 1.0.0. Historical 0.x/2.x/3.x releases remain available
as prereleases; their tags and download assets are not rewritten.
Keep App, Core, extension and installer versions aligned. A `vX.Y.Z` tag triggers
the Windows release workflow. Review its checks before publishing a stable release.
Generate the Winget manifest from the actual installer so its SHA-256 is accurate.

See [migration notes](docs/MIGRATION-1.0.md) for the one-time legacy-version transition.
