using FogModWrapper.Packaging;
using Xunit;

namespace FogModWrapper.Tests;

public class ConfigGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"speedfog_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WriteModEngineConfig_GeneratesValidToml()
    {
        ConfigGenerator.WriteModEngineConfig(_tempDir);

        var configPath = Path.Combine(_tempDir, "config_speedfog.toml");
        Assert.True(File.Exists(configPath));

        var content = File.ReadAllText(configPath);
        Assert.Contains("[modengine]", content);
        Assert.Contains("[extension.mod_loader]", content);
        Assert.Contains("fogmod", content);
        Assert.DoesNotContain("itemrando", content);
    }

    [Fact]
    public void WriteModEngineConfig_WithItemRandomizer_IncludesItemRando()
    {
        ConfigGenerator.WriteModEngineConfig(_tempDir, itemRandomizerEnabled: true);

        var content = File.ReadAllText(Path.Combine(_tempDir, "config_speedfog.toml"));
        Assert.Contains("itemrando", content);
        Assert.Contains("RandomizerHelper.dll", content);
    }

    [Fact]
    public void CopyScripts_CopiesToCorrectLocations()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        // Root-level files
        Assert.True(File.Exists(Path.Combine(_tempDir, "launch_speedfog.bat")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "recovery.bat")));

        // backups/ directory
        Assert.True(File.Exists(Path.Combine(_tempDir, "backups", "config.ini")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "backups", "launch_helper.ps1")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "backups", "backup_daemon.ps1")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "backups", "recovery.ps1")));

        // linux/ directory
        Assert.True(File.Exists(Path.Combine(_tempDir, "linux", "launch_speedfog.sh")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "linux", "backup_daemon.sh")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "linux", "recovery.sh")));
    }

    [Fact]
    public void CopyScripts_ConfigIniHasAllKeysCommentedOut()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "backups", "config.ini"));
        Assert.Contains("# enabled=true", content);
        Assert.Contains("# save_path=", content);
        Assert.Contains("# interval=1", content);
        Assert.Contains("# max_backups=10", content);

        // No uncommented key=value lines
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            Assert.StartsWith("#", trimmed);
        }
    }

    [Fact]
    public void CopyScripts_BatchLauncherCallsLaunchHelper()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "launch_speedfog.bat"));
        Assert.Contains("launch_helper.ps1", content);
        Assert.Contains("modengine2_launcher.exe", content);
    }

    [Fact]
    public void CopyScripts_RecoveryBatCallsRecoveryPs1()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "recovery.bat"));
        Assert.Contains("recovery.ps1", content);
        Assert.Contains("-ExecutionPolicy Bypass", content);
    }

    [Fact]
    public void CopyScripts_BackupDaemonPs1_AcceptsSavePathParam()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "backups", "backup_daemon.ps1"));
        Assert.Contains("[Parameter(Mandatory=$true)]", content);
        Assert.Contains("$SavePath", content);
        Assert.Contains("Compress-Archive", content);
    }

    [Fact]
    public void CopyScripts_BackupDaemonSh_AcceptsSavePathArg()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "linux", "backup_daemon.sh"));
        Assert.Contains("SAVE_PATH=\"$1\"", content);
        Assert.Contains("zip -j", content);
    }

    [Fact]
    public void CopyScripts_LaunchHelperPs1_HasSteamRegistryDetection()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "backups", "launch_helper.ps1"));
        Assert.Contains("HKCU:\\Software\\Valve\\Steam\\ActiveProcess", content);
        Assert.Contains("76561197960265728", content);
    }

    [Fact]
    public void CopyScripts_ShellLauncher_HasSteamVdfDetection()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "linux", "launch_speedfog.sh"));
        Assert.Contains("loginusers.vdf", content);
        Assert.Contains("MostRecent", content);
    }

    [Fact]
    public void CopyScripts_RecoveryPs1_HasRestoreFlow()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "backups", "recovery.ps1"));
        Assert.Contains("Expand-Archive", content);
        Assert.Contains("Restored successfully", content);
        Assert.Contains("launch_speedfog.bat", content);
    }

    [Fact]
    public void CopyScripts_RecoverySh_HasRestoreFlow()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "linux", "recovery.sh"));
        Assert.Contains("unzip -o -j", content);
        Assert.Contains("Restored successfully", content);
        Assert.Contains("launch_speedfog.sh", content);
    }

    [Fact]
    public void CopyScripts_ShellScriptsHaveShebang()
    {
        ConfigGenerator.CopyScripts(_tempDir);

        foreach (var sh in Directory.GetFiles(Path.Combine(_tempDir, "linux"), "*.sh"))
        {
            var firstLine = File.ReadLines(sh).First();
            Assert.Equal("#!/bin/bash", firstLine);
        }
    }

    [Fact]
    public void CopyScripts_OverwritesExistingFiles()
    {
        // First copy
        ConfigGenerator.CopyScripts(_tempDir);
        var firstContent = File.ReadAllText(Path.Combine(_tempDir, "backups", "config.ini"));

        // Write something different
        File.WriteAllText(Path.Combine(_tempDir, "backups", "config.ini"), "modified");

        // Second copy should overwrite
        ConfigGenerator.CopyScripts(_tempDir);
        var secondContent = File.ReadAllText(Path.Combine(_tempDir, "backups", "config.ini"));

        Assert.Equal(firstContent, secondContent);
    }
}
