namespace FogModWrapper.Packaging;

/// <summary>
/// Generates the ME3 profile and copies launcher/backup scripts to output.
/// </summary>
public static class ConfigGenerator
{
    /// <summary>
    /// Writes the ME3 profile (.me3 TOML) describing packages and natives.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    /// <param name="modPath">The relative path to the mod folder (default: "mods/fogmod").</param>
    /// <param name="itemRandomizerEnabled">Whether item randomizer was used (adds itemrando package and RandomizerHelper.dll).</param>
    public static void WriteModEngineConfig(string outputDir, string modPath = "mods/fogmod", bool itemRandomizerEnabled = false)
    {
        var configPath = Path.Combine(outputDir, "config_speedfog.me3");

        var natives = new List<string> { "lib/RandomizerCrashFix.dll" };
        if (itemRandomizerEnabled)
        {
            natives.Add("lib/RandomizerHelper.dll");
        }

        var nativesBlock = string.Join("\n\n", natives.Select(p =>
            $"[[natives]]\npath = \"{p}\""));

        var packagesLines = new List<string>
        {
            "[[packages]]",
            "id = \"fogmod\"",
            $"path = \"{modPath}\""
        };
        if (itemRandomizerEnabled)
        {
            packagesLines.Add("");
            packagesLines.Add("[[packages]]");
            packagesLines.Add("id = \"itemrando\"");
            packagesLines.Add("path = \"mods/itemrando\"");
        }
        var packagesBlock = string.Join("\n", packagesLines);

        var config = $@"# SpeedFog ME3 Profile
# Auto-generated, do not edit manually
profileVersion = ""v1""

[[supports]]
game = ""eldenring""

{nativesBlock}

{packagesBlock}
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
