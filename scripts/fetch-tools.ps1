[CmdletBinding()]
param([string]$ToolsDir = "$PSScriptRoot\..\Tools")
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$locked = Get-Content -LiteralPath "$PSScriptRoot\tools-lock.json" -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
$toolsRoot = (Resolve-Path -LiteralPath $ToolsDir).Path
$staging = Join-Path $toolsRoot ('.fetch-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null

function Get-VerifiedFile($Spec, [string]$Target) {
    if ($Spec.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $Spec.url -notmatch '^https://') {
        throw 'A pinned HTTPS URL and SHA-256 are required'
    }
    Invoke-WebRequest -Uri $Spec.url -OutFile $Target -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
    if ($actual -ne $Spec.sha256) { throw "SHA-256 mismatch for $($Spec.url)" }
}

try {
    Get-VerifiedFile $locked.ytDlp (Join-Path $staging 'yt-dlp.exe')
    Get-VerifiedFile $locked.quickJs (Join-Path $staging 'quickjs.exe')
    $archive = Join-Path $staging 'ffmpeg.zip'
    Get-VerifiedFile $locked.ffmpeg $archive
    $expanded = Join-Path $staging 'ffmpeg'
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded
    $ffmpeg = @(Get-ChildItem -LiteralPath $expanded -Recurse -File -Filter 'ffmpeg.exe')
    $ffprobe = @(Get-ChildItem -LiteralPath $expanded -Recurse -File -Filter 'ffprobe.exe')
    if ($ffmpeg.Count -ne 1 -or $ffprobe.Count -ne 1) { throw 'FFmpeg package must contain exactly one ffmpeg.exe and ffprobe.exe' }
    # Only replace installed tools after every download and checksum has succeeded.
    Copy-Item -LiteralPath (Join-Path $staging 'yt-dlp.exe') -Destination (Join-Path $toolsRoot 'yt-dlp.exe') -Force
    Copy-Item -LiteralPath (Join-Path $staging 'quickjs.exe') -Destination (Join-Path $toolsRoot 'quickjs.exe') -Force
    Copy-Item -LiteralPath $ffmpeg[0].FullName -Destination (Join-Path $toolsRoot 'ffmpeg.exe') -Force
    Copy-Item -LiteralPath $ffprobe[0].FullName -Destination (Join-Path $toolsRoot 'ffprobe.exe') -Force
    Write-Host "Verified yt-dlp $($locked.ytDlp.version), FFmpeg and ffprobe $($locked.ffmpeg.version)"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    $expectedRoot = $toolsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a staging path outside ToolsDir'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
