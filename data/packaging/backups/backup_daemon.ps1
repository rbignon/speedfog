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
    Write-Host (Write-BackupLog -BackupsDir $backupsDir -Source Backup -Message $Message)
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
# A save written at the start of a fog-gate warp cannot be loaded (see
# Get-SaveSlots in backup_store.ps1): give the loading screen this long to
# end before zipping such a save, tagged -warp, as the game may be stuck.
$midWarpWait = 45

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
$lastHash = ''
# Character slots of the launch-start save and of the last backup: they tell
# the played slot (written since the start) from a stale mid-warp one, and a
# warp in flight from a mid-warp state already backed up.
$startSlots = $null
$lastSlots = $null
if (Test-Path -LiteralPath $SavePath) {
    try {
        $zip = New-SaveBackup -BackupsDir $backupsDir -SavePath $SavePath -Kind start -Launch $launch
        $lastHash = $zip.SaveHash
        $startSlots = $zip.Slots
        $lastSlots = $zip.Slots
        $note = ''
        if ($zip.Warp) { $note = ' (warp in progress: the game cannot load it)' }
        Log "Launch $launch start backup: $($zip.Name)$note"
    } catch {
        Log "WARNING: Failed to create start-of-launch backup: $_"
    }
}
Remove-OldBackups -BackupsDir $backupsDir -Launch $launch -MaxBackups $maxBackups

Log "Backup daemon started (launch: $launch, interval: $interval min, keep: $maxBackups)"

# --- Backup loop ---
$backupCount = 0
$skipCount = 0
$skipLogged = $false
while ($true) {
    # Wait one interval; returns as soon as the game exits. Suppressed errors:
    # timeout elapsed, or the PID was already gone when the wait started.
    Wait-Process -Id $game.Id -Timeout ($interval * 60) -ErrorAction SilentlyContinue

    if ($game.HasExited) {
        Log "Game exited. Daemon stopping. ($backupCount backups created, $skipCount skipped: save unchanged)"
        exit 0
    }

    if (-not (Test-Path -LiteralPath $SavePath)) {
        Log "WARNING: Save file not found, skipping backup."
        $skipLogged = $false
        continue
    }

    try {
        $zip = New-SettledBackup -BackupsDir $backupsDir -SavePath $SavePath -Kind periodic -Launch $launch `
            -UnlessHash $lastHash -WaitSeconds $midWarpWait -PollSeconds 5 -ShouldStop { $game.HasExited } `
            -StartSlots $startSlots -LastSlots $lastSlots
    } catch {
        Log "WARNING: Failed to create backup: $_"
        $skipLogged = $false
        continue
    }

    # The game exited while waiting for a mid-warp save to settle: the next
    # loop turn reports the exit. A zip created in the meantime is logged.
    if ($null -eq $zip -and $game.HasExited) { continue }

    # Unchanged save (e.g. game stuck on a loading screen): no new backup,
    # logged once per unchanged streak.
    if (-not $zip) {
        $skipCount++
        if (-not $skipLogged) {
            Log "Save unchanged since the last backup, skipping until it changes."
            $skipLogged = $true
        }
        continue
    }
    $skipLogged = $false
    $lastHash = $zip.SaveHash
    $lastSlots = $zip.Slots
    # Start backup failed: any later copy serves as the baseline (the played
    # slot's checksum changes at every save, a stale one's never does).
    if ($null -eq $startSlots) { $startSlots = $zip.Slots }
    $backupCount++

    $zipSizeKB = [math]::Round($zip.Length / 1KB, 1)
    $zipSizeMB = [math]::Round($zip.Length / 1MB, 1)
    $note = ''
    if ($zip.WaitedSeconds -gt 0) { $note = ", loading screen: waited $($zip.WaitedSeconds) s" }
    if ($zip.Warp) { $note += ', warp in progress: the game cannot load it' }
    if ($zipSizeKB -lt 100) {
        Log "WARNING: $($zip.Name) is only $zipSizeKB KB (expected ~3 MB$note)"
    } else {
        Log "Backup: $($zip.Name) ($zipSizeMB MB$note)"
    }

    Remove-OldBackups -BackupsDir $backupsDir -Launch $launch -MaxBackups $maxBackups
}
