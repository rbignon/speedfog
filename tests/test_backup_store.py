"""Tests for the save backup scripts in data/packaging/backups/ (backup naming,
per-launch purge, recovery menu and restore flow). Run through PowerShell 7
(`pwsh`), skipped when it is not installed (`dotnet tool install -g
PowerShell`). The shipped scripts run under Windows PowerShell 5.1; these tests
cover the logic only.
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import threading
import time
import zipfile
from pathlib import Path

import pytest

BACKUPS_SRC = Path(__file__).resolve().parent.parent / "data" / "packaging" / "backups"
STORE = BACKUPS_SRC / "backup_store.ps1"
PWSH = shutil.which("pwsh")

pytestmark = pytest.mark.skipif(PWSH is None, reason="pwsh not installed")


def _pwsh(script: Path, stdin: str = "", tz: str | None = None) -> list[str]:
    env = dict(os.environ)
    if tz is not None:
        env["TZ"] = tz
    result = subprocess.run(
        [PWSH, "-NoProfile", "-File", str(script)],
        input=stdin,
        capture_output=True,
        text=True,
        check=True,
        env=env,
    )
    return result.stdout.splitlines()


def _run(tmp_path: Path, body: str, tz: str | None = None) -> list[str]:
    """Run `body` with backup_store.ps1 loaded and $Dir set to tmp_path."""
    script = tmp_path / "test.ps1"
    script.write_text(
        f"$ErrorActionPreference = 'Stop'\n. '{STORE}'\n$Dir = '{tmp_path}'\n{body}",
        encoding="utf-8",
    )
    return _pwsh(script, tz=tz)


def _touch(directory: Path, *names: str) -> None:
    for name in names:
        (directory / name).write_bytes(b"x")


def _menu(tmp_path: Path) -> list[str]:
    return _run(
        tmp_path,
        "$b = Get-SaveBackups $Dir\n"
        "Format-BackupList -Backups $b -Restores (Read-RestoreLog $Dir)\n"
        '"default=$((Get-DefaultBackup $b).Seq)"\n',
    )


def test_menu_after_a_restore_keeps_permanent_numbers(tmp_path: Path) -> None:
    """The scenario that motivated the scheme: #9 restored, relaunched, then
    the player looks for "the one before #9"."""
    _touch(tmp_path, "0001_launch1-start_2026-10-13_14.30.00.zip")
    _touch(
        tmp_path,
        *(f"{i + 1:04d}_launch1_2026-10-13_14.{30 + i}.05.zip" for i in range(1, 11)),
    )
    _touch(
        tmp_path,
        "0012_before-restore_2026-10-13_14.41.03.zip",
        "0013_launch2-start_2026-10-13_14.42.00.zip",
        "0014_launch2_2026-10-13_14.43.04.zip",
        "0015_launch2_2026-10-13_14.44.09.zip",
        "pre-run_2026-10-13_14.00.00.zip",  # outside the scheme: ignored
    )
    (tmp_path / "restores.log").write_text(
        "2026-10-13_14.41.03\t0009_launch1_2026-10-13_14.38.05.zip"
        "\t0012_before-restore_2026-10-13_14.41.03.zip\n",
        encoding="utf-8",
    )

    lines = _menu(tmp_path)

    assert lines[0] == "Launch 1 (2026-10-13, 14:30 to 14:40)"
    assert lines[1] == "  #1   14:30  start of launch"
    assert lines[8] == "  #8   14:37  3 min before last backup"
    assert lines[9] == "  #9   14:38  2 min before last backup   <- restored at 14:41"
    assert lines[11] == "  #11  14:40  last backup"
    assert lines[12] == "Restore at 14:41"
    assert lines[13] == "  #12  14:41  your save before restoring #9"
    assert lines[14] == "Launch 2 (2026-10-13, 14:42 to 14:44), started from #9"
    assert lines[15:18] == [
        "  #13  14:42  start of launch",
        "  #14  14:43  1 min before last backup",
        "  #15  14:44  last backup",
    ]
    # The undo backup is never the default pick.
    assert lines[18] == "default=15"


def test_menu_unusual_states(tmp_path: Path) -> None:
    """Start-only launch (crash on load), launch whose start backup failed,
    two restores in a row without relaunch, malformed restore log lines."""
    _touch(
        tmp_path,
        "0001_launch1-start_2026-10-13_14.30.00.zip",
        "0002_launch1_2026-10-13_14.31.00.zip",
        "0003_launch2-start_2026-10-13_14.35.00.zip",
        "0004_before-restore_2026-10-13_14.36.00.zip",
        "0005_before-restore_2026-10-13_14.37.00.zip",
        "0006_launch3_2026-10-13_14.39.00.zip",
    )
    (tmp_path / "restores.log").write_text(
        "garbage\n"
        "not-a-time\t0002_launch1_2026-10-13_14.31.00.zip\t\n"
        "2026-10-13_14.36.00\t0002_launch1_2026-10-13_14.31.00.zip"
        "\t0004_before-restore_2026-10-13_14.36.00.zip\n"
        "2026-10-13_14.37.00\t0001_launch1-start_2026-10-13_14.30.00.zip"
        "\t0005_before-restore_2026-10-13_14.37.00.zip\n",
        encoding="utf-8",
    )

    assert _menu(tmp_path) == [
        "Launch 1 (2026-10-13, 14:30 to 14:31)",
        "  #1  14:30  start of launch   <- restored at 14:37",
        "  #2  14:31  last backup   <- restored at 14:36",
        "Launch 2 (2026-10-13, 14:35)",
        "  #3  14:35  start of launch",
        "Restore at 14:36",
        "  #4  14:36  your save before restoring #2",
        "Restore at 14:37",
        "  #5  14:37  your save before restoring #1",
        "Launch 3 (2026-10-13, 14:39), started from #1",
        "  #6  14:39  last backup",
        "default=6",
    ]


def test_purge_is_per_launch_and_survives_a_crash_loop(tmp_path: Path) -> None:
    """Launch 1 breaks the save, then five relaunches crash on load (start
    backup only), then launch 7 plays on: launch 1 keeps its history."""
    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "Set-Content -LiteralPath $save -Value 'save'\n"
        "function Snap($kind, $launch) {\n"
        "    New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind $kind"
        " -Launch $launch | Out-Null\n"
        "    Remove-OldBackups -BackupsDir $Dir -Launch $launch -MaxBackups 3\n"
        "}\n"
        "Snap start 1\n"
        "for ($i = 1; $i -le 5; $i++) { Snap periodic 1 }\n"
        "New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind before-restore"
        " | Out-Null\n"
        "for ($l = 2; $l -le 6; $l++) {\n"
        '    "next=$(Get-NextLaunchNumber $Dir)"\n'
        "    Snap start $l\n"
        "}\n"
        "Snap start 7\n"
        "for ($i = 1; $i -le 4; $i++) { Snap periodic 7 }\n"
        "$all = Get-SaveBackups $Dir | ForEach-Object {\n"
        '    "$($_.Seq):$($_.Kind):$($_.Launch)"\n'
        "}\n"
        "$all -join ' '\n",
    )

    assert lines[:5] == [f"next={launch}" for launch in range(2, 7)]
    # Each launch keeps its last 3 periodic backups; start and before-restore
    # backups are never purged; numbers never shift.
    assert lines[5].split() == [
        "1:start:1",
        "4:periodic:1",
        "5:periodic:1",
        "6:periodic:1",
        "7:before-restore:0",
        "8:start:2",
        "9:start:3",
        "10:start:4",
        "11:start:5",
        "12:start:6",
        "13:start:7",
        "15:periodic:7",
        "16:periodic:7",
        "17:periodic:7",
    ]


def test_unchanged_save_is_not_backed_up_again(tmp_path: Path) -> None:
    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "Set-Content -LiteralPath $save -Value 'one'\n"
        "$first = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind start"
        " -Launch 1\n"
        "$same = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic"
        " -Launch 1 -UnlessHash $first.SaveHash\n"
        "Set-Content -LiteralPath $save -Value 'two'\n"
        "$changed = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic"
        " -Launch 1 -UnlessHash $first.SaveHash\n"
        '"same=$($null -eq $same)"\n'
        '"changed=$($changed.Name.Substring(0, 13)) $($changed.SaveHash -ne $first.SaveHash)"\n'
        "(Get-SaveBackups $Dir | ForEach-Object { $_.Seq }) -join ' '\n"
        "@(Get-ChildItem -LiteralPath $Dir -Filter 'ER0000.sl2').Count\n",
    )

    assert lines == ["same=True", "changed=0002_launch1_ True", "1 2", "0"]


def test_log_lines_carry_local_time_with_utc_offset(tmp_path: Path) -> None:
    lines = _run(
        tmp_path,
        "Write-BackupLog -BackupsDir $Dir -Source Recovery -Message 'hello'\n"
        "Get-Content -LiteralPath (Join-Path $Dir 'backup.log')\n",
        tz="Asia/Kolkata",  # +05:30 all year: pins the offset, not just its shape
    )

    pattern = (
        r"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \+05:30 " r"\[SpeedFog Recovery\] hello$"
    )
    assert len(lines) == 2
    assert lines[0] == lines[1]
    assert re.match(pattern, lines[1])


def _zip_save(path: Path, content: str) -> None:
    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("ER0000.sl2", content)


def _recovery_setup(tmp_path: Path) -> tuple[Path, Path]:
    """Backups dir with the scripts, two backups ("one", "two") and a
    config.ini pointing at a save path that does not exist yet."""
    backups = tmp_path / "backups"
    save_dir = tmp_path / "save"
    backups.mkdir()
    save_dir.mkdir()
    for name in ("recovery.ps1", "backup_store.ps1"):
        shutil.copy(BACKUPS_SRC / name, backups / name)
    save = save_dir / "ER0000.sl2"
    (backups / "config.ini").write_text(f"save_path={save}\n", encoding="utf-8")
    _zip_save(backups / "0001_launch1-start_2026-10-13_14.30.00.zip", "one")
    _zip_save(backups / "0002_launch1_2026-10-13_14.31.00.zip", "two")
    return backups, save


def _log_messages(backups: Path) -> list[str]:
    log = (backups / "backup.log").read_text(encoding="utf-8").splitlines()
    return [line.split("] ", 1)[1] for line in log]


def test_recovery_restores_by_number_and_keeps_an_undo(tmp_path: Path) -> None:
    backups, save = _recovery_setup(tmp_path)
    save.write_text("current", encoding="utf-8")

    # Pick #1, confirm with the default, then "Press Enter to exit".
    output = _pwsh(backups / "recovery.ps1", stdin="#1\n\n\n")

    assert save.read_text(encoding="utf-8") == "one"
    undo = list(backups.glob("0003_before-restore_*.zip"))
    assert len(undo) == 1
    with zipfile.ZipFile(undo[0]) as zf:
        assert zf.read("ER0000.sl2") == b"current"
    log = (backups / "restores.log").read_text(encoding="utf-8").splitlines()
    assert len(log) == 1
    assert log[0].split("\t")[1:] == [
        "0001_launch1-start_2026-10-13_14.30.00.zip",
        undo[0].name,
    ]
    assert "Restored backup #1 successfully." in output
    assert "Your previous save was kept as backup #3: restore it to undo." in output
    assert _log_messages(backups) == [
        f"Recovery started (save: {save}, 2 backups)",
        "Restoring #1 (0001_launch1-start_2026-10-13_14.30.00.zip)",
        f"Current save kept as #3 ({undo[0].name})",
        "Restored #1",
    ]


def test_recovery_default_pick_without_a_current_save(tmp_path: Path) -> None:
    backups, save = _recovery_setup(tmp_path)

    # Enter at every prompt: default backup, default confirmation, exit.
    _pwsh(backups / "recovery.ps1", stdin="\n\n\n")

    assert save.read_text(encoding="utf-8") == "two"
    assert not list(backups.glob("*before-restore*"))
    assert _log_messages(backups) == [
        f"Recovery started (save: {save}, 2 backups)",
        "Restoring #2 (0002_launch1_2026-10-13_14.31.00.zip, default pick)",
        "No current save file to keep",
        "Restored #2",
    ]


# --- Mid-warp saves -----------------------------------------------------------
#
# Elden Ring writes its save at the start of every fog-gate warp with the
# slot's map set to the transition placeholder m55_89_00_00 and the warp still
# pending; the game cannot load that state (infinite loading screen). The
# fake saves below only carry the bytes the detection reads: the BND4 magic,
# and per slot the version (slot+0x10) and the map (slot+0x14).

SAVE_SIZE = 28_967_888
SLOT_BASE = 0x300
SLOT_STRIDE = 0x280010
WARP_MAP = bytes.fromhex("00005937")  # m55_89_00_00
GIDEON_MAP = bytes.fromhex("0000050b")  # m11_05_00_00


def _fake_save(path: Path, slots: dict[int, tuple]) -> None:
    """Sparse ER0000.sl2 lookalike; `slots` maps a slot index to its
    (version, map bytes) or (version, map bytes, 16-byte checksum). Written
    atomically so a concurrent copy never sees a half-written file."""
    tmp = path.with_suffix(".tmp")
    with open(tmp, "wb") as f:
        f.write(b"BND4")
        for k, spec in slots.items():
            version, map_bytes = spec[0], spec[1]
            base = SLOT_BASE + k * SLOT_STRIDE
            if len(spec) > 2:
                f.seek(base)
                f.write(spec[2])
            f.seek(base + 0x10)
            f.write(version.to_bytes(4, "little") + map_bytes)
        f.seek(SAVE_SIZE - 1)
        f.write(b"\0")
    os.replace(tmp, path)


def _sum(byte: int) -> bytes:
    """A distinguishable 16-byte slot checksum."""
    return bytes([byte]) * 16


def test_warp_suffix_marks_mid_warp_backups_of_every_kind(tmp_path: Path) -> None:
    _touch(
        tmp_path,
        "0010_launch1_2026-10-19_17.58.41.zip",
        "0011_launch1-warp_2026-10-19_17.59.42.zip",
        "0014_launch2-start-warp_2026-10-19_18.02.34.zip",
        "0016_before-restore-warp_2026-10-19_18.06.49.zip",
    )

    lines = _run(
        tmp_path,
        "Get-SaveBackups $Dir | ForEach-Object {"
        ' "$($_.Seq):$($_.Kind):$($_.Launch):$($_.Warp)" }\n',
    )

    assert lines == [
        "10:periodic:1:False",
        "11:periodic:1:True",
        "14:start:2:True",
        "16:before-restore:0:True",
    ]


def test_menu_flags_mid_warp_backups_and_never_defaults_to_them(tmp_path: Path) -> None:
    """The 2026-10-19 case: two mid-warp periodic backups, the quit save of a
    game stuck on a loading screen (launch 2), a restore of a mid-warp
    backup. The default pick is the last clean backup, #13."""
    _touch(
        tmp_path,
        "0010_launch1_2026-10-19_17.58.41.zip",
        "0011_launch1-warp_2026-10-19_17.59.42.zip",
        "0012_launch1-warp_2026-10-19_18.00.43.zip",
        "0013_launch1_2026-10-19_18.01.43.zip",
        "0014_launch2-start-warp_2026-10-19_18.02.34.zip",
        "0015_launch2-warp_2026-10-19_18.03.36.zip",
        "0016_before-restore-warp_2026-10-19_18.06.49.zip",
    )
    (tmp_path / "restores.log").write_text(
        "2026-10-19_18.06.50\t0012_launch1-warp_2026-10-19_18.00.43.zip"
        "\t0016_before-restore-warp_2026-10-19_18.06.49.zip\n",
        encoding="utf-8",
    )
    warp = "   (warp in progress: the game cannot load it)"

    assert _menu(tmp_path) == [
        "Launch 1 (2026-10-19, 17:58 to 18:01)",
        "  #10  17:58  3 min before last backup",
        f"  #11  17:59  2 min before last backup{warp}",
        f"  #12  18:00  1 min before last backup{warp}   <- restored at 18:06",
        "  #13  18:01  last backup",
        "Launch 2 (2026-10-19, 18:02 to 18:03)",
        f"  #14  18:02  start of launch{warp}",
        f"  #15  18:03  last backup{warp}",
        "Restore at 18:06",
        f"  #16  18:06  your save before restoring #12{warp}",
        "default=13",
    ]


def test_mid_warp_detection_reads_every_non_empty_slot(tmp_path: Path) -> None:
    _fake_save(tmp_path / "clean.sl2", {0: (261, GIDEON_MAP)})
    _fake_save(tmp_path / "warp0.sl2", {0: (261, WARP_MAP)})
    _fake_save(tmp_path / "warp3.sl2", {0: (261, GIDEON_MAP), 3: (261, WARP_MAP)})
    # The placeholder in an empty slot (version 0) is leftover bytes, not a warp.
    _fake_save(tmp_path / "empty.sl2", {0: (261, GIDEON_MAP), 5: (0, WARP_MAP)})
    (tmp_path / "tiny.sl2").write_bytes(b"x")

    lines = _run(
        tmp_path,
        'foreach ($n in "clean", "warp0", "warp3", "empty", "tiny") {'
        ' $warp = @(Get-SaveSlots -Path (Join-Path $Dir "$n.sl2") | Where-Object { $_.MidWarp })\n'
        " \"$n=$(($warp | ForEach-Object { $_.Index }) -join ',')\" }\n",
    )

    assert lines == ["clean=", "warp0=0", "warp3=3", "empty=", "tiny="]


def test_mid_warp_save_is_held_back_or_tagged(tmp_path: Path) -> None:
    save = tmp_path / "ER0000.src"
    _fake_save(save, {0: (261, WARP_MAP)})

    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$held = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic"
        " -Launch 1 -UnlessMidWarp\n"
        '"held=$($held.MidWarp) zips=$(@(Get-ChildItem -LiteralPath $Dir -Filter *.zip).Count)'
        ' temp=$(@(Get-ChildItem -LiteralPath $Dir -Filter ER0000.sl2).Count)"\n'
        "$tagged = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1\n"
        '"tagged=$($tagged.Name.Substring(0, 17)) warp=$($tagged.Warp)"\n'
        "$same = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -UnlessHash $tagged.SaveHash -UnlessMidWarp\n"
        '"same=$($null -eq $same)"\n'
        "$start = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind start -Launch 2\n"
        "$undo = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind before-restore\n"
        '"start=$($start.Name.Substring(0, 23)) undo=$($undo.Name.Substring(0, 24))"\n',
    )
    assert lines == [
        "held=True zips=0 temp=0",
        "tagged=0001_launch1-warp warp=True",
        "same=True",
        "start=0002_launch2-start-warp undo=0003_before-restore-warp",
    ]

    _fake_save(save, {0: (261, GIDEON_MAP)})
    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$clean = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic"
        " -Launch 2 -UnlessMidWarp\n"
        '"clean=$($clean.Name.Substring(0, 13)) warp=$($clean.Warp)"\n',
    )
    assert lines == ["clean=0004_launch2_ warp=False"]


def test_settled_backup_waits_for_the_loading_screen_to_end(tmp_path: Path) -> None:
    save = tmp_path / "ER0000.src"
    ready = tmp_path / "ready"
    _fake_save(save, {0: (261, WARP_MAP)})

    def land() -> None:
        # The game lands 2.5 s after the wait starts and rewrites a clean save.
        deadline = time.monotonic() + 60
        while not ready.exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        time.sleep(2.5)
        _fake_save(save, {0: (261, GIDEON_MAP)})

    threading.Thread(target=land).start()

    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$t0 = Get-Date\n"
        "Set-Content -LiteralPath (Join-Path $Dir 'ready') -Value 1\n"
        "$z = New-SettledBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -WaitSeconds 20 -PollSeconds 1\n"
        '"name=$($z.Name.Substring(0, 13)) warp=$($z.Warp) waited=$($z.WaitedSeconds -ge 2)'
        ' fast=$(((Get-Date) - $t0).TotalSeconds -lt 15)"\n',
    )

    assert lines == ["name=0001_launch1_ warp=False waited=True fast=True"]


def test_settled_backup_gives_up_after_the_wait_and_tags_the_backup(
    tmp_path: Path,
) -> None:
    save = tmp_path / "ER0000.src"
    _fake_save(save, {0: (261, WARP_MAP)})

    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$z = New-SettledBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -WaitSeconds 3 -PollSeconds 1\n"
        '"name=$($z.Name.Substring(0, 17)) warp=$($z.Warp) waited=$($z.WaitedSeconds -ge 3)"\n'
        "$stopped = New-SettledBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -UnlessHash $z.SaveHash -WaitSeconds 10 -PollSeconds 1\n"
        "$exited = New-SettledBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -WaitSeconds 10 -PollSeconds 1 -ShouldStop { $true }\n"
        '"unchanged=$($null -eq $stopped) exited=$($null -eq $exited)'
        ' zips=$(@(Get-ChildItem -LiteralPath $Dir -Filter *.zip).Count)"\n',
    )

    assert lines == [
        "name=0001_launch1-warp warp=True waited=True",
        "unchanged=True exited=True zips=1",
    ]


def test_recovery_never_restores_a_mid_warp_backup_by_default(tmp_path: Path) -> None:
    backups, save = _recovery_setup(tmp_path)
    _zip_save(backups / "0003_launch1-warp_2026-10-13_14.32.00.zip", "warp")
    save.write_text("current", encoding="utf-8")

    # Enter at every prompt: the default pick skips the mid-warp #3.
    _pwsh(backups / "recovery.ps1", stdin="\n\n\n")
    assert save.read_text(encoding="utf-8") == "two"

    # #3 can still be typed, but its confirmation defaults to no.
    output = _pwsh(backups / "recovery.ps1", stdin="#3\n\n\n")
    assert save.read_text(encoding="utf-8") == "two"
    assert "Cancelled." in output
    assert len(list(backups.glob("*before-restore*"))) == 1
    assert _log_messages(backups)[-3:] == [
        "Restored #2",
        f"Recovery started (save: {save}, 4 backups)",
        "Cancelled at confirmation of #3",
    ]


def test_save_slots_expose_checksum_emptiness_and_mid_warp_state(
    tmp_path: Path,
) -> None:
    _fake_save(
        tmp_path / "save.sl2",
        {
            0: (261, GIDEON_MAP, _sum(0xAA)),
            3: (261, WARP_MAP, _sum(0xBB)),
            5: (0, WARP_MAP, _sum(0xCC)),
        },
    )
    (tmp_path / "tiny.sl2").write_bytes(b"x")

    lines = _run(
        tmp_path,
        "Get-SaveSlots -Path (Join-Path $Dir 'save.sl2') | ForEach-Object {"
        ' "$($_.Index):$($_.Empty):$($_.Checksum):$($_.MidWarp)" }\n'
        "\"tiny=$(@(Get-SaveSlots -Path (Join-Path $Dir 'tiny.sl2')).Count)\"\n",
    )

    assert lines[0] == "0:False:" + "AA" * 16 + ":False"
    assert lines[3] == "3:False:" + "BB" * 16 + ":True"
    assert lines[5] == "5:True:" + "CC" * 16 + ":False"
    assert len(lines) == 11 and lines[10] == "tiny=0"


def test_stale_mid_warp_slot_is_ignored_once_the_played_slot_was_written(
    tmp_path: Path,
) -> None:
    """A character left mid-warp in slot 2 by an abandoned seed, while the
    player plays slot 0: the stale slot never tags a periodic backup once
    slot 0 has been written this launch, and never makes the daemon wait."""
    save = tmp_path / "ER0000.src"
    _fake_save(save, {0: (261, GIDEON_MAP, _sum(0xA0)), 2: (261, WARP_MAP, _sum(0xC2))})

    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$start = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind start -Launch 1\n"
        '"start=$($start.Name.Substring(0, 18)) slots=$(@($start.Slots).Count)"\n'
        # Nothing written yet this launch: the stale slot still counts, but it
        # did not change since the last backup, so no hold even with -UnlessMidWarp.
        "$early = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -StartSlots $start.Slots -LastSlots $start.Slots -UnlessMidWarp\n"
        '"early=$($early.Name.Substring(0, 17)) warp=$($early.Warp)"\n',
    )
    assert lines == [
        "start=0001_launch1-start slots=10",
        "early=0002_launch1-warp warp=True",
    ]

    # The game wrote slot 0 (new checksum): only slot 0 is watched from now on.
    # The start-of-launch slot states are what the daemon keeps in memory:
    # rebuild them from a copy of the launch-start save.
    _fake_save(save, {0: (261, GIDEON_MAP, _sum(0xA1)), 2: (261, WARP_MAP, _sum(0xC2))})
    _fake_save(
        tmp_path / "start.sl2",
        {0: (261, GIDEON_MAP, _sum(0xA0)), 2: (261, WARP_MAP, _sum(0xC2))},
    )
    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$startSlots = Get-SaveSlots -Path (Join-Path $Dir 'start.sl2')\n"
        "$t0 = Get-Date\n"
        "$z = New-SettledBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -StartSlots $startSlots -LastSlots $startSlots -WaitSeconds 10 -PollSeconds 1\n"
        '"name=$($z.Name.Substring(0, 13)) warp=$($z.Warp) waited=$($z.WaitedSeconds)"\n',
    )
    assert lines == ["name=0003_launch1_ warp=False waited=0"]

    # A new character created in a previously empty slot is the played slot too.
    _fake_save(
        save,
        {
            0: (261, GIDEON_MAP, _sum(0xA0)),
            2: (261, WARP_MAP, _sum(0xC2)),
            4: (261, GIDEON_MAP, _sum(0xD4)),
        },
    )
    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$startSlots = Get-SaveSlots -Path (Join-Path $Dir 'start.sl2')\n"
        "$z = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -StartSlots $startSlots -LastSlots $startSlots -UnlessMidWarp\n"
        '"name=$($z.Name.Substring(0, 13)) warp=$($z.Warp)"\n',
    )
    assert lines == ["name=0004_launch1_ warp=False"]


def test_in_flight_warp_is_held_back_only_while_its_slot_keeps_changing(
    tmp_path: Path,
) -> None:
    save = tmp_path / "ER0000.src"
    _fake_save(tmp_path / "last.sl2", {0: (261, GIDEON_MAP, _sum(0xA0))})
    _fake_save(save, {0: (261, WARP_MAP, _sum(0xA1))})

    lines = _run(
        tmp_path,
        "$save = Join-Path $Dir 'ER0000.src'\n"
        "$last = Get-SaveSlots -Path (Join-Path $Dir 'last.sl2')\n"
        # Slot 0 changed since the last backup and is mid-warp: a warp in flight, held back.
        "$held = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -LastSlots $last -UnlessMidWarp\n"
        '"held=$($held.MidWarp) slots=$(@($held.Slots).Count)"\n'
        # The last backup already saw this very mid-warp state: not in flight, zip it tagged.
        "$seen = New-SaveBackup -BackupsDir $Dir -SavePath $save -Kind periodic -Launch 1"
        " -LastSlots $held.Slots -UnlessMidWarp\n"
        '"seen=$($seen.Name.Substring(0, 17)) warp=$($seen.Warp)"\n',
    )

    assert lines == ["held=True slots=10", "seen=0001_launch1-warp warp=True"]
