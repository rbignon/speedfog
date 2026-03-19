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

REM Launch ModEngine with our config
""%SCRIPT_DIR%ModEngine\modengine2_launcher.exe"" -t er -c ""%SCRIPT_DIR%config_speedfog.toml""

endlocal
";

        File.WriteAllText(batPath, script);
    }

    /// <summary>
    /// Writes the Linux/Proton shell launcher script.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void WriteShellLauncher(string outputDir)
    {
        var shPath = Path.Combine(outputDir, "launch_speedfog.sh");

        var script = @"#!/bin/bash
# SpeedFog Launcher for Elden Ring (Linux/Proton)
# Auto-generated - do not edit manually

SCRIPT_DIR=""$(cd ""$(dirname ""${BASH_SOURCE[0]}"")"" && pwd)""

# Note: For Proton/Wine users, you may need to configure
# Steam to use this script as a custom launch command
# or run through protontricks

wine ""$SCRIPT_DIR/ModEngine/modengine2_launcher.exe"" -t er -c ""$SCRIPT_DIR/config_speedfog.toml""
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
}
