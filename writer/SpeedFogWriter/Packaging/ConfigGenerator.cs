// writer/SpeedFogWriter/Packaging/ConfigGenerator.cs

namespace SpeedFogWriter.Packaging;

/// <summary>
/// Generates ModEngine 2 configuration files and launcher scripts.
/// </summary>
public static class ConfigGenerator
{
    /// <summary>
    /// Writes the ModEngine 2 TOML configuration file.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    /// <param name="modPath">The relative path to the mod folder (default: "mods/speedfog").</param>
    public static void WriteModEngineConfig(string outputDir, string modPath = "mods/speedfog")
    {
        var configPath = Path.Combine(outputDir, "config_speedfog.toml");

        var config = $@"# SpeedFog ModEngine 2 Configuration
# Auto-generated - do not edit manually

[modengine]
debug = false
external_dlls = []

[extension.mod_loader]
enabled = true
loose_params = false
mods = [
    {{ enabled = true, name = ""speedfog"", path = ""{modPath}"" }}
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
}
