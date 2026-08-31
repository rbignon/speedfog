#!/usr/bin/env python3
"""Refresh the bundled FogMod snapshot from an unpacked game directory.

FogMod (writer/FogModWrapper/eldendata/Vanilla) never reads the installed
game: every seed is built from this bundled snapshot. After a game patch,
the executable may require param rows that only exist in the patched
regulation.bin (Elden Ring 1.17: RideParam 80020-80050 for Torrent), so the
snapshot must move to the patched files. See docs/game-patch-migration.md.

The Item Randomizer (v0.12+) extracts its own diste/Vanilla snapshot from
the installed game directory on the fly, driven by its files.txt manifest,
so it needs no refreshing here.

By default this copies regulation.bin and every message bundle the snapshot
already carries (msg/<lang>/*.msgbnd.dcx). Map files (MSB, EMEVD, talk ESD)
are left alone unless named with --file, because refreshing a map exposes
fog.txt and fogevents.txt to moved entities and is a per-map decision; --all
refreshes every file the snapshot carries, for the day the maps move too.
Seeds are 1.17-only now, so --all is the standard post-bootstrap invocation.

The snapshot is gitignored and re-copied wholesale by tools/bootstrap.py,
which therefore runs this refresh itself as its final step (--all; skip
with the bootstrap's --no-refresh flag). Manual runs are for game-patch
triage and one-off --file refreshes; check the regulation.bin md5 printed
at the end against the one recorded in the playbook.

Usage:
    python tools/refresh_vanilla_snapshot.py /path/to/Game --dry-run
    python tools/refresh_vanilla_snapshot.py /path/to/Game
    python tools/refresh_vanilla_snapshot.py /path/to/Game --file m60_52_39_00.emevd.dcx
    python tools/refresh_vanilla_snapshot.py /path/to/Game --all
"""

from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

from diff_vanilla_snapshot import (
    PROJECT_ROOT,
    file_digest,
    game_path_for,
    is_unpacked_game_dir,
    iter_snapshot_files,
)

SNAPSHOTS = {
    "fogmod": PROJECT_ROOT / "writer" / "FogModWrapper" / "eldendata" / "Vanilla",
}

REPLACED = "replaced"
UP_TO_DATE = "up to date"
MISSING_IN_GAME = "missing in game"
MISSING_IN_SNAPSHOT = "missing in snapshot"


@dataclass
class RefreshResult:
    snapshot_rel: str
    game_rel: str
    status: str


def default_targets(snapshot_dir: Path) -> list[str]:
    """regulation.bin plus every message bundle the snapshot carries."""
    targets = ["regulation.bin"]
    msg_dir = snapshot_dir / "msg"
    if msg_dir.is_dir():
        targets.extend(
            sorted(
                p.relative_to(snapshot_dir).as_posix()
                for p in msg_dir.rglob("*.msgbnd.dcx")
            )
        )
    return targets


def validate_extra_file(snapshot_rel: str) -> str:
    """Return the game path of an extra --file target, or raise ValueError."""
    if ".." in Path(snapshot_rel).parts:
        raise ValueError(f"path escapes the snapshot: {snapshot_rel}")
    return game_path_for(snapshot_rel)


def all_targets(snapshot_dir: Path) -> tuple[list[str], list[str]]:
    """Every snapshot file with a known game location, plus the ones without one."""
    targets: list[str] = []
    skipped: list[str] = []
    for rel in iter_snapshot_files(snapshot_dir):
        try:
            game_path_for(rel)
        except ValueError:
            skipped.append(rel)
            continue
        targets.append(rel)
    return targets, skipped


def plan_targets(
    snapshot_dir: Path, extra_files: list[str], all_files: bool = False
) -> list[str]:
    """Default (or all) targets followed by the extra files not already among them."""
    targets = (
        all_targets(snapshot_dir)[0] if all_files else default_targets(snapshot_dir)
    )
    for extra in extra_files:
        if extra not in targets:
            targets.append(extra)
    return targets


def refresh_snapshot(
    snapshot_dir: Path,
    game_dir: Path,
    targets: list[str],
    dry_run: bool = False,
) -> list[RefreshResult]:
    """Copy each target from the game into the snapshot when the content differs."""
    results: list[RefreshResult] = []
    for snapshot_rel in targets:
        game_rel = game_path_for(snapshot_rel)
        snapshot_file = snapshot_dir / snapshot_rel
        game_file = game_dir / game_rel
        if not game_file.is_file():
            results.append(RefreshResult(snapshot_rel, game_rel, MISSING_IN_GAME))
            continue
        if not snapshot_file.is_file():
            results.append(RefreshResult(snapshot_rel, game_rel, MISSING_IN_SNAPSHOT))
            continue
        if file_digest(snapshot_file) == file_digest(game_file):
            results.append(RefreshResult(snapshot_rel, game_rel, UP_TO_DATE))
            continue
        if not dry_run:
            # Copy next to the target, then rename: an interrupted copy must
            # not leave a truncated regulation.bin behind.
            tmp = snapshot_file.with_name(snapshot_file.name + ".refresh-tmp")
            shutil.copyfile(game_file, tmp)
            os.replace(tmp, snapshot_file)
        results.append(RefreshResult(snapshot_rel, game_rel, REPLACED))
    return results


def md5(path: Path) -> str:
    """md5 is what the playbook records for regulation.bin (file_digest uses sha1)."""
    with path.open("rb") as f:
        return hashlib.file_digest(f, "md5").hexdigest()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Refresh regulation.bin and msg bundles of the bundled FogMod snapshot from an unpacked game directory",
    )
    parser.add_argument(
        "game_dir",
        type=Path,
        help="Unpacked game directory (contains eldenring.exe and loose files)",
    )
    parser.add_argument(
        "--snapshot",
        choices=sorted(SNAPSHOTS),
        action="append",
        default=None,
        help="Snapshot to refresh (repeatable; default: the fogmod snapshot, currently the only one)",
    )
    parser.add_argument(
        "--file",
        action="append",
        default=[],
        metavar="SNAPSHOT_REL",
        help="Extra snapshot-relative file to refresh, e.g. m60_52_39_00.emevd.dcx (repeatable)",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Refresh every file the snapshot carries (maps, events, talk ESDs included), not only "
        "regulation.bin and msg",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Report what would be replaced without writing",
    )
    args = parser.parse_args(argv)

    if not is_unpacked_game_dir(args.game_dir):
        print(
            f"Error: {args.game_dir} is not an unpacked game directory", file=sys.stderr
        )
        return 1

    existing = [name for name, path in SNAPSHOTS.items() if path.is_dir()]
    names = args.snapshot or existing
    if not names:
        print(
            "Error: no snapshot directory found (run tools/bootstrap.py first)",
            file=sys.stderr,
        )
        return 1
    for name in names:
        if name not in existing:
            print(
                f"Error: {name} snapshot not found: {SNAPSHOTS[name]}", file=sys.stderr
            )
            return 1
    for extra in args.file:
        try:
            validate_extra_file(extra)
        except ValueError as exc:
            print(f"Error: --file {extra}: {exc}", file=sys.stderr)
            return 1

    problems = 0
    for name in names:
        snapshot_dir = SNAPSHOTS[name]
        print(f"== {name}: {snapshot_dir}{' (dry run)' if args.dry_run else ''}")
        targets = plan_targets(snapshot_dir, args.file, args.all)
        if args.all:
            for rel in all_targets(snapshot_dir)[1]:
                print(f"  skipped (unknown file type, extend SUFFIX_DIRS): {rel}")
                problems += 1
        results = refresh_snapshot(snapshot_dir, args.game_dir, targets, args.dry_run)
        for r in results:
            if r.status == UP_TO_DATE:
                continue
            label = (
                "would replace" if args.dry_run and r.status == REPLACED else r.status
            )
            print(f"  {label}: {r.snapshot_rel}  <-  {r.game_rel}")
            if r.status in (MISSING_IN_GAME, MISSING_IN_SNAPSHOT):
                problems += 1
        counts = {
            s: sum(1 for r in results if r.status == s) for s in (REPLACED, UP_TO_DATE)
        }
        verb = "would replace" if args.dry_run else "replaced"
        print(f"  {verb} {counts[REPLACED]}, up to date {counts[UP_TO_DATE]}")
        regulation = snapshot_dir / "regulation.bin"
        if regulation.is_file():
            label = (
                "current regulation.bin md5" if args.dry_run else "regulation.bin md5"
            )
            print(f"  {label}: {md5(regulation)}")

    return 2 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
