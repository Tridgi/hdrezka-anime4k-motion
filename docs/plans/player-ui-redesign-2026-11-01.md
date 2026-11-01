# Player UI redesign plan — 2026-11-01

## Scope
Preserve the mpv renderer, high-DPI video transforms, Anime4K and smoothness processing, and settings persistence. Only update player UI and menus on the draft PR branch.

## Implementation sequence
1. Audit the XAML page, code-behind, separate Anime4K, Smoothness, Stats, Opening modules, and SettingsService.
2. Upper-left: Back → title → Season/Episode chip. Keep chip visible in narrow windows. Reduce title width and use ellipsis where needed, never separate title/chip with a growing spacer.
3. Upper-right: effects dropdowns shown only when user setting enables them and viewport fits → ✨ menu → pin → minimize → close. The right-side four buttons never disappear. ✨ menu includes Anime4K, Smoothness, statistics toggle and persistently saved upper-toolbar shortcut toggle. Existing ComboBoxes remain the processing source of truth.
4. Lower-left: play/pause → previous/next → transparent double-fast-forward opening skip → transparent speed selector → volume/mute → ⚙ menu. The menu provides subtitles, source quality, and Auto/Fill/Stretch zoom; optional donor download is accessible here. Lower-right: fullscreen. Remove external-player action from player; app-level integration deferred.
5. Adaptivity: compact screens hide volume slider and series navigation as needed; keep opening skip, speed, ⚙ and fullscreen. The season/episode chip and the top-right four buttons remain available. Keep skip duration configurable.
6. Validation: XAML structure and handler verification, GitHub Actions Windows build and core tests, followed by manual UI checks at 100%, 125%, 150%, 175% Windows scaling on CHUWI N100. Check short/long titles, season chip, top shortcuts persistence, stats on/off, menus and all items, slow/fast/window resizing, switching quality and subtitles, zoom, pin, minimize, fullscreen and video performance.

## Remaining device checks
A successful CI build verifies compilation, not visual placement or playback quality on an Intel N100.
