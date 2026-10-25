param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
# Installation changes registry/shortcuts: run only on GitHub's disposable Windows runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or !$env:RUNNER_TEMP) {
    throw 'This installation smoke test is restricted to the disposable Windows CI runner.'
}
if (Get-Process FMDownloadManager -ErrorAction SilentlyContinue) { throw 'An unrelated FM process is already running.' }
$root = Join-Path $env:RUNNER_TEMP ('fm-package-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$install = Join-Path $root 'installed'
function Start-Hidden([string]$Executable, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($start)
}
$app = $null
try {
    $setup = Start-Hidden (Resolve-Path -LiteralPath $InstallerPath).Path @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=', "/DIR=$install", "/LOG=$root/setup.log")
    if (!$setup.WaitForExit(120000) -or $setup.ExitCode -ne 0) { throw 'Silent installation failed.' }
    $exe = Join-Path $install 'FMDownloadManager.exe'
    if ((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath 'publish/win-x64/FMDownloadManager.exe').Hash) { throw 'Installed application differs from the published binary.' }
    foreach ($relative in @('Tools/yt-dlp.exe', 'Tools/ffmpeg.exe', 'Tools/ffprobe.exe', 'extension/manifest.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $install $relative))) { throw "Installed package lacks $relative" }
    }
    if (Test-Path -LiteralPath (Join-Path $install 'extension/token.json')) { throw 'Installed package contains a pairing secret.' }
    $app = Start-Hidden $exe @('--background')
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($app.HasExited) { throw "Installed app exited during startup ($($app.ExitCode))." }
        try {
            $tokenPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MC Download Manager/bridge.token'
            if (Test-Path -LiteralPath $tokenPath) {
                $token = [IO.File]::ReadAllText($tokenPath).Trim()
                $ping = Invoke-RestMethod 'http://127.0.0.1:48213/ping' -Headers @{ 'X-MCDM-Extension' = 'ci'; 'X-MCDM-Token' = $token } -TimeoutSec 2
                if ($ping.version -eq '1.0.0' -and $ping.paired) { $ready = $true; break }
            }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) { throw 'Installed app did not start its authenticated bridge.' }
    Write-Output 'PASS installed package: silent install, binary hash, bundled tools, no packaged secret, actual application startup and authenticated bridge.'
} finally {
    if ($app -and !$app.HasExited) { $app.Kill($true); $app.WaitForExit() }
    $uninstall = Join-Path $install 'unins000.exe'
    if (Test-Path -LiteralPath $uninstall) {
        $remove = Start-Hidden $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        if (!$remove.WaitForExit(60000) -or $remove.ExitCode -ne 0) { throw 'CI package cleanup failed.' }
    }
}
