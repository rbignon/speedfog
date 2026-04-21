"""Port BossArenaRandomizer tag JSON into a merged speedfog data file.

Reads:
  <bar_dir>/bosses.json      (boss tags keyed by display name)
  <bar_dir>/bossArena.json   (arena tags keyed by display name)

Writes:
  <out_path>                 (JSON dict keyed by entity id string, values
                              describe boss/arena/pool tags)

- Entries in bossArena.json carry BOTH ``boss`` and ``arena`` blocks. The
  ``boss.exclude_from_pool`` flag is set to ``True`` if the entity ID is in
  ``EXCLUDE_FROM_POOL_IDS`` (replaces the C# MinorBossRemoveSourceNames list,
  manually resolved to entity IDs below).
- Promoted minor-pool IDs (``EXTRA_MINOR_POOL_ENTRIES``, from C#'s
  ExtraMinorBossPoolIds) are materialized as source-only entries ONLY when
  they are not already present in BAR. BAR always wins.
- BASIC_REMOVE_SOURCE_IDS (C# constant) is intentionally dropped: it
  configured the Basic pool in class-based randomization, which only applies
  to arenas outside the DAG. SpeedFog runs never visit those arenas.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

BOSS_FLAG_MAP = {
    "isTwoPhase": "is_two_phase",
    "isDragon": "is_dragon",
    "isNPC": "is_npc",
    "canEscape": "can_escape",
    "nightBoss": "night_boss",
}

ARENA_FLAG_MAP = {
    "twoPhaseNotAllowed": "two_phase_not_allowed",
    "dragonNotAllowed": "dragon_not_allowed",
    "npcNotAllowed": "npc_not_allowed",
    "isEscapable": "is_escapable",
    "nightBoss": "night_boss",
}

# Promoted minor-pool IDs. (entity_id, display_name) from
# writer/ItemRandomizerWrapper/Program.cs:249-282 (ExtraMinorBossPoolIds).
# Names come from the inline // comments in that file.
# Task 6 deletes the C# constant after this port.
EXTRA_MINOR_POOL_ENTRIES: tuple[tuple[int, str], ...] = (
    (1051400299, "Guardian Golem"),
    (1051570310, "Elder Lion"),
    (20010451, "Divine Bird Warrior (Lightning)"),
    (20010450, "Hornsent"),
    (20010453, "Divine Bird Warrior (Frost)"),
    (20010455, "Divine Bird Warrior (Wind)"),
    (1035430230, "Lobster"),
    (21000453, "Fire Knight (Drained Church District)"),
    (21010461, "Fire Knight (before Messmer)"),
    (21010450, "Fire Knight (First Floor)"),
    (11000495, "Crucible Knight"),
    (13000295, "Crucible Knight"),
    (42000200, "Smith Golem"),
    (42030300, "Smith Golem"),
    (1051530322, "Colossal Fingercreeper"),
    (1051530324, "Colossal Fingercreeper"),
    (11000394, "Colossal Fingercreeper"),
    (12010715, "Blaidd"),
    (14000499, "Moongrum"),
    (2047440360, "Moonrithyll"),
    (2048380800, "DLC's Tibia Mariner"),
    (22000460, "Golden Leonine Misbegotten"),
    (35000486, "Omen"),
    (40010301, "Large Bigmouth Imp"),
    (2045470200, "Crucible Knight Devonia"),
    (1035540200, "Fire Prelate"),
    (1039510800, "Death Rite Bird"),
    (1043370340, "Deathbird"),
    (1047400800, "Night's cavalry"),
)

# Entity IDs to exclude from the source pool in the Python matcher.
# Resolved manually from writer/ItemRandomizerWrapper/Program.cs:302-315
# (MinorBossRemoveSourceNames) against BAR's bosses.json. Each entry here is
# the concrete entity ID that corresponds to one of those category names.
# If BAR grows a new entry for an excluded archetype, add its ID here.
# Task 6 deletes the C# constant after this port.
EXCLUDE_FROM_POOL_IDS: frozenset[int] = frozenset(
    {
        # Night's Cavalry (all locations)
        1043370340,  # Limgrave
        1044320342,  # Weeping Peninsula
        1047400800,  # Caelid
        # Cemetery Shade Boss
        30000800,
        # Guardian Golem Boss
        31170800,
        # Tibia Mariner Boss
        1045390800,
        # Erdtree Avatar Boss (all weeping/field variants)
        1043330800,  # Weeping
        # Ulcerated Tree Spirit Boss
        18000800,
        # Putrid Avatar / Burial Watchdog Boss variants (cross-check against
        # BAR bossArena.json; extend this set when porting finds new IDs).
        30020800,  # Fire Erdtree Burial Watchdog
        30010800,  # Erdtree Burial Watchdog and Imps
        # Divine Beast Dancing Lion and Basilisks
        2046460800,
    }
)


def _as_bool(v: Any) -> bool:
    return bool(int(v)) if v is not None else False


def _boss_block(boss: dict[str, Any], exclude_from_pool: bool) -> dict[str, Any]:
    return {
        "size": int(boss["bossSize"]),
        "type": int(boss["bossType"]),
        **{dst: _as_bool(boss.get(src, 0)) for src, dst in BOSS_FLAG_MAP.items()},
        "exclude_from_pool": exclude_from_pool,
    }


def _arena_block(arena: dict[str, Any]) -> dict[str, Any]:
    return {
        "size": int(arena["arenaSize"]),
        "type": int(arena["arenaType"]),
        **{dst: _as_bool(arena.get(src, 0)) for src, dst in ARENA_FLAG_MAP.items()},
    }


def _source_only_boss_block() -> dict[str, Any]:
    """Neutral boss tags for source-only promoted entries (no BAR data).

    Defaults to a small, unconstrained boss that fits any arena. Refine the
    generated file by hand if specific tags matter (e.g. mark a dragon).
    """
    return {
        "size": 1,
        "type": 1,
        "is_two_phase": False,
        "is_dragon": False,
        "is_npc": False,
        "can_escape": False,
        "night_boss": False,
        "exclude_from_pool": False,
    }


def build_entities(
    bosses: dict[str, dict[str, Any]],
    arenas: dict[str, dict[str, Any]],
) -> dict[str, dict[str, Any]]:
    """Merge BAR tables and add SpeedFog's promoted source-only entries."""
    out: dict[str, dict[str, Any]] = {}

    # Every arena MUST have a boss entry.
    for name, arena in arenas.items():
        boss = bosses.get(name)
        if boss is None:
            raise KeyError(f"Arena entry {name!r} has no matching boss entry")
        entity_id = str(arena["id"])
        if entity_id != str(boss["id"]):
            raise ValueError(
                f"ID mismatch for {name!r}: boss={boss['id']} arena={arena['id']}"
            )
        out[entity_id] = {
            "name": name,
            "boss": _boss_block(boss, int(entity_id) in EXCLUDE_FROM_POOL_IDS),
            "arena": _arena_block(arena),
            "region": int(boss.get("region", 0)),
            "scaling": int(boss.get("scaling", 0)),
            "dlc": _as_bool(boss.get("dlc", 0)),
        }

    # Bosses that do NOT have a matching arena are already source-only in BAR.
    for name, boss in bosses.items():
        entity_id = str(boss["id"])
        if entity_id in out:
            continue
        out[entity_id] = {
            "name": name,
            "boss": _boss_block(boss, int(entity_id) in EXCLUDE_FROM_POOL_IDS),
            "region": int(boss.get("region", 0)),
            "scaling": int(boss.get("scaling", 0)),
            "dlc": _as_bool(boss.get("dlc", 0)),
        }

    # Promoted minor-pool IDs that are NOT in BAR: create source-only entries.
    # If an ID is ALREADY in BAR (via arena or source-only), do not touch it:
    # BAR's name/data is authoritative.
    for eid, comment_name in EXTRA_MINOR_POOL_ENTRIES:
        key = str(eid)
        if key in out:
            continue
        out[key] = {
            "name": comment_name,
            "boss": _source_only_boss_block(),
            "pool": "minor",
            "region": 0,
            "scaling": 0,
            "dlc": False,
        }

    return out


def port(bar_dir: Path, out_path: Path) -> None:
    bosses = json.loads((bar_dir / "bosses.json").read_text())
    arenas = json.loads((bar_dir / "bossArena.json").read_text())
    entities = build_entities(bosses, arenas)
    sorted_data = {k: entities[k] for k in sorted(entities.keys(), key=int)}
    out_path.write_text(json.dumps(sorted_data, indent=2) + "\n")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--bar-dir",
        type=Path,
        required=True,
        help="Path to BossArenaRandomizer/BossArenaRandomizer directory",
    )
    parser.add_argument(
        "--out", type=Path, required=True, help="Output path for merged JSON"
    )
    args = parser.parse_args(argv)
    port(args.bar_dir, args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
