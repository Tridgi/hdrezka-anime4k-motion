# Changelog

## v2.0.0

First cleaned public release of HDREZKA Anime4K Motion, based on HDREZKA commit a4676233114dbc0af1221826ae8440d9293226bb.

- Embedded Direct3D 11 libmpv rendering and Anime4K A/B/C/Light.
- Independent OpenCL motion interpolation targets: 60, 120, experimental 165 FPS.
- Configurable opening skip and inactivity cursor/control hiding.
- Fixed delayed access violation after disabling FPS by pinning worker runtime modules for the process lifetime.
- Hidden accidental global keyboard accelerator tooltips over the video.
- Separate clean app data folder; project-specific update link; no bundled personal state.

Earlier v1/v2/v2.1 names referred to local development builds; they are not separate public releases.
