"""Boss/arena tag model and compatibility check.

Ported from BossArenaRandomizer: same flag semantics, same bitmap-equivalent
logic. See docs/boss-arena-constraints.md for the compatibility rules.
"""

from __future__ import annotations

import json
import random
from collections.abc import Iterable, Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True, slots=True)
class BossTags:
    size: int
    type: int
    is_two_phase: bool
    is_dragon: bool
    is_npc: bool
    can_escape: bool
    night_boss: bool
    exclude_from_pool: bool


@dataclass(frozen=True, slots=True)
class ArenaTags:
    size: int
    type: int
    two_phase_not_allowed: bool
    dragon_not_allowed: bool
    npc_not_allowed: bool
    is_escapable: bool
    night_boss: bool


@dataclass(frozen=True, slots=True)
class EntityTags:
    entity_id: int
    name: str
    boss: BossTags  # always present
    arena: ArenaTags | None  # None for source-only entries
    pool: str | None  # "minor"/"major" for source-only, None otherwise
    region: int
    scaling: int
    dlc: bool


def load_tags(path: Path) -> dict[int, EntityTags]:
    raw: dict[str, dict[str, Any]] = json.loads(Path(path).read_text())
    out: dict[int, EntityTags] = {}
    for key, entry in raw.items():
        eid = int(key)
        out[eid] = EntityTags(
            entity_id=eid,
            name=entry["name"],
            boss=BossTags(**entry["boss"]),
            arena=ArenaTags(**entry["arena"]) if "arena" in entry else None,
            pool=entry.get("pool"),
            region=int(entry.get("region", 0)),
            scaling=int(entry.get("scaling", 0)),
            dlc=bool(entry.get("dlc", False)),
        )
    return out


def is_compatible(arena: ArenaTags, boss: BossTags, *, check_size: bool) -> bool:
    if arena.dragon_not_allowed and boss.is_dragon:
        return False
    if arena.two_phase_not_allowed and boss.is_two_phase:
        return False
    if arena.npc_not_allowed and boss.is_npc:
        return False
    if arena.is_escapable and boss.can_escape:
        return False
    if check_size and boss.size > arena.size:
        return False
    return True


class MatchingError(RuntimeError):
    """Raised when no valid arena-boss assignment exists."""


def match_arenas_to_bosses(
    *,
    arena_ids: Iterable[int],
    boss_ids: Iterable[int],
    tags: Mapping[int, EntityTags],
    rng: random.Random,
    check_size: bool,
) -> dict[int, int]:
    """Randomly assign each arena a compatible boss, no boss used twice.

    Greedy with backtracking: shuffles both sides, then at each step picks the
    first compatible candidate that has not been used. Backtracks when no
    candidate works. Raises ``MatchingError`` if no perfect matching exists.

    Args:
        arena_ids: Entity IDs of arenas to fill (order preserved only for
            logging; the matcher shuffles internally).
        boss_ids: Candidate boss entity IDs (superset allowed).
        tags: Tag map covering every id in ``arena_ids`` and ``boss_ids``.
        rng: Seeded RNG for deterministic output.
        check_size: Apply the size constraint (``boss.size <= arena.size``).

    Returns:
        Mapping arena_id -> boss_id.
    """
    arenas = list(arena_ids)
    bosses = list(boss_ids)
    missing = [i for i in (*arenas, *bosses) if i not in tags]
    if missing:
        raise KeyError(f"tags missing entries: {missing}")

    # Excluded bosses never appear as sources, but their own arenas remain valid
    # targets (they can receive a replacement from the pool).
    eligible_sources = [b for b in bosses if not tags[b].boss.exclude_from_pool]

    shuffled_arenas = arenas[:]
    rng.shuffle(shuffled_arenas)

    candidates: dict[int, list[int]] = {}
    for arena_id in shuffled_arenas:
        arena = tags[arena_id].arena
        if arena is None:
            raise ValueError(f"arena_id {arena_id} has no arena block in tags")
        compat = [
            bid
            for bid in eligible_sources
            if is_compatible(arena, tags[bid].boss, check_size=check_size)
        ]
        rng.shuffle(compat)
        candidates[arena_id] = compat

    assignment: dict[int, int] = {}
    used: set[int] = set()

    def backtrack(idx: int) -> bool:
        if idx == len(shuffled_arenas):
            return True
        arena_id = shuffled_arenas[idx]
        for boss_id in candidates[arena_id]:
            if boss_id in used:
                continue
            assignment[arena_id] = boss_id
            used.add(boss_id)
            if backtrack(idx + 1):
                return True
            used.remove(boss_id)
            del assignment[arena_id]
        return False

    if not backtrack(0):
        raise MatchingError(
            f"No valid arena-boss matching for "
            f"{len(arenas)} arenas against {len(bosses)} candidates"
        )

    # Return in original arena order for stability in downstream output.
    return {aid: assignment[aid] for aid in arenas}
