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
- Display the tool's remaining-time estimate, including unknown-size media;
  mark approximate HLS estimates with ~ and hide stale estimates while stalled.
- Group numbered archive volumes under an expandable aggregate, with group selection for batch actions.
- Add full-card selection, select-all, distinct action icons, two compact add
  actions and selected details that remain accessible in smaller windows.
- Reduce card spacing, distinguish touchpad scrolling from wheel easing, and
  animate startup sections, buttons and theme/accent transitions.
- Preserve the actual Python exception behind generic yt-dlp crash banners.
- Accept valid capped/extended range responses while rejecting missing, shifted,
  changed-length and non-byte Content-Range headers before writing data.

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
