# Update package signing

The app pins `src/McDm.Core/update-public-key.pem`. Never replace this key casually:
installed clients trust it. This is an application update signature, not Windows
Authenticode. A full compromise of the signing account/workflow remains outside
the protection it provides. Restrict Actions secrets and review workflow changes.

The initial maintainer setup uses PowerShell 7 on Windows:

```powershell
pwsh -NoProfile -File scripts/initialize-update-signing.ps1
```

It retains a DPAPI-encrypted private key outside the repository under the current
user's LocalAppData `FM Download Manager Release Keys` folder and configures the
`FM_UPDATE_SIGNING_KEY` Actions secret through standard input. It refuses to
silently replace an existing public key. Back up the signing identity securely;
the DPAPI file alone cannot be decrypted on another Windows identity/machine.
Never commit, print, share or include the private key in a release artifact.

Tag builds sign both the official installer and the legacy bootstrap, each with
its own `update-manifest.json` and binary `update-manifest.sig`. The exact UTF-8
manifest bytes are signed using RSA-PSS/SHA-256. The client verifies the signature
before trusting the hash and requires the package to match the signed byte count.
An absent/mismatched signing secret fails publication rather than emitting an
unsigned update. Main/PR builds do not need the secret.

For a manually published, already-tested installer, sign it locally:

```powershell
pwsh -NoProfile -File scripts/sign-update.ps1 -AssetPath dist/FMDownloadManager-Setup-1.0.0.exe -Version 1.0.0 -Build 5 -UseLocalKey
```

Upload the executable and both manifest files to the same release. Sign the bridge
separately after embedding that exact installer. Update the release build marker
only after its matching assets are uploaded. Clients intentionally refuse partial
or mixed releases; retry after publication completes. Keep historical files and
the compatibility discovery behavior described in the migration guide.

Old installed clients cannot acquire signature verification until they have
installed this build. Windows may still display an unknown-publisher prompt.
