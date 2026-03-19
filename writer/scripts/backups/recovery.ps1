# SpeedFog Save Recovery

$backupsDir = $PSScriptRoot
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
$zips = @(Get-ChildItem "$backupsDir\*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
if ($zips.Count -eq 0) {
    Write-Host "No backups found."
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host "Available backups (newest last):"
Write-Host ""
for ($i = 0; $i -lt $zips.Count; $i++) {
    $num = $i + 1
    $name = $zips[$i].Name
    $annotation = ""
    if ($name -match '^pre-run_') { $annotation = "  (Pre-run backup)" }
    if ($i -eq $zips.Count - 1) {
        if ($annotation -ne "") { $annotation = "$annotation (most recent)" }
        else { $annotation = "  (most recent)" }
    }
    Write-Host "  [$num] $name$annotation"
}
Write-Host ""

# --- Prompt for selection ---
$default = $zips.Count
$selRaw = Read-Host "Select backup to restore [$default]"
if ($selRaw -eq '') { $selRaw = "$default" }
try { $selNum = [int]$selRaw } catch { Write-Host "Invalid selection."; Read-Host "Press Enter to exit"; exit 1 }
if ($selNum -lt 1 -or $selNum -gt $zips.Count) { Write-Host "Invalid selection."; Read-Host "Press Enter to exit"; exit 1 }
$selIdx = $selNum - 1
$zipPath = $zips[$selIdx].FullName
$zipName = $zips[$selIdx].Name

# --- Confirm ---
$confirmRestore = Read-Host "Restore $zipName? (y/n) [y]"
if ($confirmRestore -eq '') { $confirmRestore = 'y' }
if ($confirmRestore -ne 'y') {
    Write-Host "Cancelled."
    Read-Host "Press Enter to exit"
    exit 0
}
Write-Host ""

# --- Restore ---
try {
    Expand-Archive -Path $zipPath -DestinationPath (Split-Path $savePath -Parent) -Force
    Write-Host "Restored successfully."
    Write-Host "You can relaunch the game with launch_speedfog.bat."
} catch {
    Write-Host "ERROR: Failed to restore backup: $_"
}

Write-Host ""
Read-Host "Press Enter to exit"
