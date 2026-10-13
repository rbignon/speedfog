"""Tests for the save backup scripts in data/packaging/backups/ (backup naming,
per-launch purge, recovery menu and restore flow). Run through PowerShell 7
(`pwsh`), skipped when it is not installed (`dotnet tool install -g
PowerShell`). The shipped scripts run under Windows PowerShell 5.1; these tests
cover the logic only.
"""

from __future__ import annotations

import shutil
import subprocess
import zipfile
from pathlib import Path

import pytest

BACKUPS_SRC = Path(__file__).resolve().parent.parent / "data" / "packaging" / "backups"
STORE = BACKUPS_SRC / "backup_store.ps1"
PWSH = shutil.which("pwsh")

pytestmark = pytest.mark.skipif(PWSH is None, reason="pwsh not installed")


def _pwsh(script: Path, stdin: str = "") -> list[str]:
    result = subprocess.run(
        [PWSH, "-NoProfile", "-File", str(script)],
        input=stdin,
        capture_output=True,
        text=True,
        check=True,
    )
    return result.stdout.splitlines()


def _run(tmp_path: Path, body: str) -> list[str]:
    """Run `body` with backup_store.ps1 loaded and $Dir set to tmp_path."""
    script = tmp_path / "test.ps1"
    script.write_text(
        f"$ErrorActionPreference = 'Stop'\n. '{STORE}'\n$Dir = '{tmp_path}'\n{body}",
        encoding="utf-8",
    )
    return _pwsh(script)


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
    assert lines[8] == "  #8   14:37  3 min before the end"
    assert lines[9] == "  #9   14:38  2 min before the end   <- restored at 14:41"
    assert lines[11] == "  #11  14:40  last backup"
    assert lines[12] == "Restore at 14:41"
    assert lines[13] == "  #12  14:41  your save before restoring #9"
    assert lines[14] == "Launch 2 (2026-10-13, 14:42 to 14:44), started from #9"
    assert lines[15:18] == [
        "  #13  14:42  start of launch",
        "  #14  14:43  1 min before the end",
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


def _zip_save(path: Path, content: str) -> None:
    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("ER0000.sl2", content)


def test_recovery_restores_by_number_and_keeps_an_undo(tmp_path: Path) -> None:
    backups = tmp_path / "backups"
    save_dir = tmp_path / "save"
    backups.mkdir()
    save_dir.mkdir()
    for name in ("recovery.ps1", "backup_store.ps1"):
        shutil.copy(BACKUPS_SRC / name, backups / name)
    save = save_dir / "ER0000.sl2"
    save.write_text("current", encoding="utf-8")
    (backups / "config.ini").write_text(f"save_path={save}\n", encoding="utf-8")
    _zip_save(backups / "0001_launch1-start_2026-10-13_14.30.00.zip", "one")
    _zip_save(backups / "0002_launch1_2026-10-13_14.31.00.zip", "two")

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
