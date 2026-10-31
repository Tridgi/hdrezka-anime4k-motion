param([string]$SevenZip)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repo '.dependencies'
$app = Join-Path $repo 'src/HDREZKA.App'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (-not $SevenZip) {
    $cmd = Get-Command 7z -ErrorAction SilentlyContinue
    if ($cmd) { $SevenZip = $cmd.Source } else { $SevenZip = Join-Path $env:ProgramFiles '7-Zip/7z.exe' }
}
if (-not (Test-Path -LiteralPath $SevenZip)) { throw 'Install official 7-Zip or supply -SevenZip.' }
foreach ($dep in (Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json)) {
    $archive = Join-Path $cache $dep.file
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $dep.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dep.sha256) { throw "SHA256 mismatch: $($dep.name)" }
    $extract = Join-Path $cache $dep.name
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    & $SevenZip x $archive "-o$extract" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Extraction failed: $($dep.name)" }
}
$native = Join-Path $app 'Native'
$python = Join-Path $app 'runtime/python'
$site = Join-Path $python 'Lib/site-packages'
$svp = Join-Path $app 'runtime/open-svpflow'
New-Item -ItemType Directory -Path $native,$python,$site,$svp -Force | Out-Null
$mpv = Get-ChildItem (Join-Path $cache 'mpv') -Recurse -Filter libmpv-2.dll | Select-Object -First 1
if (-not $mpv) { throw 'libmpv DLL missing' }
Copy-Item -LiteralPath $mpv.FullName -Destination $native -Force
Copy-Item -Path (Join-Path $cache 'python/*') -Destination $python -Recurse -Force
Add-Content -LiteralPath (Join-Path $python 'python312._pth') -Value 'Lib/site-packages' -Encoding ascii
$wheel = Get-ChildItem (Join-Path $cache 'vapoursynth') -Recurse -Filter 'vapoursynth-81-cp312-abi3-win_amd64.whl' | Select-Object -First 1
if (-not $wheel) { throw 'VapourSynth Python 3.12 wheel missing' }
& $SevenZip x $wheel.FullName "-o$site" -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Wheel extraction failed' }
foreach ($name in @('svpflow1_vs.dll','svpflow2_vs.dll','open_svpflow.dll','open_svpflow.h','LICENSE','CRATES.txt','BUILD.txt')) {
    $file = Get-ChildItem (Join-Path $cache 'svpflow') -Recurse -File | Where-Object Name -EQ $name | Select-Object -First 1
    if ($file) { Copy-Item -LiteralPath $file.FullName -Destination $svp -Force }
}
Get-ChildItem -LiteralPath (Join-Path $app 'runtime') -Recurse -Filter '*.pdb' -File | Remove-Item -Force
Write-Output 'Pinned dependencies verified and restored.'
