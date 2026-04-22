"""Tests for boss-arena compatibility tags and validation."""

from __future__ import annotations

import json
import random
from pathlib import Path

import pytest

from speedfog.boss_arena_constraints import (
    ArenaTags,
    BossTags,
    EntityTags,
    MatchingError,
    _mrv_sort,
    is_compatible,
    load_tags,
    match_arenas_to_bosses,
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
        name=f"e{eid}",
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


def test_mrv_sort_orders_by_ascending_candidate_count() -> None:
    """Fewest-candidates arena goes first."""
    arena_ids = [1, 2, 3]
    candidates = {1: [10, 20, 30], 2: [10], 3: [10, 20]}
    _mrv_sort(arena_ids, candidates)
    assert arena_ids == [2, 3, 1]


def test_mrv_sort_is_stable_on_ties() -> None:
    """Stable sort preserves input order (shuffle order) for ties. The input
    order [3, 1, 2] must survive the sort because all three have the same
    candidate count."""
    arena_ids = [3, 1, 2]
    candidates = {1: [10, 20], 2: [10, 20], 3: [10, 20]}
    _mrv_sort(arena_ids, candidates)
    assert arena_ids == [3, 1, 2]
