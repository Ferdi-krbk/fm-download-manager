# Automatic antivirus integration

Enable **Automatically scan completed downloads** in Settings. FM detects product
names through Windows Security Center. A detected Kaspersky antivirus console
scanner is preferred for direct file scans. Otherwise, registered Windows AMSI
providers are tried directly, with all Microsoft Defender routes ordered last.
This priority applies to every compatible third-party provider, not just Kaspersky.
No executable selection, command arguments or administrator configuration is
required. Existing files also have **Scan with antivirus** in their context menu.

AMSI scanning uses a 64-bit file stream, so large files do not need to be copied
into one large memory buffer. The scan runs in a separate FM helper process with
timeout/cancellation handling. The application does not change system antivirus
settings or disable protection.

Kaspersky VPN/password-manager consoles are not antivirus scanners and are excluded.
Legacy Kaspersky GUI paths are never launched
as file scanners. Unknown GUI programs are not treated as successful scanners
merely because they return exit code zero.

Kaspersky success requires completed scan statistics with processed objects and
no errors/skipped/password-protected/corrupted objects; an exit code of zero alone
does not prove a scan took place. Positive detection statistics and scan threat
exit codes are treated as attention. Unrecognized/localized statistics are left
unverified rather than falsely reporting a completed scan.

The Settings panel displays the selected scan route separately from registered
product names. Each AMSI worker is pinned to a specific registered provider CLSID;
its registered local DLL must pass Windows Authenticode trust verification before
activation. Registry/signing policies are never disabled or edited. Failed or
unavailable providers are tried in order; detected threats and policy blocks stop
fallback. AMSI activity need not appear as a normal file-scan task in the product's
interface. Unsupported products are not simulated by launching a GUI executable.

## Results

- `passed`: a supported scan completed without reporting a threat. This is not a
  guarantee that a file is harmless.
- `unverified`: no compatible provider, timeout or scan failure. The completed
  download is preserved and the UI distinguishes this from a threat detection.
- `attention`: a reported threat, a Windows security-policy block, or a file
  removed/quarantined while scanning. This requires review in the protection app.

Detection and scan capability are separate: an installed product can be listed
even when its scan provider is unavailable. Bitdefender and other brands use the
Windows route when their installed version registers a compatible provider. Their
GUI executable is not guessed or launched as a substitute. Not every product or
version has been tested; the Windows route and a real harmless-file rescan were
verified on the development PC with Kaspersky installed.

The direct Kaspersky 21.26 file-scanner route was separately exercised with clean
generated fixtures: one processed object, zero detections/errors, a completed
scan task and unchanged file bytes. Its VPN companion was excluded from detection.

## References

- [Microsoft: AMSI developer audience and samples](https://learn.microsoft.com/en-us/windows/win32/amsi/dev-audience)
- [Microsoft: IAntimalware.Scan](https://learn.microsoft.com/en-us/windows/win32/api/amsi/nf-amsi-iantimalware-scan)
- [Microsoft: file-stream example](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/AmsiStream/AmsiStream.cpp)
- [Kaspersky: supported command-line interface](https://support.kaspersky.com/help/Kaspersky/Win21.7/en-US/83989.htm)
- [Bitdefender: consumer CLI limitation and Endpoint Security tooling](https://community.bitdefender.com/en/discussion/90097/command-line-scan-for-my-custom-business-software)
