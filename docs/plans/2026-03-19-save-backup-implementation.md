# Save Backup System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add automatic save file backup during gameplay and interactive recovery to the SpeedFog output package.

**Architecture:** Six new script files (PowerShell for Windows, bash for Linux) are generated as string literals by `ConfigGenerator.cs` and written to the output directory by `PackagingWriter.cs`. The existing launcher scripts are modified to detect the save file path and start a backup daemon before launching ModEngine. The Linux launcher moves from root to `linux/`.

**Tech Stack:** C# (.NET 8.0) for generation, PowerShell 5.1+ for Windows runtime scripts, bash for Linux runtime scripts.

**Spec:** `docs/plans/2026-03-19-save-backup-system.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `writer/FogModWrapper/Packaging/ConfigGenerator.cs` | Add 6 new Write* methods, modify 2 existing launcher methods |
| Modify | `writer/FogModWrapper/Packaging/PackagingWriter.cs` | Call new methods, create directories, update "To play" output |
| Modify | `CLAUDE.md` | Update `launch_speedfog.sh` path references |
| Modify | `README.md` | Update `launch_speedfog.sh` path references |
| Modify | `writer/README.md` | Update `launch_speedfog.sh` path references |
| Modify | `docs/architecture.md` | Update output structure diagram |

All runtime scripts (`backup_daemon.ps1`, `recovery.ps1`, `backup_daemon.sh`, `recovery.sh`, `config.ini`, `recovery.bat`) are generated as string literals inside `ConfigGenerator.cs`. No new C# files are created.

**Important conventions for all `Write*` methods:**
- Each method must call `Directory.CreateDirectory` for its target directory before writing. This makes methods independently callable and avoids ordering fragility.
- All bash scripts that create zip files must use `zip -j` (junk paths) so the archive contains only `ER0000.sl2`, not the full directory structure.
- All bash scripts must use `$SCRIPT_DIR`-relative paths (never bare relative paths) to avoid working-directory sensitivity.
- When a config key is absent from `config.ini`, use the defaults: `enabled=true`, `interval=1`, `max_backups=10`.

---

### Task 1: Generate `backups/config.ini`

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs`

- [ ] **Step 1: Add `WriteBackupConfig` method**

Add to `ConfigGenerator.cs`:

```csharp
/// <summary>
/// Writes the backup configuration file with all values commented out.
/// </summary>
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
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: add WriteBackupConfig to generate backups/config.ini"
```

---

### Task 2: Generate `backups/backup_daemon.ps1`

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs`

- [ ] **Step 1: Add `WriteBackupDaemonPs1` method**

Add to `ConfigGenerator.cs`. The PowerShell script must:
1. Accept `-SavePath` parameter (mandatory)
2. Parse `config.ini` for `interval` (default 1) and `max_backups` (default 10)
3. Define a `Log` function that writes to both console and `backups/backup.log`
4. Wait phase: poll for `eldenring` process every 5 seconds, timeout after 5 minutes
5. If save file exists, create pre-run backup (`pre-run_<timestamp>.zip`)
6. Backup loop: sleep interval, check process, compress to zip, purge oldest
7. Size warning if zip < 100 KB

Key PowerShell patterns:
- `Get-Process eldenring -ErrorAction SilentlyContinue` for process detection
- `Compress-Archive -Path $SavePath -DestinationPath $zipPath` for compression
- `Get-ChildItem $backupsDir/ER0000_*.zip | Sort-Object Name | Select-Object -SkipLast $max | Remove-Item` for purge
- `Get-Date -Format "yyyy-MM-dd_HH.mm.ss"` for timestamps

The script determines `$backupsDir` as `$PSScriptRoot`. The log file path is `$backupsDir/backup.log`. All paths must be `$backupsDir`-relative, never bare relative.

The `Compress-Archive` call must be wrapped in `try { ... } catch { Log "WARNING: Failed to create backup: $_" ; continue }` to handle locked files gracefully (the save file is briefly locked during Elden Ring autosaves). The daemon continues to the next interval on failure.

The method must call `Directory.CreateDirectory` for the `backups/` directory before writing.

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: add WriteBackupDaemonPs1 for Windows backup daemon"
```

---

### Task 3: Generate `linux/backup_daemon.sh`

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs`

- [ ] **Step 1: Add `WriteBackupDaemonSh` method**

Same logic as the PowerShell daemon, translated to bash:
1. Accept save path as `$1` argument
2. Resolve `BACKUPS_DIR="$SCRIPT_DIR/../backups"` and parse `$BACKUPS_DIR/config.ini` for interval and max_backups
3. `log()` function appending to `$BACKUPS_DIR/backup.log`
4. Wait phase: `pgrep -x eldenring.exe`, sleep 5, timeout 300s
5. Pre-run backup: `zip -j "$BACKUPS_DIR/pre-run_<timestamp>.zip" "$SAVE_PATH"` (the `-j` flag strips directory paths so the archive contains only `ER0000.sl2`)
6. Backup loop: `sleep ${interval}m`, process check, `zip -j` for backup, purge with `ls -1t "$BACKUPS_DIR"/ER0000_*.zip | tail -n +$((max+1)) | xargs rm -f`
7. Size warning: `stat -c%s` check < 102400
8. Wrap zip commands in error handling: `if ! zip -j ...; then log "WARNING: ..."; continue; fi`

All paths must use `$SCRIPT_DIR` or `$BACKUPS_DIR`, never bare relative paths.

The method must call `Directory.CreateDirectory` for the `linux/` directory before writing. Set Unix file mode (executable) like the existing `WriteShellLauncher` does.

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: add WriteBackupDaemonSh for Linux backup daemon"
```

---

### Task 4: Generate `backups/recovery.ps1` and `recovery.bat`

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs`

- [ ] **Step 1: Add `WriteRecoveryPs1` method**

The PowerShell script must:
1. Determine `$backupsDir` as `$PSScriptRoot` and `$configPath` as `$backupsDir/config.ini`
2. Parse config.ini for `save_path`
3. If no `save_path`, auto-detect: scan `$env:APPDATA\EldenRing\*\ER0000.sl2`
   - Exactly one: use it
   - None: print error with instructions, `pause`, exit
   - Multiple: display numbered menu, prompt for selection
4. Warn if `eldenring` process is running
5. List `$backupsDir\*.zip` sorted oldest-first by modification time
6. Display with 1-based indices (oldest = 1, newest = N)
7. Annotate pre-run backups with `(Pre-run backup)`, last item with `(most recent)`
8. Prompt for index (default = N = most recent), prompt for confirmation (default y)
9. `Expand-Archive` selected zip, overwrite save file
10. Print success, `pause` (so the window stays open)

- [ ] **Step 2: Add `WriteRecoveryBat` method**

One-line wrapper:

```csharp
public static void WriteRecoveryBat(string outputDir)
{
    var batPath = Path.Combine(outputDir, "recovery.bat");
    var script = @"@powershell -ExecutionPolicy Bypass -NoProfile -File ""%~dp0backups\recovery.ps1""
";
    File.WriteAllText(batPath, script);
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: add recovery scripts (recovery.ps1 + recovery.bat wrapper)"
```

---

### Task 5: Generate `linux/recovery.sh`

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs`

- [ ] **Step 1: Add `WriteRecoverySh` method**

Same logic as `recovery.ps1`, translated to bash:
1. Resolve `BACKUPS_DIR="$SCRIPT_DIR/../backups"`, config at `$BACKUPS_DIR/config.ini`
2. Auto-detect under Proton prefix: `~/.local/share/Steam/steamapps/compatdata/1245620/pfx/drive_c/users/steamuser/AppData/Roaming/EldenRing/*/ER0000.sl2`
3. Prompt if multiple, error if none
4. Warn if `pgrep -x eldenring.exe` finds the game running
5. List, display, prompt, restore with `unzip -o -j "$ZIP_PATH" -d "$(dirname "$SAVE_PATH")"` (the `-j` flag strips paths, `-d` extracts to the save file's directory)
6. The method must call `Directory.CreateDirectory` for the `linux/` directory before writing. Set Unix file mode (executable).

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: add WriteRecoverySh for Linux recovery script"
```

---

### Task 6: Modify `WriteBatchLauncher` to include save detection and daemon launch

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs` (the existing `WriteBatchLauncher` method)

- [ ] **Step 1: Rewrite `WriteBatchLauncher`**

The new `.bat` script must:
1. Set `SCRIPT_DIR`
2. Run an inline PowerShell block that:
   a. Parses `backups/config.ini` for `enabled` and `save_path`
   b. If `enabled` is `false`: do nothing
   c. If `save_path` set: use it
   d. Else auto-detect: `Get-ChildItem "$env:APPDATA\EldenRing\*\ER0000.sl2"`
   e. If none found: `Write-Host` warning, skip daemon
   f. If multiple: display numbered menu, `Read-Host` for selection
   g. If resolved: `Start-Process -WindowStyle Minimized powershell -ArgumentList "-ExecutionPolicy Bypass -NoProfile -File backups\backup_daemon.ps1 -SavePath '<path>'"`
3. Launch ModEngine (unchanged)

The PowerShell block is passed as a single `-Command` string. Use a C# verbatim string with careful escaping: double-quote `""` inside the batch, and PowerShell single quotes for the inner script.

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: launcher detects save file and starts backup daemon"
```

---

### Task 7: Modify `WriteShellLauncher` to move to `linux/` and add daemon launch

**Files:**
- Modify: `writer/FogModWrapper/Packaging/ConfigGenerator.cs` (the existing `WriteShellLauncher` method)

- [ ] **Step 1: Rewrite `WriteShellLauncher`**

Changes:
1. Write to `linux/launch_speedfog.sh` instead of root
2. Add save detection logic (read config, scan Proton prefix, prompt if multiple)
3. Launch `backup_daemon.sh "$SAVE_PATH" &` if resolved
4. ModEngine path becomes `$SCRIPT_DIR/../ModEngine/...` and config becomes `$SCRIPT_DIR/../config_speedfog.toml`
5. Keep the `SetUnixFileMode` call

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs
git commit -m "feat: move shell launcher to linux/, add save detection and daemon"
```

---

### Task 8: Update `PackagingWriter.cs` to call new methods and create directories

**Files:**
- Modify: `writer/FogModWrapper/Packaging/PackagingWriter.cs` (the `WritePackageAsync` method)

- [ ] **Step 1: Update `WritePackageAsync`**

After step 6 (launcher scripts), add the backup system generation. Note: each `Write*` method creates its own directories defensively, so no separate `Directory.CreateDirectory` calls are needed here.

```csharp
// 7. Generate backup system scripts
ConfigGenerator.WriteBackupConfig(_outputDir);
ConfigGenerator.WriteBackupDaemonPs1(_outputDir);
ConfigGenerator.WriteBackupDaemonSh(_outputDir);
ConfigGenerator.WriteRecoveryPs1(_outputDir);
ConfigGenerator.WriteRecoveryBat(_outputDir);
ConfigGenerator.WriteRecoverySh(_outputDir);
Console.WriteLine("Generated backup and recovery scripts");
```

- [ ] **Step 2: Update "To play" output**

Change from:
```csharp
Console.WriteLine($"  Linux:   run {Path.Combine(_outputDir, "launch_speedfog.sh")}");
```
to:
```csharp
Console.WriteLine($"  Linux:   run {Path.Combine(_outputDir, "linux", "launch_speedfog.sh")}");
```

- [ ] **Step 3: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add writer/FogModWrapper/Packaging/PackagingWriter.cs
git commit -m "feat: package backup system scripts in output"
```

---

### Task 9: Update documentation for Linux path change

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `writer/README.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: Update CLAUDE.md**

In the Commands section, change:
```
./output/launch_speedfog.sh    # Linux/Proton
```
to:
```
./output/linux/launch_speedfog.sh    # Linux/Proton
```

Also update the output directory structure in the Directory Structure section to include the new `backups/` and `linux/` directories.

- [ ] **Step 2: Update README.md**

Change references from `launch_speedfog.sh` at root to `linux/launch_speedfog.sh`. Update the output structure listing to include `backups/` and `linux/`.

- [ ] **Step 3: Update writer/README.md**

Change `output/launch_speedfog.sh` to `output/linux/launch_speedfog.sh`. Update the output directory tree.

- [ ] **Step 4: Update docs/architecture.md**

Update the output directory structure diagram to show:
```
├── linux/
│   └── launch_speedfog.sh
├── backups/
│   ├── config.ini
│   ├── backup_daemon.ps1
│   └── recovery.ps1
├── recovery.bat
```

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md README.md writer/README.md docs/architecture.md
git commit -m "docs: update paths for linux/ directory and backup system"
```

---

### Task 10: Integration test

**Files:**
- No new files

- [ ] **Step 1: Generate a run and inspect output**

```bash
cd /home/dev/src/games/ER/fog/speedfog
uv run speedfog config.toml --spoiler
```

Then build and run FogModWrapper to generate the full output package. Verify the output contains:

```
output/
├── launch_speedfog.bat        (contains PowerShell save detection block)
├── recovery.bat               (one-line wrapper)
├── linux/
│   ├── launch_speedfog.sh     (contains save detection + daemon launch)
│   ├── recovery.sh
│   └── backup_daemon.sh
├── backups/
│   ├── config.ini
│   ├── backup_daemon.ps1
│   └── recovery.ps1
```

- [ ] **Step 2: Verify script content**

Spot-check each generated script:
- `backups/config.ini`: all values commented out
- `backups/backup_daemon.ps1`: accepts `-SavePath`, has wait phase, backup loop, purge logic
- `linux/backup_daemon.sh`: accepts `$1`, same logic in bash
- `backups/recovery.ps1`: lists zips, decremental indices, restore logic
- `linux/recovery.sh`: same logic in bash
- `recovery.bat`: calls `recovery.ps1` with `-ExecutionPolicy Bypass`
- `launch_speedfog.bat`: PowerShell inline block for save detection + daemon start
- `linux/launch_speedfog.sh`: bash save detection + daemon start
- `linux/*.sh` files are executable

- [ ] **Step 3: Commit if any fixes needed**

Stage only the specific files that were modified (do not use `git add -A`, it would stage pre-existing untracked files):

```bash
git add writer/FogModWrapper/Packaging/ConfigGenerator.cs writer/FogModWrapper/Packaging/PackagingWriter.cs
git commit -m "fix: address integration test findings"
```

---

### Task 11: Code review

- [ ] **Step 1: Run code review agent**

Use `superpowers:requesting-code-review` to review all changes against the spec.

- [ ] **Step 2: Address review findings**

Fix any issues, commit.
