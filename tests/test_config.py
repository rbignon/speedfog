"""Tests for config parsing."""

import pytest

from speedfog.config import (
    BudgetConfig,
    Config,
    load_config,
    resolve_final_boss_candidates,
)


def test_budget_min_max():
    """BudgetConfig computes min/max weight from total and tolerance."""
    budget = BudgetConfig(total_weight=30, tolerance=5)
    assert budget.min_weight == 25
    assert budget.max_weight == 35


def test_config_defaults():
    """Config.from_dict with empty dict uses all defaults."""
    config = Config.from_dict({})
    assert config.seed == 0
    assert config.run_complete_message == "RUN COMPLETE"
    assert config.chapel_grace is True
    assert config.budget.total_weight == 30
    assert config.budget.tolerance == 5
    assert config.requirements.bosses == 5
    assert config.requirements.legacy_dungeons == 1
    assert config.requirements.mini_dungeons == 5
    assert config.structure.max_parallel_paths == 3


def test_config_from_toml(tmp_path):
    """Config.from_toml parses TOML file correctly."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 42

[budget]
total_weight = 25
tolerance = 3

[requirements]
bosses = 7
""")
    config = Config.from_toml(config_file)
    assert config.seed == 42
    assert config.budget.total_weight == 25
    assert config.budget.tolerance == 3
    assert config.budget.min_weight == 22
    assert config.budget.max_weight == 28
    assert config.requirements.bosses == 7
    # Defaults for unspecified values
    assert config.requirements.legacy_dungeons == 1


def test_config_full_toml(tmp_path):
    """Config.from_toml parses all sections correctly."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 12345

[budget]
total_weight = 40
tolerance = 10

[requirements]
legacy_dungeons = 2
bosses = 8
mini_dungeons = 6

[structure]
max_parallel_paths = 4
min_layers = 5
max_layers = 12

[paths]
game_dir = "/path/to/game"
output_dir = "./custom_output"
platform = "linux"
""")
    config = Config.from_toml(config_file)
    # Run section
    assert config.seed == 12345
    # Budget section
    assert config.budget.total_weight == 40
    assert config.budget.tolerance == 10
    assert config.budget.min_weight == 30
    assert config.budget.max_weight == 50
    # Requirements section
    assert config.requirements.legacy_dungeons == 2
    assert config.requirements.bosses == 8
    assert config.requirements.mini_dungeons == 6
    # Structure section
    assert config.structure.max_parallel_paths == 4
    assert config.structure.min_layers == 5
    assert config.structure.max_layers == 12
    # Paths section
    assert config.paths.game_dir == "/path/to/game"
    assert config.paths.output_dir == "./custom_output"
    assert config.paths.platform == "linux"


def test_load_config_helper(tmp_path):
    """load_config convenience function works correctly."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 99

[budget]
total_weight = 20
""")
    config = load_config(config_file)
    assert config.seed == 99
    assert config.budget.total_weight == 20
    # Verify defaults are applied
    assert config.budget.tolerance == 5
    assert config.requirements.bosses == 5


def test_paths_defaults():
    """PathsConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.paths.game_dir == ""
    assert config.paths.output_dir == "./seeds"
    assert config.paths.platform is None


def test_structure_defaults():
    """StructureConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.structure.max_parallel_paths == 3
    assert config.structure.min_layers == 6
    assert config.structure.max_layers == 10
    assert config.structure.first_layer_type is None
    assert config.structure.major_boss_ratio == 0.0
    assert config.structure.final_boss_candidates == []
    assert config.structure.final_tier == 28


def test_structure_new_options(tmp_path):
    """StructureConfig parses new DAG generation options."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
first_layer_type = "legacy_dungeon"
major_boss_ratio = 0.2
final_boss_candidates = ["caelid_radahn", "haligtree_malenia", "leyndell_erdtree"]
""")
    config = Config.from_toml(config_file)
    assert config.structure.first_layer_type == "legacy_dungeon"
    assert config.structure.major_boss_ratio == 0.2
    assert config.structure.final_boss_candidates == [
        "caelid_radahn",
        "haligtree_malenia",
        "leyndell_erdtree",
    ]


def test_effective_final_boss_candidates_default():
    """effective_final_boss_candidates returns default when list is empty."""
    config = Config.from_dict({})
    assert config.structure.final_boss_candidates == []
    assert config.structure.effective_final_boss_candidates == [
        "leyndell_erdtree",
        "enirilim_radahn",
    ]


def test_effective_final_boss_candidates_custom():
    """effective_final_boss_candidates returns custom list when set."""
    config = Config.from_dict(
        {"structure": {"final_boss_candidates": ["caelid_radahn", "mohgwyn_boss"]}}
    )
    assert config.structure.effective_final_boss_candidates == [
        "caelid_radahn",
        "mohgwyn_boss",
    ]


def test_resolve_final_boss_candidates_explicit_list():
    """resolve_final_boss_candidates returns explicit list unchanged."""
    all_zones = {"zone_a", "zone_b", "zone_c"}
    candidates = ["zone_a", "zone_b"]
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == ["zone_a", "zone_b"]


def test_resolve_final_boss_candidates_all_keyword():
    """resolve_final_boss_candidates expands 'all' to all zones."""
    all_zones = {"zone_a", "zone_b", "zone_c"}
    candidates = ["all"]
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == ["zone_a", "zone_b", "zone_c"]  # Sorted


def test_resolve_final_boss_candidates_empty_list():
    """resolve_final_boss_candidates returns empty list unchanged."""
    all_zones = {"zone_a", "zone_b"}
    candidates: list[str] = []
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == []


def test_starting_items_defaults():
    """StartingItemsConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.starting_items.academy_key is True
    assert config.starting_items.pureblood_medal is True
    assert config.starting_items.drawing_room_key is True
    assert config.starting_items.lantern is True
    assert config.starting_items.whetblades is True
    assert config.starting_items.great_runes is True
    assert config.starting_items.talisman_pouches == 3
    assert config.starting_items.golden_seeds == 0
    assert config.starting_items.sacred_tears == 0
    assert config.starting_items.starting_runes == 0


def test_starting_items_consumables(tmp_path):
    """StartingItemsConfig parses consumable starting resources."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[starting_items]
talisman_pouches = 2
golden_seeds = 5
sacred_tears = 3
starting_runes = 50000
""")
    config = Config.from_toml(config_file)
    assert config.starting_items.talisman_pouches == 2
    assert config.starting_items.golden_seeds == 5
    assert config.starting_items.sacred_tears == 3
    assert config.starting_items.starting_runes == 50000


def test_starting_items_get_starting_goods():
    """get_starting_goods returns correct Good IDs for DirectlyGivePlayerItem."""
    config = Config.from_dict(
        {
            "starting_items": {
                "academy_key": True,
                "pureblood_medal": False,
                "drawing_room_key": True,
                "great_runes": False,
                "rune_godrick": True,
                "rune_radahn": False,
            }
        }
    )
    goods = config.starting_items.get_starting_goods()
    assert 8109 in goods  # Academy Glintstone Key
    assert 2160 not in goods  # Pureblood Knight's Medal disabled
    assert 8134 in goods  # Drawing-Room Key
    assert 2070 in goods  # Lantern
    assert 8590 in goods  # Whetstone Knife
    assert 8970 in goods  # Iron Whetblade
    assert 191 in goods  # Godrick's Great Rune (restored, Good ID 191)
    assert 192 not in goods  # Radahn's Great Rune disabled
    # Default 3 talisman pouches
    assert goods.count(10040) == 3  # 3x Talisman Pouch


def test_starting_items_get_starting_goods_no_pouches():
    """get_starting_goods omits pouches when talisman_pouches=0."""
    config = Config.from_dict({"starting_items": {"talisman_pouches": 0}})
    goods = config.starting_items.get_starting_goods()
    assert 10040 not in goods


def test_starting_items_lantern_whetblades_from_dict():
    """lantern and whetblades are parsed from config dict."""
    config = Config.from_dict(
        {"starting_items": {"lantern": False, "whetblades": False}}
    )
    assert config.starting_items.lantern is False
    assert config.starting_items.whetblades is False
    goods = config.starting_items.get_starting_goods()
    assert 2070 not in goods  # Lantern disabled
    assert 8590 not in goods  # Whetstone Knife disabled
    assert 8970 not in goods  # Iron Whetblade disabled


def test_starting_items_get_starting_goods_all_runes():
    """get_starting_goods includes all Great Runes when great_runes=True."""
    config = Config.from_dict(
        {
            "starting_items": {
                "great_runes": True,
            }
        }
    )
    goods = config.starting_items.get_starting_goods()
    # Should have all 6 Great Runes (restored, 191-196) + key items (defaults)
    assert 8109 in goods  # Academy Glintstone Key
    assert 2160 in goods  # Pureblood Knight's Medal
    assert 8134 in goods  # Drawing-Room Key
    assert 2070 in goods  # Lantern
    assert 8590 in goods  # Whetstone Knife
    assert 8974 in goods  # Black Whetblade
    assert 191 in goods  # Godrick (restored)
    assert 192 in goods  # Radahn (restored)
    assert 193 in goods  # Morgott (restored)
    assert 194 in goods  # Rykard (restored)
    assert 195 in goods  # Mohg (restored)
    assert 196 in goods  # Malenia (restored)
    assert goods.count(10040) == 3  # 3x Talisman Pouch (default)


def test_starting_items_validation_talisman_pouches():
    """talisman_pouches must be 0-3."""
    import pytest

    with pytest.raises(ValueError, match="talisman_pouches must be 0-3"):
        Config.from_dict({"starting_items": {"talisman_pouches": 4}})
    with pytest.raises(ValueError, match="talisman_pouches must be 0-3"):
        Config.from_dict({"starting_items": {"talisman_pouches": -1}})


def test_starting_items_validation_golden_seeds():
    """golden_seeds must be 0-99."""
    import pytest

    with pytest.raises(ValueError, match="golden_seeds must be 0-99"):
        Config.from_dict({"starting_items": {"golden_seeds": 100}})
    with pytest.raises(ValueError, match="golden_seeds must be 0-99"):
        Config.from_dict({"starting_items": {"golden_seeds": -1}})


def test_starting_items_validation_sacred_tears():
    """sacred_tears must be 0-12."""
    import pytest

    with pytest.raises(ValueError, match="sacred_tears must be 0-12"):
        Config.from_dict({"starting_items": {"sacred_tears": 13}})
    with pytest.raises(ValueError, match="sacred_tears must be 0-12"):
        Config.from_dict({"starting_items": {"sacred_tears": -1}})


def test_starting_items_validation_runes():
    """starting_runes must be 0-10000000."""
    import pytest

    with pytest.raises(ValueError, match="starting_runes must be 0-10000000"):
        Config.from_dict({"starting_items": {"starting_runes": 10_000_001}})
    with pytest.raises(ValueError, match="starting_runes must be 0-10000000"):
        Config.from_dict({"starting_items": {"starting_runes": -1}})


def test_starting_items_validation_larval_tears():
    """larval_tears must be 0-99."""
    import pytest

    with pytest.raises(ValueError, match="larval_tears must be 0-99"):
        Config.from_dict({"starting_items": {"larval_tears": 100}})
    with pytest.raises(ValueError, match="larval_tears must be 0-99"):
        Config.from_dict({"starting_items": {"larval_tears": -1}})


def test_starting_items_larval_tears_default():
    """Default larval_tears is 10."""
    config = Config.from_dict({})
    assert config.starting_items.larval_tears == 10


def test_item_randomizer_defaults():
    """ItemRandomizerConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.item_randomizer.enabled is True
    assert config.item_randomizer.difficulty == 50
    assert config.item_randomizer.remove_requirements is True
    assert config.item_randomizer.auto_upgrade_weapons is True


def test_item_randomizer_from_toml(tmp_path):
    """ItemRandomizerConfig parses from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[item_randomizer]
enabled = false
difficulty = 75
remove_requirements = false
auto_upgrade_weapons = false
""")
    config = Config.from_toml(config_file)
    assert config.item_randomizer.enabled is False
    assert config.item_randomizer.difficulty == 75
    assert config.item_randomizer.remove_requirements is False
    assert config.item_randomizer.auto_upgrade_weapons is False


def test_item_randomizer_validation_difficulty():
    """difficulty must be 0-100."""
    import pytest

    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": 101}})
    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": -1}})


def test_structure_final_tier_from_toml(tmp_path):
    """final_tier can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
final_tier = 20
""")
    config = Config.from_toml(config_file)
    assert config.structure.final_tier == 20


def test_structure_final_tier_validation():
    """final_tier must be 1-28."""
    import pytest

    with pytest.raises(ValueError, match="final_tier must be 1-28"):
        Config.from_dict({"structure": {"final_tier": 0}})
    with pytest.raises(ValueError, match="final_tier must be 1-28"):
        Config.from_dict({"structure": {"final_tier": 29}})
    with pytest.raises(ValueError, match="final_tier must be 1-28"):
        Config.from_dict({"structure": {"final_tier": -5}})


def test_structure_final_tier_type_validation():
    """final_tier must be an integer."""
    import pytest

    with pytest.raises(TypeError, match="final_tier must be int"):
        Config.from_dict({"structure": {"final_tier": "20"}})
    with pytest.raises(TypeError, match="final_tier must be int"):
        Config.from_dict({"structure": {"final_tier": 20.5}})


def test_run_complete_message_from_toml(tmp_path):
    """run_complete_message can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
run_complete_message = "GG EZ"
""")
    config = Config.from_toml(config_file)
    assert config.run_complete_message == "GG EZ"


def test_structure_final_tier_valid_range():
    """final_tier accepts valid values 1-28."""
    # Test boundary values
    config_low = Config.from_dict({"structure": {"final_tier": 1}})
    assert config_low.structure.final_tier == 1

    config_high = Config.from_dict({"structure": {"final_tier": 28}})
    assert config_high.structure.final_tier == 28

    config_mid = Config.from_dict({"structure": {"final_tier": 15}})
    assert config_mid.structure.final_tier == 15


def test_chapel_grace_from_toml(tmp_path):
    """chapel_grace can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
chapel_grace = false
""")
    config = Config.from_toml(config_file)
    assert config.chapel_grace is False


def test_max_branches_cross_validation():
    """max_parallel_paths=1 with max_branches>=2 raises ValueError."""
    with pytest.raises(ValueError, match="max_parallel_paths must be >= 2"):
        Config.from_dict({"structure": {"max_parallel_paths": 1, "max_branches": 2}})


def test_max_branches_one_allows_single_path():
    """max_branches=1 with max_parallel_paths=1 is valid (linear only)."""
    config = Config.from_dict(
        {"structure": {"max_parallel_paths": 1, "max_branches": 1}}
    )
    assert config.structure.max_parallel_paths == 1
    assert config.structure.max_branches == 1
