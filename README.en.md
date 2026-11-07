# HDREZKA Anime4K Motion 2.1

**An unofficial Windows HDRezka client with integrated Anime4K enhancement, independent motion interpolation, and a redesigned browsing experience.**

**[Download the stable v2.1.0 release](https://github.com/Tridgi/hdrezka-anime4k-motion/releases/tag/v2.1.0)** · [🇷🇺 Русский](README.md) · [Complete changes](CHANGELOG.md) · [Build](docs/BUILD.md) · [Third-party notices](THIRD_PARTY.md)

> Independent derivative of [eddinwork/HDREZKA](https://github.com/eddinwork/HDREZKA), upstream commit `a4676233114dbc0af1221826ae8440d9293226bb`. Not an official HDRezka release. No copyrighted media, credentials or user profiles are included.

## What's new in 2.1

| Area | Improvements |
| --- | --- |
| **Catalog and genres** | Genre choices discovered from the current mirror, media categories, sorting, and responsive controls that reflow instead of being hidden behind a horizontal scrollbar |
| **Collections** | Searchable responsive cover grid, complete artwork, pagination, and correct in-collection Back navigation |
| **Movie details** | Redesigned HDRezka / IMDb / Kinopoisk rating badges, clickable actors, directors, genres and franchise references, season and voiceover selectors |
| **Comments** | Richer nested threads, replies, reactions, sorting, editor with emoji, readable site errors, own-comment highlighting |
| **Appearance** | System/light/dark/manual themes, accent-pair presets, custom hex colors, colored or neutral chips, poster-size preview |
| **Home screen** | Redesigned Top 5 spotlight, poster carousels, hover previews, visual fixes and ratings |
| **Updates** | Stable/test channels, release history, SHA-256 verification, update backup and rollback |
| **Boosty** | Dedicated support screen with direct link and QR code |

See the [full v2.1 notes](docs/RELEASE_2.1.md) and [CHANGELOG](CHANGELOG.md) for details.

## Playback features

- **Anime4K:** Off, A, B (softer), C (denoising), Light. Applied inside the separate player window through libmpv/Direct3D 11; no additional external video player is needed.
- **Frame interpolation:** independent Off, 60, 120, or experimental 165 FPS target with SVPflow/VapourSynth and an OpenCL-capable device.
- **Opening skip:** 85 seconds by default, adjustable from 1 to 600 seconds.
- Existing quality, language, subtitle, volume, speed, season and episode controls; saved playback position and automatic player-control hiding.
- Fixes for FPS reconfiguration across sources and low-resolution 426×240 / 854×480 inputs.

**Hardware limitations:** experimental 165 FPS is not guaranteed. Anime4K is not DLSS or RIFE; the filters do not restore missing details and interpolation can introduce motion artifacts. Tested local reference hardware includes NVIDIA P106-100 6 GB, Intel i5-6500, 2560×1440 / 165 Hz. Results will vary by GPU, video and network.

## Requirements and installation

| Requirement | Details |
| --- | --- |
| OS | Windows 10 2004 (build 19041)+ or Windows 11 |
| Architecture | **x64** portable distribution |
| GPU | Direct3D 11 for rendering; compatible OpenCL device/driver for interpolation |
| Dependencies | .NET 8 and Windows App SDK packaged in the self-contained archive |
| Connectivity | Working HDRezka mirror for catalog and playback; GitHub for update checks |

1. Open [v2.1.0 on GitHub](https://github.com/Tridgi/hdrezka-anime4k-motion/releases/tag/v2.1.0).
2. Download `HDREZKA-Anime4K-Motion-2.1.0-win-x64.zip`; optionally verify with `SHA256SUMS.txt`.
3. **Extract the entire ZIP** to a writable directory, for example `D:\Apps\HDREZKA`, then run `HDREZKA.App.exe`. Do not remove `runtime`, `motion`, `shaders`, or the update helper.
4. Select an accessible HTTPS mirror and sign in to use account-dependent actions.

**Updating from a testing build:** select **Settings → Updates → Stable → Check for updates**. If the in-app updater is unavailable, extract the stable ZIP to a separate folder. Close the application before replacing files. Do not delete `%APPDATA%\HDREZKA-Anime4K-Motion` — settings, login state, and watch history are stored separately from the portable application.

## Notes on catalog, comments and privacy

Genres are based on URLs actually exposed by the selected mirror; external site changes, access restrictions or CAPTCHA challenges may still affect them. Comment posting is subject to the site's authentication, moderation and CAPTCHA policies. On a rejected post, the draft remains intact and readable error information is shown when provided by the website. Own-comment styling relies on author-specific controls in source markup, or matching an authenticated username where reliable.

Official GitHub Releases provide the stable `vX.Y.Z` and test `test-<commit>` channels. Downloads require valid SHA-256 verification; the updater keeps a backup and attempts rollback on errors. Cached release information may lag temporarily under GitHub API rate limits.

The portable archive contains no user account, cookies, media library, private logs or test videos. The application communicates with your configured HDRezka mirror, video CDN and GitHub.

## Further reading

- [Russian user guide](docs/USER_GUIDE.md) — navigation, settings and troubleshooting
- [Detailed 2.1 release notes](docs/RELEASE_2.1.md)
- [Release workflow](docs/RELEASES.md) · [Updater design](docs/UPDATER.md)
- [Building from source](docs/BUILD.md)
- [Licenses and third-party code](THIRD_PARTY.md) · [LICENSE](LICENSE.md)

**Support development:** [Boosty — boosty.to/tridgi](https://boosty.to/tridgi).

This repository is an unofficial derivative; original attribution to voidboost and eddinwork is retained. The client includes MIT notices; combined distribution with libmpv components is subject to relevant GPL and other third-party terms listed in THIRD_PARTY.md.
