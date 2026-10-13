# SpeedFog Save Recovery

$backupsDir = $PSScriptRoot
. "$PSScriptRoot/backup_store.ps1"
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
        try { $idx = [int]$selRaw - 1 } catch { Write-Host "Invalid selection."; Read-Host "Press Enter to exit"; exit 1 }
        if ($idx -lt 0 -or $idx -ge $candidates.Count) {
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

# --- Warn if game is running ---
if (Get-Process -Name eldenring -ErrorAction SilentlyContinue) {
    $confirm = Read-Host "Warning: Elden Ring appears to be running. Restoring while the game is running may not work. Continue? (y/n)"
    if ($confirm -ne 'y') { exit 0 }
}

# --- List available backups ---
$backups = @(Get-SaveBackups $backupsDir)
if ($backups.Count -eq 0) {
    Write-Host "No backups found."
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host "Backups keep being taken until the game closes: if you stayed stuck for a"
Write-Host "while before quitting, the most recent ones already contain the problem."
Write-Host "Pick one from BEFORE it."
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
if (-not $selected) { Write-Host "Invalid selection."; Read-Host "Press Enter to exit"; exit 1 }

# --- Confirm ---
$confirmRestore = Read-Host "Restore #$($selected.Seq) ($(Format-BackupClock $selected.Time))? (y/n) [y]"
if ($confirmRestore -eq '') { $confirmRestore = 'y' }
if ($confirmRestore -ne 'y') {
    Write-Host "Cancelled."
    Read-Host "Press Enter to exit"
    exit 0
}
Write-Host ""

# --- Keep the current save, so a wrong pick can be undone ---
$undoName = ''
if (Test-Path -LiteralPath $savePath) {
    try {
        $undo = New-SaveBackup -BackupsDir $backupsDir -SavePath $savePath -Kind before-restore
        $undoName = $undo.Name
    } catch {
        Write-Host "WARNING: Could not back up your current save: $_"
        $confirmAnyway = Read-Host "Restore anyway? Your current save will be lost. (y/n) [n]"
        if ($confirmAnyway -ne 'y') {
            Write-Host "Cancelled."
            Read-Host "Press Enter to exit"
            exit 0
        }
    }
}

# --- Restore ---
try {
    Expand-Archive -LiteralPath $selected.Path -DestinationPath (Split-Path $savePath -Parent) -Force -ErrorAction Stop
    Add-RestoreLog -BackupsDir $backupsDir -Restored $selected.Name -Undo $undoName
    Write-Host "Restored backup #$($selected.Seq) successfully."
    if ($undoName) {
        Write-Host "Your previous save was kept as backup #$((ConvertFrom-BackupName $undoName).Seq): restore it to undo."
    }
    Write-Host "You can relaunch the game with launch_speedfog.bat."
} catch {
    Write-Host "ERROR: Failed to restore backup: $_"
    if ($undoName) {
        Write-Host "Your previous save is in backup #$((ConvertFrom-BackupName $undoName).Seq): restore it if the game no longer loads."
    }
}

Write-Host ""
Read-Host "Press Enter to exit"
