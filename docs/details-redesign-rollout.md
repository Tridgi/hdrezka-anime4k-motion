# Details page redesign — implementation and verification plan

> **Архивный план редизайна (2026).** Этот документ фиксирует промежуточные решения тестовой разработки, а не гарантированную структуру версии 2.1. Актуальная пользовательская инструкция: [USER_GUIDE.md](USER_GUIDE.md). Финальная сводка изменений: [RELEASE_2.1.md](RELEASE_2.1.md). Описание старых порогов адаптивности ниже сохранено для истории тестирования; реализация могла измениться.

## Existing behavior and dependencies

- `DetailsPage.xaml.cs` owns `MovieDetailed`, `MovieVoiceActing`, `MovieSeason` and `MovieEpisode` selection. The selected objects are passed unchanged through `PlayerLaunch` into a separate `PlayerWindow`.
- Changing a translator calls `GetSeriesSeasonsAsync`; its response can arrive out of order. The existing `_seasonsGeneration` guard must remain. Startup restoration uses `PositionService.GetLatestFor` and selects the saved voice/season/episode by immutable IDs.
- Episode and watched buttons call `PositionService`, while tracking calls `TrackedSeriesService`. Downloads depend on `DonorUnlock` and `DownloadJobs`. Bookmarks require account authentication.
- `SettingsService` persists JSON settings and already provides appearance/theme. The separate `DetailPalette` is intentionally not an `AppTheme` variant: it changes semantic badges only.

## Visual composition implemented

1. **Top hero:** poster and only its action buttons (trailer → watch → bookmarks → track), with title, site ratings, 10-star user rating, age badge and metadata alongside.
2. **Metadata:** country flags, calm semantic genre badges (no emojis), duration badge, person hyperlinks, year and slogan. Colorful palette uses light/dark-aware tints; neutral palette uses grayscale. Person links keep their navigation.
3. **Description:** its own row across the entire page, limited to three lines with a centered expand/collapse action.
4. **Playback:** left season selector and right translator selector with its ratings button immediately after the label. Episode chips are small, with at most ten chips per 480-DIP row. Episode selection still uses original IDs.
5. **Secondary actions:** mark watched and donor download below the playback selector. Schedule, comments and recommendations remain underneath and retain their handlers.

## Adaptive rules (DIP, available DetailsPage width)

| Width | Layout |
| --- | --- |
| >= 1050 | Poster capped at 250, facts alongside, horizontal season/voice, 10 episode chips per row |
| 740–1049 | Poster capped at 210, facts alongside, dense badges |
| 670–739 | Poster above facts; season and translator remain side-by-side |
| 510–669 | Poster above facts; season and translator stack; chip rows adapt |
| < 510 | Reduced page padding, stacked selectors, secondary actions stack, episode grid contracts to fit |

All width calculations use `DetailsPage.ActualWidth`, which reacts to the NavigationView pane and window resize, not monitor resolution. The existing settings-driven poster scale remains the source of truth but is capped for details.

## Regression checklist before moving PR #2 into main

- Films with no seasons, no voiceover and coming-soon titles: no empty selector panel; watch button disabled when unavailable.
- 1, 7, 24, 50+ episodes: rows wrap, full episode name in tooltip, correct series ID and watched state; no clipped first/last buttons.
- 1, 12, 30+ translators: selection/dropdown labels remain legible, series reload for each translator; rapidly change translators to confirm the generation guard.
- Restore saved watch position, bookmark add/remove, track/untrack, trailer open, premium download, 10-star rating submission, selected episode playback in the separate player window.
- At 1920, 1366, 900, 740, 670, 510 and 390 effective DIP widths; with expanded/collapsed NavigationView and 100%, 125%, 150%, 175% Windows DPI.
- Neutral/Colorful settings persist after restart; test against both light and dark theme. Switch theme while details is active.
- Verify description expansion, cast 'more', person links and ratings modal opening at the application center.
- Check the original schedule, comments and recommendation list after the layout changes.

CI can verify source compilation, unit tests and packaging. Actual Windows rendering and behavior must be checked in the published test build; a successful build alone cannot certify UI appearance.
