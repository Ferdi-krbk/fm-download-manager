Winget manifests are generated from the actual release installer so their version,
repository URL and SHA-256 cannot remain at placeholder values.

After building the installer, run:

```powershell
./scripts/generate-winget.ps1 -InstallerPath ./dist/FMDownloadManager-Setup-1.0.0.exe -Version 1.0.0
```

The generated manifest is written to `dist/winget/FM.DownloadManager.yaml`.
CI generates it after compiling the installer and uploads it alongside the installer.
Publish the matching release asset before submitting the manifest to winget-pkgs.
