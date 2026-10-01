"""Tests for Item Randomizer integration."""

import json
from contextlib import contextmanager
from pathlib import Path

import pytest

from speedfog.boss_arena_constraints import (
    ArenaTags,
    BossTags,
    EntityTags,
    MatchingError,
    extreme_sets,
)
from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.item_randomizer import (
    _compose_pool,
    _family_forbidden,
    ensure_vanilla_cache,
    generate_item_config,
    run_item_randomizer,
    vanilla_cache_guard,
    vanilla_cache_is_complete,
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
    name: str | None = None,
    is_dragon: bool = False,
    arena_forbids_dragon: bool = False,
    arena_size: int = 5,
    arena_type: int = 1,
    boss_size: int = 1,
    exclude_from_pool: bool = False,
    pool: str | None = None,
    has_arena: bool = True,
    dlc: bool = False,
    weight: float = 1.0,
    weights: tuple[float, float, float] | None = None,
) -> EntityTags:
    arena = (
        ArenaTags(
            size=arena_size,
            type=arena_type,
            two_phase_not_allowed=False,
            dragon_not_allowed=arena_forbids_dragon,
            npc_not_allowed=False,
            is_escapable=False,
            night_boss=False,
        )
        if has_arena
        else None
    )
    return EntityTags(
        entity_id=eid,
        name=name if name is not None else f"e{eid}",
        region=1,
        scaling=1,
        dlc=dlc,
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
            weights=weights if weights is not None else (weight, weight, weight),
        ),
        arena=arena,
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
    # Pinned GUI defaults: v0.12 keys these on Switch: options in the
    # annotations, and unset booleans are false headless (silent flip).
    assert result["options"]["dlcblessing"] is True
    assert result["options"]["spellshops"] is True
    # editnames was intentionally removed (596cad7): boss names are no longer edited.
    assert "editnames" not in result["options"]
    assert result["options"]["scale"] is True
    assert result["options"]["phasehp"] is True
    assert "preset" not in result
    assert result["enemy_options"]["randomize_bosses"] == "none"
    assert result["helper_options"]["autoUpgradeWeapons"] is True
    # All 15 bool options must be explicitly set (DLL defaults most to true)
    helper = result["helper_options"]
    assert len(helper) == 15
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
    assert helper["autoUpgradeEquipped"] is True
    assert helper["regionLockWeapons"] is False


def test_generate_item_config_tarnished_default_off():
    """The Tarnished Pack option defaults to off: pack items and invaders stay
    out of the pool and non-owners get no startup error dialog."""
    config = Config.from_dict({})

    result = generate_item_config(config, 12345)

    assert result["options"]["tarnished"] is False


def test_generate_item_config_tarnished_enabled():
    """[tarnished] enabled = true flows through to the randomizer
    option (pack-owner-only seeds)."""
    config = Config.from_dict({"tarnished": {"enabled": True}})

    result = generate_item_config(config, 12345)

    assert result["options"]["tarnished"] is True


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


AUTO_EQUIP_KEYS = (
    "autoEquip",
    "equipWeapons",
    "bowLeft",
    "castLeft",
    "equipArmor",
    "equipAccessory",
    "equipSpells",
    "equipCrystalTears",
)


def test_generate_item_config_auto_equip_enabled():
    """auto_equip=True flips the eight auto-equip helper options to True."""
    config = Config.from_dict({"item_randomizer": {"auto_equip": True}})
    result = generate_item_config(config, 42)

    helper = result["helper_options"]
    for key in AUTO_EQUIP_KEYS:
        assert helper[key] is True, key
    # equipShop is not part of auto_equip and stays disabled.
    assert helper["equipShop"] is False


def test_generate_item_config_auto_equip_default_disabled():
    """auto_equip defaults to False, keeping the eight options disabled."""
    config = Config.from_dict({})
    result = generate_item_config(config, 42)

    helper = result["helper_options"]
    for key in AUTO_EQUIP_KEYS:
        assert helper[key] is False, key
    assert helper["equipShop"] is False


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

    def mock_stream(cmd, cwd=None):
        captured_cmd.extend(cmd)
        return 0

    # Only run if wrapper exists (skip in CI)
    if not wrapper_exe.exists():
        import pytest

        pytest.skip("ItemRandomizerWrapper not built")

    monkeypatch.setattr("speedfog.item_randomizer.stream_command", mock_stream)

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


def test_compose_pool_includes_phase1_siblings_of_vanilla_leaders():
    """Phase-1 entities are independent arena slots (see
    ``_build_enemy_assignments``) but share the pool of their phase-2 leader.
    Without symmetrising the pool here the matcher can hit ``|arenas| >
    |pool|`` purely because of phase expansion. Matches BossArenaRandomizer,
    where every entity is both arena and boss.
    """
    tags = {
        100: _entity(100),  # vanilla leader, two-phase boss
        101: _entity(101),  # its phase-1 sibling
        200: _entity(200),  # another vanilla leader, single phase
        999: _entity(999, exclude_from_pool=True),  # excluded phase-1
        998: _entity(998),  # phase-1 of a leader not in vanilla_ids
    }
    phase_mapping = {100: 101, 200: 999, 500: 998}
    pool = _compose_pool(
        tags, "major", vanilla_ids=[100, 200], phase_mapping=phase_mapping
    )
    # 101 added because its leader 100 is a vanilla major.
    assert 101 in pool
    # 999 rejected by exclude_from_pool, same rule as leaders.
    assert 999 not in pool
    # 998 skipped because leader 500 is not in vanilla_ids (its cluster would
    # not be in the DAG either).
    assert 998 not in pool
    assert sorted(pool) == [100, 101, 200]


def test_compose_pool_phase1_with_matching_source_pool_not_double_counted():
    """A phase-1 entity tagged ``pool = "major"`` reaches the pool through
    both the phase-mapping branch and the source-only sweep. Dict semantics
    prevent a duplicate; the entry appears exactly once with its real
    ``BossTags``.
    """
    tags = {
        100: _entity(100),  # vanilla major leader
        101: _entity(101, pool="major"),  # phase-1 sibling also source-tagged
    }
    pool = _compose_pool(tags, "major", vanilla_ids=[100], phase_mapping={100: 101})
    assert list(pool) == [100, 101]
    assert pool[101] is tags[101].boss


def test_compose_pool_minor_kind_ignores_major_only_phase_mapping():
    """Both call sites pass the full ``phase_mapping`` to both pools. When
    ``kind`` is minor and a mapped leader is a major (not in the minor
    vanilla list), the phase-mapping branch must not pull the phase-1
    sibling into the minor pool. Verifies the leader-membership guard
    (``leader not in vanilla_set``).

    Source-only entries (no arena) keep the orphan-arena rule out of the
    way so the assertion isolates the phase-mapping branch.
    """
    tags = {
        100: _entity(100, pool="major", has_arena=False),  # source-only major leader
        101: _entity(101, pool="major", has_arena=False),  # its phase-1 sibling
        200: _entity(200, pool="minor", has_arena=False),  # source-only minor
        300: _entity(
            300, pool="minor", has_arena=False
        ),  # vanilla minor (per the call)
    }
    # vanilla_ids=[300] makes the phase-mapping branch run (vanilla_set is
    # non-empty); 100 is intentionally absent so the leader-membership
    # guard skips 101.
    pool = _compose_pool(tags, "minor", vanilla_ids=[300], phase_mapping={100: 101})
    assert 101 not in pool
    assert list(pool) == [200, 300]


def test_compose_pool_orphan_arena_type2_joins_major_pool():
    """An entry with an arena block, no ``pool`` field, and not in
    ``vanilla_ids`` is an "orphan" arena binding (DLC field bosses, evergaol
    variants, phase entities for fights absent from clusters.json). The
    orphan rule routes ``arena.type == 2`` orphans to the major pool so
    they can serve as replacement candidates in major slots.
    """
    tags = {
        100: _entity(100, arena_type=2),  # orphan, type=2 → major
        200: _entity(200, arena_type=3),  # orphan, type≠2 → minor (not here)
    }
    major = _compose_pool(tags, "major", vanilla_ids=[])
    minor = _compose_pool(tags, "minor", vanilla_ids=[])
    assert list(major) == [100]
    assert 100 not in minor
    assert list(minor) == [200]


def test_compose_pool_orphan_arena_other_types_join_minor_pool():
    """Every ``arena.type`` other than 2 routes an orphan to the minor pool
    (covers field bosses with type=3, evergaols with type=7, tombs with
    type=5, etc.). Verifies the catch-all branch of the orphan rule.
    """
    tags = {
        100: _entity(100, arena_type=1),
        300: _entity(300, arena_type=3),
        500: _entity(500, arena_type=5),
        700: _entity(700, arena_type=7),
    }
    pool = _compose_pool(tags, "minor", vanilla_ids=[])
    assert list(pool) == [100, 300, 500, 700]
    assert _compose_pool(tags, "major", vanilla_ids=[]) == {}


def test_compose_pool_orphan_respects_exclude_from_pool():
    """The orphan branch reuses the same ``exclude_from_pool`` filter as the
    vanilla and source-only branches: variants tagged as duplicates
    (``Night's Cavalry`` / ``Deathbird`` / ``Death Rite Bird`` aliases) stay
    out of the pool even when their arena block would otherwise admit them.
    """
    tags = {
        100: _entity(
            100, arena_type=2, exclude_from_pool=True
        ),  # excluded major orphan
        200: _entity(
            200, arena_type=3, exclude_from_pool=True
        ),  # excluded minor orphan
        300: _entity(300, arena_type=3),  # kept minor orphan
    }
    assert _compose_pool(tags, "major", vanilla_ids=[]) == {}
    minor = _compose_pool(tags, "minor", vanilla_ids=[])
    assert list(minor) == [300]


def test_compose_pool_vanilla_entry_does_not_leak_via_orphan_branch():
    """A vanilla entry whose ``arena.type`` would route to the *other* kind
    must stay out of that other pool: the orphan branch is gated by the
    union ``vanilla_ids | other_vanilla_ids``. Without that guard, a
    ``boss_arena`` cluster with ``arena.type == 2`` would silently end up
    in the major pool as well (real-data regression: 4 such entries exist
    in the live ``boss_arena_tags.json``).
    """
    tags = {
        # Vanilla minor (boss_arena cluster) that happens to have arena.type=2.
        100: _entity(100, arena_type=2),
    }
    minor = _compose_pool(tags, "minor", vanilla_ids=[100])
    major = _compose_pool(tags, "major", vanilla_ids=[], other_vanilla_ids=[100])
    assert list(minor) == [100]  # vanilla branch keeps it where it belongs
    assert 100 not in major  # orphan branch must not pick it up


def test_compose_pool_pool_field_overrides_arena_type():
    """An entry with both a ``pool`` field and an ``arena`` block routes by
    ``pool``, not by ``arena.type``. This matters for entries explicitly
    tagged into a pool different from what their arena type would suggest
    (the ``pool`` field is authoritative).
    """
    tags = {
        # arena.type=2 would route to major, but pool="minor" wins.
        100: _entity(100, arena_type=2, pool="minor"),
        # arena.type=3 would route to minor, but pool="major" wins.
        200: _entity(200, arena_type=3, pool="major"),
    }
    major = _compose_pool(tags, "major", vanilla_ids=[])
    minor = _compose_pool(tags, "minor", vanilla_ids=[])
    assert list(major) == [200]
    assert list(minor) == [100]


def test_compose_pool_phase1_missing_from_tags_is_silent():
    """A leader may have a phase mapping whose phase-1 entity is absent from
    the tag file (partial data). That's a tolerable gap, not fatal: the
    leader's own slot still gets an assignment. The strict check stays on
    ``vanilla_ids`` where a gap indicates a misconfigured cluster.
    """
    tags = {100: _entity(100)}
    pool = _compose_pool(tags, "major", vanilla_ids=[100], phase_mapping={100: 777})
    assert list(pool) == [100]


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


def test_compose_pool_exclude_dlc_filters_vanilla_branch():
    """When ``exclude_dlc=True``, DLC-tagged vanilla entries drop from the pool
    (the matcher will pick a non-DLC replacement for their arena instead)."""
    tags = {
        100: _entity(100),
        200: _entity(200, dlc=True),
    }
    pool = _compose_pool(tags, "major", vanilla_ids=[100, 200], exclude_dlc=True)
    assert list(pool) == [100]


def test_compose_pool_exclude_dlc_filters_phase1_branch():
    """``exclude_dlc=True`` skips DLC-tagged phase-1 siblings even when their
    leader is a vanilla pool entry."""
    tags = {
        100: _entity(100),  # vanilla leader, non-DLC
        101: _entity(101, dlc=True),  # DLC phase-1 sibling
    }
    pool = _compose_pool(
        tags, "major", vanilla_ids=[100], phase_mapping={100: 101}, exclude_dlc=True
    )
    assert list(pool) == [100]


def test_compose_pool_exclude_dlc_filters_source_only_branch():
    """``exclude_dlc=True`` skips DLC-tagged source-only entries (``pool`` set)."""
    tags = {
        100: _entity(100, pool="major"),
        200: _entity(200, pool="major", dlc=True),
    }
    pool = _compose_pool(tags, "major", vanilla_ids=[], exclude_dlc=True)
    assert list(pool) == [100]


def test_compose_pool_exclude_dlc_filters_orphan_branch():
    """``exclude_dlc=True`` skips DLC-tagged orphan arena entries (no ``pool``
    field, not in vanilla_ids, routed by ``arena.type``)."""
    tags = {
        100: _entity(100, arena_type=3),  # non-DLC orphan minor
        200: _entity(200, arena_type=3, dlc=True),  # DLC orphan minor
    }
    pool = _compose_pool(tags, "minor", vanilla_ids=[], exclude_dlc=True)
    assert list(pool) == [100]


def test_compose_pool_exclude_dlc_default_off_keeps_dlc_entries():
    """Default behaviour (``exclude_dlc=False``) is unchanged: DLC entries
    remain eligible. Regression guard against silently flipping the default."""
    tags = {
        100: _entity(100),
        200: _entity(200, dlc=True),
    }
    pool = _compose_pool(tags, "major", vanilla_ids=[100, 200])
    assert list(pool) == [100, 200]


def test_generate_item_config_enemy_dlc_bosses_filters_pool():
    """``enemy.dlc_bosses = false`` removes DLC bosses from the candidate
    pool, so a DLC vanilla arena receives a non-DLC replacement."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "dlc_bosses": False}}
    )
    # Cluster leader 1000 is itself DLC; its arena must still be filled, but
    # from the non-DLC subset of the pool (only 3000 qualifies).
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {
        1000: _entity(1000, dlc=True),
        2000: _entity(2000, dlc=True),
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
    # Arena 1000 is preserved (only the pool was filtered, not the arena set).
    assert result["enemy_assignments"] == {"1000": "3000"}


def test_generate_item_config_enemy_dlc_bosses_default_keeps_dlc():
    """Default ``enemy.dlc_bosses = true`` is non-restrictive: DLC bosses
    remain in the candidate pool."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {
        1000: _entity(1000),
        2000: _entity(2000, dlc=True),
    }
    result = generate_item_config(
        config,
        seed=42,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[1000, 2000],
        vanilla_minor_ids=[],
        phase_mapping={},
    )
    # Either source is acceptable; the DLC one must not be filtered.
    assert result["enemy_assignments"]["1000"] in {"1000", "2000"}


def test_generate_item_config_enemy_dlc_bosses_empty_pool_raises():
    """When ``enemy.dlc_bosses=False`` strips the pool below what arenas
    require, the matcher raises ``MatchingError`` rather than silently
    leaving the arena vanilla. Locks the failure-surface contract for this
    code path."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "dlc_bosses": False}}
    )
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    # Sole pool candidate is DLC; the filter empties the pool entirely.
    tags = {1000: _entity(1000, dlc=True)}
    with pytest.raises(MatchingError):
        generate_item_config(
            config,
            seed=42,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[1000],
            vanilla_minor_ids=[],
            phase_mapping={},
        )


def test_compose_pool_exclude_dlc_dlc_leader_keeps_non_dlc_phase1():
    """The leader filter and the phase-1 filter act independently: a DLC
    vanilla leader is dropped from the pool, but its non-DLC phase-1 sibling
    is still inserted (the phase-1 branch gates on ``leader in vanilla_set``,
    not on the leader's presence in the resulting pool). Locks this against
    a future "tidy-up" refactor that would couple the two filters."""
    tags = {
        100: _entity(100, dlc=True),  # DLC vanilla leader
        101: _entity(101),  # non-DLC phase-1 sibling
    }
    pool = _compose_pool(
        tags,
        "major",
        vanilla_ids=[100],
        phase_mapping={100: 101},
        exclude_dlc=True,
    )
    assert list(pool) == [101]


def test_generate_item_config_allowlist_pins_single_boss():
    """enemy.bosses=['Malenia'] pins every randomized arena to Malenia."""
    config = Config.from_dict(
        {
            "enemy": {
                "randomize_bosses": "all",
                "ignore_arena_size": True,
                "bosses": ["Malenia"],
            }
        }
    )
    # One major arena + one minor arena; Malenia (15000800) is the only allowed
    # boss and lives outside the arena set.
    boss_clusters = [
        _boss_cluster("major1", "major_boss", defeat_flag=1000),
        _boss_cluster("minor1", "boss_arena", defeat_flag=2000),
    ]
    tags = {
        1000: _entity(1000, arena_size=5),
        2000: _entity(2000, arena_size=1),  # small arena
        15000800: _entity(
            15000800, name="Malenia Blade of Miquella", boss_size=5
        ),  # large boss, no arena slot needed
    }
    result = generate_item_config(
        config,
        seed=7,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[1000],
        vanilla_minor_ids=[2000],
        phase_mapping={},
    )
    assert result["enemy_assignments"] == {"1000": "15000800", "2000": "15000800"}


def test_generate_item_config_allowlist_minor_only_skips_majors():
    """With randomize_bosses='minor', only minor arenas are pinned."""
    config = Config.from_dict(
        {
            "enemy": {
                "randomize_bosses": "minor",
                "ignore_arena_size": True,
                "bosses": ["Malenia"],
            }
        }
    )
    boss_clusters = [
        _boss_cluster("major1", "major_boss", defeat_flag=1000),
        _boss_cluster("minor1", "boss_arena", defeat_flag=2000),
    ]
    tags = {
        1000: _entity(1000),
        2000: _entity(2000, arena_size=1),
        15000800: _entity(15000800, name="Malenia Blade of Miquella", boss_size=5),
    }
    result = generate_item_config(
        config,
        seed=7,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[1000],
        vanilla_minor_ids=[2000],
        phase_mapping={},
    )
    # Major arena 1000 is untouched; only minor arena 2000 is pinned.
    assert result["enemy_assignments"] == {"2000": "15000800"}


def test_generate_item_config_allowlist_unsatisfiable_raises():
    """A large pinned boss in a small arena with size checks on fails."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "bosses": ["Malenia"]}}
    )
    boss_clusters = [_boss_cluster("minor1", "boss_arena", defeat_flag=2000)]
    tags = {
        2000: _entity(2000, arena_size=1),
        15000800: _entity(15000800, name="Malenia Blade of Miquella", boss_size=5),
    }
    with pytest.raises(MatchingError):
        generate_item_config(
            config,
            seed=7,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=[2000],
            phase_mapping={},
        )


def test_generate_item_config_allowlist_expands_phase_slots():
    """Allowlist path expands multi-phase clusters into one slot per phase."""
    config = Config.from_dict(
        {
            "enemy": {
                "randomize_bosses": "all",
                "ignore_arena_size": True,
                "bosses": ["Malenia"],
            }
        }
    )
    # Two-phase major cluster: leader 500, phase-1 sibling 501.
    boss_clusters = [_boss_cluster("two_phase", "major_boss", defeat_flag=500)]
    tags = {
        500: _entity(500),
        501: _entity(501),
        15000800: _entity(15000800, name="Malenia Blade of Miquella"),
    }
    result = generate_item_config(
        config,
        seed=3,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[500],
        vanilla_minor_ids=[],
        phase_mapping={500: 501},
    )
    # Both phase slots should be assigned to Malenia (only pool candidate).
    assert result["enemy_assignments"] == {"500": "15000800", "501": "15000800"}


def test_generate_item_config_allowlist_raises_when_cluster_leader_missing_from_tags():
    """Allowlist path raises KeyError for a cluster leader absent from tags."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "bosses": ["Malenia"]}}
    )
    boss_clusters = [_boss_cluster("c1", "boss_arena", defeat_flag=9999)]
    tags = {
        15000800: _entity(15000800, name="Malenia Blade of Miquella"),
    }
    with pytest.raises(KeyError, match="9999"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=[9999],
            phase_mapping={},
        )


def test_generate_item_config_allowlist_raises_when_cluster_leader_has_no_arena_block():
    """Allowlist path raises KeyError for a cluster leader with no arena block."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "bosses": ["Malenia"]}}
    )
    boss_clusters = [_boss_cluster("c1", "boss_arena", defeat_flag=2000)]
    source_only = EntityTags(
        entity_id=2000,
        name="orphan",
        region=1,
        scaling=1,
        dlc=False,
        pool="minor",
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
    tags = {
        2000: source_only,
        15000800: _entity(15000800, name="Malenia Blade of Miquella"),
    }
    with pytest.raises(KeyError, match="no arena block"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=[2000],
            phase_mapping={},
        )


def test_generate_item_config_force_maps_final_boss_in_all_mode():
    """A final_boss arena is force-mapped from the major pool in "all" mode."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    # final_boss cluster; defeat_flag 1000 -> entity_id 1000 is the arena.
    boss_clusters = [_boss_cluster("final", "final_boss", defeat_flag=1000)]
    # Arena 1000 forbids dragons. Among sources {2000, 3000}, only 2000 fits.
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
    assert result["enemy_assignments"] == {"1000": "2000"}


def test_generate_item_config_final_boss_not_assigned_in_minor_mode():
    """final_boss is gated by randomize_majors: untouched in "minor" mode."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    boss_clusters = [_boss_cluster("final", "final_boss", defeat_flag=1000)]
    tags = {1000: _entity(1000), 2000: _entity(2000)}
    result = generate_item_config(
        config,
        seed=42,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[2000],
        vanilla_minor_ids=[],
        phase_mapping={},
    )
    assert "enemy_assignments" not in result


def test_generate_item_config_force_maps_final_boss_uniform_allowlist():
    """final_boss arena is matched in the allowlist (uniform) path too."""
    config = Config.from_dict(
        {
            "enemy": {
                "randomize_bosses": "all",
                "bosses": ["Target"],
                "ignore_arena_size": True,
            }
        }
    )
    boss_clusters = [_boss_cluster("final", "final_boss", defeat_flag=1000)]
    tags = {
        1000: _entity(1000, name="Final Arena"),
        5000: _entity(5000, name="Target Boss"),
    }
    result = generate_item_config(
        config,
        seed=42,
        boss_clusters=boss_clusters,
        tags=tags,
        phase_mapping={},
    )
    assert result["enemy_assignments"] == {"1000": "5000"}


def test_family_forbidden_single_phase_is_self_only():
    assert _family_forbidden([1000], {}) == {1000: frozenset({1000})}


def test_family_forbidden_multi_phase_covers_both_directions():
    """Leader and phase-1 arenas each forbid the whole family."""
    result = _family_forbidden([100, 101, 2000], {100: 101})
    assert result == {
        100: frozenset({100, 101}),
        101: frozenset({100, 101}),
        2000: frozenset({2000}),
    }


def test_generate_item_config_never_places_boss_in_own_arena():
    """The vanilla pool contains the arena's own entity; it must never stay."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {1000: _entity(1000), 2000: _entity(2000)}
    for seed in range(32):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[1000, 2000],
            vanilla_minor_ids=[],
            phase_mapping={},
        )
        assert result["enemy_assignments"] == {"1000": "2000"}, f"seed {seed}"


def test_generate_item_config_multi_phase_excludes_whole_family():
    """No family member may land in any family slot (leader or phase 1)."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("fg", "major_boss", defeat_flag=1052520800)]
    tags = {eid: _entity(eid) for eid in (1052520800, 1052520801, 2000, 3000)}
    for seed in range(32):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[1052520800, 2000, 3000],
            vanilla_minor_ids=[],
            phase_mapping={1052520800: 1052520801},
        )
        assignments = result["enemy_assignments"]
        assert set(assignments.keys()) == {"1052520800", "1052520801"}
        assert set(assignments.values()) == {"2000", "3000"}, f"seed {seed}"


def test_generate_item_config_allowlist_prefers_non_self():
    """Allowlist mode avoids self when another pinned boss is compatible."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "bosses": ["Malenia", "Radahn"]}}
    )
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {
        1000: _entity(1000, name="Malenia Blade of Miquella"),
        2000: _entity(2000, name="Starscourge Radahn"),
    }
    for seed in range(32):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[1000],
            vanilla_minor_ids=[],
            phase_mapping={},
        )
        assert result["enemy_assignments"] == {"1000": "2000"}, f"seed {seed}"


def test_generate_item_config_allowlist_self_fallback():
    """A single-boss allowlist naming the arena's own boss still generates."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "all", "bosses": ["Malenia"]}}
    )
    boss_clusters = [_boss_cluster("c1", "major_boss", defeat_flag=1000)]
    tags = {1000: _entity(1000, name="Malenia Blade of Miquella")}
    result = generate_item_config(
        config,
        seed=1,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[1000],
        vanilla_minor_ids=[],
        phase_mapping={},
    )
    assert result["enemy_assignments"] == {"1000": "1000"}


def _layered_minor_setup():
    """Three layers of two parallel minor arenas and a 28-boss pool with rare extremes."""
    arena_ids = [1001, 1002, 1003, 1004, 1005, 1006]
    boss_clusters = [
        _boss_cluster(f"c{eid}", "boss_arena", defeat_flag=eid) for eid in arena_ids
    ]
    boss_layers = {f"c{eid}": 1 + i // 2 for i, eid in enumerate(arena_ids)}
    weights = (0.5, 1.0, 1.0, 1.0, 1.5, 3.0)
    pool_ids = list(range(2001, 2029))
    tags = {eid: _entity(eid) for eid in arena_ids}
    tags.update(
        {
            eid: _entity(eid, weight=weights[i % len(weights)])
            for i, eid in enumerate(pool_ids)
        }
    )
    return boss_clusters, boss_layers, tags, pool_ids


def test_generate_item_config_keeps_minor_extremes_apart():
    config = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    boss_clusters, boss_layers, tags, pool_ids = _layered_minor_setup()
    # The minor pool: vanilla minors plus the six arena entities (orphans).
    pool = {
        eid: tags[eid].boss for eid in pool_ids + [1001, 1002, 1003, 1004, 1005, 1006]
    }
    light, heavy = extreme_sets(pool, "mid", 0.10)
    assert light and heavy
    for seed in range(30):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=pool_ids,
            phase_mapping={},
            boss_layers=boss_layers,
        )
        placed = {int(a): int(b) for a, b in result["enemy_assignments"].items()}
        for layer in (1, 2, 3):
            bosses = {
                placed[int(cid[1:])] for cid, lyr in boss_layers.items() if lyr == layer
            }
            assert not (bosses & light and bosses & heavy), f"seed {seed} layer {layer}"


def test_generate_item_config_balance_off_keeps_legacy_assignments():
    """No layers, a zero fraction or uniform weights leave assignments untouched."""
    boss_clusters, boss_layers, tags, pool_ids = _layered_minor_setup()
    uniform = {eid: _entity(eid) for eid in tags}
    legacy_cfg = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    off_cfg = Config.from_dict(
        {"enemy": {"randomize_bosses": "minor", "boss_extreme_fraction": 0}}
    )

    def run(config, tag_map, layers, seed):
        return generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tag_map,
            vanilla_major_ids=[],
            vanilla_minor_ids=pool_ids,
            phase_mapping={},
            boss_layers=layers,
        )["enemy_assignments"]

    for seed in range(20):
        legacy = run(legacy_cfg, tags, None, seed)
        assert run(off_cfg, tags, boss_layers, seed) == legacy, f"seed {seed}"
        assert run(legacy_cfg, uniform, boss_layers, seed) == run(
            legacy_cfg, uniform, None, seed
        ), f"seed {seed}"


def test_generate_item_config_groups_phase1_slot_with_leader():
    """Leader and phase-1 slots share their node's layer, hence one group."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    boss_clusters = [_boss_cluster("fg", "major_boss", defeat_flag=1052520800)]
    tags = {eid: _entity(eid) for eid in (1052520800, 1052520801)}
    tags.update(
        {
            2001: _entity(2001, weight=1.0),
            2002: _entity(2002, weight=1.0),
            2003: _entity(2003, weight=5.0),
            2004: _entity(2004, weight=5.0),
        }
    )
    for seed in range(30):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[2001, 2002, 2003, 2004],
            vanilla_minor_ids=[],
            phase_mapping={1052520800: 1052520801},
            boss_layers={"fg": 4},
        )
        placed = [
            tags[int(b)].boss.weight_at("mid")
            for b in result["enemy_assignments"].values()
        ]
        assert len(placed) == 2
        assert placed[0] == placed[1], f"seed {seed}: {placed}"


def test_generate_item_config_raises_when_boss_cluster_missing_from_tiers():
    config = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    boss_clusters, boss_layers, tags, pool_ids = _layered_minor_setup()
    with pytest.raises(KeyError, match="boss_tiers"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=pool_ids,
            phase_mapping={},
            boss_layers=boss_layers,
            boss_tiers={},
        )


def test_generate_item_config_judges_a_layer_at_its_tier():
    """A boss heavy only late never meets the light extreme on a late layer."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    arena_ids = [1001, 1002, 1003, 1004]
    boss_clusters = [
        _boss_cluster(f"c{eid}", "boss_arena", defeat_flag=eid) for eid in arena_ids
    ]
    boss_layers = {"c1001": 3, "c1002": 3, "c1003": 9, "c1004": 9}
    boss_tiers = {"c1001": 5, "c1002": 5, "c1003": 18, "c1004": 18}
    tags = {eid: _entity(eid) for eid in arena_ids}
    tags.update(
        {
            2001: _entity(2001, weight=0.2),
            2002: _entity(2002, weights=(1.0, 1.0, 5.0)),
            2003: _entity(2003),
            2009: _entity(2009, weights=(3.0, 1.0, 1.0)),
        }
    )
    for seed in range(60):
        result = generate_item_config(
            config,
            seed=seed,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=[2001, 2002, 2003, 2009],
            phase_mapping={},
            boss_layers=boss_layers,
            boss_tiers=boss_tiers,
        )
        placed = {int(a): int(b) for a, b in result["enemy_assignments"].items()}
        assert {placed[1003], placed[1004]} != {2001, 2002}, f"seed {seed}"
        assert {placed[1001], placed[1002]} != {2001, 2009}, f"seed {seed}"


def test_generate_item_config_raises_when_boss_cluster_missing_from_layers():
    config = Config.from_dict({"enemy": {"randomize_bosses": "minor"}})
    boss_clusters, _, tags, pool_ids = _layered_minor_setup()
    with pytest.raises(KeyError, match="boss_layers"):
        generate_item_config(
            config,
            seed=1,
            boss_clusters=boss_clusters,
            tags=tags,
            vanilla_major_ids=[],
            vanilla_minor_ids=pool_ids,
            phase_mapping={},
            boss_layers={},
        )


def test_generate_item_config_allowlist_ignores_boss_weights():
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": "minor", "bosses": ["Light", "Heavy"]}}
    )
    boss_clusters = [
        _boss_cluster("a", "boss_arena", defeat_flag=1001),
        _boss_cluster("b", "boss_arena", defeat_flag=1002),
    ]
    tags = {
        1001: _entity(1001),
        1002: _entity(1002),
        3001: _entity(3001, name="Light Boss", weight=1.0),
        3002: _entity(3002, name="Heavy Boss", weight=5.0),
    }
    result = generate_item_config(
        config,
        seed=3,
        boss_clusters=boss_clusters,
        tags=tags,
        vanilla_major_ids=[],
        vanilla_minor_ids=[1001, 1002],
        phase_mapping={},
        boss_layers={"a": 2, "b": 2},
    )
    assert sorted(result["enemy_assignments"].values()) == ["3001", "3002"]


def _write_vanilla_manifest(
    diste_dir: Path, entries: list[tuple[str, str, int]]
) -> None:
    """Write a diste/Vanilla/files.txt listing (path, archive, size) entries."""
    vanilla_dir = diste_dir / "Vanilla"
    vanilla_dir.mkdir(parents=True, exist_ok=True)
    lines = [
        f"{path} {archive} {size} 0123456789abcdef" for path, archive, size in entries
    ]
    (vanilla_dir / "files.txt").write_text("\n".join(lines) + "\n")


def test_vanilla_cache_incomplete_without_manifest(tmp_path):
    """A diste without files.txt is never considered warm."""
    assert vanilla_cache_is_complete(tmp_path / "diste", tmp_path / "game") is False


def test_vanilla_cache_complete_when_every_file_matches(tmp_path):
    """Cached files of the expected size make the cache warm."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(
        diste_dir,
        [
            ("/map/mapstudio/m61_50_43_00.msb.dcx", "Data0", 4),
            ("/regulation.bin", "Data0", 2),
        ],
    )
    (diste_dir / "Vanilla" / "m61_50_43_00.msb.dcx").write_bytes(b"1234")
    (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"12")

    assert vanilla_cache_is_complete(diste_dir, tmp_path / "game") is True


def test_vanilla_cache_incomplete_when_file_missing(tmp_path):
    """A missing file (fresh bootstrap stub) makes the cache cold."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(
        diste_dir, [("/map/mapstudio/m61_50_43_00.msb.dcx", "Data0", 4)]
    )

    assert vanilla_cache_is_complete(diste_dir, tmp_path / "game") is False


def test_vanilla_cache_incomplete_when_size_differs(tmp_path):
    """A truncated file (interrupted extraction) makes the cache cold."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(
        diste_dir, [("/map/mapstudio/m61_50_43_00.msb.dcx", "Data0", 4)]
    )
    (diste_dir / "Vanilla" / "m61_50_43_00.msb.dcx").write_bytes(b"12")

    assert vanilla_cache_is_complete(diste_dir, tmp_path / "game") is False


def test_vanilla_cache_dlc_files_optional_without_dlc(tmp_path):
    """DLC entries are not required when the game has no DLC installed."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(
        diste_dir, [("/map/mapstudio/m20_00_00_00.msb.dcx", "DLC", 4)]
    )
    game_dir = tmp_path / "game"
    game_dir.mkdir()

    assert vanilla_cache_is_complete(diste_dir, game_dir) is True


def test_vanilla_cache_dlc_files_required_with_dlc(tmp_path):
    """DLC entries are required once DLC.bdt is installed."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(
        diste_dir, [("/map/mapstudio/m20_00_00_00.msb.dcx", "DLC", 4)]
    )
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    (game_dir / "DLC.bdt").write_bytes(b"x" * 2000)

    assert vanilla_cache_is_complete(diste_dir, game_dir) is False


def test_vanilla_cache_malformed_manifest_line_is_incomplete(tmp_path):
    """A line the randomizer would reject counts as cold, never as warm."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"12")
    manifest = diste_dir / "Vanilla" / "files.txt"
    manifest.write_text(manifest.read_text() + "/event/common.emevd.dcx Data0\n")

    assert vanilla_cache_is_complete(diste_dir, tmp_path / "game") is False


def test_vanilla_cache_msgbnd_keeps_its_directories(tmp_path):
    """msgbnd entries are cached under their archive path, not flattened."""
    diste_dir = tmp_path / "diste"
    _write_vanilla_manifest(diste_dir, [("/msg/engus/item.msgbnd.dcx", "Data0", 4)])
    msg_dir = diste_dir / "Vanilla" / "msg" / "engus"
    msg_dir.mkdir(parents=True)
    (msg_dir / "item.msgbnd.dcx").write_bytes(b"1234")

    assert vanilla_cache_is_complete(diste_dir, tmp_path / "game") is True


def test_ensure_vanilla_cache_warm_runs_nothing(tmp_path, monkeypatch):
    """A warm cache costs no subprocess."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"12")
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )

    def fail_stream(cmd, cwd=None):
        raise AssertionError(f"unexpected command: {cmd}")

    monkeypatch.setattr("speedfog.item_randomizer.stream_command", fail_stream)

    assert ensure_vanilla_cache(tmp_path / "game") is True


def test_ensure_vanilla_cache_cold_runs_extract_only(tmp_path, monkeypatch):
    """A cold cache runs the wrapper's extract-only mode under the lock."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )

    captured: list[list[str]] = []

    def mock_stream(cmd, cwd=None):
        captured.append(cmd)
        return 0

    monkeypatch.setattr("speedfog.item_randomizer.stream_command", mock_stream)

    assert ensure_vanilla_cache(game_dir) is True
    assert captured == [
        [
            "wrapper.exe",
            "--game-dir",
            str(game_dir.resolve()),
            "--data-dir",
            str(diste_dir),
            "--extract-only",
        ]
    ]


def test_ensure_vanilla_cache_reports_extraction_failure(tmp_path, monkeypatch):
    """A failed extraction is reported instead of running the randomization."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )
    monkeypatch.setattr(
        "speedfog.item_randomizer.stream_command", lambda cmd, cwd=None: 1
    )

    assert ensure_vanilla_cache(tmp_path / "game") is False


def test_ensure_vanilla_cache_missing_diste(tmp_path, monkeypatch):
    """A missing diste directory fails loudly rather than creating one."""
    wrapper_dir = tmp_path / "wrapper"
    wrapper_dir.mkdir()
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )

    assert ensure_vanilla_cache(tmp_path / "game") is False
    assert not (wrapper_dir / "diste").exists()


def test_run_item_randomizer_command_and_cache_order(tmp_path, monkeypatch):
    """The cache warm-up runs before the randomization, with both commands right."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    output_dir = tmp_path / "out"
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )

    captured: list[list[str]] = []

    def mock_stream(cmd, cwd=None):
        captured.append(cmd)
        if "--extract-only" in cmd:
            # The extraction fills the cache the randomization then reads.
            (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"12")
        return 0

    monkeypatch.setattr("speedfog.item_randomizer.stream_command", mock_stream)

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=game_dir,
        output_dir=output_dir,
        platform="windows",
        verbose=False,
    )

    assert result is True
    assert captured[0][-1] == "--extract-only"
    assert captured[1] == [
        "wrapper.exe",
        str(seed_dir / "item_config.json"),
        "--game-dir",
        str(game_dir.resolve()),
        "--data-dir",
        str(diste_dir),
        "-o",
        str(output_dir.resolve()),
    ]


def test_run_item_randomizer_aborts_on_cache_failure(tmp_path, monkeypatch):
    """A failed extraction stops the run instead of randomizing on a torn cache."""
    wrapper_dir = tmp_path / "wrapper"
    _write_vanilla_manifest(wrapper_dir / "diste", [("/regulation.bin", "Data0", 2)])
    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )
    monkeypatch.setattr(
        "speedfog.item_randomizer.stream_command", lambda cmd, cwd=None: 1
    )

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=tmp_path / "game",
        output_dir=tmp_path / "out",
        platform="windows",
        verbose=False,
    )

    assert result is False


@contextmanager
def _recording_lock(events: list[str], lock_path):
    """Stand-in for _exclusive_lock that records when it is held."""
    events.append("acquired")
    try:
        yield
    finally:
        events.append("released")


def test_vanilla_cache_guard_releases_lock_around_a_complete_cache(
    tmp_path, monkeypatch
):
    """A cache that came out complete is read without holding the lock."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"12")
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )
    events: list[str] = []
    monkeypatch.setattr(
        "speedfog.item_randomizer._exclusive_lock",
        lambda lock_path: _recording_lock(events, lock_path),
    )

    with vanilla_cache_guard(tmp_path / "game") as ok:
        assert ok is True
        assert events == ["acquired", "released"]


def test_vanilla_cache_guard_holds_lock_when_cache_cannot_complete(
    tmp_path, monkeypatch
):
    """A cache the extraction cannot complete keeps generations serialized.

    The randomizer then re-extracts it from inside the randomization, out of
    reach of the lock, so the lock has to cover the whole block.
    """
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    # The game no longer matches the shipped manifest: the extraction runs and
    # succeeds, but the sizes still differ afterwards.
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )
    events: list[str] = []
    monkeypatch.setattr(
        "speedfog.item_randomizer._exclusive_lock",
        lambda lock_path: _recording_lock(events, lock_path),
    )

    def mock_stream(cmd, cwd=None):
        (diste_dir / "Vanilla" / "regulation.bin").write_bytes(b"mismatched")
        return 0

    monkeypatch.setattr("speedfog.item_randomizer.stream_command", mock_stream)

    with vanilla_cache_guard(tmp_path / "game") as ok:
        assert ok is True
        assert events == ["acquired"]

    assert events == ["acquired", "released"]


def test_vanilla_cache_guard_releases_lock_on_extraction_failure(tmp_path, monkeypatch):
    """A failed extraction releases the lock instead of blocking every run."""
    wrapper_dir = tmp_path / "wrapper"
    diste_dir = wrapper_dir / "diste"
    _write_vanilla_manifest(diste_dir, [("/regulation.bin", "Data0", 2)])
    monkeypatch.setattr(
        "speedfog.item_randomizer._resolve_wrapper",
        lambda platform: (["wrapper.exe"], wrapper_dir),
    )
    events: list[str] = []
    monkeypatch.setattr(
        "speedfog.item_randomizer._exclusive_lock",
        lambda lock_path: _recording_lock(events, lock_path),
    )
    monkeypatch.setattr(
        "speedfog.item_randomizer.stream_command", lambda cmd, cwd=None: 1
    )

    with vanilla_cache_guard(tmp_path / "game") as ok:
        assert ok is False

    assert events == ["acquired", "released"]
