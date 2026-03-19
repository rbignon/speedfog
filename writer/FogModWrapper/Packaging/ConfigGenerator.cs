namespace FogModWrapper.Packaging;

/// <summary>
/// Generates ModEngine 2 configuration files and launcher scripts.
/// </summary>
public static class ConfigGenerator
{
    /// <summary>
    /// Writes the ModEngine 2 TOML configuration file.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    /// <param name="modPath">The relative path to the mod folder (default: "mods/fogmod").</param>
    /// <param name="itemRandomizerEnabled">Whether item randomizer was used (adds RandomizerHelper.dll).</param>
    public static void WriteModEngineConfig(string outputDir, string modPath = "mods/fogmod", bool itemRandomizerEnabled = false)
    {
        var configPath = Path.Combine(outputDir, "config_speedfog.toml");

        var externalDlls = new List<string> { @"lib\\RandomizerCrashFix.dll" };
        if (itemRandomizerEnabled)
        {
            externalDlls.Add(@"lib\\RandomizerHelper.dll");
        }

        var dllsString = string.Join(",\n    ", externalDlls.Select(d => $"\"{d}\""));

        // Build mods list - fogmod first (higher priority), then itemrando for non-merged files
        // MergedMods merges regulation.bin and EMEVD, but map/msg/sfx need separate loading
        var modsLines = new List<string>
        {
            $"    {{ enabled = true, name = \"fogmod\", path = \"{modPath}\" }}"
        };
        if (itemRandomizerEnabled)
        {
            modsLines.Add("    { enabled = true, name = \"itemrando\", path = \"mods/itemrando\" }");
        }

        var config = $@"# SpeedFog ModEngine 2 Configuration
# Auto-generated - do not edit manually

[modengine]
debug = false
external_dlls = [
    {dllsString},
]

[extension.mod_loader]
enabled = true
loose_params = false
mods = [
{string.Join(",\n", modsLines)}
]
";

        File.WriteAllText(configPath, config);
    }

    /// <summary>
    /// Writes the Windows batch launcher script.
    /// Runs the launch helper (save detection + backup daemon) before launching ModEngine.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteBatchLauncher(string outputDir)
    {
        var batPath = Path.Combine(outputDir, "launch_speedfog.bat");

        var script = @"@echo off
REM SpeedFog Launcher for Elden Ring
REM Auto-generated - do not edit manually

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File ""%SCRIPT_DIR%backups\launch_helper.ps1""

REM Launch ModEngine with our config
""%SCRIPT_DIR%ModEngine\modengine2_launcher.exe"" -t er -c ""%SCRIPT_DIR%config_speedfog.toml""

endlocal
";

        File.WriteAllText(batPath, script);
    }

    /// <summary>
    /// Writes the PowerShell launch helper script that detects the save file
    /// and starts the backup daemon before the game launches.
    /// Called by launch_speedfog.bat before ModEngine starts.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteLaunchHelperPs1(string outputDir)
    {
        var ps1Path = Path.Combine(outputDir, "backups", "launch_helper.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(ps1Path)!);

        var script = @"# SpeedFog Launch Helper
# Auto-generated - do not edit manually
# Detects the Elden Ring save file and starts the backup daemon.
# Called by launch_speedfog.bat.

$configPath = ""$PSScriptRoot\config.ini""

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
    $candidates = @(Get-ChildItem ""$env:APPDATA\EldenRing\*\ER0000.sl2"" -ErrorAction SilentlyContinue)

    if ($candidates.Count -eq 1) {
        $savePath = $candidates[0].FullName
    } elseif ($candidates.Count -eq 0) {
        Write-Host ""WARNING: Could not auto-detect Elden Ring save file.""
        Write-Host ""To enable backups, set save_path in backups\config.ini""
        exit 0
    } else {
        Write-Host ""Multiple Elden Ring save files found:""
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            Write-Host ""  [$($i + 1)] $($candidates[$i].FullName)""
        }
        $sel = Read-Host ""Select save file""
        $idx = [int]$sel - 1
        if ($idx -lt 0 -or $idx -ge $candidates.Count) {
            Write-Host ""Invalid selection. Skipping backups.""
            exit 0
        }
        $savePath = $candidates[$idx].FullName
    }
}

# --- Start backup daemon ---
$daemonPath = ""$PSScriptRoot\backup_daemon.ps1""
if (-not (Test-Path $daemonPath)) {
    Write-Host ""WARNING: backup_daemon.ps1 not found. Skipping backups.""
    exit 0
}

Write-Host ""Starting backup daemon for: $savePath""
Start-Process -WindowStyle Minimized powershell -ArgumentList ""-ExecutionPolicy Bypass -NoProfile -File """"$daemonPath"""" -SavePath """"$savePath""""""
";

        File.WriteAllText(ps1Path, script);
    }

    /// <summary>
    /// Writes the Linux/Proton shell launcher script.
    /// Written to linux/launch_speedfog.sh; references paths via $OUTPUT_DIR (parent of linux/).
    /// Reads backups/config.ini for enabled/save_path, auto-detects save under Proton prefix,
    /// launches the backup daemon, then launches ModEngine via Wine.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteShellLauncher(string outputDir)
    {
        var shPath = Path.Combine(outputDir, "linux", "launch_speedfog.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(shPath)!);

        var script = @"#!/bin/bash
# SpeedFog Launcher for Elden Ring (Linux/Proton)
# Auto-generated - do not edit manually

SCRIPT_DIR=""$(cd ""$(dirname ""${BASH_SOURCE[0]}"")"" && pwd)""
OUTPUT_DIR=""$SCRIPT_DIR/..""

# --- Parse backups/config.ini ---
enabled=true
SAVE_PATH=""""
config_path=""$OUTPUT_DIR/backups/config.ini""
if [ -f ""$config_path"" ]; then
    _parsed=$(grep -v '^\s*#' ""$config_path"" | grep -v '^\s*$')
    _enabled=$(echo ""$_parsed"" | grep '^enabled=' | cut -d= -f2 | tr -d '[:space:]')
    _save_path=$(echo ""$_parsed"" | grep '^save_path=' | cut -d= -f2- | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    if [ -n ""$_enabled"" ]; then enabled=""$_enabled""; fi
    if [ -n ""$_save_path"" ]; then SAVE_PATH=""$_save_path""; fi
fi

# --- Save detection and backup daemon ---
if [ ""$enabled"" != ""false"" ]; then
    if [ -z ""$SAVE_PATH"" ]; then
        PROTON_APPDATA=""$HOME/.local/share/Steam/steamapps/compatdata/1245620/pfx/drive_c/users/steamuser/AppData/Roaming/EldenRing""
        mapfile -t _candidates < <(ls ""$PROTON_APPDATA""/*/ER0000.sl2 2>/dev/null)
        _count=${#_candidates[@]}
        if [ ""$_count"" -eq 0 ]; then
            echo ""WARNING: Could not auto-detect Elden Ring save file.""
            echo ""To enable backups, set save_path in backups/config.ini""
        elif [ ""$_count"" -eq 1 ]; then
            SAVE_PATH=""${_candidates[0]}""
        else
            echo ""Multiple Elden Ring save files found:""
            for _i in ""${!_candidates[@]}""; do
                echo ""  [$((_i + 1))] ${_candidates[$_i]}""
            done
            read -r -p ""Select save file: "" _sel
            _idx=$((_sel - 1))
            if ! [[ ""$_sel"" =~ ^[0-9]+$ ]] || [ ""$_idx"" -lt 0 ] || [ ""$_idx"" -ge ""$_count"" ]; then
                echo ""Invalid selection. Skipping backups.""
            else
                SAVE_PATH=""${_candidates[$_idx]}""
            fi
        fi
    fi

    if [ -n ""$SAVE_PATH"" ]; then
        bash ""$SCRIPT_DIR/backup_daemon.sh"" ""$SAVE_PATH"" &
    fi
fi

# --- Launch ModEngine ---
wine ""$OUTPUT_DIR/ModEngine/modengine2_launcher.exe"" -t er -c ""$OUTPUT_DIR/config_speedfog.toml""
";

        File.WriteAllText(shPath, script);

        // Make executable on Unix systems
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// Writes the backup configuration file with all values commented out.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteBackupConfig(string outputDir)
    {
        var configPath = Path.Combine(outputDir, "backups", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var config = @"# SpeedFog Save Backup Configuration
# Uncomment and edit to override defaults

# Set to false to disable automatic backups entirely
# enabled=true

# Path to save file (auto-detected if not set)
# save_path=C:\Users\...\AppData\Roaming\EldenRing\...\ER0000.sl2

# Backup interval in minutes (default: 1)
# interval=1

# Number of backups to keep (default: 10)
# max_backups=10
";

        File.WriteAllText(configPath, config);
    }

    /// <summary>
    /// Writes the Windows backup daemon PowerShell script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteBackupDaemonPs1(string outputDir)
    {
        var ps1Path = Path.Combine(outputDir, "backups", "backup_daemon.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(ps1Path)!);

        var script = @"# SpeedFog Save Backup Daemon
# Auto-generated - do not edit manually
# Launched by launch_speedfog.bat with the save file path as an argument.

param(
    [Parameter(Mandatory=$true)]
    [string]$SavePath
)

$backupsDir = $PSScriptRoot

# --- Logging ---
function Log {
    param([string]$Message)
    $line = ""[SpeedFog Backup] $Message""
    Write-Host $line
    Add-Content -Path ""$backupsDir/backup.log"" -Value $line
}

# --- Parse config.ini ---
$interval = 1
$maxBackups = 10
$configPath = ""$PSScriptRoot/config.ini""
if (Test-Path $configPath) {
    foreach ($line in Get-Content $configPath) {
        $line = $line.Trim()
        if ($line -match '^\s*#' -or $line -eq '') { continue }
        if ($line -match '^\s*interval\s*=\s*(\d+)\s*$') { $interval = [int]$Matches[1] }
        if ($line -match '^\s*max_backups\s*=\s*(\d+)\s*$') { $maxBackups = [int]$Matches[1] }
    }
}

Log ""Save file: $SavePath""

# --- Wait for Elden Ring to start ---
Log ""Waiting for Elden Ring to start...""
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
    Log ""ERROR: Timed out waiting for Elden Ring to start. Exiting.""
    exit 1
}
Log ""Game detected.""

# --- Pre-run backup ---
if (Test-Path $SavePath) {
    $ts = Get-Date -Format ""yyyy-MM-dd_HH.mm.ss""
    $preRunZip = ""$backupsDir/pre-run_$ts.zip""
    try {
        Compress-Archive -Path $SavePath -DestinationPath $preRunZip -Force
        Log ""Pre-run backup: $(Split-Path $preRunZip -Leaf)""
    } catch {
        Log ""WARNING: Failed to create pre-run backup: $_""
    }
}

Log ""Backup daemon started (interval: $interval min, keep: $maxBackups)""

# --- Backup loop ---
$backupCount = 0
while ($true) {
    Start-Sleep -Seconds ($interval * 60)

    if (-not (Get-Process -Name eldenring -ErrorAction SilentlyContinue)) {
        Log ""Game exited. Daemon stopping. ($backupCount backups created)""
        exit 0
    }

    if (-not (Test-Path $SavePath)) {
        Log ""WARNING: Save file not found, skipping backup.""
        continue
    }

    $ts = Get-Date -Format ""yyyy-MM-dd_HH.mm.ss""
    $zipName = ""ER0000_$ts.zip""
    $zipPath = ""$backupsDir/$zipName""

    try {
        Compress-Archive -Path $SavePath -DestinationPath $zipPath -Force
    } catch {
        Log ""WARNING: Failed to create backup: $_""
        continue
    }

    $backupCount++

    $zipSizeKB = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
    $zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    if ($zipSizeKB -lt 100) {
        Log ""WARNING: $zipName is only $zipSizeKB KB (expected ~3 MB)""
    } else {
        Log ""Backup: $zipName ($zipSizeMB MB)""
    }

    # Purge old backups beyond maxBackups
    Get-ChildItem ""$backupsDir/ER0000_*.zip"" | Sort-Object Name | Select-Object -SkipLast $maxBackups | Remove-Item -Force
}
";

        File.WriteAllText(ps1Path, script);
    }

    /// <summary>
    /// Writes the Linux backup daemon bash script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteBackupDaemonSh(string outputDir)
    {
        var shPath = Path.Combine(outputDir, "linux", "backup_daemon.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(shPath)!);

        var script = @"#!/bin/bash
# SpeedFog Save Backup Daemon
# Auto-generated - do not edit manually
# Launched by launch_speedfog.sh with the save file path as an argument.

SAVE_PATH=""$1""
if [ -z ""$SAVE_PATH"" ]; then
    echo ""Usage: $0 <save_path>""
    exit 1
fi

SCRIPT_DIR=""$(cd ""$(dirname ""${BASH_SOURCE[0]}"")"" && pwd)""
BACKUPS_DIR=""$SCRIPT_DIR/../backups""

# --- Logging ---
log() {
    local line=""[SpeedFog Backup] $1""
    echo ""$line""
    echo ""$line"" >> ""$BACKUPS_DIR/backup.log""
}

# --- Parse config.ini ---
interval=1
max_backups=10
config_path=""$BACKUPS_DIR/config.ini""
if [ -f ""$config_path"" ]; then
    _parsed=$(grep -v '^\s*#' ""$config_path"" | grep -v '^\s*$')
    _interval=$(echo ""$_parsed"" | grep '^interval=' | cut -d= -f2 | tr -d '[:space:]')
    _max_backups=$(echo ""$_parsed"" | grep '^max_backups=' | cut -d= -f2 | tr -d '[:space:]')
    if [ -n ""$_interval"" ]; then interval=""$_interval""; fi
    if [ -n ""$_max_backups"" ]; then max_backups=""$_max_backups""; fi
fi

log ""Save file: $SAVE_PATH""

# --- Wait for Elden Ring to start ---
log ""Waiting for Elden Ring to start...""
waited=0
found=0
while [ $waited -lt 60 ]; do
    if pgrep -x eldenring.exe > /dev/null 2>&1; then
        found=1
        break
    fi
    sleep 5
    waited=$((waited + 1))
done
if [ $found -eq 0 ]; then
    log ""ERROR: Timed out waiting for Elden Ring to start. Exiting.""
    exit 1
fi
log ""Game detected.""

# --- Pre-run backup ---
if [ -f ""$SAVE_PATH"" ]; then
    ts=$(date +%Y-%m-%d_%H.%M.%S)
    pre_run_zip=""$BACKUPS_DIR/pre-run_$ts.zip""
    if ! zip -j ""$pre_run_zip"" ""$SAVE_PATH""; then
        log ""WARNING: Failed to create pre-run backup.""
    else
        log ""Pre-run backup: pre-run_$ts.zip""
    fi
fi

log ""Backup daemon started (interval: $interval min, keep: $max_backups)""

# --- Backup loop ---
backup_count=0
while true; do
    sleep $((interval * 60))

    if ! pgrep -x eldenring.exe > /dev/null 2>&1; then
        log ""Game exited. Daemon stopping. ($backup_count backups created)""
        exit 0
    fi

    if [ ! -f ""$SAVE_PATH"" ]; then
        log ""WARNING: Save file not found, skipping backup.""
        continue
    fi

    ts=$(date +%Y-%m-%d_%H.%M.%S)
    zip_name=""ER0000_$ts.zip""
    zip_path=""$BACKUPS_DIR/$zip_name""

    if ! zip -j ""$zip_path"" ""$SAVE_PATH""; then
        log ""WARNING: Failed to create backup: $zip_name""
        continue
    fi

    backup_count=$((backup_count + 1))

    zip_size=$(stat -c%s ""$zip_path"")
    if [ ""$zip_size"" -lt 102400 ]; then
        zip_size_kb=$((zip_size / 1024))
        log ""WARNING: $zip_name is only ${zip_size_kb} KB (expected ~3 MB)""
    else
        zip_size_mb=$(echo ""scale=1; $zip_size / 1048576"" | bc)
        log ""Backup: $zip_name ($zip_size_mb MB)""
    fi

    # Purge old backups beyond max_backups
    ls -1t ""$BACKUPS_DIR""/ER0000_*.zip 2>/dev/null | tail -n +$((max_backups + 1)) | xargs -r rm -f
done
";

        File.WriteAllText(shPath, script);

        // Make executable on Unix systems
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// Writes the save recovery PowerShell script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteRecoveryPs1(string outputDir)
    {
        var ps1Path = Path.Combine(outputDir, "backups", "recovery.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(ps1Path)!);

        var script = @"# SpeedFog Save Recovery
# Auto-generated - do not edit manually

$backupsDir = $PSScriptRoot
$configPath = ""$PSScriptRoot/config.ini""

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
    $candidates = @(Get-ChildItem ""$env:APPDATA\EldenRing\*\ER0000.sl2"" -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 1) {
        $savePath = $candidates[0].FullName
    } elseif ($candidates.Count -eq 0) {
        Write-Host ""ERROR: Could not find ER0000.sl2. Please set save_path in backups\config.ini.""
        Read-Host ""Press Enter to exit""
        exit 1
    } else {
        Write-Host ""Multiple save files found. Select one:""
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            Write-Host ""  [$i] $($candidates[$i].FullName)""
        }
        $sel = Read-Host ""Enter number""
        $savePath = $candidates[[int]$sel].FullName
    }
}

# --- Header ---
Write-Host ""SpeedFog Save Recovery""
Write-Host ""======================""
Write-Host """"
Write-Host ""Save file: $savePath""
Write-Host """"

# --- Warn if game is running ---
if (Get-Process -Name eldenring -ErrorAction SilentlyContinue) {
    $confirm = Read-Host ""Warning: Elden Ring appears to be running. Restoring while the game is running may not work. Continue? (y/n)""
    if ($confirm -ne 'y') { exit 0 }
}

# --- List available backups ---
$zips = @(Get-ChildItem ""$backupsDir\*.zip"" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
if ($zips.Count -eq 0) {
    Write-Host ""No backups found.""
    Read-Host ""Press Enter to exit""
    exit 0
}

Write-Host ""Available backups (newest last):""
Write-Host """"
$maxIdx = $zips.Count - 1
for ($i = 0; $i -lt $zips.Count; $i++) {
    $idx = $maxIdx - $i
    $name = $zips[$i].Name
    $annotation = """"
    if ($name -match '^pre-run_') { $annotation = ""  (Pre-run backup)"" }
    if ($idx -eq 0) {
        if ($annotation -ne """") { $annotation = ""$annotation (most recent)"" }
        else { $annotation = ""  (most recent)"" }
    }
    Write-Host ""  [$idx] $name$annotation""
}
Write-Host """"

# --- Prompt for selection ---
$selRaw = Read-Host ""Select backup to restore [0]""
if ($selRaw -eq '') { $selRaw = '0' }
try { $selNum = [int]$selRaw } catch { Write-Host ""Invalid selection.""; Read-Host ""Press Enter to exit""; exit 1 }
if ($selNum -lt 0 -or $selNum -gt $maxIdx) { Write-Host ""Invalid selection.""; Read-Host ""Press Enter to exit""; exit 1 }
$selIdx = $maxIdx - $selNum
$zipPath = $zips[$selIdx].FullName
$zipName = $zips[$selIdx].Name

# --- Confirm ---
$confirmRestore = Read-Host ""Restore $zipName? (y/n) [y]""
if ($confirmRestore -eq '') { $confirmRestore = 'y' }
if ($confirmRestore -ne 'y') {
    Write-Host ""Cancelled.""
    Read-Host ""Press Enter to exit""
    exit 0
}
Write-Host """"

# --- Restore ---
try {
    Expand-Archive -Path $zipPath -DestinationPath (Split-Path $savePath -Parent) -Force
    Write-Host ""Restored successfully.""
    Write-Host ""You can relaunch the game with launch_speedfog.bat.""
} catch {
    Write-Host ""ERROR: Failed to restore backup: $_""
}

Write-Host """"
Read-Host ""Press Enter to exit""
";

        File.WriteAllText(ps1Path, script);
    }

    /// <summary>
    /// Writes the recovery batch wrapper script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteRecoveryBat(string outputDir)
    {
        var batPath = Path.Combine(outputDir, "recovery.bat");

        var script = "@powershell -ExecutionPolicy Bypass -NoProfile -File \"%~dp0backups\\recovery.ps1\"\r\n";

        File.WriteAllText(batPath, script);
    }

    /// <summary>
    /// Writes the Linux save recovery bash script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteRecoverySh(string outputDir)
    {
        var shPath = Path.Combine(outputDir, "linux", "recovery.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(shPath)!);

        var script = @"#!/bin/bash
# SpeedFog Save Recovery
# Auto-generated - do not edit manually

SCRIPT_DIR=""$(cd ""$(dirname ""${BASH_SOURCE[0]}"")"" && pwd)""
BACKUPS_DIR=""$SCRIPT_DIR/../backups""

# --- Parse config.ini for save_path ---
SAVE_PATH=""""
config_path=""$BACKUPS_DIR/config.ini""
if [ -f ""$config_path"" ]; then
    _parsed=$(grep -v '^\s*#' ""$config_path"" | grep -v '^\s*$')
    _save_path=$(echo ""$_parsed"" | grep '^save_path=' | cut -d= -f2- | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    if [ -n ""$_save_path"" ]; then SAVE_PATH=""$_save_path""; fi
fi

# --- Auto-detect save file if not configured ---
if [ -z ""$SAVE_PATH"" ]; then
    PROTON_APPDATA=""$HOME/.local/share/Steam/steamapps/compatdata/1245620/pfx/drive_c/users/steamuser/AppData/Roaming/EldenRing""
    mapfile -t candidates < <(ls ""$PROTON_APPDATA""/*/ER0000.sl2 2>/dev/null)
    count=${#candidates[@]}
    if [ ""$count"" -eq 0 ]; then
        echo ""ERROR: Could not find ER0000.sl2. Please set save_path in backups/config.ini.""
        read -r -p ""Press Enter to exit""
        exit 1
    elif [ ""$count"" -eq 1 ]; then
        SAVE_PATH=""${candidates[0]}""
    else
        echo ""Multiple save files found. Select one:""
        for i in ""${!candidates[@]}""; do
            echo ""  [$i] ${candidates[$i]}""
        done
        read -r -p ""Enter number: "" sel
        SAVE_PATH=""${candidates[$sel]}""
    fi
fi

# --- Header ---
echo ""SpeedFog Save Recovery""
echo ""======================""
echo """"
echo ""Save file: $SAVE_PATH""
echo """"

# --- Warn if game is running ---
if pgrep -x eldenring.exe > /dev/null 2>&1; then
    read -r -p ""Warning: Elden Ring appears to be running. Restoring while the game is running may not work. Continue? (y/n) "" confirm_run
    if [ ""$confirm_run"" != ""y"" ]; then exit 0; fi
fi

# --- List available backups ---
mapfile -t zips < <(ls -1t ""$BACKUPS_DIR""/*.zip 2>/dev/null | tac)
if [ ${#zips[@]} -eq 0 ]; then
    echo ""No backups found.""
    read -r -p ""Press Enter to exit""
    exit 0
fi

echo ""Available backups (newest last):""
echo """"
max_idx=$(( ${#zips[@]} - 1 ))
for i in ""${!zips[@]}""; do
    idx=$(( max_idx - i ))
    name=$(basename ""${zips[$i]}"")
    annotation=""""
    if [[ ""$name"" == pre-run_* ]]; then annotation=""  (Pre-run backup)""; fi
    if [ ""$idx"" -eq 0 ]; then
        if [ -n ""$annotation"" ]; then annotation=""$annotation (most recent)""
        else annotation=""  (most recent)""; fi
    fi
    echo ""  [$idx] $name$annotation""
done
echo """"

# --- Prompt for selection ---
read -r -p ""Select backup to restore [0]: "" sel_raw
if [ -z ""$sel_raw"" ]; then sel_raw=0; fi
if ! [[ ""$sel_raw"" =~ ^[0-9]+$ ]] || [ ""$sel_raw"" -gt ""$max_idx"" ]; then
    echo ""Invalid selection.""
    read -r -p ""Press Enter to exit""
    exit 1
fi
sel_idx=$(( max_idx - sel_raw ))
zip_path=""${zips[$sel_idx]}""
zip_name=$(basename ""$zip_path"")

# --- Confirm ---
read -r -p ""Restore $zip_name? (y/n) [y]: "" confirm_restore
if [ -z ""$confirm_restore"" ]; then confirm_restore=y; fi
if [ ""$confirm_restore"" != ""y"" ]; then
    echo ""Cancelled.""
    read -r -p ""Press Enter to exit""
    exit 0
fi
echo """"

# --- Restore ---
save_dir=""$(dirname ""$SAVE_PATH"")""
if unzip -o -j ""$zip_path"" -d ""$save_dir""; then
    echo ""Restored successfully.""
    echo ""You can relaunch the game with linux/launch_speedfog.sh.""
else
    echo ""ERROR: Failed to restore backup.""
fi

echo """"
read -r -p ""Press Enter to exit""
";

        File.WriteAllText(shPath, script);

        // Make executable on Unix systems
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
