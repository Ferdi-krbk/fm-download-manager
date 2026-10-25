param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [string]$OutputPath = 'dist/legacy-bridge/FM-Legacy-Update.exe'
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
$folder = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$hashPath = Join-Path $folder 'installer.sha256'
(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $hashPath -Encoding ascii
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler is required to build the legacy bootstrap.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll "/resource:$installer,installer.exe" "/resource:$hashPath,installer.sha256" "/out:$output" (Join-Path $PSScriptRoot 'LegacyUpdateBootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw 'Legacy bootstrap compilation failed' }
$check = Start-Process -FilePath $output -ArgumentList '--verify-only' -Wait -PassThru -WindowStyle Hidden
if ($check.ExitCode -ne 0) { throw 'Embedded installer verification failed' }
Get-FileHash -LiteralPath $output -Algorithm SHA256
