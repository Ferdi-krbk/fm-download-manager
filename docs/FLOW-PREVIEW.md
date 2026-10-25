# Flow interface preview

This documents the Flow interface included in the refreshed stable build. The main WPF window uses `Themes/FlowControls.xaml`; its control
templates are separate from the older dialog styles. The browser popup was
rebuilt from the approved preview markup. Media discovery remains in the
background/content scripts and the popup's separate `popup-media.js` helper.

The browser trigger is a 32 × 32 px `fm.` button. Clicking opens choices; clicking
the mark alone never starts a download. Its menu remains keyboard accessible.

Accent color and light/dark mode are independent. Presets and a custom color are
available. Small text uses an adjusted accent for contrast, while the user's
selected color remains the stored setting. Existing saved preferences persist.

Static Inter font files are bundled with the app, popup and hover menu. Their
actual glyph resources are checked so a silent system-font fallback is caught.
The startup window fits its monitor work area. Compact layouts, a slim scrollbar,
eased wheel scrolling, native high-resolution touchpad deltas, staggered startup,
card interactions and theme/accent transitions are included. Motion
respects the system animation preference.
Rendering engines and DPI settings can produce visible differences from the
in-conversation preview. Exported UI checks use a real off-screen WPF window and 192 DPI for native captures and a
device scale of 2 for Chromium; these are real controls, not redesigned pictures. Dispatcher tests drain idle-priority binding work so background progress updates are actually verified.

## Verification

```powershell
dotnet run -c Release --project tests/McDm.Tests
dotnet run -c Release --project tests/McDm.Acceptance -- --ui-only dist/ui-polish-checks
dotnet run -c Release --project tests/McDm.Acceptance -- dist/flow-check dist/flow-check/screenshots
Get-ChildItem tests/test_*.js | ForEach-Object { node $_.FullName; if ($LASTEXITCODE -ne 0) { throw $_.Name } }
cd tests/ui
npm ci
npx playwright install chromium
$env:MCDM_SAMPLE_VIDEO = 'dist/flow-check/fixtures/sample-clip.mp4'
npm test
```

The UI browser test uses real extension code with a fixture for `chrome.*` APIs
and generated sample media in a separate headless browser. It checks the compact
trigger, keyboard dismissal, video/audio/subtitle dispatch, theme selection,
custom colors and accent text contrast. This does not establish compatibility
with every live streaming provider or replace an installed-extension check.

Numbered archive volumes (part01.rar, 7z.001 and legacy r00/z01 patterns) share
an expandable group scoped to their target folder and archive name. Headers show
actual aggregate progress and select the underlying jobs for batch actions.
Ordinary same-named files are not grouped by guesswork. Grouping is presentation
only; no archive extraction or concatenation runs automatically.

Unknown file formats remain in Files; no attempt is made to guess all possible
formats. Audio-only and subtitle-only requests have explicit classifications.
