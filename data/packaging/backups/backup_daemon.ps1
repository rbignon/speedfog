# SpeedFog Save Backup Daemon
# Launched by launch_speedfog.bat with the save file path as an argument.

param(
    [Parameter(Mandatory=$true)]
    [string]$SavePath
)

$backupsDir = $PSScriptRoot
. "$PSScriptRoot/backup_store.ps1"

# --- Logging ---
function Log {
    param([string]$Message)
    $line = "[SpeedFog Backup] $Message"
    Write-Host $line
    Add-Content -Path "$backupsDir/backup.log" -Value $line
}

# --- Parse config.ini ---
$interval = 1
$maxBackups = 10
$configPath = "$PSScriptRoot/config.ini"
if (Test-Path $configPath) {
    foreach ($line in Get-Content $configPath) {
        $line = $line.Trim()
        if ($line -match '^\s*#' -or $line -eq '') { continue }
        if ($line -match '^\s*interval\s*=\s*(\d+)\s*$') { $interval = [int]$Matches[1] }
        if ($line -match '^\s*max_backups\s*=\s*(\d+)\s*$') { $maxBackups = [int]$Matches[1] }
    }
}
# Wait-Process -Timeout rejects values above 32767 seconds; clamp to sane bounds.
if ($interval -lt 1) { $interval = 1 } elseif ($interval -gt 540) { $interval = 540 }

Log "Save file: $SavePath"

# --- Wait for Elden Ring to start ---
Log "Waiting for Elden Ring to start..."
$waited = 0
$game = $null
while ($waited -lt 60) {
    $game = Get-Process -Name eldenring -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($game) { break }
    Start-Sleep -Seconds 5
    $waited++
}
if (-not $game) {
    Log "ERROR: Timed out waiting for Elden Ring to start. Exiting."
    exit 1
}
Log "Game detected."

# --- Start-of-launch backup ---
# Each daemon run is one launch; its backups are grouped under this number in
# the recovery menu and purged independently of the other launches.
$launch = Get-NextLaunchNumber $backupsDir
if (Test-Path -LiteralPath $SavePath) {
    try {
        $zip = New-SaveBackup -BackupsDir $backupsDir -SavePath $SavePath -Kind start -Launch $launch
        Log "Launch $launch start backup: $($zip.Name)"
    } catch {
        Log "WARNING: Failed to create start-of-launch backup: $_"
    }
}
Remove-OldBackups -BackupsDir $backupsDir -Launch $launch -MaxBackups $maxBackups

Log "Backup daemon started (launch: $launch, interval: $interval min, keep: $maxBackups)"

# --- Backup loop ---
$backupCount = 0
while ($true) {
    # Wait one interval; returns as soon as the game exits. Suppressed errors:
    # timeout elapsed, or the PID was already gone when the wait started.
    Wait-Process -Id $game.Id -Timeout ($interval * 60) -ErrorAction SilentlyContinue

    if ($game.HasExited) {
        Log "Game exited. Daemon stopping. ($backupCount backups created)"
        exit 0
    }

    if (-not (Test-Path $SavePath)) {
        Log "WARNING: Save file not found, skipping backup."
        continue
    }

    try {
        $zip = New-SaveBackup -BackupsDir $backupsDir -SavePath $SavePath -Kind periodic -Launch $launch
    } catch {
        Log "WARNING: Failed to create backup: $_"
        continue
    }

    $backupCount++

    $zipSizeKB = [math]::Round($zip.Length / 1KB, 1)
    $zipSizeMB = [math]::Round($zip.Length / 1MB, 1)
    if ($zipSizeKB -lt 100) {
        Log "WARNING: $($zip.Name) is only $zipSizeKB KB (expected ~3 MB)"
    } else {
        Log "Backup: $($zip.Name) ($zipSizeMB MB)"
    }

    Remove-OldBackups -BackupsDir $backupsDir -Launch $launch -MaxBackups $maxBackups
}
