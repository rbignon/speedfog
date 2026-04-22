"""Boss/arena tag model and compatibility check.

Ported from BossArenaRandomizer: same flag semantics, same bitmap-equivalent
logic. See docs/boss-arena-constraints.md for the compatibility rules.
"""

from __future__ import annotations

import json
import random
from collections.abc import Mapping
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


def _mrv_sort(arena_ids: list[int], candidates: Mapping[int, list[int]]) -> None:
    """Reorder ``arena_ids`` in-place: most-constrained (fewest candidates) first.

    Stable sort preserves the caller's input order for ties, so a prior shuffle
    still drives variety between arenas with the same candidate count.
    """
    arena_ids.sort(key=lambda a: len(candidates[a]))


def match_arenas_to_bosses(
    *,
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    rng: random.Random,
    check_size: bool,
) -> dict[int, int]:
    """Randomly assign each arena a compatible boss, no boss used twice.

    Greedy with backtracking under MRV (most constrained arena first): shuffles
    both sides for seed-driven variety, then orders arenas by ascending
    candidate count so pathological branches are pruned early. The stable sort
    preserves the shuffle order for ties, keeping variety across seeds. Raises
    ``MatchingError`` if no perfect matching exists.

    Args:
        arenas: arena_id -> ArenaTags for every slot to fill. Iteration order
            of the caller's mapping is preserved in the result for stable
            logging and spoilers.
        bosses: boss_id -> BossTags candidate pool. Any pre-filtering (e.g.
            ``exclude_from_pool``) must be applied by the caller.
        rng: Seeded RNG for deterministic output.
        check_size: Apply the size constraint (``boss.size <= arena.size``).

    Returns:
        Mapping arena_id -> boss_id.
    """
    arena_ids = list(arenas.keys())
    rng.shuffle(arena_ids)

    candidates: dict[int, list[int]] = {}
    for arena_id in arena_ids:
        arena = arenas[arena_id]
        compat = [
            bid
            for bid, btags in bosses.items()
            if is_compatible(arena, btags, check_size=check_size)
        ]
        rng.shuffle(compat)
        candidates[arena_id] = compat

    _mrv_sort(arena_ids, candidates)

    assignment: dict[int, int] = {}
    used: set[int] = set()

    def backtrack(idx: int) -> bool:
        if idx == len(arena_ids):
            return True
        arena_id = arena_ids[idx]
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

    return {aid: assignment[aid] for aid in arenas}
