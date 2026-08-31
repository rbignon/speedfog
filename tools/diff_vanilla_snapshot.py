#!/usr/bin/env python3
"""Hash-diff an unpacked game directory against FogRando's eldendata/Vanilla snapshot.

FogMod never reads the installed game while writing: every file it edits
comes from the bundled snapshot in writer/FogModWrapper/eldendata/Vanilla
(see docs/game-patch-migration.md). When a game patch lands, this tool lists
which snapshot files the patch actually touched, which is the input of every
refresh decision.

The game directory must be unpacked to loose files first (UXM, Nuxe, or an
equivalent), so that map/mapstudio/, event/, script/talk/, msg/, sfx/ and
regulation.bin exist next to eldenring.exe.

Usage:
    python tools/diff_vanilla_snapshot.py /path/to/Game
    python tools/diff_vanilla_snapshot.py /path/to/Game --reference /path/to/Game.frozen

With --reference, a second (pre-patch) unpacked game directory is hashed as
well, which turns the two-way diff into a three-way one:

- game differs from reference: a patch change, whatever the snapshot holds;
- game equals reference but differs from the snapshot: the snapshot was
  already stale (older game version, or a file FogMod does not use), not a
  patch change.

A few injectors read from the installed game rather than from the snapshot
(RunCompleteInjector: menu_dlc02 of every language; TextTheme: item and
item_dlc02). For those the game-versus-reference column is the one that
matters. Message bundles the snapshot does not carry at all (item_dlc01,
menu_dlc01, ngword, the araae language) are listed separately, compared to
the reference only.
"""

from __future__ import annotations

import argparse
import hashlib
import sys
from dataclasses import dataclass
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SNAPSHOT = PROJECT_ROOT / "writer" / "FogModWrapper" / "eldendata" / "Vanilla"

# Snapshot file suffix -> directory under the game root holding that file.
SUFFIX_DIRS = {
    ".msb.dcx": "map/mapstudio",
    ".emevd.dcx": "event",
    ".talkesdbnd.dcx": "script/talk",
    ".ffxbnd.dcx": "sfx",
}

# Markers proving the game directory has been unpacked to loose files.
UNPACKED_MARKERS = ("regulation.bin", "map/mapstudio", "event", "msg")

SAME = "same"
CHANGED = "changed"
MISSING = "missing"
# Snapshot file whose game location is unknown to this tool (new file type).
UNMAPPED = "unmapped"


@dataclass
class FileDiff:
    """Comparison of one snapshot file with its counterparts in the game directories."""

    snapshot_rel: str
    # None when the snapshot file could not be mapped to a game location.
    game_rel: str | None
    # Game versus snapshot: SAME, CHANGED, MISSING or UNMAPPED.
    status: str
    snapshot_size: int
    game_size: int | None
    # Reference versus snapshot; None without --reference or when unmapped.
    reference_status: str | None = None
    # Game versus reference (MISSING when the reference file is absent);
    # None without --reference or when unmapped.
    game_vs_reference: str | None = None

    @property
    def is_patch_change(self) -> bool | None:
        """True when the game file differs from the reference, None without a usable reference."""
        if self.game_vs_reference in (None, MISSING):
            return None
        return self.game_vs_reference == CHANGED


@dataclass
class ExtraMsgDiff:
    """A message bundle present in the game but absent from the snapshot."""

    game_rel: str
    game_size: int
    # Game versus reference; None without --reference.
    reference_status: str | None = None


def game_path_for(snapshot_rel: str) -> str:
    """Map a snapshot-relative path to the matching game-relative path.

    The snapshot keeps map, event, talk and sfx bundles flat at its root and
    mirrors msg/<lang>/ and regulation.bin as-is.
    """
    rel = snapshot_rel.replace("\\", "/")
    if rel == "regulation.bin" or rel.startswith("msg/"):
        return rel
    if "/" in rel:
        raise ValueError(f"Unexpected snapshot subdirectory: {rel}")
    for suffix, directory in SUFFIX_DIRS.items():
        if rel.endswith(suffix):
            return f"{directory}/{rel}"
    raise ValueError(f"Unexpected snapshot file: {rel}")


def iter_snapshot_files(snapshot_dir: Path) -> list[str]:
    """Return sorted snapshot-relative paths of every file in the snapshot."""
    return sorted(
        p.relative_to(snapshot_dir).as_posix()
        for p in snapshot_dir.rglob("*")
        if p.is_file()
    )


def file_digest(path: Path) -> str:
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha1").hexdigest()


def compare_files(baseline: Path, baseline_hash: str, candidate: Path) -> str:
    """SAME, CHANGED or MISSING for candidate against an already hashed baseline."""
    if not candidate.is_file():
        return MISSING
    if candidate.stat().st_size != baseline.stat().st_size:
        return CHANGED
    return SAME if file_digest(candidate) == baseline_hash else CHANGED


def is_unpacked_game_dir(game_dir: Path) -> bool:
    return all((game_dir / marker).exists() for marker in UNPACKED_MARKERS)


def diff_snapshot(
    snapshot_dir: Path,
    game_dir: Path,
    reference_dir: Path | None = None,
) -> list[FileDiff]:
    """Compare every snapshot file with the game (and optional reference) copy."""
    results: list[FileDiff] = []
    for snapshot_rel in iter_snapshot_files(snapshot_dir):
        snapshot_file = snapshot_dir / snapshot_rel
        snapshot_size = snapshot_file.stat().st_size
        try:
            game_rel = game_path_for(snapshot_rel)
        except ValueError:
            results.append(
                FileDiff(
                    snapshot_rel=snapshot_rel,
                    game_rel=None,
                    status=UNMAPPED,
                    snapshot_size=snapshot_size,
                    game_size=None,
                )
            )
            continue

        snapshot_hash = file_digest(snapshot_file)
        game_file = game_dir / game_rel
        status = compare_files(snapshot_file, snapshot_hash, game_file)
        reference_status = None
        game_vs_reference = None
        if reference_dir is not None:
            reference_file = reference_dir / game_rel
            reference_status = compare_files(
                snapshot_file, snapshot_hash, reference_file
            )
            if reference_status == MISSING:
                game_vs_reference = MISSING
            elif status == MISSING:
                game_vs_reference = CHANGED
            elif status == SAME and reference_status == SAME:
                game_vs_reference = SAME
            else:
                game_vs_reference = compare_files(
                    reference_file, file_digest(reference_file), game_file
                )
        results.append(
            FileDiff(
                snapshot_rel=snapshot_rel,
                game_rel=game_rel,
                status=status,
                snapshot_size=snapshot_size,
                game_size=game_file.stat().st_size if game_file.is_file() else None,
                reference_status=reference_status,
                game_vs_reference=game_vs_reference,
            )
        )
    return results


def diff_extra_msg(
    snapshot_dir: Path,
    game_dir: Path,
    reference_dir: Path | None = None,
) -> list[ExtraMsgDiff]:
    """List game msg bundles the snapshot does not carry, compared to the reference."""
    snapshot_msg = {
        p.relative_to(snapshot_dir).as_posix()
        for p in (snapshot_dir / "msg").rglob("*.msgbnd.dcx")
    }
    results: list[ExtraMsgDiff] = []
    for game_file in sorted((game_dir / "msg").rglob("*.msgbnd.dcx")):
        game_rel = game_file.relative_to(game_dir).as_posix()
        if game_rel in snapshot_msg:
            continue
        reference_status = None
        if reference_dir is not None:
            reference_status = compare_files(
                game_file, file_digest(game_file), reference_dir / game_rel
            )
        results.append(
            ExtraMsgDiff(
                game_rel=game_rel,
                game_size=game_file.stat().st_size,
                reference_status=reference_status,
            )
        )
    return results


def _size_delta(diff: FileDiff) -> str:
    if diff.game_size is None:
        return "absent"
    delta = diff.game_size - diff.snapshot_size
    return f"{diff.snapshot_size} -> {diff.game_size} ({delta:+d})"


def print_report(
    diffs: list[FileDiff],
    extra_msg: list[ExtraMsgDiff],
    has_reference: bool,
) -> None:
    changed = [d for d in diffs if d.status == CHANGED]
    missing = [d for d in diffs if d.status == MISSING]
    same = [d for d in diffs if d.status == SAME]
    unmapped = [d for d in diffs if d.status == UNMAPPED]

    print(f"Snapshot files: {len(diffs)}")
    print(f"  unchanged in game: {len(same)}")
    print(f"  changed in game:   {len(changed)}")
    print(f"  missing in game:   {len(missing)}")
    if unmapped:
        print(f"  unmapped (unknown file type, not compared): {len(unmapped)}")

    if has_reference:
        patch_changes = [d for d in changed if d.is_patch_change is True]
        stale_only = [d for d in changed if d.is_patch_change is False]
        unknown = [d for d in changed if d.is_patch_change is None]
        print(
            "\nChanged, game differs from reference (patch changes):"
            f" {len(patch_changes)}"
        )
        for d in patch_changes:
            note = (
                "" if d.reference_status == SAME else "  [snapshot was already stale]"
            )
            print(f"  {d.game_rel}: {_size_delta(d)}{note}")
        print(
            "\nChanged, game equals reference (stale snapshot, not a patch change):"
            f" {len(stale_only)}"
        )
        for d in stale_only:
            print(f"  {d.game_rel}: {_size_delta(d)}")
        if unknown:
            print(f"\nChanged, absent from reference (cannot classify): {len(unknown)}")
            for d in unknown:
                print(f"  {d.game_rel}: {_size_delta(d)}")
    elif changed:
        print("\nChanged (game differs from snapshot):")
        for d in changed:
            print(f"  {d.game_rel}: {_size_delta(d)}")

    if missing:
        print("\nMissing in game:")
        for d in missing:
            print(f"  {d.game_rel}")

    if unmapped:
        print("\nUnmapped snapshot files (extend SUFFIX_DIRS):")
        for d in unmapped:
            print(f"  {d.snapshot_rel}")

    if extra_msg:
        print(f"\nMessage bundles outside the snapshot: {len(extra_msg)}")
        if has_reference:
            extra_changed = [e for e in extra_msg if e.reference_status != SAME]
            print(f"  differing from reference: {len(extra_changed)}")
            for e in extra_changed:
                print(f"  {e.game_rel}: {e.reference_status}")
        else:
            print("  (no --reference: cannot tell whether they changed)")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Hash-diff an unpacked game directory against eldendata/Vanilla",
    )
    parser.add_argument(
        "game_dir",
        type=Path,
        help="Unpacked game directory (contains eldenring.exe and loose files)",
    )
    parser.add_argument(
        "--snapshot",
        type=Path,
        default=DEFAULT_SNAPSHOT,
        help=f"Snapshot directory (default: {DEFAULT_SNAPSHOT})",
    )
    parser.add_argument(
        "--reference",
        type=Path,
        default=None,
        help="Pre-patch unpacked game directory, to separate patch changes from stale snapshot files",
    )
    args = parser.parse_args()

    if not args.snapshot.is_dir():
        print(f"Error: snapshot directory not found: {args.snapshot}", file=sys.stderr)
        return 1
    for label, path in (("game", args.game_dir), ("reference", args.reference)):
        if path is None:
            continue
        if not is_unpacked_game_dir(path):
            print(
                f"Error: {label} directory {path} is not an unpacked game directory "
                f"(expected {', '.join(UNPACKED_MARKERS)})",
                file=sys.stderr,
            )
            return 1

    print(f"Snapshot:  {args.snapshot}")
    print(f"Game:      {args.game_dir}")
    if args.reference is not None:
        print(f"Reference: {args.reference}")
    print()

    diffs = diff_snapshot(args.snapshot, args.game_dir, args.reference)
    extra_msg = diff_extra_msg(args.snapshot, args.game_dir, args.reference)
    print_report(diffs, extra_msg, args.reference is not None)
    return 0


if __name__ == "__main__":
    sys.exit(main())
