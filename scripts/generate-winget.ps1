[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = "$PSScriptRoot\..\dist\winget"
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch' }
$installerFile = Get-Item -LiteralPath $InstallerPath
if ($installerFile.Name -ne "FMDownloadManager-Setup-$Version.exe") { throw 'Installer name does not match the version' }
if ($installerFile.Length -lt 2) { throw 'Installer is empty' }
$sha = (Get-FileHash -LiteralPath $installerFile.FullName -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifest = @"
PackageIdentifier: FM.DownloadManager
PackageVersion: $Version
PackageLocale: en-US
Publisher: FM Download Manager Authors
PublisherUrl: https://github.com/Ferdi-krbk/fm-download-manager
PackageName: FM Download Manager
PackageUrl: https://github.com/Ferdi-krbk/fm-download-manager
License: MIT
LicenseUrl: https://github.com/Ferdi-krbk/fm-download-manager/blob/main/LICENSE
ShortDescription: Download manager for Windows with browser integration and media downloads.
InstallerType: inno
Scope: user
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/Ferdi-krbk/fm-download-manager/releases/download/v$Version/FMDownloadManager-Setup-$Version.exe
    InstallerSha256: $sha
ManifestType: singleton
ManifestVersion: 1.6.0
"@
$manifest | Set-Content -LiteralPath (Join-Path $OutputDirectory 'FM.DownloadManager.yaml') -Encoding utf8
Write-Host "Winget manifest generated from $($installerFile.Name) (SHA-256: $sha)"
