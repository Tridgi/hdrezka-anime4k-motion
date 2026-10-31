# HDREZKA Anime4K Motion v2

Unofficial Windows HDREZKA client with **Anime4K upscaling and independent 60 / 120 / experimental 165 FPS motion interpolation** in the existing separate player window.

Based on [eddinwork/HDREZKA](https://github.com/eddinwork/HDREZKA), commit `a4676233114dbc0af1221826ae8440d9293226bb`. Original authorship and license notices are retained. This is an independent derivative, not an official release.

[Русский](README.md) · [Downloads](https://github.com/Tridgi/hdrezka-anime4k-motion/releases) · [Build](docs/BUILD.md) · [Third-party notices](THIRD_PARTY.md)

## Features

- Anime4K Off / A / soft B / denoising C / Light, rendered with libmpv's Direct3D 11 API into the WinUI player.
- Independent Off / 60 / 120 / 165 FPS interpolation using open-svpflow, VapourSynth and OpenCL. No external player process.
- Configurable opening skip: 85 seconds by default, range 1–600 seconds.
- Cursor and controls hide after three seconds of playback inactivity; movement or pause restores them.
- Existing subtitles, quality, playback speed, volume, episode controls and playback position.
- Russian and English resources; no bundled user accounts, cookies or watch history.

## Install and requirements

Extract the complete Windows x64 portable ZIP from Releases and run `HDREZKA.App.exe`. Keep `runtime`, `motion` and `shaders`. The self-contained release includes .NET and Windows App SDK. Windows 10 build 19041+ / Windows 11, Direct3D 11; interpolation additionally needs a compatible OpenCL driver.

Tested hardware: NVIDIA P106-100 6 GB, Intel Core i5-6500, 2560×1440 165 Hz display. Other GPU configurations need testing. Start with Light + 120 FPS, or A + 60 FPS. A/B/C are processing variants, not increasing quality levels.

165 FPS is a target, not a guaranteed result: the short local 1080p24 test delivered about 140–144 rendered frames/s at that target with drops. Light + 120 and A + 60 had no drops during their short measured intervals. Results depend on content, hardware and network. This is not DLSS or RIFE; interpolation can introduce motion artifacts and Anime4K can alter fine textures. Fixed known source frame rates are the intended input.

Settings and login state are stored separately in `%APPDATA%\HDREZKA-Anime4K-Motion`; previous accounts are not imported. The update button opens this project's Releases and does not install upstream builds. Source code retains MIT notices; combined distribution with GPL libmpv components is subject to GPL terms. See THIRD_PARTY.md for dependency sources and licenses.
