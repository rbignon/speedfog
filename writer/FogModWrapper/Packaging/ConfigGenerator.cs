namespace FogModWrapper.Packaging;

/// <summary>
/// Generates ModEngine 2 configuration files and copies launcher/backup scripts to output.
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
    /// Copies launcher, backup, and recovery scripts from writer/scripts/ to the output directory.
    /// Scripts are static files (no dynamic content), so they are simply copied rather than generated.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    public static void CopyScripts(string outputDir)
    {
        var scriptsDir = FindScriptsDirectory()
            ?? throw new DirectoryNotFoundException(
                "Could not find writer/scripts/ directory. " +
                "Ensure you are running from the project directory or a published build.");

        // Root-level scripts
        CopyFile(scriptsDir, outputDir, "launch_speedfog.bat");
        CopyFile(scriptsDir, outputDir, "recovery.bat");

        // backups/ directory
        var backupsOut = Path.Combine(outputDir, "backups");
        Directory.CreateDirectory(backupsOut);
        CopyFile(scriptsDir, outputDir, Path.Combine("backups", "config.ini"));
        CopyFile(scriptsDir, outputDir, Path.Combine("backups", "launch_helper.ps1"));
        CopyFile(scriptsDir, outputDir, Path.Combine("backups", "backup_daemon.ps1"));
        CopyFile(scriptsDir, outputDir, Path.Combine("backups", "recovery.ps1"));

        // linux/ directory
        var linuxOut = Path.Combine(outputDir, "linux");
        Directory.CreateDirectory(linuxOut);
        CopyFile(scriptsDir, outputDir, Path.Combine("linux", "launch_speedfog.sh"));
        CopyFile(scriptsDir, outputDir, Path.Combine("linux", "backup_daemon.sh"));
        CopyFile(scriptsDir, outputDir, Path.Combine("linux", "recovery.sh"));

        // Make shell scripts executable on Unix
        if (!OperatingSystem.IsWindows())
        {
            var execMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

            foreach (var sh in Directory.GetFiles(linuxOut, "*.sh"))
            {
                File.SetUnixFileMode(sh, execMode);
            }
        }
    }

    private static void CopyFile(string scriptsDir, string outputDir, string relativePath)
    {
        var src = Path.Combine(scriptsDir, relativePath);
        var dst = Path.Combine(outputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
    }

    /// <summary>
    /// Finds the scripts/ directory by searching up from the executable location.
    /// Scripts are at writer/scripts/, executable may be at various depths.
    /// </summary>
    private static string? FindScriptsDirectory()
    {
        var candidates = new List<string>();

        // Try relative to executable
        var exeDir = AppContext.BaseDirectory;
        for (int i = 0; i <= 7; i++)
        {
            var path = exeDir;
            for (int j = 0; j < i; j++)
                path = Path.Combine(path, "..");
            candidates.Add(Path.Combine(path, "scripts"));
        }

        // Also try relative to current working directory
        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "scripts"));
        candidates.Add(Path.Combine(cwd, "..", "scripts"));
        candidates.Add(Path.Combine(cwd, "..", "..", "scripts"));

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (Directory.Exists(normalized) &&
                File.Exists(Path.Combine(normalized, "launch_speedfog.bat")))
                return normalized;
        }

        return null;
    }
}
