param(
    [Parameter(Mandatory)][string]$AssetPath,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][int]$Build,
    [string]$OutputDirectory,
    [switch]$UseLocalKey
)
$ErrorActionPreference = 'Stop'
$asset = Get-Item -LiteralPath $AssetPath
if (!$OutputDirectory) { $OutputDirectory = $asset.DirectoryName }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
if ($asset.Name -notmatch '^[A-Za-z0-9._-]+\.exe$' -or $Build -lt 1) { throw 'Invalid update metadata' }
$rsa = [Security.Cryptography.RSA]::Create()
try {
    if ($UseLocalKey) {
        $keyPath = Join-Path $env:LOCALAPPDATA 'FM Download Manager Release Keys/update-signing.dpapi'
        $private = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath), $null, 'CurrentUser')
    } else {
        if (!$env:FM_UPDATE_SIGNING_KEY) { throw 'Release signing key is missing; refusing an unsigned release.' }
        $private = [Convert]::FromBase64String($env:FM_UPDATE_SIGNING_KEY)
    }
    $read = 0
    $rsa.ImportPkcs8PrivateKey($private, [ref]$read)
    $expected = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/McDm.Core/update-public-key.pem')).Trim()
    if ($rsa.ExportSubjectPublicKeyInfoPem().Trim() -ne $expected) { throw 'Signing key does not match the application verification key' }
    $manifest = [ordered]@{
        version = $Version; build = $Build; asset = $asset.Name
        sha256 = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $asset.Length
    } | ConvertTo-Json -Compress
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest)
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    if (!$rsa.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Signature self-check failed' }
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'update-manifest.json'), $bytes)
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'update-manifest.sig'), $signature)
    Write-Output "Signed update: $($asset.Name), version $Version, build $Build"
} finally {
    if ($private) { [Array]::Clear($private, 0, $private.Length) }
    $rsa.Dispose()
}
