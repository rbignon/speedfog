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
    layer_weight_bands,
    load_tags,
    match_arenas_balanced,
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


def _write_single_boss(tmp_path: Path, **boss_overrides) -> Path:
    path = tmp_path / "single.json"
    path.write_text(
        json.dumps(
            {
                "5000": {
                    "name": "Weighted",
                    "boss": _boss_block(**boss_overrides),
                    "arena": _arena_block(),
                    "region": 1,
                    "scaling": 1,
                    "dlc": False,
                }
            }
        )
    )
    return path


def test_boss_weight_defaults_to_one_when_absent(sample_tags: Path) -> None:
    tags = load_tags(sample_tags)
    assert all(entry.boss.weight == 1.0 for entry in tags.values())


def test_boss_weight_is_read(tmp_path: Path) -> None:
    tags = load_tags(_write_single_boss(tmp_path, weight=2.5))
    assert tags[5000].boss.weight == 2.5


def test_boss_weight_integer_is_coerced_to_float(tmp_path: Path) -> None:
    tags = load_tags(_write_single_boss(tmp_path, weight=3))
    weight = tags[5000].boss.weight
    assert weight == 3.0
    assert isinstance(weight, float)


@pytest.mark.parametrize("bad", [0, -1.5, True, "2", float("nan"), float("inf")])
def test_boss_weight_rejects_invalid_values(tmp_path: Path, bad) -> None:
    with pytest.raises(ValueError, match=r"5000.*boss\.weight"):
        load_tags(_write_single_boss(tmp_path, weight=bad))


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
    weight: float = 1.0,
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
            weight=weight,
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


def test_match_never_assigns_forbidden_boss() -> None:
    """An arena whose own ID is in the pool must never receive itself."""
    tags = {1: _entity(1), 2: _entity(2)}
    for seed in range(32):
        result = match_arenas_to_bosses(
            arenas=_arenas_of(tags, [1]),
            bosses=_bosses_of(tags, [1, 2]),
            rng=random.Random(seed),
            check_size=False,
            forbidden={1: frozenset({1})},
        )
        assert result == {1: 2}, f"seed {seed} returned {result}"


def test_match_forbidden_covers_phase_family_both_directions() -> None:
    """Leader arena must not receive the phase-1 boss and vice versa."""
    leader, phase1 = 100, 101
    tags = {
        leader: _entity(leader),
        phase1: _entity(phase1),
        2: _entity(2),
        3: _entity(3),
    }
    family = frozenset({leader, phase1})
    for seed in range(32):
        result = match_arenas_to_bosses(
            arenas=_arenas_of(tags, [leader, phase1]),
            bosses=_bosses_of(tags, [leader, phase1, 2, 3]),
            rng=random.Random(seed),
            check_size=False,
            forbidden={leader: family, phase1: family},
        )
        assert set(result.values()) == {2, 3}, f"seed {seed} returned {result}"


def test_match_raises_when_only_candidate_is_forbidden() -> None:
    """Self-exclusion can make the graph unsatisfiable; the error says so."""
    tags = {1: _entity(1)}
    with pytest.raises(MatchingError, match="placement excluded"):
        match_arenas_to_bosses(
            arenas=_arenas_of(tags, [1]),
            bosses=_bosses_of(tags, [1]),
            rng=random.Random(0),
            check_size=False,
            forbidden={1: frozenset({1})},
        )


# --- Layer weight balance -------------------------------------------------

_BAND_WEIGHTS = (0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0)


def _weighted_pool(ids: list[int]) -> dict[int, EntityTags]:
    """Entities whose boss weights cycle through _BAND_WEIGHTS."""
    return {
        eid: _entity(eid, weight=_BAND_WEIGHTS[i % len(_BAND_WEIGHTS)])
        for i, eid in enumerate(ids)
    }


def test_bands_skip_single_slot_groups() -> None:
    tags = _weighted_pool([1, 10, 11, 12])
    bands = layer_weight_bands(
        groups=[[1]],
        arenas=_arenas_of(tags, [1]),
        bosses=_bosses_of(tags, [10, 11, 12]),
        rng=random.Random(0),
        check_size=False,
        spread=1.0,
    )
    assert bands == {}


def test_bands_exclude_nothing_when_weights_are_equal() -> None:
    tags = {eid: _entity(eid) for eid in (1, 2, 10, 11, 12)}
    bands = layer_weight_bands(
        groups=[[1, 2]],
        arenas=_arenas_of(tags, [1, 2]),
        bosses=_bosses_of(tags, [10, 11, 12]),
        rng=random.Random(0),
        check_size=False,
        spread=1.0,
    )
    assert bands == {}


def test_bands_keep_a_spread_wide_window_shared_by_the_group() -> None:
    pool_ids = list(range(10, 31))
    tags = {**_weighted_pool(pool_ids), 1: _entity(1), 2: _entity(2), 3: _entity(3)}
    bosses = _bosses_of(tags, pool_ids)
    for seed in range(50):
        bands = layer_weight_bands(
            groups=[[1, 2, 3]],
            arenas=_arenas_of(tags, [1, 2, 3]),
            bosses=bosses,
            rng=random.Random(seed),
            check_size=False,
            spread=1.0,
        )
        assert bands[1] == bands[2] == bands[3], f"seed {seed}"
        kept = [bosses[b].weight for b in bosses if b not in bands[1]]
        assert kept, f"seed {seed}: band kept no boss"
        assert max(kept) - min(kept) <= 1.0 + 1e-9, f"seed {seed}"


def test_bands_anchor_ignores_forbidden_candidates() -> None:
    """The anchor is drawn among the drawn slot's allowed candidates only."""
    tags = {
        1: _entity(1),
        2: _entity(2),
        10: _entity(10, weight=1.0),
        11: _entity(11, weight=5.0),
        12: _entity(12, weight=5.0),
    }
    blocked = frozenset({10})
    for seed in range(32):
        bands = layer_weight_bands(
            groups=[[1, 2]],
            arenas=_arenas_of(tags, [1, 2]),
            bosses=_bosses_of(tags, [10, 11, 12]),
            rng=random.Random(seed),
            check_size=False,
            spread=1.0,
            forbidden={1: blocked, 2: blocked},
        )
        assert bands == {1: frozenset({10}), 2: frozenset({10})}, f"seed {seed}"


def test_bands_skip_group_without_candidates() -> None:
    tags = {
        1: _entity(1, arena_forbids_dragon=True),
        2: _entity(2, arena_forbids_dragon=True),
        10: _entity(10, is_dragon=True, weight=1.0),
        11: _entity(11, is_dragon=True, weight=5.0),
    }
    bands = layer_weight_bands(
        groups=[[1, 2]],
        arenas=_arenas_of(tags, [1, 2]),
        bosses=_bosses_of(tags, [10, 11]),
        rng=random.Random(0),
        check_size=False,
        spread=1.0,
    )
    assert bands == {}


def _three_layer_setup() -> (
    tuple[dict[int, ArenaTags], dict[int, BossTags], list[list[int]]]
):
    """Six arenas in three layers of two, against a 28-boss weighted pool."""
    pool_ids = list(range(10, 38))
    tags = {**_weighted_pool(pool_ids), **{a: _entity(a) for a in range(1, 7)}}
    groups = [[1, 2], [3, 4], [5, 6]]
    return _arenas_of(tags, list(range(1, 7))), _bosses_of(tags, pool_ids), groups


def test_balanced_keeps_each_layer_within_spread() -> None:
    arenas, bosses, groups = _three_layer_setup()
    for seed in range(50):
        result = match_arenas_balanced(
            arenas=arenas,
            bosses=bosses,
            groups=groups,
            rng=random.Random(seed),
            band_rng=random.Random(seed + 1000),
            check_size=False,
            spread=1.0,
        )
        assert len(set(result.values())) == len(arenas), f"seed {seed}"
        for group in groups:
            weights = [bosses[result[a]].weight for a in group]
            assert max(weights) - min(weights) <= 1.0 + 1e-9, f"seed {seed}"


def test_balanced_equals_plain_matching_when_spread_is_zero() -> None:
    arenas, bosses, groups = _three_layer_setup()
    for seed in range(20):
        plain = match_arenas_to_bosses(
            arenas=arenas, bosses=bosses, rng=random.Random(seed), check_size=False
        )
        balanced = match_arenas_balanced(
            arenas=arenas,
            bosses=bosses,
            groups=groups,
            rng=random.Random(seed),
            band_rng=random.Random(seed),
            check_size=False,
            spread=0.0,
        )
        assert balanced == plain, f"seed {seed}"


def test_balanced_equals_plain_matching_when_weights_are_equal() -> None:
    tags = {eid: _entity(eid) for eid in range(1, 38)}
    arenas = _arenas_of(tags, list(range(1, 7)))
    bosses = _bosses_of(tags, list(range(10, 38)))
    groups = [[1, 2], [3, 4], [5, 6]]
    forbidden = {a: frozenset({a}) for a in arenas}
    for seed in range(20):
        plain = match_arenas_to_bosses(
            arenas=arenas,
            bosses=bosses,
            rng=random.Random(seed),
            check_size=False,
            forbidden=forbidden,
        )
        balanced = match_arenas_balanced(
            arenas=arenas,
            bosses=bosses,
            groups=groups,
            rng=random.Random(seed),
            band_rng=random.Random(seed),
            check_size=False,
            spread=1.0,
            forbidden=forbidden,
        )
        assert balanced == plain, f"seed {seed}"


def test_balanced_redraws_bands_until_matching_fits() -> None:
    """An anchor on the lone heavy boss cannot fill two slots; a redraw can."""
    tags = {
        1: _entity(1),
        2: _entity(2),
        10: _entity(10, weight=1.0),
        11: _entity(11, weight=1.0),
        12: _entity(12, weight=5.0),
    }
    for seed in range(32):
        result = match_arenas_balanced(
            arenas=_arenas_of(tags, [1, 2]),
            bosses=_bosses_of(tags, [10, 11, 12]),
            groups=[[1, 2]],
            rng=random.Random(seed),
            band_rng=random.Random(seed),
            check_size=False,
            spread=1.0,
        )
        assert set(result.values()) == {10, 11}, f"seed {seed}"


def test_balanced_raises_when_no_band_fits_the_layer() -> None:
    tags = {
        1: _entity(1),
        2: _entity(2),
        10: _entity(10, weight=1.0),
        11: _entity(11, weight=5.0),
    }
    with pytest.raises(MatchingError, match="layer weight bands"):
        match_arenas_balanced(
            arenas=_arenas_of(tags, [1, 2]),
            bosses=_bosses_of(tags, [10, 11]),
            groups=[[1, 2]],
            rng=random.Random(0),
            band_rng=random.Random(0),
            check_size=False,
            spread=1.0,
            attempts=3,
        )


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


def test_uniform_prefers_non_forbidden_candidate() -> None:
    """With an alternative available, the arena's own boss is avoided."""
    tags = {1: _entity(1), 2: _entity(2)}
    for seed in range(32):
        result = assign_bosses_uniform(
            arenas=_arenas_of(tags, [1]),
            pool=_bosses_of(tags, [1, 2]),
            rng=random.Random(seed),
            check_size=False,
            forbidden={1: frozenset({1})},
        )
        assert result == {1: 2}, f"seed {seed} returned {result}"


def test_uniform_falls_back_to_forbidden_when_no_alternative() -> None:
    """Malenia-only: her own arena still gets her rather than failing."""
    tags = {1: _entity(1)}
    result = assign_bosses_uniform(
        arenas=_arenas_of(tags, [1]),
        pool=_bosses_of(tags, [1]),
        rng=random.Random(0),
        check_size=False,
        forbidden={1: frozenset({1})},
    )
    assert result == {1: 1}


def test_uniform_falls_back_when_alternatives_are_incompatible() -> None:
    """Fallback also fires when the only non-forbidden candidates fail
    compatibility (dragon in a dragon-forbidding arena), not just when the
    pool is a single entry."""
    tags = {
        1: _entity(1, arena_forbids_dragon=True),
        2: _entity(2, is_dragon=True),
    }
    result = assign_bosses_uniform(
        arenas=_arenas_of(tags, [1]),
        pool=_bosses_of(tags, [1, 2]),
        rng=random.Random(0),
        check_size=False,
        forbidden={1: frozenset({1})},
    )
    assert result == {1: 1}


class TestPromotedSkeletons:
    """Halloween allowlist-only skeleton sources (spec 2.1)."""

    PROMOTED = {
        "Skeletal Militiaman": 11000295,
        "Giant Skeleton": 31190300,
        "Shadow Skeleton": 43010200,
    }

    def _real_tags(self):
        path = Path(__file__).parent.parent / "data" / "boss_arena_tags.json"
        return load_tags(path)

    def test_allowlist_resolves_each_promoted_name_uniquely(self):
        tags = self._real_tags()
        pool = resolve_boss_allowlist(tags, self.PROMOTED.keys())
        assert set(pool) == set(self.PROMOTED.values())

    def test_promoted_entries_are_standard_path_inert_minor_sources(self):
        tags = self._real_tags()
        for eid in self.PROMOTED.values():
            entry = tags[eid]
            assert entry.pool == "minor"
            assert entry.arena is None
            assert entry.boss.exclude_from_pool is True

    def test_aging_untouchable_promoted(self):
        tags = self._real_tags()
        resolved = resolve_boss_allowlist(tags, ["Aging Untouchable"])
        assert list(resolved) == [2049420200]
        entry = tags[2049420200]
        assert entry.pool == "minor"
        assert entry.arena is None
        assert entry.boss is not None
        assert entry.boss.exclude_from_pool is True
        assert entry.boss.size == 2
        assert entry.dlc is True
