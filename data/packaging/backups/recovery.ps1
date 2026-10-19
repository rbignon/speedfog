# SpeedFog Save Recovery

$backupsDir = $PSScriptRoot
. "$PSScriptRoot/backup_store.ps1"

# Every recovery run is traced in backup.log next to the daemon's lines.
function RecoveryLog {
    param([string]$Message)
    Write-BackupLog -BackupsDir $backupsDir -Source Recovery -Message $Message | Out-Null
}
$configPath = "$PSScriptRoot/config.ini"

# --- Parse config.ini for save_path ---
$savePath = $null
if (Test-Path $configPath) {
    foreach ($line in Get-Content $configPath) {
        $line = $line.Trim()
        if ($line -match '^\s*#' -or $line -eq '') { continue }
        if ($line -match '^\s*save_path\s*=\s*(.+)\s*$') { $savePath = $Matches[1].Trim(); break }
    }
}

# --- Auto-detect save file if not configured ---
if (-not $savePath) {
    # Try to detect active Steam user from registry
    try {
        $steamReg = Get-ItemProperty "HKCU:\Software\Valve\Steam\ActiveProcess" -ErrorAction SilentlyContinue
        if ($steamReg -and $steamReg.ActiveUser -and $steamReg.ActiveUser -ne 0) {
            $steamId64 = [long]$steamReg.ActiveUser + 76561197960265728
            $steamSave = "$env:APPDATA\EldenRing\$steamId64\ER0000.sl2"
            if (Test-Path $steamSave) {
                $savePath = $steamSave
            }
        }
    } catch {}
}

if (-not $savePath) {
    # Fallback: scan for save files
    $candidates = @(Get-ChildItem "$env:APPDATA\EldenRing\*\ER0000.sl2" -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 1) {
        $savePath = $candidates[0].FullName
    } elseif ($candidates.Count -eq 0) {
        Write-Host "ERROR: Could not find ER0000.sl2. Please set save_path in backups\config.ini."
        RecoveryLog "ERROR: Could not find ER0000.sl2"
        Read-Host "Press Enter to exit"
        exit 1
    } else {
        $candidates = $candidates | Sort-Object LastWriteTime
        Write-Host "Multiple save files found. Select one:"
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            $mod = $candidates[$i].LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            $label = if ($i -eq $candidates.Count - 1) { " (most recent)" } else { "" }
            Write-Host "  [$($i + 1)] $($candidates[$i].FullName)  [$mod]$label"
        }
        $default = $candidates.Count
        $selRaw = Read-Host "Select save file [$default]"
        if ($selRaw -eq '') { $selRaw = "$default" }
        try { $idx = [int]$selRaw - 1 } catch { $idx = -1 }
        if ($idx -lt 0 -or $idx -ge $candidates.Count) {
            RecoveryLog "Invalid save file selection: '$selRaw'"
            Write-Host "Invalid selection."
            Read-Host "Press Enter to exit"
            exit 1
        }
        $savePath = $candidates[$idx].FullName
    }
}

# --- Header ---
Write-Host "SpeedFog Save Recovery"
Write-Host "======================"
Write-Host ""
Write-Host "Save file: $savePath"
Write-Host ""

$backups = @(Get-SaveBackups $backupsDir)
RecoveryLog "Recovery started (save: $savePath, $($backups.Count) backups)"

# --- Warn if game is running ---
if (Get-Process -Name eldenring -ErrorAction SilentlyContinue) {
    $confirm = Read-Host "Warning: Elden Ring appears to be running. Restoring while the game is running may not work. Continue? (y/n)"
    if ($confirm -ne 'y') { RecoveryLog "Cancelled: Elden Ring is running"; exit 0 }
    RecoveryLog "Elden Ring is running, continuing anyway"
}

# --- List available backups ---
if ($backups.Count -eq 0) {
    Write-Host "No backups found."
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host "A backup is taken every minute while the save changes, until the game closes."
Write-Host "If you stayed stuck for a while before quitting, the most recent ones may"
Write-Host "already contain the problem: pick one from BEFORE it."
if (@($backups | Where-Object { $_.Warp }).Count -gt 0) {
    Write-Host "Backups marked 'warp in progress' were taken during a loading screen:"
    Write-Host "the game cannot load them, pick another one."
}
Write-Host ""
Write-Host "Available backups (newest last):"
Write-Host ""
foreach ($line in Format-BackupList -Backups $backups -Restores @(Read-RestoreLog $backupsDir)) {
    Write-Host $line
}
Write-Host ""
Write-Host "Reminder: pick a backup from BEFORE the problem, the most recent ones may already contain it."

# --- Prompt for selection (by permanent backup number) ---
$default = Get-DefaultBackup $backups
if (-not $default) { $default = $backups[-1] }
$selRaw = (Read-Host "Number of the backup to restore [$($default.Seq)]").Trim().TrimStart('#')
$selected = $null
if ($selRaw -eq '') {
    $selected = $default
} elseif ($selRaw -match '^\d+$') {
    $selected = $backups | Where-Object { $_.Seq -eq [int]$selRaw } | Select-Object -First 1
}
if (-not $selected) {
    RecoveryLog "Invalid selection: '$selRaw'"
    Write-Host "Invalid selection."
    Read-Host "Press Enter to exit"
    exit 1
}

# --- Confirm (a mid-warp backup cannot be loaded: default to no) ---
$confirmDefault = 'y'
$pickNote = ''
if ($selected.Warp) {
    $confirmDefault = 'n'
    $pickNote = ', warp in progress: the game cannot load it'
}
$confirmRestore = Read-Host "Restore #$($selected.Seq) ($(Format-BackupClock $selected.Time)$pickNote)? (y/n) [$confirmDefault]"
if ($confirmRestore -eq '') { $confirmRestore = $confirmDefault }
if ($confirmRestore -ne 'y') {
    RecoveryLog "Cancelled at confirmation of #$($selected.Seq)"
    Write-Host "Cancelled."
    Read-Host "Press Enter to exit"
    exit 0
}
Write-Host ""
$how = ''
if ($selRaw -eq '') { $how = ', default pick' }
RecoveryLog "Restoring #$($selected.Seq) ($($selected.Name)$how)"

# --- Keep the current save, so a wrong pick can be undone ---
$undoName = ''
if (Test-Path -LiteralPath $savePath) {
    try {
        $undo = New-SaveBackup -BackupsDir $backupsDir -SavePath $savePath -Kind before-restore
        $undoName = $undo.Name
    } catch {
        RecoveryLog "WARNING: Could not back up the current save: $_"
        Write-Host "WARNING: Could not back up your current save: $_"
        $confirmAnyway = Read-Host "Restore anyway? Your current save will be lost. (y/n) [n]"
        if ($confirmAnyway -ne 'y') {
            RecoveryLog "Cancelled: no undo backup"
            Write-Host "Cancelled."
            Read-Host "Press Enter to exit"
            exit 0
        }
        RecoveryLog "Restoring without an undo backup (confirmed)"
    }
    if ($undoName) { RecoveryLog "Current save kept as #$((ConvertFrom-BackupName $undoName).Seq) ($undoName)" }
} else {
    RecoveryLog "No current save file to keep"
}

# --- Restore ---
try {
    Expand-Archive -LiteralPath $selected.Path -DestinationPath (Split-Path $savePath -Parent) -Force -ErrorAction Stop
    Add-RestoreLog -BackupsDir $backupsDir -Restored $selected.Name -Undo $undoName
    RecoveryLog "Restored #$($selected.Seq)"
    Write-Host "Restored backup #$($selected.Seq) successfully."
    if ($undoName) {
        Write-Host "Your previous save was kept as backup #$((ConvertFrom-BackupName $undoName).Seq): restore it to undo."
    }
    Write-Host "You can relaunch the game with launch_speedfog.bat."
} catch {
    RecoveryLog "ERROR: Failed to restore #$($selected.Seq): $_"
    Write-Host "ERROR: Failed to restore backup: $_"
    if ($undoName) {
        Write-Host "Your previous save is in backup #$((ConvertFrom-BackupName $undoName).Seq): restore it if the game no longer loads."
    }
}

Write-Host ""
Read-Host "Press Enter to exit"
