param([string]$Repository = 'Ferdi-krbk/fm-download-manager')
$ErrorActionPreference = 'Stop'
# The private key never enters the checkout, command line, or tool output.
$keyDirectory = Join-Path $env:LOCALAPPDATA 'FM Download Manager Release Keys'
[IO.Directory]::CreateDirectory($keyDirectory) | Out-Null
$keyPath = Join-Path $keyDirectory 'update-signing.dpapi'
$rsa = [Security.Cryptography.RSA]::Create(3072)
try {
    if (Test-Path -LiteralPath $keyPath) {
        $protected = [IO.File]::ReadAllBytes($keyPath)
        $private = [Security.Cryptography.ProtectedData]::Unprotect($protected, $null, 'CurrentUser')
        $read = 0
        $rsa.ImportPkcs8PrivateKey($private, [ref]$read)
    } else {
        $private = $rsa.ExportPkcs8PrivateKey()
        $protected = [Security.Cryptography.ProtectedData]::Protect($private, $null, 'CurrentUser')
        [IO.File]::WriteAllBytes($keyPath, $protected)
    }
    $publicPath = Join-Path $PSScriptRoot '../src/McDm.Core/update-public-key.pem'
    $public = $rsa.ExportSubjectPublicKeyInfoPem()
    if ((Test-Path -LiteralPath $publicPath) -and [IO.File]::ReadAllText($publicPath).Trim() -ne $public.Trim()) {
        throw 'Existing public key differs; explicit key rotation is required.'
    }
    [IO.File]::WriteAllText($publicPath, $public + "`n", [Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new('gh')
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    foreach ($arg in @('secret', 'set', 'FM_UPDATE_SIGNING_KEY', '--repo', $Repository)) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Write([Convert]::ToBase64String($private))
    $process.StandardInput.Close()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Could not configure the signing secret.' }
    Write-Output 'Public verification key written; encrypted local key retained; GitHub signing secret configured.'
} finally {
    if ($private) { [Array]::Clear($private, 0, $private.Length) }
    $rsa.Dispose()
}
