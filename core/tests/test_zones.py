"""Tests for zone parsing."""

import pytest
from speedfog_core.zones import Zone, ZonePool, ZoneType, load_zones


def test_zone_type_from_string():
    """ZoneType.from_string parses zone type strings."""
    assert ZoneType.from_string("legacy_dungeon") == ZoneType.LEGACY_DUNGEON
    assert ZoneType.from_string("catacomb_short") == ZoneType.CATACOMB_SHORT
    assert ZoneType.from_string("catacomb") == ZoneType.CATACOMB_MEDIUM  # default
    assert ZoneType.from_string("cave") == ZoneType.CAVE_MEDIUM  # default
    assert ZoneType.from_string("tunnel") == ZoneType.TUNNEL
    assert ZoneType.from_string("gaol") == ZoneType.GAOL
    assert ZoneType.from_string("boss_arena") == ZoneType.BOSS_ARENA


def test_zone_type_is_mini_dungeon():
    """ZoneType.is_mini_dungeon returns True for mini-dungeon types."""
    assert ZoneType.CATACOMB_SHORT.is_mini_dungeon() is True
    assert ZoneType.CATACOMB_MEDIUM.is_mini_dungeon() is True
    assert ZoneType.CATACOMB_LONG.is_mini_dungeon() is True
    assert ZoneType.CAVE_SHORT.is_mini_dungeon() is True
    assert ZoneType.CAVE_MEDIUM.is_mini_dungeon() is True
    assert ZoneType.CAVE_LONG.is_mini_dungeon() is True
    assert ZoneType.TUNNEL.is_mini_dungeon() is True
    assert ZoneType.GAOL.is_mini_dungeon() is True
    # Not mini-dungeons
    assert ZoneType.LEGACY_DUNGEON.is_mini_dungeon() is False
    assert ZoneType.BOSS_ARENA.is_mini_dungeon() is False
    assert ZoneType.START.is_mini_dungeon() is False
    assert ZoneType.FINAL_BOSS.is_mini_dungeon() is False


def test_zone_type_is_boss():
    """ZoneType.is_boss returns True for zones with bosses."""
    # Boss zones
    assert ZoneType.LEGACY_DUNGEON.is_boss() is True
    assert ZoneType.BOSS_ARENA.is_boss() is True
    assert ZoneType.FINAL_BOSS.is_boss() is True
    assert ZoneType.CATACOMB_SHORT.is_boss() is True  # mini-dungeons have bosses
    assert ZoneType.CAVE_MEDIUM.is_boss() is True
    assert ZoneType.TUNNEL.is_boss() is True
    assert ZoneType.GAOL.is_boss() is True
    # Not boss zones
    assert ZoneType.START.is_boss() is False


def test_zone_can_split_or_merge():
    """Zone.can_split_or_merge checks fog_count >= 3."""
    zone_3fogs = Zone(
        id="stormveil",
        map="m10_00_00_00",
        name="Stormveil Castle",
        type=ZoneType.LEGACY_DUNGEON,
        weight=15,
        fog_count=3,
    )
    assert zone_3fogs.can_split_or_merge() is True

    zone_2fogs = Zone(
        id="murkwater",
        map="m30_00_00_00",
        name="Murkwater Catacombs",
        type=ZoneType.CATACOMB_SHORT,
        weight=4,
        fog_count=2,
    )
    assert zone_2fogs.can_split_or_merge() is False


def test_zone_from_dict():
    """Zone.from_dict creates Zone from dictionary."""
    data = {
        "id": "stormveil",
        "map": "m10_00_00_00",
        "name": "Stormveil Castle",
        "type": "legacy_dungeon",
        "weight": 15,
        "fog_count": 3,
        "boss": "godrick",
        "tags": ["required", "early"],
    }
    zone = Zone.from_dict(data)

    assert zone.id == "stormveil"
    assert zone.map == "m10_00_00_00"
    assert zone.name == "Stormveil Castle"
    assert zone.type == ZoneType.LEGACY_DUNGEON
    assert zone.weight == 15
    assert zone.fog_count == 3
    assert zone.boss == "godrick"
    assert zone.tags == ["required", "early"]


def test_zone_from_dict_defaults():
    """Zone.from_dict uses defaults for missing fields."""
    data = {"id": "test_zone"}
    zone = Zone.from_dict(data)

    assert zone.id == "test_zone"
    assert zone.map == ""
    assert zone.name == "test_zone"  # defaults to id
    assert zone.type == ZoneType.BOSS_ARENA  # default
    assert zone.weight == 5
    assert zone.fog_count == 2
    assert zone.boss == ""
    assert zone.tags == []


def test_zone_pool_from_toml(tmp_path):
    """ZonePool.from_toml loads zones from TOML file."""
    zones_file = tmp_path / "zones.toml"
    zones_file.write_text('''
[[zones]]
id = "stormveil"
map = "m10_00_00_00"
name = "Stormveil Castle"
type = "legacy_dungeon"
weight = 15
fog_count = 3
boss = "godrick"

[[zones]]
id = "murkwater"
map = "m30_00_00_00"
name = "Murkwater Catacombs"
type = "catacomb_short"
weight = 4
fog_count = 2
''')
    pool = ZonePool.from_toml(zones_file)

    assert len(pool.all_zones()) == 2
    assert len(pool.legacy_dungeons()) == 1
    assert len(pool.mini_dungeons()) == 1

    stormveil = pool.get("stormveil")
    assert stormveil is not None
    assert stormveil.name == "Stormveil Castle"
    assert stormveil.boss == "godrick"
    assert stormveil.can_split_or_merge() is True


def test_zone_pool_by_type():
    """ZonePool.by_type returns zones of specified type."""
    pool = ZonePool()
    pool.add(Zone(id="z1", map="m10", name="Zone 1", type=ZoneType.LEGACY_DUNGEON, weight=10))
    pool.add(Zone(id="z2", map="m30", name="Zone 2", type=ZoneType.CATACOMB_SHORT, weight=4))
    pool.add(Zone(id="z3", map="m30", name="Zone 3", type=ZoneType.CATACOMB_SHORT, weight=5))

    assert len(pool.by_type(ZoneType.LEGACY_DUNGEON)) == 1
    assert len(pool.by_type(ZoneType.CATACOMB_SHORT)) == 2
    assert len(pool.by_type(ZoneType.CAVE_MEDIUM)) == 0


def test_zone_pool_boss_arenas():
    """ZonePool.boss_arenas returns standalone boss arena zones."""
    pool = ZonePool()
    pool.add(Zone(id="godrick", map="m10", name="Godrick Arena", type=ZoneType.BOSS_ARENA, weight=5))
    pool.add(Zone(id="cave", map="m30", name="Cave", type=ZoneType.CAVE_SHORT, weight=3))

    arenas = pool.boss_arenas()
    assert len(arenas) == 1
    assert arenas[0].id == "godrick"


def test_load_zones_helper(tmp_path):
    """load_zones convenience function works correctly."""
    zones_file = tmp_path / "zones.toml"
    zones_file.write_text('''
[[zones]]
id = "test"
name = "Test Zone"
type = "cave"
weight = 5
''')
    pool = load_zones(zones_file)
    assert len(pool.all_zones()) == 1
    assert pool.get("test").type == ZoneType.CAVE_MEDIUM


def test_load_zones_file_not_found(tmp_path):
    """load_zones raises FileNotFoundError for missing file."""
    with pytest.raises(FileNotFoundError):
        load_zones(tmp_path / "nonexistent.toml")
