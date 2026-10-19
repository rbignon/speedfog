# Save Backup System

**Date:** 2026-03-19, updated 2026-10-19 (mid-warp saves)
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
     - If the save still has the SHA256 of the last backup of this launch:
       skip (logged once per unchanged streak). A game stuck on a loading
       screen does not write its save; without this, every stuck launch
       filled the menu with copies of the broken state.
     - If the save is mid-warp (see "Mid-warp saves" below): re-read it
       every 5 s for up to 45 s, until it settles or the game exits. Still
       mid-warp after that (game stuck on the loading screen): zip it
       anyway, tagged `-warp`.
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
| `0013_launch2-warp_2026-10-13_14.43.00.zip` | daemon | Mid-warp save, the game cannot load it (`-warp` follows any kind: `launch2-start-warp`, `before-restore-warp`) |

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

`backups/backup.log` (append mode) tells the daemon's and the recovery's runs
as one chronological story: it is the file to ask a player for. Every line
starts with the local time and its UTC offset (`Write-BackupLog` in
`backup_store.ps1`), so it lines up with the racing mod log, which is in UTC.
The daemon also prints its lines to its console.

```
2026-10-13 08:05:12 -05:00 [SpeedFog Backup] Save file: C:\Users\...\ER0000.sl2
2026-10-13 08:05:12 -05:00 [SpeedFog Backup] Waiting for Elden Ring to start...
2026-10-13 08:05:12 -05:00 [SpeedFog Backup] Game detected.
2026-10-13 08:05:13 -05:00 [SpeedFog Backup] Launch 1 start backup: 0001_launch1-start_2026-10-13_08.05.12.zip
2026-10-13 08:05:13 -05:00 [SpeedFog Backup] Backup daemon started (launch: 1, interval: 1 min, keep: 10)
2026-10-13 08:06:14 -05:00 [SpeedFog Backup] Backup: 0002_launch1_2026-10-13_08.06.14.zip (2.8 MB)
2026-10-13 08:29:25 -05:00 [SpeedFog Backup] Game exited. Daemon stopping. (24 backups created)
2026-10-13 08:30:24 -05:00 [SpeedFog Backup] Launch 2 start backup: 0026_launch2-start_2026-10-13_08.30.24.zip
2026-10-13 08:31:27 -05:00 [SpeedFog Backup] Save unchanged since the last backup, skipping until it changes.
2026-10-13 08:35:40 -05:00 [SpeedFog Backup] Game exited. Daemon stopping. (0 backups created, 4 skipped: save unchanged)
2026-10-13 08:36:02 -05:00 [SpeedFog Recovery] Recovery started (save: C:\Users\...\ER0000.sl2, 12 backups)
2026-10-13 08:36:10 -05:00 [SpeedFog Recovery] Restoring #25 (0025_launch1_2026-10-13_08.29.24.zip)
2026-10-13 08:36:11 -05:00 [SpeedFog Recovery] Current save kept as #27 (0027_before-restore_2026-10-13_08.36.10.zip)
2026-10-13 08:36:11 -05:00 [SpeedFog Recovery] Restored #25
```

Recovery also logs whether the backup was typed or the default pick,
cancellations, invalid selections, a missing save file, the "game is running"
override and failures. A daemon killed together with
the game (no "Game exited" line) is visible as a gap before the next launch.

## Mid-warp saves

Elden Ring writes its save at the start of every fog-gate warp, with the
character slot's map set to a transition placeholder (m55_89_00_00, not a
map of the game) and the warp still pending, then again about half a second
after the player lands. The game cannot load the in-between state: it stays
on the loading screen forever. On disk, the save is mid-warp for the whole
loading screen, and forever when the game hangs on it, which is exactly the
case the backups exist for.

Case of 2026-10-19 (boss rush seed, 15 warps in 12 minutes): 5 of the 12
periodic backups and the quit save were mid-warp, while every per-entry MD5
of all 23 backups was valid (nothing is corrupted in the file sense). The
player restored two mid-warp backups whose warps had completed fine in the
live game, and both froze at load; a clean backup taken 40 s before the hang
was never tried. Before this, the recovery menu's default pick was the quit
save, always mid-warp after a hang.

Detection (`Test-SaveMidWarp` in `backup_store.ps1`): `ER0000.sl2` is a
BND4 with the 10 character slots at `0x300 + k * 0x280010`; a slot whose
version (uint32 at +0x10) is nonzero and whose map bytes at +0x14 are
`00 00 59 37` is mid-warp (clean saves store the map the player stands in).
Any non-empty slot counts: a character left mid-warp on another slot would
tag every backup, a documented limitation. The check reads the daemon's
temporary copy, never the live save. Slot layout from the
[elden-ring-save-manager](https://elden-ring-save-manager.readthedocs.io/en/main/technical/save-file-structure/)
documentation and ClayAmore's ER-Save-Editor, verified on the 23 backups.

What the scripts do with it:

- Periodic backups wait for the save to settle (`New-SettledBackup`: every
  5 s for up to 45 s, copying the save again only once the game has written
  it, stopping if the game exits), then zip whatever is there. A backup still mid-warp after the wait gets the
  `-warp` suffix; the unchanged-hash rule then keeps a stuck game from
  piling up copies.
- Start-of-launch and before-restore backups zip the file as is and get the
  suffix when it is mid-warp (after a hang, both are).
- The recovery menu annotates them, explains the label in its intro when
  one is listed, never picks one by default, and defaults the confirmation
  to n when the player types one anyway.

```
2026-10-19 17:59:53 +02:00 [SpeedFog Backup] Backup: 0011_launch1_2026-10-19_17.59.53.zip (3.1 MB, loading screen: waited 10 s)
2026-10-19 18:04:21 +02:00 [SpeedFog Backup] Backup: 0015_launch2-warp_2026-10-19_18.04.21.zip (2.9 MB, loading screen: waited 45 s, warp in progress: the game cannot load it)
```

Menu excerpt for a session with two mid-warp periodic backups, a restore of
one of them, and a launch stuck on the loading screen (default pick: #13):

```
Launch 1 (2026-10-19, 17:58 to 18:01)
  #10  17:58  3 min before last backup
  #11  17:59  2 min before last backup   (warp in progress: the game cannot load it)
  #12  18:00  1 min before last backup   (warp in progress: the game cannot load it)   <- restored at 18:06
  #13  18:01  last backup
Launch 2 (2026-10-19, 18:02 to 18:03)
  #14  18:02  start of launch   (warp in progress: the game cannot load it)
  #15  18:03  last backup   (warp in progress: the game cannot load it)
```

## Recovery

The player runs `recovery.bat`. The example below is the state after the
player restored #9, relaunched and played two more minutes:

```
SpeedFog Save Recovery
======================

Save file: C:\Users\...\ER0000.sl2

A backup is taken every minute while the save changes, until the game closes.
If you stayed stuck for a while before quitting, the most recent ones may
already contain the problem: pick one from BEFORE it.

Available backups (newest last):

Launch 1 (2026-10-13, 14:30 to 14:40)
  #1   14:30  start of launch
  ...
  #8   14:37  3 min before last backup
  #9   14:38  2 min before last backup   <- restored at 14:41
  #10  14:39  1 min before last backup
  #11  14:40  last backup
Restore at 14:41
  #12  14:41  your save before restoring #9
Launch 2 (2026-10-13, 14:42 to 14:44), started from #9
  #13  14:42  start of launch
  #14  14:43  1 min before last backup
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
  The reference is the last backup, not the game's exit: the daemon skips
  unchanged saves, so a game frozen on a loading screen adds no backup after
  the save stopped changing (its last backup already holds the problem).
- **Restore history.** `recovery.ps1` appends
  `<time>\t<restored zip>\t<undo zip>` to `restores.log` after each restore.
  The menu marks restored backups, labels undo backups and shows which
  backup each later launch started from.
- **Undo.** Before overwriting the save, recovery zips it as a
  `before-restore` backup. If that fails, the player must confirm the restore
  (default no). If the restore itself fails (`Expand-Archive -Force` deletes
  the save before extracting), the error message points to the undo backup.
- **Default selection**: the most recent backup taken while playing (an undo
  backup or a mid-warp backup is never the default), confirmation defaults
  to y (n for a mid-warp backup). Enter twice restores the most recent
  loadable backup. `#8` and `8` are both accepted.
- The script warns if Elden Ring is currently running.
- Every run is traced in `backup.log` (see Logging above).

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
