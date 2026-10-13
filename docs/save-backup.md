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
└── backups/
    ├── config.ini             # Optional backup settings (the game path override lives in %APPDATA%\SpeedFog\config.ini or <seed>\config.ini)
    ├── launch_helper.ps1      # Save detection + daemon launch (Windows)
    ├── resolve_game_path.ps1  # Optional eldenring.exe override for ModEngine 2
    ├── backup_daemon.ps1      # Windows daemon
    ├── backup_store.ps1       # Backup naming, listing, creation, purge (dot-sourced by daemon and recovery)
    ├── recovery.ps1           # Windows recovery
    ├── *.zip                  # Backup files (created at runtime)
    └── restores.log           # One line per restore (created at runtime by recovery)
```

The output root contains only files the player interacts with directly
(`launch_speedfog.bat`, `recovery.bat`). Internal scripts live in `backups/`.

## Configuration

`backups/config.ini` is generated with all values commented out. Defaults:

| Key | Default | Description |
|-----|---------|-------------|
| `enabled` | `true` | Set to `false` to disable backups entirely |
| `save_path` | (auto-detect) | Override save file path |
| `interval` | `1` | Minutes between backups (clamped to 1-540) |
| `max_backups` | `10` | Number of periodic backups to keep per launch |

## Save File Detection

The save file is at `%APPDATA%\EldenRing\<steam_id>\ER0000.sl2`.
The `<steam_id>` varies per player. ModEngine 2 does not isolate saves, so the backup target is the same vanilla ER0000.sl2.

Detection runs in the **launcher** (visible console window):

Before anything else, the launcher resolves an optional game override
(`resolve_game_path.ps1`: `SPEEDFOG_GAME_PATH`, then `game_path=` in
`%APPDATA%\SpeedFog\config.ini`, then `game_path=` in `<seed>\config.ini`
at the seed root; the first configured value wins) and passes it to
ModEngine 2 as `-p <eldenring.exe>`. This lets a player keep a frozen copy
of an older game version. The seed-root file is not part of the packaging
output: a distributor such as SpeedFog Racing adds it to the seed from the
player's account settings, and the per-machine sources take precedence over
it. An override that points nowhere aborts the launch (`INVALID` on stdout)
so the player never silently plays the Steam install instead. The save file
is not affected by the override: both game copies share it.

Then the launcher checks whether `eldenring.exe` is already
running. If so, it shows an error popup and aborts entirely (exit code 2
from `launch_helper.ps1`, checked by `launch_speedfog.bat`): no backup
daemon, no ModEngine launch. Any other helper failure is non-fatal and the
game still launches.

1. If `save_path` is set in `config.ini`, use it directly.
2. Otherwise, scan for `ER0000.sl2` in the platform-specific directory.
3. One match: use it. Multiple: numbered menu. None: warning, game launches
   without backup.

The resolved path is passed to the daemon as an argument.

## Backup Daemon

Launched by the launcher in a minimized window. Lifecycle:

1. **Wait phase**: poll for `eldenring.exe` every 5 seconds, up to 5 minutes,
   and capture the matching process (PID).
2. **Start-of-launch backup**: the daemon takes the next launch number (one
   more than the highest in the backups directory) and, if the save file
   exists, zips it as the launch's `start` backup.
3. **Backup loop**: wait on the game PID with a timeout of `interval` minutes:
   - If the game exited: log summary, exit immediately (no lag until the
     next interval; a quickly relaunched game is a new PID, so a stale
     daemon never latches onto the next seed's session).
   - On timeout (game still running):
     - If save file missing: skip.
     - Zip it as a periodic backup of the current launch.
     - If zip < 100 KB: log warning (possible partial copy from locked file).
     - Purge (see below).
4. Compression errors (e.g. file locked during autosave) are caught and
   retried next interval.

### Backup Naming

All backup logic lives in `backup_store.ps1`. Names follow
`<seq>_<kind>_<yyyy-MM-dd_HH.mm.ss>.zip`:

| Example | Created by | Meaning |
|---------|------------|---------|
| `0001_launch1-start_2026-10-13_14.30.00.zip` | daemon | Save when launch 1 began |
| `0002_launch1_2026-10-13_14.31.00.zip` | daemon | Periodic backup of launch 1 |
| `0012_before-restore_2026-10-13_14.41.03.zip` | recovery | Save replaced by a restore (undo) |

`<seq>` is a permanent number: one more than the highest existing number,
never shifted by a purge (only the number of a deleted newest backup can come
back). It is what the player types in the recovery menu. Zips outside this
scheme are ignored by the scripts.

### Purge

- Periodic backups: the last `max_backups` of each launch are kept. Other
  launches are never touched, so a new launch (after a restore, or a crash
  loop on reload) never purges the history the player is digging into.
- Start-of-launch and before-restore backups are never purged.
- Disk use grows by up to `max_backups` x 3 MB per launch (about 30 MB by
  default). A seed sees few launches; a cap across launches was tried and
  dropped because a crash loop of start-only launches purged the backups of
  the launch where the save broke.

Only `ER0000.sl2` is backed up. Elden Ring's own `ER0000.sl2.bak` is not
included; the `.sl2` alone is sufficient for recovery.

### Compression

Backups use ZIP (Deflate). Save files compress ~10:1 (~30 MB to ~3 MB).
The daemon copies the save file to a temporary `ER0000.sl2` in the backups
directory before compressing, so that no read handle is held on the live
save file during the slower compression step. This avoids potential
interference with Elden Ring's autosave. PowerShell uses `Copy-Item` +
`Compress-Archive`.

### Logging

The daemon logs to both console and `backups/backup.log` (append mode).

```
[SpeedFog Backup] Save file: C:\Users\...\ER0000.sl2
[SpeedFog Backup] Waiting for Elden Ring to start...
[SpeedFog Backup] Game detected.
[SpeedFog Backup] Launch 1 start backup: 0001_launch1-start_2026-03-19_14.30.00.zip
[SpeedFog Backup] Backup daemon started (launch: 1, interval: 1 min, keep: 10)
[SpeedFog Backup] Backup: 0002_launch1_2026-03-19_14.31.00.zip (3.1 MB)
[SpeedFog Backup] Game exited. Daemon stopping. (12 backups created)
```

## Recovery

The player runs `recovery.bat`. The example below is the state after the
player restored #9, relaunched and played two more minutes:

```
SpeedFog Save Recovery
======================

Save file: C:\Users\...\ER0000.sl2

Backups keep being taken until the game closes: if you stayed stuck for a
while before quitting, the most recent ones already contain the problem.
Pick one from BEFORE it.

Available backups (newest last):

Launch 1 (2026-10-13, 14:30 to 14:40)
  #1   14:30  start of launch
  ...
  #8   14:37  3 min before the end
  #9   14:38  2 min before the end   <- restored at 14:41
  #10  14:39  1 min before the end
  #11  14:40  last backup
Restore at 14:41
  #12  14:41  your save before restoring #9
Launch 2 (2026-10-13, 14:42 to 14:44), started from #9
  #13  14:42  start of launch
  #14  14:43  1 min before the end
  #15  14:44  last backup

Reminder: pick a backup from BEFORE the problem, the most recent ones may already contain it.
Number of the backup to restore [15]: 8
Restore #8 (14:37)? (y/n) [y]:

Restored backup #8 successfully.
Your previous save was kept as backup #16: restore it to undo.
You can relaunch the game with launch_speedfog.bat.
```

- **Permanent numbers.** The player types the backup's `<seq>`, which never
  changes. The menu used to number backups by position: each relaunch added
  entries and the global purge removed the oldest ones, so after restoring
  "9" and relaunching, "8" could designate a backup taken *after* the
  restored one.
- **Grouped by launch**, oldest first. Each periodic backup shows how long
  before the last backup of its launch it was taken: players reason in
  "I was stuck for about two minutes before quitting", not in clock times.
- **Restore history.** `recovery.ps1` appends
  `<time>\t<restored zip>\t<undo zip>` to `restores.log` after each restore.
  The menu marks restored backups, labels undo backups and shows which
  backup each later launch started from.
- **Undo.** Before overwriting the save, recovery zips it as a
  `before-restore` backup. If that fails, the player must confirm the restore
  (default no). If the restore itself fails (`Expand-Archive -Force` deletes
  the save before extracting), the error message points to the undo backup.
- **Default selection**: the most recent backup taken while playing (an undo
  backup is never the default), confirmation defaults to y. Enter twice
  restores the most recent backup, as before. `#8` and `8` are both accepted.
- The script warns if Elden Ring is currently running.

## Scripts and Packaging

Scripts are standalone files in `data/packaging/`, copied to the seed
directory by the Python packaging step. Only `config_speedfog.toml` is
dynamically generated (it depends on item randomizer configuration).

```
data/packaging/
├── launch_speedfog.bat
├── recovery.bat
└── backups/
    ├── config.ini
    ├── launch_helper.ps1
    ├── backup_daemon.ps1
    ├── backup_store.ps1
    └── recovery.ps1
```

`tools/bootstrap.py` populates the rest of `data/packaging/`, including
`modengine2/` and runtime DLLs in `lib/`.

## FogMod Comparison

FogMod has a similar system (`SaveBackupService.cs`) with a WinForms UI. Our
implementation differs:

- Scripts instead of GUI (no WinForms dependency)
- ZIP compression like FogMod (same ~10:1 ratio)
- Save detection in the launcher (visible window) instead of in the daemon
- Start-of-launch backup equivalent to FogMod's `.randobak`
- Recovery via interactive console menu instead of WinForms dialog
