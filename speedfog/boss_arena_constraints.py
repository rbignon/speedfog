"""Boss/arena tag model and compatibility check.

Ported from BossArenaRandomizer: same flag semantics, same bitmap-equivalent
logic. See docs/boss-arena-constraints.md for the compatibility rules.
"""

from __future__ import annotations

import json
import math
import random
from collections.abc import Iterable, Mapping, Sequence
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
    # Expected time to beat the boss in minutes, retries included. Indicative:
    # compared across bosses rather than read in absolute terms. Absent in
    # the JSON means "not annotated".
    weight: float = 1.0


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


def _parse_weight(eid: int, value: Any) -> float:
    """Validate an optional ``boss.weight``: a positive, finite number of minutes."""
    if (
        isinstance(value, bool)
        or not isinstance(value, int | float)
        or not math.isfinite(value)
        or value <= 0
    ):
        raise ValueError(
            f"boss_arena_tags.json entity {eid}: boss.weight must be a positive "
            f"number of minutes, got {value!r}"
        )
    return float(value)


def load_tags(path: Path) -> dict[int, EntityTags]:
    raw: dict[str, dict[str, Any]] = json.loads(Path(path).read_text())
    out: dict[int, EntityTags] = {}
    for key, entry in raw.items():
        eid = int(key)
        boss_block = dict(entry["boss"])
        if "weight" in boss_block:
            boss_block["weight"] = _parse_weight(eid, boss_block["weight"])
        out[eid] = EntityTags(
            entity_id=eid,
            name=entry["name"],
            boss=BossTags(**boss_block),
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
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    rng: random.Random,
    check_size: bool,
    forbidden: Mapping[int, frozenset[int]] | None = None,
) -> dict[int, int]:
    """Randomly assign each arena a compatible boss, no boss used twice.

    Augmenting-path bipartite matching (Hungarian-style): shuffles both sides
    for seed-driven variety, then for each arena finds an augmenting path,
    re-routing earlier assignments when needed. Runs in O(V*E), so tight or
    unsatisfiable compatibility graphs no longer trigger exponential search.
    Raises ``MatchingError`` if no perfect matching exists.

    Args:
        arenas: arena_id -> ArenaTags for every slot to fill. Iteration order
            of the caller's mapping is preserved in the result for stable
            logging and spoilers.
        bosses: boss_id -> BossTags candidate pool. Any pre-filtering (e.g.
            ``exclude_from_pool``) must be applied by the caller.
        rng: Seeded RNG for deterministic output.
        check_size: Apply the size constraint (``boss.size <= arena.size``).
        forbidden: Optional arena_id -> boss IDs that must not be placed in
            that arena (no-vanilla-placement rule: an arena's own entity and
            its multi-phase siblings). Strictly enforced; exclusions can make
            the matching unsatisfiable.

    Returns:
        Mapping arena_id -> boss_id.
    """
    arena_ids = list(arenas.keys())
    rng.shuffle(arena_ids)

    forbidden = forbidden or {}
    candidates: dict[int, list[int]] = {}
    for arena_id in arena_ids:
        arena = arenas[arena_id]
        blocked = forbidden.get(arena_id, frozenset())
        compat = [
            bid
            for bid, btags in bosses.items()
            if bid not in blocked and is_compatible(arena, btags, check_size=check_size)
        ]
        rng.shuffle(compat)
        candidates[arena_id] = compat

    boss_to_arena: dict[int, int] = {}

    def try_augment(arena_id: int, visited: set[int]) -> bool:
        for boss_id in candidates[arena_id]:
            if boss_id in visited:
                continue
            visited.add(boss_id)
            prev = boss_to_arena.get(boss_id)
            if prev is None or try_augment(prev, visited):
                boss_to_arena[boss_id] = arena_id
                return True
        return False

    for arena_id in arena_ids:
        if not try_augment(arena_id, set()):
            hint = ", self/family placement excluded" if forbidden else ""
            raise MatchingError(
                f"No valid arena-boss matching for "
                f"{len(arenas)} arenas against {len(bosses)} candidates{hint}"
            )

    assignment = {aid: bid for bid, aid in boss_to_arena.items()}
    return {aid: assignment[aid] for aid in arenas}


# Slack on the spread comparison, same as generator.pick_cluster_weight_matched.
_SPREAD_EPSILON = 1e-9

# Matcher draws before match_arenas_balanced gives up; the caller's
# MatchingError handling then rerolls the DAG (auto seed) or fails (fixed).
BALANCE_ATTEMPTS = 50


def _layers_within_spread(
    assignment: Mapping[int, int],
    bosses: Mapping[int, BossTags],
    groups: Sequence[Sequence[int]],
    spread: float,
) -> bool:
    """True when every group's placed boss weights fit within ``spread``."""
    for group in groups:
        if len(group) < 2:
            continue
        weights = [bosses[assignment[arena_id]].weight for arena_id in group]
        if max(weights) - min(weights) > spread + _SPREAD_EPSILON:
            return False
    return True


def match_arenas_balanced(
    *,
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    groups: Sequence[Sequence[int]],
    rng: random.Random,
    check_size: bool,
    spread: float,
    forbidden: Mapping[int, frozenset[int]] | None = None,
    attempts: int = BALANCE_ATTEMPTS,
) -> dict[int, int]:
    """``match_arenas_to_bosses`` keeping each layer's boss weights close.

    Rejection sampling: run the unchanged matcher and accept its result when
    every group (the arena slots of one DAG layer) has a ``max - min`` of the
    placed bosses' weights within ``spread``; otherwise draw again with the
    same ``rng``. The accepted matching is a uniform draw among the valid
    ones, so an extreme boss only loses the combinations the rule forbids.
    Suited to a loose rule (keep extremes apart); a tight one exhausts
    ``attempts``.

    ``spread <= 0`` disables the rule. A job the plain matcher cannot solve
    raises its ``MatchingError`` at once: the matching is exact, so a
    redraw cannot help. After ``attempts`` rejected draws, a
    ``MatchingError`` naming the layer weight spread is raised; the caller
    turns it into a DAG reroll.

    The first draw is exactly ``match_arenas_to_bosses`` with the same
    ``rng``: a seed whose plain matching already satisfies the rule keeps
    its assignments.
    """
    tries = max(1, attempts)
    for _ in range(tries):
        assignment = match_arenas_to_bosses(
            arenas=arenas,
            bosses=bosses,
            rng=rng,
            check_size=check_size,
            forbidden=forbidden,
        )
        if spread <= 0 or _layers_within_spread(assignment, bosses, groups, spread):
            return assignment
    raise MatchingError(
        f"no arena-boss matching keeps every layer weight spread within "
        f"{spread} after {tries} attempts (enemy.max_minor_boss_weight_spread "
        f"/ max_major_boss_weight_spread; 0 disables)"
    )


def assign_bosses_uniform(
    *,
    arenas: Mapping[int, ArenaTags],
    pool: Mapping[int, BossTags],
    rng: random.Random,
    check_size: bool,
    forbidden: Mapping[int, frozenset[int]] | None = None,
) -> dict[int, int]:
    """Assign each arena a compatible boss from ``pool``, reuse permitted.

    Used for the ``enemy.bosses`` allowlist (uniform mode): the major/minor
    distinction is already collapsed by the caller, so every arena draws from
    one pool. Because reuse is allowed, each arena chooses independently; the
    least-used compatible boss is picked (ties broken by ``rng``) so a
    multi-boss allowlist spreads evenly. With a single boss, it fills every
    arena.

    ``forbidden`` (arena_id -> boss IDs) is best-effort in this mode: a
    non-forbidden compatible candidate is preferred, but when every
    compatible candidate is forbidden the rule is waived for that arena
    instead of failing (the allowlist is authoritative, so a "Malenia only"
    run can still place Malenia in her own arena).

    Raises ``MatchingError`` if any arena has no compatible boss in ``pool``;
    the message names the arena and points at ``ignore_arena_size`` /
    broadening ``enemy.bosses``.

    Returns ``{arena_id: boss_id}`` in the original ``arenas`` iteration order
    for stable spoilers.
    """
    usage: dict[int, int] = dict.fromkeys(pool, 0)
    arena_ids = list(arenas.keys())
    rng.shuffle(arena_ids)

    out: dict[int, int] = {}
    for arena_id in arena_ids:
        arena = arenas[arena_id]
        compat = [
            bid
            for bid, btags in pool.items()
            if is_compatible(arena, btags, check_size=check_size)
        ]
        if not compat:
            raise MatchingError(
                f"arena {arena_id} has no compatible boss in the allowlist "
                f"(size or fight constraints). Set enemy.ignore_arena_size = "
                f"true or broaden enemy.bosses."
            )
        blocked = forbidden.get(arena_id, frozenset()) if forbidden else frozenset()
        preferred = [bid for bid in compat if bid not in blocked]
        if preferred:
            compat = preferred
        min_use = min(usage[bid] for bid in compat)
        choices = [bid for bid in compat if usage[bid] == min_use]
        rng.shuffle(choices)
        pick = choices[0]
        out[arena_id] = pick
        usage[pick] += 1

    return {aid: out[aid] for aid in arenas}


def resolve_boss_allowlist(
    tags: Mapping[int, EntityTags], names: Iterable[str]
) -> dict[int, BossTags]:
    """Resolve allowlist names to a boss pool.

    Each name is matched case-insensitively as a substring of
    ``EntityTags.name``. Every name must resolve to exactly one entity, else a
    ``ValueError`` is raised (zero matches: typo; multiple: ambiguous). The
    allowlist is authoritative: ``exclude_from_pool`` and DLC flags are NOT
    applied here, because the user named the boss explicitly.

    Returns ``{entity_id: BossTags}`` for the matched entities, key-sorted so
    the result is independent of ``tags`` iteration order.
    """
    pool: dict[int, BossTags] = {}
    for name in names:
        needle = name.strip().lower()
        matches = [
            (eid, entry) for eid, entry in tags.items() if needle in entry.name.lower()
        ]
        if not matches:
            raise ValueError(f"enemy.bosses: no boss matches {name!r}")
        if len(matches) > 1:
            found = ", ".join(sorted(entry.name for _, entry in matches))
            raise ValueError(f"enemy.bosses: {name!r} is ambiguous: matches [{found}]")
        eid, entry = matches[0]
        pool[eid] = entry.boss
    return dict(sorted(pool.items()))
