"""Tests for Item Randomizer integration."""

import json
from pathlib import Path
from unittest.mock import MagicMock

import pytest

from speedfog.boss_arena_constraints import ArenaTags, BossTags, EntityTags
from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.item_randomizer import (
    _compose_pool,
    generate_item_config,
    run_item_randomizer,
)


def _boss_cluster(
    cid: str, ctype: str, defeat_flag: int, zone: str = "z"
) -> ClusterData:
    """Build a minimal boss cluster matching the DAG's ClusterData shape."""
    return ClusterData(
        id=cid,
        zones=[zone],
        type=ctype,
        weight=0,
        entry_fogs=[],
        exit_fogs=[],
        defeat_flag=defeat_flag,
    )


def _entity(
    eid: int,
    *,
    is_dragon: bool = False,
    arena_forbids_dragon: bool = False,
    arena_size: int = 5,
    boss_size: int = 1,
    exclude_from_pool: bool = False,
    pool: str | None = None,
) -> EntityTags:
    return EntityTags(
        entity_id=eid,
        name=f"e{eid}",
        region=1,
        scaling=1,
        dlc=False,
        pool=pool,
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
        arena=ArenaTags(
            size=arena_size,
            type=1,
            two_phase_not_allowed=False,
            dragon_not_allowed=arena_forbids_dragon,
            npc_not_allowed=False,
            is_escapable=False,
            night_boss=False,
        ),
    )


def test_generate_item_config_basic():
    """generate_item_config creates correct JSON structure."""
    config = Config.from_dict({})
    seed = 12345

    result = generate_item_config(config, seed)

    assert result["seed"] == 12345
    assert result["difficulty"] == 50
    assert result["options"]["item"] is True
    assert result["options"]["enemy"] is True
    assert result["options"]["fog"] is True
    assert result["options"]["crawl"] is True
    assert result["options"]["weaponreqs"] is True
    assert result["options"]["dlc"] is True
    assert result["options"]["sombermode"] is True
    assert result["options"]["mats"] is True
    assert result["options"]["editnames"] is True
    assert "preset" not in result
    assert result["enemy_options"]["randomize_bosses"] == "none"
    assert result["helper_options"]["autoUpgradeWeapons"] is True
    # All 14 bool options must be explicitly set (DLL defaults most to true)
    helper = result["helper_options"]
    assert len(helper) == 14
    # Auto-equip: all disabled
    assert helper["autoEquip"] is False
    assert helper["equipShop"] is False
    assert helper["equipWeapons"] is False
    assert helper["bowLeft"] is False
    assert helper["castLeft"] is False
    assert helper["equipArmor"] is False
    assert helper["equipAccessory"] is False
    assert helper["equipSpells"] is False
    assert helper["equipCrystalTears"] is False
    # Auto-upgrade: enabled
    assert helper["autoUpgrade"] is True
    assert helper["autoUpgradeSpiritAshes"] is True
    assert helper["autoUpgradeDropped"] is True
    assert helper["regionLockWeapons"] is False


def test_generate_item_config_custom_settings():
    """generate_item_config respects custom config."""
    config = Config.from_dict(
        {
            "item_randomizer": {
                "difficulty": 75,
                "remove_requirements": False,
                "auto_upgrade_weapons": False,
            }
        }
    )
    seed = 99999

    result = generate_item_config(config, seed)

    assert result["seed"] == 99999
    assert result["difficulty"] == 75
    assert result["options"]["weaponreqs"] is False
    assert result["helper_options"]["autoUpgradeWeapons"] is False
    # Auto-equip still disabled regardless of auto_upgrade_weapons
    assert result["helper_options"]["autoEquip"] is False


def test_generate_item_config_with_item_preset():
    """generate_item_config includes item_preset_path when item_preset enabled."""
    config = Config.from_dict({"item_randomizer": {"item_preset": True}})
    result = generate_item_config(config, 42)

    assert result["item_preset_path"] == "item_preset.yaml"


def test_generate_item_config_without_item_preset():
    """generate_item_config omits item_preset_path when item_preset disabled."""
    config = Config.from_dict({"item_randomizer": {"item_preset": False}})
    result = generate_item_config(config, 42)

    assert "item_preset_path" not in result


def test_generate_item_config_json_serializable():
    """generate_item_config output is JSON serializable."""
    config = Config.from_dict({})
    result = generate_item_config(config, 42)

    # Should not raise
    json_str = json.dumps(result)
    assert isinstance(json_str, str)


def test_run_item_randomizer_missing_wrapper(tmp_path):
    """run_item_randomizer returns False if wrapper not found."""
    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    output_dir = tmp_path / "output"

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=game_dir,
        output_dir=output_dir,
        platform=None,
        verbose=False,
    )

    assert result is False


def test_run_item_randomizer_builds_correct_command(tmp_path, monkeypatch):
    """run_item_randomizer builds correct command line."""
    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    (seed_dir / "item_config.json").write_text("{}")
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    output_dir = tmp_path / "output"

    # Mock the wrapper executable existence
    project_root = Path(__file__).parent.parent
    wrapper_exe = (
        project_root
        / "writer"
        / "ItemRandomizerWrapper"
        / "publish"
        / "win-x64"
        / "ItemRandomizerWrapper.exe"
    )

    captured_cmd = []

    def mock_popen(cmd, **kwargs):
        captured_cmd.extend(cmd)
        mock_process = MagicMock()
        mock_process.stdout = iter([])
        mock_process.wait.return_value = None
        mock_process.returncode = 0
        return mock_process

    # Only run if wrapper exists (skip in CI)
    if not wrapper_exe.exists():
        import pytest

        pytest.skip("ItemRandomizerWrapper not built")

    monkeypatch.setattr("subprocess.Popen", mock_popen)

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=game_dir,
        output_dir=output_dir,
        platform="windows",
        verbose=False,
    )

    assert result is True
    assert str(seed_dir / "item_config.json") in captured_cmd
    assert "--game-dir" in captured_cmd


def test_generate_item_config_enemy_options_default():
    """generate_item_config includes enemy_options with defaults."""
    config = Config.from_dict({})
    result = generate_item_config(config, 42)

    assert "enemy_options" in result
    assert result["enemy_options"]["randomize_bosses"] == "none"
    assert result["enemy_options"]["ignore_arena_size"] is False
    assert result["enemy_options"]["swap_boss"] is False
    # preset key should no longer be present
    assert "preset" not in result


def test_generate_item_config_enemy_options_enabled():
    """generate_item_config passes through enemy randomization settings."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all", "swap_boss": True}})
    # tags must be provided whenever randomize_bosses != "none"; empty map
    # means no DAG boss clusters to assign.
    result = generate_item_config(config, 42, tags={})

    assert result["enemy_options"]["randomize_bosses"] == "all"
    assert result["enemy_options"]["swap_boss"] is True


def test_generate_item_config_no_assignments_when_randomize_bosses_none():
    """No enemy_assignments when boss randomization is disabled."""
    config = Config.from_dict({})
    result = generate_item_config(config, seed=1, boss_clusters=[], tags={})
    assert "enemy_assignments" not in result


def test_generate_item_config_includes_assignments_for_major():
    """Compatibility filters shrink the pool before the greedy matcher picks."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    # Single major_boss cluster; defeat_flag 1000 maps to entity_id 1000.
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    # Arena 1000 forbids dragons. Among {2000, 3000}, only 2000 is non-dragon.
    tags = {
        1000: _entity(1000, arena_forbids_dragon=True),
        2000: _entity(2000),
        3000: _entity(3000, is_dragon=True),
    }
    result = generate_item_config(
        config,
        seed=42,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[2000, 3000],
        vanilla_minor_ids=[],
        phase_mapping={},
    )
    assert "enemy_assignments" in result
    assert result["enemy_assignments"] == {"1000": "2000"}


def test_compose_pool_merges_vanilla_and_source_only_entries():
    """Vanilla IDs for ``kind`` plus source-only entries with matching ``pool``
    are unioned; the filter ``exclude_from_pool`` drops entries from either
    source; the result is key-sorted for stable iteration."""
    tags = {
        100: _entity(100, pool="major"),  # source-only major
        200: _entity(200),  # vanilla, not excluded
        300: _entity(300, exclude_from_pool=True),  # vanilla, excluded
        400: _entity(400, pool="minor"),  # source-only minor (wrong kind)
    }
    pool = _compose_pool(tags, "major", vanilla_ids=[200, 300])
    assert list(pool.keys()) == [100, 200]
    assert 300 not in pool
    assert 400 not in pool


def test_compose_pool_raises_when_vanilla_id_missing_from_tags():
    """A vanilla boss entity from clusters.json that is absent from
    boss_arena_tags.json is a data gap, not a silent skip."""
    tags = {100: _entity(100)}
    with pytest.raises(KeyError, match="9999"):
        _compose_pool(tags, "major", vanilla_ids=[9999])


def test_generate_item_config_raises_when_cluster_leader_missing_from_tags():
    """A DAG boss cluster whose entity has no tag entry is a config error,
    not something to silently skip (the run would keep a vanilla boss)."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=9999)]
    tags = {2000: _entity(2000)}  # 9999 is absent
    with pytest.raises(KeyError, match="9999"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[2000],
            vanilla_minor_ids=[],
            phase_mapping={},
        )


def test_generate_item_config_raises_when_cluster_leader_has_no_arena_block():
    """An entity that is source-only (no arena block) cannot be an arena target."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    source_only = EntityTags(
        entity_id=1000,
        name="orphan",
        region=1,
        scaling=1,
        dlc=False,
        pool="major",
        boss=BossTags(
            size=1,
            type=1,
            is_two_phase=False,
            is_dragon=False,
            is_npc=False,
            can_escape=False,
            night_boss=False,
            exclude_from_pool=False,
        ),
        arena=None,
    )
    tags = {1000: source_only, 2000: _entity(2000)}
    with pytest.raises(KeyError, match="no arena block"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[2000],
            vanilla_minor_ids=[],
            phase_mapping={},
        )


def test_generate_item_config_pool_excludes_from_pool_entries():
    """Entities with boss.exclude_from_pool=True are filtered at pool
    composition: they never appear as sources, but their arena can still
    receive a replacement."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {
        1000: _entity(1000),  # arena target, also self-source
        2000: _entity(2000, exclude_from_pool=True),  # never a source
        3000: _entity(3000),
    }
    result = generate_item_config(
        config,
        seed=42,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[1000, 2000, 3000],
        vanilla_minor_ids=[],
        phase_mapping={},
    )
    # Arena 1000 can receive any non-excluded source, but never 2000.
    assert result["enemy_assignments"]["1000"] in {"1000", "3000"}


def test_generate_item_config_expands_multi_phase_slots():
    """Multi-phase majors must produce one entry per phase, independently."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    # Fire Giant-shaped cluster: leader entity 1052520800, phase1 at 1052520801.
    boss_clusters = [
        _boss_cluster("fg", "major_boss", defeat_flag=1052520800),
    ]

    tags = {eid: _entity(eid) for eid in (1052520800, 1052520801, 2000, 3000)}
    result = generate_item_config(
        config,
        seed=1,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[2000, 3000],
        vanilla_minor_ids=[],
        phase_mapping={1052520800: 1052520801},
    )
    assignments = result["enemy_assignments"]
    assert set(assignments.keys()) == {"1052520800", "1052520801"}
    # Both slots get distinct sources drawn from the major pool.
    assert len(set(assignments.values())) == 2
    assert set(assignments.values()) <= {"2000", "3000"}
