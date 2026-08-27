"""Tests for diff_vanilla_snapshot.py (snapshot vs unpacked game hash-diff)."""

from __future__ import annotations

from pathlib import Path

import pytest
from diff_vanilla_snapshot import (
    CHANGED,
    MISSING,
    SAME,
    UNMAPPED,
    diff_extra_msg,
    diff_snapshot,
    game_path_for,
    is_unpacked_game_dir,
    print_report,
)

# --- game_path_for ---


@pytest.mark.parametrize(
    ("snapshot_rel", "expected"),
    [
        ("regulation.bin", "regulation.bin"),
        ("msg/engus/item_dlc02.msgbnd.dcx", "msg/engus/item_dlc02.msgbnd.dcx"),
        ("m10_00_00_00.msb.dcx", "map/mapstudio/m10_00_00_00.msb.dcx"),
        ("common_func.emevd.dcx", "event/common_func.emevd.dcx"),
        ("m00_00_00_00.talkesdbnd.dcx", "script/talk/m00_00_00_00.talkesdbnd.dcx"),
        ("sfxbnd_c4720.ffxbnd.dcx", "sfx/sfxbnd_c4720.ffxbnd.dcx"),
        ("msg\\engus\\menu.msgbnd.dcx", "msg/engus/menu.msgbnd.dcx"),
    ],
)
def test_game_path_for_known_layout(snapshot_rel: str, expected: str):
    assert game_path_for(snapshot_rel) == expected


@pytest.mark.parametrize(
    "snapshot_rel", ["m10_00_00_00.nva.dcx", "chr/c0000.chrbnd.dcx"]
)
def test_game_path_for_rejects_unknown_files(snapshot_rel: str):
    with pytest.raises(ValueError):
        game_path_for(snapshot_rel)


# --- diff_snapshot ---


def _write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


def _make_game_dir(root: Path) -> Path:
    for marker in ("map/mapstudio", "event", "msg"):
        (root / marker).mkdir(parents=True, exist_ok=True)
    _write(root / "regulation.bin", b"reg")
    return root


@pytest.fixture
def snapshot(tmp_path: Path) -> Path:
    snap = tmp_path / "Vanilla"
    _write(snap / "regulation.bin", b"reg")
    _write(snap / "m10_00_00_00.msb.dcx", b"stormveil-msb")
    _write(snap / "m11_00_00_00.emevd.dcx", b"leyndell-emevd")
    _write(snap / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text")
    return snap


def test_diff_snapshot_classifies_same_changed_missing(snapshot: Path, tmp_path: Path):
    game = _make_game_dir(tmp_path / "Game")
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb")
    # Same size, different content: the hash must catch it.
    _write(game / "event/m11_00_00_00.emevd.dcx", b"leyndell-EMEVD")
    # msg/engus/item_dlc02.msgbnd.dcx is absent from the game.

    by_rel = {d.snapshot_rel: d for d in diff_snapshot(snapshot, game)}

    assert by_rel["regulation.bin"].status == SAME
    assert by_rel["m10_00_00_00.msb.dcx"].status == SAME
    assert by_rel["m11_00_00_00.emevd.dcx"].status == CHANGED
    assert by_rel["m11_00_00_00.emevd.dcx"].game_size == len(b"leyndell-EMEVD")
    assert by_rel["msg/engus/item_dlc02.msgbnd.dcx"].status == MISSING
    assert by_rel["msg/engus/item_dlc02.msgbnd.dcx"].game_size is None
    assert all(d.reference_status is None for d in by_rel.values())
    assert all(d.is_patch_change is None for d in by_rel.values())


def test_diff_snapshot_three_way_classification(snapshot: Path, tmp_path: Path):
    game = _make_game_dir(tmp_path / "Game")
    reference = _make_game_dir(tmp_path / "Game.frozen")
    # Patch change on a fresh snapshot: game differs, reference matches the snapshot.
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb-1.17")
    _write(reference / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb")
    # Stale snapshot only: game and reference agree, both differ from the snapshot.
    _write(game / "event/m11_00_00_00.emevd.dcx", b"leyndell-emevd-1.16")
    _write(reference / "event/m11_00_00_00.emevd.dcx", b"leyndell-emevd-1.16")
    # Three distinct contents: a patch change on top of a stale snapshot file.
    # This is the case a two-way comparison misreports as "not a patch change".
    _write(game / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text-1.17")
    _write(reference / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text-1.16")

    by_rel = {d.snapshot_rel: d for d in diff_snapshot(snapshot, game, reference)}

    fresh = by_rel["m10_00_00_00.msb.dcx"]
    assert (fresh.status, fresh.reference_status, fresh.game_vs_reference) == (
        CHANGED,
        SAME,
        CHANGED,
    )
    assert fresh.is_patch_change is True

    stale = by_rel["m11_00_00_00.emevd.dcx"]
    assert (stale.status, stale.reference_status, stale.game_vs_reference) == (
        CHANGED,
        CHANGED,
        SAME,
    )
    assert stale.is_patch_change is False

    both = by_rel["msg/engus/item_dlc02.msgbnd.dcx"]
    assert (both.status, both.reference_status, both.game_vs_reference) == (
        CHANGED,
        CHANGED,
        CHANGED,
    )
    assert both.is_patch_change is True

    untouched = by_rel["regulation.bin"]
    assert (
        untouched.status,
        untouched.reference_status,
        untouched.game_vs_reference,
    ) == (SAME, SAME, SAME)
    assert untouched.is_patch_change is False


def test_diff_snapshot_reference_missing_cannot_classify(
    snapshot: Path, tmp_path: Path
):
    game = _make_game_dir(tmp_path / "Game")
    reference = _make_game_dir(tmp_path / "Game.frozen")
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb-1.17")
    # No reference copy of the map at all.

    diff = {d.snapshot_rel: d for d in diff_snapshot(snapshot, game, reference)}[
        "m10_00_00_00.msb.dcx"
    ]

    assert diff.status == CHANGED
    assert diff.reference_status == MISSING
    assert diff.game_vs_reference == MISSING
    assert diff.is_patch_change is None


def test_diff_snapshot_reports_unmapped_files_without_aborting(
    snapshot: Path, tmp_path: Path
):
    _write(snapshot / "m10_00_00_00.nva.dcx", b"navmesh")
    _write(snapshot / "chr/c0000.chrbnd.dcx", b"player")
    game = _make_game_dir(tmp_path / "Game")
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb")

    by_rel = {d.snapshot_rel: d for d in diff_snapshot(snapshot, game)}

    assert by_rel["m10_00_00_00.nva.dcx"].status == UNMAPPED
    assert by_rel["m10_00_00_00.nva.dcx"].game_rel is None
    assert by_rel["chr/c0000.chrbnd.dcx"].status == UNMAPPED
    # The mapped files are still compared.
    assert by_rel["m10_00_00_00.msb.dcx"].status == SAME


# --- diff_extra_msg ---


def test_diff_extra_msg_lists_only_bundles_outside_snapshot(
    snapshot: Path, tmp_path: Path
):
    game = _make_game_dir(tmp_path / "Game")
    reference = _make_game_dir(tmp_path / "Game.frozen")
    _write(game / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text")  # in snapshot
    _write(game / "msg/engus/item.msgbnd.dcx", b"base-item-1.17")
    _write(reference / "msg/engus/item.msgbnd.dcx", b"base-item")
    _write(game / "msg/frafr/menu.msgbnd.dcx", b"menu-fr")
    _write(reference / "msg/frafr/menu.msgbnd.dcx", b"menu-fr")
    _write(game / "msg/engus/notes.txt", b"ignored")

    extra = {e.game_rel: e for e in diff_extra_msg(snapshot, game, reference)}

    assert set(extra) == {"msg/engus/item.msgbnd.dcx", "msg/frafr/menu.msgbnd.dcx"}
    assert extra["msg/engus/item.msgbnd.dcx"].reference_status == CHANGED
    assert extra["msg/frafr/menu.msgbnd.dcx"].reference_status == SAME


# --- print_report ---


def test_print_report_buckets_changed_files_by_reference(
    snapshot: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
):
    game = _make_game_dir(tmp_path / "Game")
    reference = _make_game_dir(tmp_path / "Game.frozen")
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb-1.17")
    _write(reference / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb")
    _write(game / "event/m11_00_00_00.emevd.dcx", b"leyndell-emevd-1.16")
    _write(reference / "event/m11_00_00_00.emevd.dcx", b"leyndell-emevd-1.16")
    _write(game / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text-1.17")
    _write(reference / "msg/engus/item_dlc02.msgbnd.dcx", b"item-text-1.16")
    _write(snapshot / "m10_00_00_00.nva.dcx", b"navmesh")

    diffs = diff_snapshot(snapshot, game, reference)
    print_report(diffs, [], has_reference=True)
    out = capsys.readouterr().out

    patch_section = out.split("patch changes): 2")[1].split("\n\n")[0]
    assert "map/mapstudio/m10_00_00_00.msb.dcx: 13 -> 18 (+5)" in patch_section
    assert (
        "msg/engus/item_dlc02.msgbnd.dcx: 9 -> 14 (+5)  [snapshot was already stale]"
        in patch_section
    )
    stale_section = out.split("not a patch change): 1")[1].split("\n\n")[0]
    assert "event/m11_00_00_00.emevd.dcx" in stale_section
    assert "event/m11_00_00_00.emevd.dcx" not in patch_section
    assert "unmapped (unknown file type, not compared): 1" in out
    assert "  m10_00_00_00.nva.dcx" in out


def test_print_report_without_reference_lists_changed_only(
    snapshot: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]
):
    game = _make_game_dir(tmp_path / "Game")
    _write(game / "map/mapstudio/m10_00_00_00.msb.dcx", b"stormveil-msb-1.17")
    _write(game / "event/m11_00_00_00.emevd.dcx", b"leyndell-emevd")

    print_report(diff_snapshot(snapshot, game), [], has_reference=False)
    out = capsys.readouterr().out

    assert "changed in game:   1" in out
    assert "missing in game:   1" in out
    assert (
        "Changed (game differs from snapshot):\n  map/mapstudio/m10_00_00_00.msb.dcx"
        in out
    )
    assert "Missing in game:\n  msg/engus/item_dlc02.msgbnd.dcx" in out
    assert "patch changes" not in out


# --- is_unpacked_game_dir ---


def test_is_unpacked_game_dir_requires_loose_files(tmp_path: Path):
    packed = tmp_path / "Packed"
    packed.mkdir()
    (packed / "eldenring.exe").write_bytes(b"")
    assert not is_unpacked_game_dir(packed)
    assert is_unpacked_game_dir(_make_game_dir(tmp_path / "Game"))
