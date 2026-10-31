# Components, attribution and source availability

HDREZKA Anime4K Motion is derived from [eddinwork/HDREZKA](https://github.com/eddinwork/HDREZKA/tree/a4676233114dbc0af1221826ae8440d9293226bb), MIT. The existing `LICENSE.md` and `licenses/HDREZKA-LICENSE.md` preserve the original notices, including voidboost's MIT macOS client attribution. New application source is distributed under the same MIT terms; no exclusive authorship of the upstream work is claimed.

The binary package also links to libmpv and includes third-party runtimes. Their licenses are independent of the application source license. Distribution of the combined work must respect the applicable GPL terms; the GPL-2.0 and GPL-3.0 texts are included. Do not describe the entire binary package as MIT-only.

| Component | Version/source | License notices |
| --- | --- | --- |
| Anime4K | v3.2 shader set, [bloc97/Anime4K](https://github.com/bloc97/Anime4K) | MIT notices retained in every GLSL file |
| libmpv | [mpv e471496c56](https://github.com/mpv-player/mpv/tree/e471496c56), [shinchiro build 20261021](https://github.com/shinchiro/mpv-winbuild-cmake/tree/20261021) | GPL; `licenses/mpv-GPL.txt`; includes FFmpeg and other dependencies |
| open-svpflow | [deadbad2201d77056173c05c2380758d88612019](https://github.com/Z1xus/open-svpflow/tree/deadbad2201d77056173c05c2380758d88612019) | Apache-2.0; runtime/open-svpflow/LICENSE and CRATES.txt |
| VapourSynth | [R81](https://github.com/vapoursynth/vapoursynth/tree/R81) | LGPL-2.1-or-later; wheel metadata and COPYING.LESSER retained |
| Python | [3.12.10](https://www.python.org/downloads/release/python-31210/) | PSF and included notices; runtime/python/LICENSE.txt |
| .NET | [dotnet/runtime](https://github.com/dotnet/runtime) | MIT and third-party notices shipped in self-contained output |
| Windows App SDK | [microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | Upstream NuGet/runtime notices and terms |

Exact binary archive URLs and hashes are in `scripts/dependencies.json` and `licenses/*SOURCES.txt`. The mpv build repository supplies toolchain scripts and third-party dependency recipes; these are important when rebuilding the GPL library and its dependencies. Runtime DLLs are unmodified and replaceable. No proprietary SVP binaries are used.

The release source archive contains this application's full source, shaders, motion scripts and dependency/bootstrap/build scripts. Dependency source repositories and pinned revisions are linked above; when redistributing binaries, preserve these notices and provide the corresponding dependency source and build materials required by each component's license. A link alone is not a substitute for any applicable source-distribution obligation.
