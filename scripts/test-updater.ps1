$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Exercise the same PowerShell 5.1 helper as the shipped updater with fake
# portable app files. No live WinUI app or network request is required.
$root = Join-Path $env:RUNNER_TEMP 'hdrezka-updater-smoke'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$install = Join-Path $root 'HDREZKA-TestInstall'
$payload = Join-Path $root 'payload'
$archive = Join-Path $root 'update.zip'
New-Item -ItemType Directory -Path $install, $payload -Force | Out-Null
$exe = Join-Path $env:WINDIR 'System32\whoami.exe'
Copy-Item -LiteralPath $exe -Destination (Join-Path $install 'HDREZKA.App.exe')
Copy-Item -LiteralPath $exe -Destination (Join-Path $payload 'HDREZKA.App.exe')
Set-Content -LiteralPath (Join-Path $install 'build-marker.txt') -Value 'OLD'
Set-Content -LiteralPath (Join-Path $payload 'build-marker.txt') -Value 'NEW'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive -CompressionLevel Fastest
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$installer = Join-Path $PSScriptRoot '..\src\HDREZKA.App\Updater\InstallUpdate.ps1'

# A deliberately nonexistent PID simulates an application that has exited.
# The real UI performs that shutdown after handing off to this helper.
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $installer `
    -InstallPath $install -ArchivePath $archive -ExpectedSha256 $hash `
    -ParentProcessId 2147483000 -ReleaseTag 'test-updater-smoke' `
    -Commit '1111111111111111111111111111111111111111' -ReleaseChannel Testing
if ($LASTEXITCODE -ne 0) { throw "Updater smoke process returned $LASTEXITCODE" }

$marker = (Get-Content -LiteralPath (Join-Path $install 'build-marker.txt') -Raw).Trim()
if ($marker -ne 'NEW') { throw "New build not installed: $marker" }
$backup = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -Like 'HDREZKA-TestInstall.backup-*')
if ($backup.Count -ne 1) { throw "Expected one backup directory, found $($backup.Count)" }
$previous = (Get-Content -LiteralPath (Join-Path $backup[0].FullName 'build-marker.txt') -Raw).Trim()
if ($previous -ne 'OLD') { throw "Previous build not preserved: $previous" }
$metadata = Get-Content -LiteralPath (Join-Path $install 'update-build.json') -Raw | ConvertFrom-Json
if ($metadata.Tag -ne 'test-updater-smoke' -or $metadata.Channel -ne 'Testing') {
    throw 'Installed version metadata invalid'
}
Write-Host 'Updater smoke passed: verified archive, replaced files, kept backup and version identity.'
