# Save Backup System

**Date:** 2026-03-19
**Status:** Active

Automatic save file backup during gameplay with interactive recovery.

## Problem

SpeedFog performs deep modding of Elden Ring (fog gates, warps, EMEVD events).
A crash during a modded warp can corrupt the save file (`ER0000.sl2`), making
it impossible to continue the run. Since runs are short (~1 hour), even a few
minutes of lost progress is significant.

## Solution

A backup daemon runs alongside the game, periodically zipping the save file.
A separate recovery script lets the player restore any backup interactively.

## Output Files

```
output/
├── launch_speedfog.bat        # Detects save, starts daemon, launches game
├── recovery.bat               # Wrapper → backups/recovery.ps1
├── linux/
│   ├── launch_speedfog.sh     # Same flow for Linux/Proton
│   ├── recovery.sh            # Linux recovery
│   └── backup_daemon.sh       # Linux daemon
└── backups/
    ├── config.ini             # Optional config overrides
    ├── launch_helper.ps1      # Save detection + daemon launch (Windows)
    ├── backup_daemon.ps1      # Windows daemon
    ├── recovery.ps1           # Windows recovery
    └── *.zip                  # Backup files (created at runtime)
```

The output root contains only files the player interacts with directly
(`launch_speedfog.bat`, `recovery.bat`). Internal scripts live in `backups/`
and `linux/`.

## Configuration

`backups/config.ini` is generated with all values commented out. Defaults:

| Key | Default | Description |
|-----|---------|-------------|
| `enabled` | `true` | Set to `false` to disable backups entirely |
| `save_path` | (auto-detect) | Override save file path |
| `interval` | `1` | Minutes between backups |
| `max_backups` | `10` | Number of periodic backups to keep |

## Save File Detection

The save file is at `%APPDATA%\EldenRing\<steam_id>\ER0000.sl2` (Windows) or
under the Proton prefix (Linux). The `<steam_id>` varies per player.

Detection runs in the **launcher** (visible console window):

1. If `save_path` is set in `config.ini`, use it directly.
2. Otherwise, scan for `ER0000.sl2` in the platform-specific directory.
3. One match: use it. Multiple: numbered menu. None: warning, game launches
   without backup.

The resolved path is passed to the daemon as an argument.

## Backup Daemon

Launched by the launcher in a minimized window (Windows) or background process
(Linux). Lifecycle:

1. **Wait phase**: poll for `eldenring.exe` every 5 seconds, up to 5 minutes.
2. **Pre-run backup**: if save file exists, zip it as `pre-run_<timestamp>.zip`.
3. **Backup loop**: every `interval` minutes:
   - If game exited: log summary, exit.
   - If save file missing: skip.
   - Compress to `ER0000_<timestamp>.zip`.
   - If zip < 100 KB: log warning (possible partial copy from locked file).
   - Purge oldest periodic backups beyond `max_backups`.
4. Compression errors (e.g. file locked during autosave) are caught and
   retried next interval.

Pre-run backups are never purged. Periodic backups are purged by name
(lexicographic order = chronological for these timestamp filenames).

Only `ER0000.sl2` is backed up. Elden Ring's own `ER0000.sl2.bak` is not
included; the `.sl2` alone is sufficient for recovery.

### Compression

Backups use ZIP (Deflate). Save files compress ~10:1 (~30 MB to ~3 MB).
PowerShell uses `Compress-Archive`, bash uses `zip -j` (the `-j` flag strips
directory paths so the archive contains only `ER0000.sl2`).

### Logging

The daemon logs to both console and `backups/backup.log` (append mode).

```
[SpeedFog Backup] Save file: C:\Users\...\ER0000.sl2
[SpeedFog Backup] Waiting for Elden Ring to start...
[SpeedFog Backup] Game detected.
[SpeedFog Backup] Pre-run backup: pre-run_2026-03-19_14.30.00.zip
[SpeedFog Backup] Backup daemon started (interval: 1 min, keep: 10)
[SpeedFog Backup] Backup: ER0000_2026-03-19_14.31.00.zip (3.1 MB)
[SpeedFog Backup] Game exited. Daemon stopping. (12 backups created)
```

## Recovery

The player runs `recovery.bat` (Windows) or `linux/recovery.sh`.

```
SpeedFog Save Recovery
======================

Save file: C:\Users\...\ER0000.sl2

Available backups (newest last):

  [5] pre-run_2026-03-19_14.30.00.zip  (Pre-run backup)
  [4] ER0000_2026-03-19_14.31.00.zip
  [3] ER0000_2026-03-19_14.32.00.zip
  [2] ER0000_2026-03-19_14.33.00.zip
  [1] ER0000_2026-03-19_14.34.00.zip
  [0] ER0000_2026-03-19_14.35.00.zip   (most recent)

Select backup to restore [0]:
Restore ER0000_2026-03-19_14.35.00.zip? (y/n) [y]:
Restored successfully.
```

Index 0 = most recent. Default selection is 0, default confirmation is y.
Double-Enter restores the most recent backup. The script warns if Elden Ring
is currently running.

## Code Generation

All scripts are generated as C# string literals in
`writer/FogModWrapper/Packaging/ConfigGenerator.cs`:

| Method | Output |
|--------|--------|
| `WriteBackupConfig` | `backups/config.ini` |
| `WriteBackupDaemonPs1` | `backups/backup_daemon.ps1` |
| `WriteBackupDaemonSh` | `linux/backup_daemon.sh` |
| `WriteLaunchHelperPs1` | `backups/launch_helper.ps1` |
| `WriteRecoveryPs1` | `backups/recovery.ps1` |
| `WriteRecoveryBat` | `recovery.bat` |
| `WriteRecoverySh` | `linux/recovery.sh` |
| `WriteBatchLauncher` | `launch_speedfog.bat` (modified) |
| `WriteShellLauncher` | `linux/launch_speedfog.sh` (modified) |

`PackagingWriter.WritePackageAsync` calls all methods during output packaging.

## FogMod Comparison

FogMod has a similar system (`SaveBackupService.cs`) with a WinForms UI. Our
implementation differs:

- Scripts instead of GUI (no WinForms dependency, works on Linux)
- ZIP compression like FogMod (same ~10:1 ratio)
- Save detection in the launcher (visible window) instead of in the daemon
- Pre-run backup equivalent to FogMod's `.randobak`
- Recovery via interactive console menu instead of WinForms dialog
