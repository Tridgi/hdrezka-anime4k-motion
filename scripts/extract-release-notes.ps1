param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Encoding UTF8)
$heading = if ($Version -eq 'Unreleased') { '## [Unreleased]' } else { "## $Version" }
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq $heading) { $start = $i + 1; break }
}
if ($start -lt 0) { throw "Missing changelog section: $heading" }
$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $end = $i; break }
}
$body = ($lines[$start..($end - 1)] -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($body)) { throw "Empty release notes for $Version" }
$prefix = if ($Version -eq 'Unreleased') {
    "Тестовая сборка. Ниже перечислены накопленные изменения, запланированные для следующего стабильного выпуска."
} else { "Изменения стабильного релиза $Version." }
$targetDirectory = Split-Path -Parent $OutputPath
if ($targetDirectory) { New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null }
"$prefix`n`n$body" | Set-Content -LiteralPath $OutputPath -Encoding UTF8
