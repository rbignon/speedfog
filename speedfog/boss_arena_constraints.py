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

# Scaling tier bands of boss.weight (docs/boss-arena-constraints.md, "Layer
# weight balance"): bosses react differently to scaling, so weights are
# measured per band. All boss nodes of a layer share one tier.
WEIGHT_BANDS = ("early", "mid", "late")
_EARLY_MAX_TIER = 8
_MID_MAX_TIER = 14


def weight_band(tier: int) -> str:
    """Band of a scaling tier: early (<= 8), mid (9-14) or late (>= 15)."""
    if tier <= _EARLY_MAX_TIER:
        return "early"
    if tier <= _MID_MAX_TIER:
        return "mid"
    return "late"


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
    # Expected time to beat the boss in minutes, retries included, per scaling
    # band (early, mid, late), each band brought to a mid-run tier's scale:
    # compared across bosses rather than read in absolute terms. Absent in
    # the JSON means "not annotated".
    weights: tuple[float, float, float] = (1.0, 1.0, 1.0)

    def weight_at(self, band: str) -> float:
        """Weight of this boss in ``band`` (one of ``WEIGHT_BANDS``)."""
        return self.weights[WEIGHT_BANDS.index(band)]


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


def _parse_minutes(eid: int, label: str, value: Any) -> float:
    """Validate a positive, finite number of minutes."""
    if (
        isinstance(value, bool)
        or not isinstance(value, int | float)
        or not math.isfinite(value)
        or value <= 0
    ):
        raise ValueError(
            f"boss_arena_tags.json entity {eid}: {label} must be a positive "
            f"number of minutes, got {value!r}"
        )
    return float(value)


def _parse_weights(eid: int, value: Any) -> tuple[float, float, float]:
    """``boss.weight``: one number (every band) or ``{early, mid, late}``."""
    if isinstance(value, dict):
        if set(value) != set(WEIGHT_BANDS):
            raise ValueError(
                f"boss_arena_tags.json entity {eid}: boss.weight bands must be "
                f"exactly {list(WEIGHT_BANDS)}, got {sorted(value)}"
            )
        early, mid, late = (
            _parse_minutes(eid, f"boss.weight.{band}", value[band])
            for band in WEIGHT_BANDS
        )
        return (early, mid, late)
    weight = _parse_minutes(eid, "boss.weight", value)
    return (weight, weight, weight)


def load_tags(path: Path) -> dict[int, EntityTags]:
    raw: dict[str, dict[str, Any]] = json.loads(Path(path).read_text())
    out: dict[int, EntityTags] = {}
    for key, entry in raw.items():
        eid = int(key)
        boss_block = dict(entry["boss"])
        if "weight" in boss_block:
            boss_block["weights"] = _parse_weights(eid, boss_block.pop("weight"))
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


# Slack on weight comparisons, same as generator.pick_cluster_weight_matched.
_WEIGHT_EPSILON = 1e-9

# Matcher draws before match_arenas_balanced gives up; the caller's
# MatchingError handling then rerolls the DAG (auto seed) or fails (fixed).
BALANCE_ATTEMPTS = 50


def extreme_sets(
    bosses: Mapping[int, BossTags], band: str, fraction: float
) -> tuple[frozenset[int], frozenset[int]]:
    """The light and heavy extremes of ``bosses`` in ``band``.

    With ``k = max(1, round(fraction * len(bosses)))``, the light extremes
    weigh at most the k-th lightest weight and the heavy ones at least the
    k-th heaviest, ties at the cut included. There are none when
    ``fraction <= 0`` or when the two cuts do not separate (e.g. every
    weight equal, as before any annotation).
    """
    if fraction <= 0 or not bosses:
        return frozenset(), frozenset()
    ordered = sorted(tags.weight_at(band) for tags in bosses.values())
    k = max(1, round(fraction * len(ordered)))
    low, high = ordered[k - 1], ordered[-k]
    if low >= high - _WEIGHT_EPSILON:
        return frozenset(), frozenset()
    light = frozenset(
        b for b, tags in bosses.items() if tags.weight_at(band) <= low + _WEIGHT_EPSILON
    )
    heavy = frozenset(
        b
        for b, tags in bosses.items()
        if tags.weight_at(band) >= high - _WEIGHT_EPSILON
    )
    return light, heavy


def _layer_excess(
    band: str,
    placed: Sequence[int],
    bosses: Mapping[int, BossTags],
    extremes: Mapping[str, tuple[frozenset[int], frozenset[int]]],
    log_cap: float,
) -> float:
    """How far one layer's bosses are from the balance rules; 0 when they pass.

    One unit when the layer holds both a light and a heavy extreme of its
    band, plus how far the log of its heaviest-to-lightest weight ratio
    exceeds ``log_cap`` (``math.inf`` without a cap). A single slot passes.
    """
    if len(placed) < 2:
        return 0.0
    light, heavy = extremes[band]
    held = set(placed)
    excess = 1.0 if held & light and held & heavy else 0.0
    weights = [bosses[boss].weight_at(band) for boss in placed]
    over = math.log(max(weights) / min(weights)) - log_cap
    if over > _WEIGHT_EPSILON:
        excess += over
    return excess


def _layers_pass(
    assignment: Mapping[int, int],
    groups: Sequence[tuple[str, Sequence[int]]],
    bosses: Mapping[int, BossTags],
    extremes: Mapping[str, tuple[frozenset[int], frozenset[int]]],
    log_cap: float,
) -> bool:
    """True when no group of ``assignment`` breaks a balance rule."""
    return all(
        _layer_excess(band, [assignment[a] for a in group], bosses, extremes, log_cap)
        <= _WEIGHT_EPSILON
        for band, group in groups
    )


def _repair_by_swaps(
    assignment: dict[int, int],
    *,
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    groups: Sequence[tuple[str, Sequence[int]]],
    extremes: Mapping[str, tuple[frozenset[int], frozenset[int]]],
    log_cap: float,
    rng: random.Random,
    check_size: bool,
    forbidden: Mapping[int, frozenset[int]],
) -> bool:
    """Swap placed bosses between arenas until every group passes, in place.

    Each step takes a failing group at random and applies, at random, one of
    the swaps between one of its slots and any slot outside it that keeps
    both arenas compatible (``is_compatible`` and ``forbidden``) and strictly
    lowers the summed ``_layer_excess``. A slot in no group (no known layer)
    is a free swap partner. The placed bosses never change, only their
    arenas. The sum strictly decreases, so the loop ends; this greedy search
    returns False as soon as the failing group it picked has no such swap.
    """
    slot_group = {slot: gi for gi, (_, group) in enumerate(groups) for slot in group}

    def excess_of(group_index: int, change: tuple[int, int] | None = None) -> float:
        """Excess of a group, with ``change = (slot, boss)`` applied if given."""
        band, group = groups[group_index]
        changed = dict([change]) if change is not None else {}
        placed = [changed.get(s, assignment[s]) for s in group]
        return _layer_excess(band, placed, bosses, extremes, log_cap)

    def fits(slot: int, boss: int) -> bool:
        return boss not in forbidden.get(slot, frozenset()) and is_compatible(
            arenas[slot], bosses[boss], check_size=check_size
        )

    excess = [excess_of(gi) for gi in range(len(groups))]
    while True:
        failing = [gi for gi, e in enumerate(excess) if e > _WEIGHT_EPSILON]
        if not failing:
            return True
        gi = rng.choice(failing)
        moves: list[tuple[int, int, float, float]] = []
        for slot in groups[gi][1]:
            boss = assignment[slot]
            for other, other_boss in assignment.items():
                gj = slot_group.get(other)
                if gj == gi or not fits(slot, other_boss) or not fits(other, boss):
                    continue
                new_i = excess_of(gi, (slot, other_boss))
                new_j = 0.0 if gj is None else excess_of(gj, (other, boss))
                old_j = 0.0 if gj is None else excess[gj]
                if new_i + new_j < excess[gi] + old_j - _WEIGHT_EPSILON:
                    moves.append((slot, other, new_i, new_j))
        if not moves:
            return False
        slot, other, new_i, new_j = rng.choice(moves)
        assignment[slot], assignment[other] = assignment[other], assignment[slot]
        excess[gi] = new_i
        gj = slot_group.get(other)
        if gj is not None:
            excess[gj] = new_j


def match_arenas_balanced(
    *,
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    groups: Sequence[tuple[str, Sequence[int]]],
    rng: random.Random,
    check_size: bool,
    fraction: float,
    forbidden: Mapping[int, frozenset[int]] | None = None,
    attempts: int = BALANCE_ATTEMPTS,
    max_ratio: float = 0.0,
) -> dict[int, int]:
    """``match_arenas_to_bosses`` keeping each layer's bosses balanced.

    ``groups`` pairs each DAG layer's arena slots with the layer's weight
    band. Per band, ``extreme_sets`` marks the light and heavy extremes of
    the job's pool (``bosses``); a layer may not hold both. With
    ``max_ratio > 0``, a layer's heaviest boss may also weigh at most
    ``max_ratio`` times its lightest (band weights).

    Without a ratio cap, rejection sampling: run the unchanged matcher and
    accept its result when every group passes, otherwise draw again with
    the same ``rng``. The accepted matching follows the plain matcher's
    distribution conditioned on the rule, so an extreme boss only loses the
    combinations the rule forbids. With a cap, a failing draw is repaired
    instead (``_repair_by_swaps``): its bosses trade arenas until every layer
    passes, so the drawn bosses stay and only their placement changes; a
    draw the swap search cannot fix is replaced by the next one. Rejection
    alone would almost never draw a passing matching under a tight cap.

    ``fraction <= 0`` or a pool without extremes disables the extremes
    rule, ``max_ratio <= 0`` the cap. A job the plain matcher cannot solve
    raises its ``MatchingError`` at once: the matching is exact, so a redraw
    cannot help. After ``attempts`` failed draws, a ``MatchingError`` naming
    the rules is raised; the caller turns it into a DAG reroll.

    The first draw is exactly ``match_arenas_to_bosses`` with the same
    ``rng``: a seed whose plain matching already satisfies the rules keeps
    its assignments.
    """
    extremes = {band: extreme_sets(bosses, band, fraction) for band in WEIGHT_BANDS}
    capped = max_ratio > 0
    active = capped or any(light for light, _ in extremes.values())
    log_cap = math.log(max_ratio) if capped else math.inf
    tries = max(1, attempts)
    for _ in range(tries):
        assignment = match_arenas_to_bosses(
            arenas=arenas,
            bosses=bosses,
            rng=rng,
            check_size=check_size,
            forbidden=forbidden,
        )
        if not active or _layers_pass(assignment, groups, bosses, extremes, log_cap):
            return assignment
        if capped and _repair_by_swaps(
            assignment,
            arenas=arenas,
            bosses=bosses,
            groups=groups,
            extremes=extremes,
            log_cap=log_cap,
            rng=rng,
            check_size=check_size,
            forbidden=forbidden or {},
        ):
            return assignment
    if capped:
        raise MatchingError(
            f"no arena-boss matching keeps every layer within a {max_ratio:g}x "
            f"weight ratio and light/heavy extreme bosses apart after {tries} "
            f"attempts (enemy.minor_boss_max_ratio, enemy.boss_extreme_fraction; "
            f"0 disables)"
        )
    raise MatchingError(
        f"no arena-boss matching keeps light and heavy extreme bosses apart on "
        f"every layer after {tries} attempts (enemy.boss_extreme_fraction; "
        f"0 disables)"
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
    tags: Mapping[int, EntityTags],
    names: Iterable[str],
    *,
    label: str = "enemy.bosses",
) -> dict[int, BossTags]:
    """Resolve allowlist names to a boss pool.

    Each name is matched case-insensitively as a substring of
    ``EntityTags.name``. Every name must resolve to exactly one entity, else a
    ``ValueError`` is raised (zero matches: typo; multiple: ambiguous). The
    allowlist is authoritative: ``exclude_from_pool`` and DLC flags are NOT
    applied here, because the user named the boss explicitly.

    Returns ``{entity_id: BossTags}`` for the matched entities, key-sorted so
    the result is independent of ``tags`` iteration order. ``label`` names
    the config key in error messages.
    """
    pool: dict[int, BossTags] = {}
    for name in names:
        needle = name.strip().lower()
        matches = [
            (eid, entry) for eid, entry in tags.items() if needle in entry.name.lower()
        ]
        if not matches:
            raise ValueError(f"{label}: no boss matches {name!r}")
        if len(matches) > 1:
            found = ", ".join(sorted(entry.name for _, entry in matches))
            raise ValueError(f"{label}: {name!r} is ambiguous: matches [{found}]")
        eid, entry = matches[0]
        pool[eid] = entry.boss
    return dict(sorted(pool.items()))
