param([Parameter(Mandatory)][string]$InputDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { if (Get-ChildItem -LiteralPath $outputRoot -Force) { throw 'Output directory must be empty.' } }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath $inputRoot -Recurse -File)) {
    $relative = [IO.Path]::GetRelativePath($inputRoot, $file.FullName)
    $parts = $relative -split '[\\/]'
    if ($parts -contains '__pycache__' -or $file.Extension -in @('.pdb','.pyc','.log','.dmp','.db','.mp4','.mkv')) { continue }
    if ($file.Name -match '^(cookies|settings|positions|tracked-series|protocol-pending)\.') { throw "Personal-state file in build: $relative" }
    if ($parts.Count -gt 1 -and $parts[0] -match '^[a-z]{2,3}[-_][A-Za-z0-9-]+$' -and $parts[0] -notin @('ru-RU','en-us','en-GB')) { continue }
    $target = Join-Path $outputRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination $outputRoot -Recurse
foreach ($name in @('README.md','README.en.md','THIRD_PARTY.md','LICENSE.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $outputRoot }
Write-Output "Clean portable package: $outputRoot"
