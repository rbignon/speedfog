"""Tests for refresh_vanilla_snapshot.py (copy patched files into the snapshots)."""

from __future__ import annotations

from pathlib import Path

import pytest
import refresh_vanilla_snapshot as rvs
from refresh_vanilla_snapshot import (
    MISSING_IN_GAME,
    MISSING_IN_SNAPSHOT,
    REPLACED,
    UP_TO_DATE,
    default_targets,
    plan_targets,
    refresh_snapshot,
    validate_extra_file,
)


def _write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


def _make_snapshot(root: Path, suffix: bytes = b"-1.16") -> Path:
    _write(root / "regulation.bin", b"reg" + suffix)
    _write(root / "msg/engus/item_dlc02.msgbnd.dcx", b"item" + suffix)
    _write(root / "msg/frafr/menu_dlc02.msgbnd.dcx", b"menu-fr")
    _write(root / "m10_00_00_00.msb.dcx", b"stormveil" + suffix)
    _write(root / "m60_52_39_00.emevd.dcx", b"caelid" + suffix)
    return root


@pytest.fixture
def snapshot(tmp_path: Path) -> Path:
    return _make_snapshot(tmp_path / "Vanilla")


@pytest.fixture
def game(tmp_path: Path) -> Path:
    root = tmp_path / "Game"
    for marker in ("map/mapstudio", "event", "msg"):
        (root / marker).mkdir(parents=True, exist_ok=True)
    _write(root / "regulation.bin", b"reg-1.17")
    _write(root / "msg/engus/item_dlc02.msgbnd.dcx", b"item-1.17")
    _write(root / "msg/frafr/menu_dlc02.msgbnd.dcx", b"menu-fr")
    _write(root / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-1.17")
    _write(root / "event/m60_52_39_00.emevd.dcx", b"caelid-1.17")
    return root


# --- target planning ---


def test_default_targets_are_regulation_and_snapshot_msg_only(snapshot: Path):
    assert default_targets(snapshot) == [
        "regulation.bin",
        "msg/engus/item_dlc02.msgbnd.dcx",
        "msg/frafr/menu_dlc02.msgbnd.dcx",
    ]


def test_plan_targets_appends_extra_files_without_duplicates(snapshot: Path):
    targets = plan_targets(snapshot, ["m60_52_39_00.emevd.dcx", "regulation.bin"])
    assert targets == [
        "regulation.bin",
        "msg/engus/item_dlc02.msgbnd.dcx",
        "msg/frafr/menu_dlc02.msgbnd.dcx",
        "m60_52_39_00.emevd.dcx",
    ]


def test_plan_targets_all_files_covers_every_mappable_snapshot_file(snapshot: Path):
    _write(snapshot / "m10_00_00_00.nva.dcx", b"navmesh")  # unknown suffix
    _write(snapshot / "chr/c0000.chrbnd.dcx", b"player")  # unknown subdirectory

    targets = plan_targets(snapshot, [], all_files=True)

    assert targets == [
        "m10_00_00_00.msb.dcx",
        "m60_52_39_00.emevd.dcx",
        "msg/engus/item_dlc02.msgbnd.dcx",
        "msg/frafr/menu_dlc02.msgbnd.dcx",
        "regulation.bin",
    ]


def test_main_all_reports_unmappable_snapshot_files(
    snapshots: dict[str, Path], game: Path, capsys: pytest.CaptureFixture[str]
):
    _write(snapshots["fogmod"] / "m10_00_00_00.nva.dcx", b"navmesh")

    assert rvs.main([str(game), "--all"]) == 2

    out = capsys.readouterr().out
    assert (
        "skipped (unknown file type, extend SUFFIX_DIRS): m10_00_00_00.nva.dcx" in out
    )
    # The mappable files were still refreshed.
    assert (
        snapshots["fogmod"] / "m10_00_00_00.msb.dcx"
    ).read_bytes() == b"stormveil-1.17"


def test_main_all_refreshes_maps_too(snapshots: dict[str, Path], game: Path):
    assert rvs.main([str(game), "--all"]) == 0
    for path in snapshots.values():
        assert (path / "m10_00_00_00.msb.dcx").read_bytes() == b"stormveil-1.17"
        assert (path / "m60_52_39_00.emevd.dcx").read_bytes() == b"caelid-1.17"


@pytest.mark.parametrize(
    "bad", ["msg/../../x.msgbnd.dcx", "chr/c0000.chrbnd.dcx", "m10_00_00_00.nva.dcx"]
)
def test_validate_extra_file_rejects_escapes_and_unknown_types(bad: str):
    with pytest.raises(ValueError):
        validate_extra_file(bad)


def test_validate_extra_file_maps_known_layout():
    assert (
        validate_extra_file("m60_52_39_00.emevd.dcx") == "event/m60_52_39_00.emevd.dcx"
    )


# --- refresh_snapshot ---


def test_refresh_replaces_changed_files_and_leaves_maps_alone(
    snapshot: Path, game: Path
):
    results = {
        r.snapshot_rel: r.status
        for r in refresh_snapshot(snapshot, game, default_targets(snapshot))
    }

    assert results == {
        "regulation.bin": REPLACED,
        "msg/engus/item_dlc02.msgbnd.dcx": REPLACED,
        "msg/frafr/menu_dlc02.msgbnd.dcx": UP_TO_DATE,
    }
    assert (snapshot / "regulation.bin").read_bytes() == b"reg-1.17"
    assert (snapshot / "msg/engus/item_dlc02.msgbnd.dcx").read_bytes() == b"item-1.17"
    assert (snapshot / "m10_00_00_00.msb.dcx").read_bytes() == b"stormveil-1.16"
    assert not list(snapshot.rglob("*.refresh-tmp"))


def test_refresh_is_idempotent(snapshot: Path, game: Path):
    refresh_snapshot(snapshot, game, default_targets(snapshot))
    second = {
        r.snapshot_rel: r.status
        for r in refresh_snapshot(snapshot, game, default_targets(snapshot))
    }
    assert set(second.values()) == {UP_TO_DATE}


def test_dry_run_reports_without_writing(snapshot: Path, game: Path):
    results = {
        r.snapshot_rel: r.status
        for r in refresh_snapshot(
            snapshot, game, default_targets(snapshot), dry_run=True
        )
    }
    assert results["regulation.bin"] == REPLACED
    assert (snapshot / "regulation.bin").read_bytes() == b"reg-1.16"


def test_extra_file_uses_game_layout(snapshot: Path, game: Path):
    results = refresh_snapshot(snapshot, game, ["m60_52_39_00.emevd.dcx"])
    assert results[0].game_rel == "event/m60_52_39_00.emevd.dcx"
    assert results[0].status == REPLACED
    assert (snapshot / "m60_52_39_00.emevd.dcx").read_bytes() == b"caelid-1.17"


def test_missing_files_are_reported_not_copied(snapshot: Path, game: Path):
    (game / "msg/engus/item_dlc02.msgbnd.dcx").unlink()
    _write(game / "map/mapstudio/m11_00_00_00.msb.dcx", b"leyndell-1.17")
    results = {
        r.snapshot_rel: r.status
        for r in refresh_snapshot(
            snapshot, game, ["msg/engus/item_dlc02.msgbnd.dcx", "m11_00_00_00.msb.dcx"]
        )
    }
    assert results["msg/engus/item_dlc02.msgbnd.dcx"] == MISSING_IN_GAME
    assert results["m11_00_00_00.msb.dcx"] == MISSING_IN_SNAPSHOT
    assert (snapshot / "msg/engus/item_dlc02.msgbnd.dcx").read_bytes() == b"item-1.16"
    assert not (snapshot / "m11_00_00_00.msb.dcx").exists()


# --- main ---


@pytest.fixture
def snapshots(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> dict[str, Path]:
    paths = {
        "fogmod": _make_snapshot(tmp_path / "eldendata" / "Vanilla"),
    }
    monkeypatch.setattr(rvs, "SNAPSHOTS", paths)
    return paths


def test_main_refreshes_every_snapshot(snapshots: dict[str, Path], game: Path, capsys):
    assert rvs.main([str(game)]) == 0
    assert (snapshots["fogmod"] / "regulation.bin").read_bytes() == (
        game / "regulation.bin"
    ).read_bytes()


def test_main_dry_run_writes_nothing(
    snapshots: dict[str, Path], game: Path, capsys: pytest.CaptureFixture[str]
):
    assert rvs.main([str(game), "--dry-run"]) == 0
    assert (snapshots["fogmod"] / "regulation.bin").read_bytes() == b"reg-1.16"
    assert "current regulation.bin md5" in capsys.readouterr().out


def test_main_missing_game_file_is_exit_2(snapshots: dict[str, Path], game: Path):
    (game / "msg/engus/item_dlc02.msgbnd.dcx").unlink()
    assert rvs.main([str(game)]) == 2
    # The other files were still refreshed.
    assert (snapshots["fogmod"] / "regulation.bin").read_bytes() == b"reg-1.17"


def test_main_rejects_bad_inputs_before_writing(
    snapshots: dict[str, Path], game: Path, tmp_path: Path
):
    assert rvs.main([str(game), "--file", "chr/c0000.chrbnd.dcx"]) == 1
    assert rvs.main([str(tmp_path / "not-a-game")]) == 1
    for path in snapshots.values():
        assert (path / "regulation.bin").read_bytes() == b"reg-1.16"
