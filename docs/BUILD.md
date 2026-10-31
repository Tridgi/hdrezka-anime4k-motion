# Build on Windows x64

Requires .NET 8 SDK and PowerShell. `scripts/bootstrap.ps1` fetches pinned official dependency archives and checks SHA-256 before extraction. Use a recent 7-Zip installation for the mpv development archive; its executable must be in PATH or at the standard Program Files location.

```powershell
pwsh -File scripts/bootstrap.ps1
dotnet restore src/HDREZKA.App/HDREZKA.App.csproj -p:Platform=x64 -p:SelfContained=true
dotnet publish src/HDREZKA.App/HDREZKA.App.csproj -c Release -p:Platform=x64 --self-contained true -o artifacts/app
dotnet test tests/HDREZKA.Tests/HDREZKA.Tests.csproj -c Release
pwsh -File scripts/package.ps1 -InputDirectory artifacts/app -OutputDirectory artifacts/portable
```

Do not copy AppData, credentials, cookies, local logs, test clips, crash dumps or PDB files into a release. The packaging script excludes known private-state and diagnostic files, and strips unused locale resources. Source archives must exclude .git, bin, obj, Native, runtime, dependency archives and generated output. Native dependencies are recovered through bootstrap, not committed to git.

Local playback integration checks accept `--local-file=ABSOLUTE_PATH` with a generated 1080p24 test clip. Optional `--smooth-report=ABSOLUTE_PATH` measures interpolation modes; `--switch-report=ABSOLUTE_PATH` performs 12 mode switches and waits 30 seconds after disabling interpolation. These flags explicitly create local diagnostic files; they are not used in ordinary playback. Tests do not establish compatibility with every stream or GPU.
