# Security

The current stable release is the supported version. Historical prereleases are
retained for reference and may contain issues fixed in 1.0.0.

## Reporting

Please report vulnerabilities through the repository's **Security → Report a
vulnerability** option when available. If private reporting is unavailable, open
a minimal issue asking for a private reporting channel; do not post working
exploit details, credentials or private data in a public issue.

Include the affected version, the relevant component and enough information to
reproduce the issue. Remove pairing tokens, cookies and signed download URLs.

## Local data

The browser bridge binds to loopback and authenticates download/control requests.
The pairing token grants control of the local download queue; keep it private.
Only load extensions from a source you trust.

Pending-job cookies are protected with the current Windows user's DPAPI identity.
Local history and crash diagnostics can still contain URLs and filenames. Review
them before sharing. Antivirus scanning is optional and cannot guarantee that a
download is harmless.

See [Privacy](PRIVACY.md), [validation notes](docs/VALIDATION-1.0.md) and the
[migration guide](docs/MIGRATION-1.0.md).

## Update verification and downloaded files

Stable build 4 enables certificate validation for video downloads and rejects
in-app update packages unless an RSA-PSS/SHA-256 signature validates against the
public key embedded in the installed application. The signed manifest binds the
product version, stable build, executable name, byte length and SHA-256. Missing
or invalid verification data stops installation; there is no unsigned fallback.
Initial asset URLs must belong to this repository's HTTPS GitHub release path;
redirects must remain HTTPS. Downloads use randomly named temporary directories.

This authenticates updates, not arbitrary downloaded content. It does not replace
Authenticode, protect a compromised signing key/CI account, or retrofit checking
into already installed older binaries. Their first transition still uses their
old updater. The local signing-key backup is outside the checkout, encrypted with
the current Windows user's DPAPI; CI stores its copy in a GitHub Actions secret.
Maintainers must protect repository/workflow write access and signing credentials.

Completed HTTP, media and torrent files receive a ZoneId=3 internet-origin mark
where Windows alternate data streams are supported. Existing restricted-zone
marks are preserved. URLs/cookies are never written into this mark. Some disks
and network destinations cannot store it; the app does not claim OS attachment
protection is available on those destinations. Antivirus remains a separate check.

Release signing instructions: [maintainer guide](docs/RELEASE-SIGNING.md).
