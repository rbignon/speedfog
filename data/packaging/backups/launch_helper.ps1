# SpeedFog Launch Helper
# Detects the Elden Ring save file and starts the backup daemon.
# Called by launch_speedfog.bat.

$configPath = "$PSScriptRoot\config.ini"

# --- Parse config.ini ---
$enabled = $true
$savePath = $null

if (Test-Path $configPath) {
    foreach ($line in Get-Content $configPath) {
        $line = $line.Trim()
        if ($line -match '^\s*#' -or $line -eq '') { continue }
        if ($line -match '^\s*enabled\s*=\s*(.+)\s*$') {
            $val = $Matches[1].Trim()
            if ($val -eq 'false' -or $val -eq 'False' -or $val -eq 'FALSE') {
                $enabled = $false
            }
        }
        if ($line -match '^\s*save_path\s*=\s*(.+)\s*$') {
            $savePath = $Matches[1].Trim()
        }
    }
}

# --- Exit early if backups disabled ---
if (-not $enabled) {
    exit 0
}

# --- Resolve save path ---
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
        Write-Host "WARNING: Could not auto-detect Elden Ring save file."
        Write-Host "To enable backups, set save_path in backups\config.ini"
        exit 0
    } else {
        $candidates = $candidates | Sort-Object LastWriteTime
        Write-Host "Multiple Elden Ring save files found:"
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            $mod = $candidates[$i].LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            $label = if ($i -eq $candidates.Count - 1) { " (most recent)" } else { "" }
            Write-Host "  [$($i + 1)] $($candidates[$i].FullName)  [$mod]$label"
        }
        $default = $candidates.Count
        $selRaw = Read-Host "Select save file [$default]"
        if ($selRaw -eq '') { $selRaw = "$default" }
        try { $idx = [int]$selRaw - 1 } catch { Write-Host "Invalid selection. Skipping backups."; exit 0 }
        if ($idx -lt 0 -or $idx -ge $candidates.Count) {
            Write-Host "Invalid selection. Skipping backups."
            exit 0
        }
        $savePath = $candidates[$idx].FullName
    }
}

# --- Start backup daemon ---
$daemonPath = "$PSScriptRoot\backup_daemon.ps1"
if (-not (Test-Path $daemonPath)) {
    Write-Host "WARNING: backup_daemon.ps1 not found. Skipping backups."
    exit 0
}

Write-Host "Starting backup daemon for: $savePath"
Start-Process -WindowStyle Minimized powershell -ArgumentList "-ExecutionPolicy Bypass -NoProfile -File ""$daemonPath"" -SavePath ""$savePath"""
