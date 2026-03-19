# SpeedFog Save Backup Daemon
# Launched by launch_speedfog.bat with the save file path as an argument.

param(
    [Parameter(Mandatory=$true)]
    [string]$SavePath
)

$backupsDir = $PSScriptRoot

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

Log "Save file: $SavePath"

# --- Wait for Elden Ring to start ---
Log "Waiting for Elden Ring to start..."
$waited = 0
$found = $false
while ($waited -lt 60) {
    if (Get-Process -Name eldenring -ErrorAction SilentlyContinue) {
        $found = $true
        break
    }
    Start-Sleep -Seconds 5
    $waited++
}
if (-not $found) {
    Log "ERROR: Timed out waiting for Elden Ring to start. Exiting."
    exit 1
}
Log "Game detected."

# --- Pre-run backup ---
if (Test-Path $SavePath) {
    $ts = Get-Date -Format "yyyy-MM-dd_HH.mm.ss"
    $preRunZip = "$backupsDir/pre-run_$ts.zip"
    try {
        Compress-Archive -Path $SavePath -DestinationPath $preRunZip -Force
        Log "Pre-run backup: $(Split-Path $preRunZip -Leaf)"
    } catch {
        Log "WARNING: Failed to create pre-run backup: $_"
    }
}

Log "Backup daemon started (interval: $interval min, keep: $maxBackups)"

# --- Backup loop ---
$backupCount = 0
while ($true) {
    Start-Sleep -Seconds ($interval * 60)

    if (-not (Get-Process -Name eldenring -ErrorAction SilentlyContinue)) {
        Log "Game exited. Daemon stopping. ($backupCount backups created)"
        exit 0
    }

    if (-not (Test-Path $SavePath)) {
        Log "WARNING: Save file not found, skipping backup."
        continue
    }

    $ts = Get-Date -Format "yyyy-MM-dd_HH.mm.ss"
    $zipName = "ER0000_$ts.zip"
    $zipPath = "$backupsDir/$zipName"

    try {
        Compress-Archive -Path $SavePath -DestinationPath $zipPath -Force
    } catch {
        Log "WARNING: Failed to create backup: $_"
        continue
    }

    $backupCount++

    $zipSizeKB = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
    $zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    if ($zipSizeKB -lt 100) {
        Log "WARNING: $zipName is only $zipSizeKB KB (expected ~3 MB)"
    } else {
        Log "Backup: $zipName ($zipSizeMB MB)"
    }

    # Purge old backups beyond maxBackups
    Get-ChildItem "$backupsDir/ER0000_*.zip" | Sort-Object Name | Select-Object -SkipLast $maxBackups | Remove-Item -Force
}
