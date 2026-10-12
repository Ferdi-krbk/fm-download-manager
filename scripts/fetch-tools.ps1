[CmdletBinding()]
param(
    [string]$ToolsDir = "$PSScriptRoot\..\Tools"
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

if (!(Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
}

$ToolsDir = (Resolve-Path $ToolsDir).Path
Write-Host "Downloading tools to: $ToolsDir"

# yt-dlp release (latest)
$ytDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
$ytDlpShaUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS"
$ytDlpTarget = Join-Path $ToolsDir "yt-dlp.exe"

Write-Host "Fetching yt-dlp.exe..."
try {
    $shaList = (Invoke-RestMethod -Uri $ytDlpShaUrl -UseBasicParsing) -split "`r?`n"
    $expectedYtDlpHash = ""
    foreach ($line in $shaList) {
        if ($line -match "([0-9a-fA-F]{64})\s+.*yt-dlp\.exe$") {
            $expectedYtDlpHash = $Matches[1].ToUpper()
            break
        }
    }
    
    Invoke-WebRequest -Uri $ytDlpUrl -OutFile "$ytDlpTarget.tmp" -UseBasicParsing
    $actualHash = (Get-FileHash "$ytDlpTarget.tmp" -Algorithm SHA256).Hash.ToUpper()
    
    if ($expectedYtDlpHash -and $actualHash -ne $expectedYtDlpHash) {
        Remove-Item "$ytDlpTarget.tmp" -Force
        throw "yt-dlp SHA-256 mismatch! Expected $expectedYtDlpHash, got $actualHash"
    }
    
    Move-Item "$ytDlpTarget.tmp" $ytDlpTarget -Force
    Write-Host "yt-dlp.exe verified and saved (SHA-256: $actualHash)"
} catch {
    Write-Warning "Failed to download yt-dlp: $_"
}

# ffmpeg release (gyan.dev essentials or lgpl build)
# Using gyan.dev release builds which provide ffmpeg-release-essentials.zip
$ffmpegZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$ffmpegShaUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256"
$ffmpegZipTarget = Join-Path $ToolsDir "ffmpeg.zip"
$ffmpegTarget = Join-Path $ToolsDir "ffmpeg.exe"

Write-Host "Fetching ffmpeg..."
try {
    $expectedFfmpegHash = ""
    try {
        $expectedFfmpegHash = (Invoke-RestMethod -Uri $ffmpegShaUrl -UseBasicParsing).Trim().ToUpper()
    } catch {
        Write-Host "Could not fetch ffmpeg sha256 checksum file directly; proceeding with download."
    }

    Invoke-WebRequest -Uri $ffmpegZipUrl -OutFile $ffmpegZipTarget -UseBasicParsing
    $actualZipHash = (Get-FileHash $ffmpegZipTarget -Algorithm SHA256).Hash.ToUpper()

    if ($expectedFfmpegHash -and $actualZipHash -ne $expectedFfmpegHash) {
        Remove-Item $ffmpegZipTarget -Force
        throw "ffmpeg.zip SHA-256 mismatch! Expected $expectedFfmpegHash, got $actualZipHash"
    }

    Write-Host "Extracting ffmpeg.exe from zip..."
    $extractDir = Join-Path $ToolsDir "ffmpeg-extract"
    Expand-Archive -Path $ffmpegZipTarget -DestinationPath $extractDir -Force
    $extractedExe = Get-ChildItem -Path $extractDir -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    if ($extractedExe) {
        Move-Item $extractedExe.FullName $ffmpegTarget -Force
        Write-Host "ffmpeg.exe saved to $ffmpegTarget"
    }
    Remove-Item $extractDir -Recurse -Force
    Remove-Item $ffmpegZipTarget -Force
} catch {
    Write-Warning "Failed to download ffmpeg: $_"
}

Write-Host "Tools check:"
Write-Host "yt-dlp: $(Test-Path $ytDlpTarget)"
Write-Host "ffmpeg: $(Test-Path $ffmpegTarget)"
