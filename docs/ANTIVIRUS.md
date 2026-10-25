# Automatic antivirus integration

Enable **Automatically scan completed downloads** in Settings. FM detects product
names through Windows Security Center and asks registered Windows AMSI providers
to scan the downloaded file. This is not limited to a hard-coded vendor list.
No executable selection, command arguments or administrator configuration is
required. Existing files also have **Scan with antivirus** in their context menu.

AMSI scanning uses a 64-bit file stream, so large files do not need to be copied
into one large memory buffer. The scan runs in a separate FM helper process with
timeout/cancellation handling. The application does not change system antivirus
settings or disable protection.

Known Kaspersky and Microsoft Defender console scanners provide fallbacks when the
Windows integration is unavailable. Legacy Kaspersky GUI paths are never launched
as file scanners. Unknown GUI programs are not treated as successful scanners
merely because they return exit code zero.

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

## References

- [Microsoft: AMSI developer audience and samples](https://learn.microsoft.com/en-us/windows/win32/amsi/dev-audience)
- [Microsoft: IAntimalware.Scan](https://learn.microsoft.com/en-us/windows/win32/api/amsi/nf-amsi-iantimalware-scan)
- [Microsoft: file-stream example](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/AmsiStream/AmsiStream.cpp)
- [Kaspersky: supported command-line interface](https://support.kaspersky.com/help/Kaspersky/Win21.7/en-US/83989.htm)
- [Bitdefender: consumer CLI limitation and Endpoint Security tooling](https://community.bitdefender.com/en/discussion/90097/command-line-scan-for-my-custom-business-software)
