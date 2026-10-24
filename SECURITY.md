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
