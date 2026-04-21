"""Tests for the BossArenaRandomizer tag porter."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from tools.port_boss_arena_tags import build_entities, port


def _boss(eid: str) -> dict:
    return {
        "id": eid,
        "bossSize": 1,
        "bossType": 4,
        "isTwoPhase": 0,
        "isDragon": 0,
        "isNPC": 0,
        "canEscape": 0,
        "nightBoss": 0,
        "region": 1,
        "scaling": 1,
        "dlc": 0,
    }


def _arena(eid: str) -> dict:
    return {
        "id": eid,
        "arenaSize": 3,
        "arenaType": 4,
        "twoPhaseNotAllowed": 0,
        "dragonNotAllowed": 0,
        "npcNotAllowed": 0,
        "isEscapable": 0,
        "nightBoss": 0,
        "region": 1,
        "scaling": 1,
        "dlc": 0,
    }


def test_vanilla_entity_has_both_boss_and_arena() -> None:
    bosses = {"Soldier": _boss("18000850")}
    arenas = {"Soldier": _arena("18000850")}
    entities = build_entities(bosses, arenas)
    entry = entities["18000850"]
    assert entry["name"] == "Soldier"
    assert entry["boss"]["size"] == 1
    assert entry["boss"]["exclude_from_pool"] is False
    assert entry["arena"]["size"] == 3
    assert "pool" not in entry  # pool derived from cluster.type for vanilla


def test_arena_without_matching_boss_raises() -> None:
    with pytest.raises(KeyError, match="Phantom"):
        build_entities({}, {"Phantom": _arena("99999")})


def test_id_mismatch_between_boss_and_arena_raises() -> None:
    """Arena id must match its paired boss id."""
    bosses = {"Rennala": _boss("14000800")}
    arenas = {"Rennala": _arena("14000801")}  # mismatched id
    with pytest.raises(ValueError, match="ID mismatch"):
        build_entities(bosses, arenas)


def test_source_only_bar_entry_stays_without_pool_field() -> None:
    """A bosses.json entry without a matching arena stays source-only and,
    if it is not in EXTRA_MINOR_POOL_ENTRIES, gets no pool field.
    """
    bosses = {"Obscure": _boss("99999")}
    entities = build_entities(bosses, {})
    entry = entities["99999"]
    assert "arena" not in entry
    assert "pool" not in entry


def test_promoted_id_without_bar_entry_becomes_source_only() -> None:
    """IDs in EXTRA_MINOR_POOL_ENTRIES that are NOT in BAR become source-only."""
    # 1051400299 ("Guardian Golem") is a promoted ID not in BAR.
    entities = build_entities({}, {})
    entry = entities["1051400299"]
    assert entry["name"] == "Guardian Golem"  # from C# comment
    assert "arena" not in entry
    assert entry["boss"]["exclude_from_pool"] is False
    assert entry["pool"] == "minor"


def test_promoted_id_already_in_bar_is_not_overwritten() -> None:
    """Crucible Knight Devonia (2045470200) is in BAR and in promoted list;
    the BAR entry must win (no overwrite, no 'pool' field added).
    """
    bosses = {"Crucible Knight Devonia": _boss("2045470200")}
    arenas = {"Crucible Knight Devonia": _arena("2045470200")}
    entities = build_entities(bosses, arenas)
    entry = entities["2045470200"]
    assert entry["name"] == "Crucible Knight Devonia"  # from BAR, not C# comment
    assert "arena" in entry
    assert "pool" not in entry


def test_exclude_from_pool_flag_on_matching_bar_entries() -> None:
    """Night's Cavalry entries (and similar) get exclude_from_pool=True."""
    bosses = {
        "Night's Cavalry Limgrave": _boss("1043370340"),
        "Soldier of Godrick": _boss("18000850"),
    }
    arenas = {
        "Night's Cavalry Limgrave": _arena("1043370340"),
        "Soldier of Godrick": _arena("18000850"),
    }
    entities = build_entities(bosses, arenas)
    assert entities["1043370340"]["boss"]["exclude_from_pool"] is True
    assert entities["18000850"]["boss"]["exclude_from_pool"] is False


def test_port_writes_sorted_json(tmp_path: Path) -> None:
    bar_dir = tmp_path / "bar"
    bar_dir.mkdir()
    (bar_dir / "bosses.json").write_text(json.dumps({"B": _boss("2"), "A": _boss("1")}))
    (bar_dir / "bossArena.json").write_text(
        json.dumps({"B": _arena("2"), "A": _arena("1")})
    )
    out = tmp_path / "boss_arena_tags.json"
    port(bar_dir, out)
    data = json.loads(out.read_text())
    numeric_keys = list(data.keys())
    assert numeric_keys == sorted(numeric_keys, key=int)
