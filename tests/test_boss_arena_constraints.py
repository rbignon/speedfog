"""Tests for boss-arena compatibility tags and validation."""

from __future__ import annotations

import json
import random
import time
from pathlib import Path

import pytest

from speedfog.boss_arena_constraints import (
    ArenaTags,
    BossTags,
    EntityTags,
    MatchingError,
    assign_bosses_uniform,
    is_compatible,
    load_tags,
    match_arenas_to_bosses,
    resolve_boss_allowlist,
)


def _boss_block(**overrides) -> dict:
    base = {
        "size": 1,
        "type": 1,
        "is_two_phase": False,
        "is_dragon": False,
        "is_npc": False,
        "can_escape": False,
        "night_boss": False,
        "exclude_from_pool": False,
    }
    base.update(overrides)
    return base


def _arena_block(**overrides) -> dict:
    base = {
        "size": 3,
        "type": 1,
        "two_phase_not_allowed": False,
        "dragon_not_allowed": False,
        "npc_not_allowed": False,
        "is_escapable": False,
        "night_boss": False,
    }
    base.update(overrides)
    return base


@pytest.fixture
def sample_tags(tmp_path: Path) -> Path:
    data = {
        "1000": {
            "name": "TinyArenaBoss",
            "boss": _boss_block(size=1),
            "arena": _arena_block(
                size=1, two_phase_not_allowed=True, dragon_not_allowed=True
            ),
            "region": 1,
            "scaling": 1,
            "dlc": False,
        },
        "2000": {
            "name": "HugeDragon",
            "boss": _boss_block(size=5, type=3, is_dragon=True),
            "arena": _arena_block(size=5, type=3),
            "region": 1,
            "scaling": 5,
            "dlc": False,
        },
        "3000": {
            "name": "FieldPromoted",
            "boss": _boss_block(size=2),
            "pool": "minor",
            "region": 0,
            "scaling": 0,
            "dlc": False,
        },
        "4000": {
            "name": "NightsCavalry",
            "boss": _boss_block(exclude_from_pool=True),
            "arena": _arena_block(),
            "region": 1,
            "scaling": 1,
            "dlc": False,
        },
    }
    path = tmp_path / "tags.json"
    path.write_text(json.dumps(data))
    return path


def test_load_returns_entity_dict(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    assert set(tags.keys()) == {1000, 2000, 3000, 4000}
    assert isinstance(tags[1000], EntityTags)
    assert tags[1000].arena.size == 1


def test_source_only_entity_has_no_arena_block(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    entry = tags[3000]
    assert entry.arena is None
    assert entry.pool == "minor"


def test_exclude_from_pool_flag_reachable(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    assert tags[4000].boss.exclude_from_pool is True
    assert tags[1000].boss.exclude_from_pool is False


def test_dragon_in_dragon_forbidden_arena_is_incompatible(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    arena = tags[1000].arena
    dragon = tags[2000].boss
    assert not is_compatible(arena, dragon, check_size=False)


def test_size_check_rejects_oversized_boss(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    arena = tags[1000].arena
    big = tags[2000].boss
    assert not is_compatible(arena, big, check_size=True)
    arena_big = tags[2000].arena
    assert is_compatible(arena_big, big, check_size=True)


def test_size_check_ignored_when_disabled(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    arena_small = tags[1000].arena
    big = tags[2000].boss
    assert not is_compatible(arena_small, big, check_size=False)


def test_same_arena_boss_is_compatible(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    entry = tags[2000]
    assert is_compatible(entry.arena, entry.boss, check_size=True)


def test_can_escape_in_escapable_arena_is_incompatible() -> None:
    arena = ArenaTags(
        size=4,
        type=1,
        two_phase_not_allowed=False,
        dragon_not_allowed=False,
        npc_not_allowed=False,
        is_escapable=True,
        night_boss=False,
    )
    boss = BossTags(
        size=1,
        type=1,
        is_two_phase=False,
        is_dragon=False,
        is_npc=False,
        can_escape=True,
        night_boss=False,
        exclude_from_pool=False,
    )
    assert not is_compatible(arena, boss, check_size=False)


def _entity(
    eid: int,
    *,
    name: str | None = None,
    arena_forbids_dragon: bool = False,
    is_dragon: bool = False,
    arena_size: int = 3,
    boss_size: int = 1,
    source_only: bool = False,
    exclude_from_pool: bool = False,
) -> EntityTags:
    arena = (
        None
        if source_only
        else ArenaTags(
            size=arena_size,
            type=1,
            two_phase_not_allowed=False,
            dragon_not_allowed=arena_forbids_dragon,
            npc_not_allowed=False,
            is_escapable=False,
            night_boss=False,
        )
    )
    return EntityTags(
        entity_id=eid,
        name=name if name is not None else f"e{eid}",
        boss=BossTags(
            size=boss_size,
            type=1,
            is_two_phase=False,
            is_dragon=is_dragon,
            is_npc=False,
            can_escape=False,
            night_boss=False,
            exclude_from_pool=exclude_from_pool,
        ),
        arena=arena,
        pool="minor" if source_only else None,
        region=1,
        scaling=1,
        dlc=False,
    )


def _arenas_of(tags: dict[int, EntityTags], ids: list[int]) -> dict[int, ArenaTags]:
    result: dict[int, ArenaTags] = {}
    for i in ids:
        arena = tags[i].arena
        assert arena is not None, f"entity {i} has no arena block"
        result[i] = arena
    return result


def _bosses_of(tags: dict[int, EntityTags], ids: list[int]) -> dict[int, BossTags]:
    return {i: tags[i].boss for i in ids}


def test_match_returns_perfect_assignment() -> None:
    tags = {
        1: _entity(1),
        2: _entity(2),
        3: _entity(3),
    }
    result = match_arenas_to_bosses(
        arenas=_arenas_of(tags, [1, 2]),
        bosses=_bosses_of(tags, [1, 2, 3]),
        rng=random.Random(42),
        check_size=False,
    )
    assert set(result.keys()) == {1, 2}
    assert set(result.values()) <= {1, 2, 3}
    assert len(set(result.values())) == 2  # no duplicates


def test_match_is_deterministic_for_same_seed() -> None:
    tags = {i: _entity(i) for i in range(1, 6)}
    arenas = _arenas_of(tags, [1, 2, 3])
    bosses = _bosses_of(tags, [1, 2, 3, 4, 5])
    r1 = match_arenas_to_bosses(
        arenas=arenas,
        bosses=bosses,
        rng=random.Random(123),
        check_size=False,
    )
    r2 = match_arenas_to_bosses(
        arenas=arenas,
        bosses=bosses,
        rng=random.Random(123),
        check_size=False,
    )
    assert r1 == r2


def test_match_respects_dragon_constraint() -> None:
    tags = {
        1: _entity(1, arena_forbids_dragon=True),  # arena forbids dragon
        2: _entity(2, is_dragon=True),  # only this boss is dragon
        3: _entity(3),
    }
    result = match_arenas_to_bosses(
        arenas=_arenas_of(tags, [1]),
        bosses=_bosses_of(tags, [2, 3]),
        rng=random.Random(0),
        check_size=False,
    )
    # Arena 1 cannot host boss 2 (dragon); must get boss 3.
    assert result == {1: 3}


def test_match_raises_when_unsatisfiable() -> None:
    tags = {
        1: _entity(1, arena_forbids_dragon=True),
        2: _entity(2, is_dragon=True),
    }
    with pytest.raises(MatchingError):
        match_arenas_to_bosses(
            arenas=_arenas_of(tags, [1]),
            bosses=_bosses_of(tags, [2]),
            rng=random.Random(0),
            check_size=False,
        )


def test_match_does_not_repeat_bosses() -> None:
    tags = {i: _entity(i) for i in range(1, 6)}
    result = match_arenas_to_bosses(
        arenas=_arenas_of(tags, [1, 2, 3]),
        bosses=_bosses_of(tags, [1, 2, 3, 4, 5]),
        rng=random.Random(7),
        check_size=False,
    )
    assert len(set(result.values())) == 3


def test_match_reroutes_to_satisfy_constrained_arena() -> None:
    """Exercise the augmenting path: when a permissive arena would greedily
    claim the only candidate of a constrained one, the matcher must re-route
    the earlier assignment. The unique valid matching below is reached only if
    that re-routing works, regardless of which arena the shuffle processes
    first.
    """
    tags = {
        1: _entity(1, arena_forbids_dragon=True),  # arena 1: only boss 3 fits
        2: _entity(2, is_dragon=True),  # boss 2 is a dragon
        3: _entity(3),  # boss 3 fits anywhere
    }
    arenas = _arenas_of(tags, [1, 2])
    bosses = _bosses_of(tags, [2, 3])
    # Try several seeds so we cover both shuffle orders; every seed must yield
    # the one valid perfect matching {1: 3, 2: 2}, otherwise the augmenting
    # logic is broken.
    for seed in range(32):
        result = match_arenas_to_bosses(
            arenas=arenas, bosses=bosses, rng=random.Random(seed), check_size=False
        )
        assert result == {1: 3, 2: 2}, f"seed {seed} returned {result}"


def test_match_fails_fast_when_arenas_exceed_pool() -> None:
    """Regression: backtracking with static MRV used to explore exponentially
    when no perfect matching could exist (|arenas| > |bosses|). The augmenting
    path matcher must detect unsatisfiability in polynomial time.
    """
    tags = {i: _entity(i) for i in range(1, 101)}
    # 40 arenas, 20 bosses, every boss compatible with every arena: no perfect
    # matching possible, but the dense compatibility would blow up a naive
    # backtracker.
    arenas = _arenas_of(tags, list(range(1, 41)))
    bosses = _bosses_of(tags, list(range(50, 70)))
    t0 = time.perf_counter()
    with pytest.raises(MatchingError):
        match_arenas_to_bosses(
            arenas=arenas, bosses=bosses, rng=random.Random(0), check_size=False
        )
    elapsed = time.perf_counter() - t0
    assert elapsed < 1.0, f"matcher took {elapsed:.2f}s on an unsatisfiable case"


def test_resolve_allowlist_single_substring_match() -> None:
    tags = {
        15000800: _entity(15000800, name="Malenia Blade of Miquella"),
        16000800: _entity(16000800, name="Maliketh the Black Blade"),
    }
    pool = resolve_boss_allowlist(tags, ["malenia"])
    assert set(pool.keys()) == {15000800}
    assert pool[15000800] is tags[15000800].boss


def test_resolve_allowlist_is_case_insensitive() -> None:
    tags = {15000800: _entity(15000800, name="Malenia Blade of Miquella")}
    assert set(resolve_boss_allowlist(tags, ["MALENIA"]).keys()) == {15000800}


def test_resolve_allowlist_zero_matches_raises() -> None:
    tags = {15000800: _entity(15000800, name="Malenia Blade of Miquella")}
    with pytest.raises(ValueError, match="no boss matches 'Godfrey'"):
        resolve_boss_allowlist(tags, ["Godfrey"])


def test_resolve_allowlist_ambiguous_raises() -> None:
    tags = {
        1: _entity(1, name="Crucible Knight"),
        2: _entity(2, name="Crucible Knight Ordovis"),
    }
    with pytest.raises(ValueError, match="'Crucible Knight' is ambiguous"):
        resolve_boss_allowlist(tags, ["Crucible Knight"])


def test_resolve_allowlist_multiple_names() -> None:
    tags = {
        15000800: _entity(15000800, name="Malenia Blade of Miquella"),
        310000: _entity(310000, name="Starscourge Radahn"),
    }
    pool = resolve_boss_allowlist(tags, ["malenia", "radahn"])
    assert set(pool.keys()) == {15000800, 310000}


def test_resolve_allowlist_empty_names_returns_empty() -> None:
    tags = {15000800: _entity(15000800, name="Malenia Blade of Miquella")}
    assert resolve_boss_allowlist(tags, []) == {}


def test_uniform_single_boss_fills_all_arenas() -> None:
    """A one-boss pool assigns that boss to every arena (Malenia only)."""
    tags = {i: _entity(i) for i in range(1, 6)}
    arenas = _arenas_of(tags, [1, 2, 3, 4, 5])
    pool = _bosses_of(tags, [1])
    result = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(0), check_size=False
    )
    assert set(result.keys()) == {1, 2, 3, 4, 5}
    assert set(result.values()) == {1}


def test_uniform_distinct_when_pool_at_least_arenas() -> None:
    """With pool >= arenas and full compatibility, no boss is reused."""
    tags = {i: _entity(i) for i in range(1, 9)}
    arenas = _arenas_of(tags, [1, 2, 3])
    pool = _bosses_of(tags, [4, 5, 6, 7, 8])
    result = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(3), check_size=False
    )
    assert len(set(result.values())) == 3


def test_uniform_spreads_reuse_evenly() -> None:
    """With 2 bosses over 4 arenas, each boss is used about twice."""
    tags = {i: _entity(i) for i in range(1, 7)}
    arenas = _arenas_of(tags, [1, 2, 3, 4])
    pool = _bosses_of(tags, [5, 6])
    result = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(1), check_size=False
    )
    counts = {bid: list(result.values()).count(bid) for bid in (5, 6)}
    assert counts == {5: 2, 6: 2}


def test_uniform_raises_when_arena_has_no_compatible_boss() -> None:
    """A size-incompatible arena with check_size on has no candidate."""
    # Arena size 1, boss size 5: too big.
    tags = {
        1: _entity(1, arena_size=1),
        2: _entity(2, boss_size=5),
    }
    with pytest.raises(MatchingError, match="no compatible boss in the allowlist"):
        assign_bosses_uniform(
            arenas=_arenas_of(tags, [1]),
            pool=_bosses_of(tags, [2]),
            rng=random.Random(0),
            check_size=True,
        )


def test_uniform_size_relaxed_when_check_disabled() -> None:
    """Disabling the size check rescues the oversized pin."""
    tags = {
        1: _entity(1, arena_size=1),
        2: _entity(2, boss_size=5),
    }
    result = assign_bosses_uniform(
        arenas=_arenas_of(tags, [1]),
        pool=_bosses_of(tags, [2]),
        rng=random.Random(0),
        check_size=False,
    )
    assert result == {1: 2}


def test_uniform_is_deterministic_for_same_seed() -> None:
    tags = {i: _entity(i) for i in range(1, 7)}
    arenas = _arenas_of(tags, [1, 2, 3, 4])
    pool = _bosses_of(tags, [5, 6])
    r1 = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(99), check_size=False
    )
    r2 = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(99), check_size=False
    )
    assert r1 == r2


def test_uniform_preserves_arenas_iteration_order() -> None:
    """Result keys must appear in the original arenas iteration order (spoiler stability)."""
    tags = {i: _entity(i) for i in range(1, 8)}
    # Non-trivial insertion order: not 1..4 in sequence.
    arenas = _arenas_of(tags, [3, 1, 4, 2])
    pool = _bosses_of(tags, [5, 6, 7])
    result = assign_bosses_uniform(
        arenas=arenas, pool=pool, rng=random.Random(0), check_size=False
    )
    assert list(result.keys()) == [3, 1, 4, 2]
