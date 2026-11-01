param(
    [Parameter(Mandatory=$true)][string]$InstallPath,
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][int]$ParentProcessId,
    [Parameter(Mandatory=$true)][string]$ReleaseTag,
    [Parameter(Mandatory=$true)][string]$Commit,
    [Parameter(Mandatory=$true)][ValidateSet('Stable','Testing')][string]$ReleaseChannel
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$logDirectory = Join-Path $env:APPDATA 'HDREZKA-Anime4K-Motion'
$logFile = Join-Path $logDirectory 'updater.log'
$install = [IO.Path]::GetFullPath($InstallPath).TrimEnd([char[]]@('\\','/'))
$archive = [IO.Path]::GetFullPath($ArchivePath)
$directory = Split-Path -Parent $install
$name = Split-Path -Leaf $install
$nonce = [guid]::NewGuid().ToString('N')
$stage = Join-Path $directory "$name.stage-$nonce"
$backup = Join-Path $directory "$name.backup-$(Get-Date -Format yyyyMMdd-HHmmss)-$nonce"
$swapped = $false
$backedUp = $false
function Write-UpdateLog([string]$message) {
    try {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        Add-Content -LiteralPath $logFile -Value "[$(Get-Date -Format o)] $message"
    } catch {}
}
try {
    if (-not (Test-Path -LiteralPath (Join-Path $install 'HDREZKA.App.exe') -PathType Leaf)) { throw 'Current installation not found.' }
    if (([IO.Path]::GetPathRoot($install)).TrimEnd('\\') -eq $install) { throw 'Refusing to replace a volume root.' }
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Update archive not found.' }
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) { throw 'Archive checksum mismatch.' }

    # Wait for app to exit before touching its directory.
    try { Wait-Process -Id $ParentProcessId -Timeout 120 -ErrorAction Stop } catch {
        if (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) { throw 'Application did not exit.' }
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if ($zip.Entries.Count -gt 12000) { throw 'Too many ZIP entries.' }
        $size = [long]0
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('\\', '/')
            $segments = $relative.Split('/')
            if ($relative.StartsWith('/') -or $relative.Contains(':') -or
                $segments -contains '..' -or $segments -contains '.' -or
                [string]::IsNullOrWhiteSpace($relative)) {
                throw 'Unsafe ZIP entry path.'
            }
            $fileType = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($fileType -eq 0xA000) { throw 'ZIP symbolic link is not allowed.' }
            $size += $entry.Length
            if ($size -gt 2500000000) { throw 'Expanded archive exceeds the limit.' }
        }
    } finally { $zip.Dispose() }

    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $stage -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'HDREZKA.App.exe') -PathType Leaf)) { throw 'Missing executable in update.' }
    # Build identity travels with the files; never store credentials in the archive.
    @{ Tag = $ReleaseTag; Commit = $Commit; Channel = $ReleaseChannel } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $stage 'update-build.json') -Encoding UTF8

    Move-Item -LiteralPath $install -Destination $backup -ErrorAction Stop
    $backedUp = $true
    Move-Item -LiteralPath $stage -Destination $install -ErrorAction Stop
    $swapped = $true
    Start-Process -FilePath (Join-Path $install 'HDREZKA.App.exe') -WorkingDirectory $install -ErrorAction Stop
    Write-UpdateLog "Installed $ReleaseChannel / $ReleaseTag. Backup: $backup"
} catch {
    $errorMessage = $_.Exception.Message
    Write-UpdateLog "FAILED: $errorMessage"
    if ($backedUp) {
        try {
            if (Test-Path -LiteralPath $install) { Remove-Item -LiteralPath $install -Recurse -Force -ErrorAction Stop }
            Move-Item -LiteralPath $backup -Destination $install -ErrorAction Stop
            Write-UpdateLog 'Previous version restored.'
            Start-Process -FilePath (Join-Path $install 'HDREZKA.App.exe') -WorkingDirectory $install
        } catch { Write-UpdateLog "ROLLBACK FAILED: $($_.Exception.Message)"; $errorMessage += " (rollback failed)" }
    }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("Не удалось обновить приложение: $errorMessage`nЖурнал: $logFile",
            'HDREZKA — ошибка обновления', 'OK', 'Error') | Out-Null
    } catch {}
} finally {
    if (Test-Path -LiteralPath $stage) {
        try { Remove-Item -LiteralPath $stage -Recurse -Force } catch {}
    }
}
