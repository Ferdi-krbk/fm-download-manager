# Changelog

## 1.0.0 interface refresh - 2026-10-25

- Add the legacy in-app update bridge to stable 1.0.0, with verified silent installer payload and no repeated update prompts.
- Add staggered card entrances, hover lift, removal, details and progress transitions.
- Bundle static Inter font files for the app, popup and hover menu. Clamp startup
  bounds to the monitor work area, add compact layouts, a slim scrollbar,
  eased pixel scrolling and short reveal/hover transitions.
- Rebuild the main download window and browser popup around the Flow preview,
  with an outlined `fm.` identity, independent light/dark mode and user accents.
- Replace the large floating download bar with a 32 px logo button. Its menu
  offers video, audio and subtitles before dispatching a download.
- Simplify the sidebar to video, audio, files and torrents. Share format
  classification with destination folders and distinguish audio/subtitle exports.
- Report separate audio download progress and fragment retries, clear stale speed
  during processing, and terminate the audio subprocess when paused or cancelled.
- Omit the remaining-time countdown when media size is only an HLS estimate;
  continue showing fragment progress instead.

## 1.0.0 — 2026-10-24

First official stable release. Includes file, media and torrent downloads; light
and dark themes; English/Turkish localization; optional browser integration; and
the reliability/security fixes described in [release notes](release_notes_v1.md).

The repository presentation now uses current production UI renders, factual
feature descriptions, explicit limitations and reproducible validation steps.

## Historical development builds

Earlier 0.x, 2.x and 3.x versions use the legacy development numbering scheme and
are retained as prereleases on GitHub. They precede stable 1.0.0 despite their
higher version numbers. Their original tags and binary assets remain available
for historical reference. See [migration notes](docs/MIGRATION-1.0.md).
