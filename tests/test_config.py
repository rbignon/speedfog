"""Tests for config parsing."""

import pytest

from speedfog.config import (
    BudgetConfig,
    Config,
    RequirementsConfig,
    load_config,
    resolve_final_boss_candidates,
)


def test_budget_tolerance():
    """BudgetConfig stores tolerance for max allowed spread."""
    budget = BudgetConfig(tolerance=5)
    assert budget.tolerance == 5


def test_config_defaults():
    """Config.from_dict with empty dict uses all defaults."""
    config = Config.from_dict({})
    assert config.seed == 0
    assert config.run_complete_message == "RUN COMPLETE"
    assert config.chapel_grace is True
    assert config.sentry_torch_shop is True
    assert config.budget.tolerance == 5
    assert config.requirements.bosses == 5
    assert config.requirements.legacy_dungeons == 1
    assert config.requirements.mini_dungeons == 5
    assert config.requirements.major_bosses == 8
    assert config.structure.max_parallel_paths == 3


def test_config_from_toml(tmp_path):
    """Config.from_toml parses TOML file correctly."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 42

[budget]
tolerance = 3

[requirements]
bosses = 7
""")
    config = Config.from_toml(config_file)
    assert config.seed == 42
    assert config.budget.tolerance == 3
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
    assert config.budget.tolerance == 10
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
tolerance = 8
""")
    config = load_config(config_file)
    assert config.seed == 99
    assert config.budget.tolerance == 8
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
    assert config.structure.final_boss_candidates == {}
    assert config.structure.final_tier == 28
    assert config.structure.tier_curve == "linear"
    assert config.structure.tier_curve_exponent == 0.6


def test_structure_new_options(tmp_path):
    """StructureConfig parses new DAG generation options."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
first_layer_type = "legacy_dungeon"
final_boss_candidates = ["caelid_radahn", "haligtree_malenia", "leyndell_erdtree"]
""")
    config = Config.from_toml(config_file)
    assert config.structure.first_layer_type == "legacy_dungeon"
    assert config.structure.final_boss_candidates == {
        "caelid_radahn": 1,
        "haligtree_malenia": 1,
        "leyndell_erdtree": 1,
    }


def test_structure_weighted_candidates(tmp_path):
    """StructureConfig parses weighted final_boss_candidates from TOML table."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure.final_boss_candidates]
leyndell_erdtree = 5
haligtree_malenia = 3
stormveil_godrick = 1
""")
    config = Config.from_toml(config_file)
    assert config.structure.final_boss_candidates == {
        "leyndell_erdtree": 5,
        "haligtree_malenia": 3,
        "stormveil_godrick": 1,
    }


def test_major_bosses_from_toml(tmp_path):
    """major_bosses is parsed from requirements section."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[requirements]
major_bosses = 5
""")
    config = Config.from_toml(config_file)
    assert config.requirements.major_bosses == 5


def test_major_bosses_default():
    """major_bosses defaults to 8."""
    config = Config.from_dict({})
    assert config.requirements.major_bosses == 8


def test_effective_final_boss_candidates_default():
    """effective_final_boss_candidates returns default when dict is empty."""
    config = Config.from_dict({})
    assert config.structure.final_boss_candidates == {}
    assert config.structure.effective_final_boss_candidates == {
        "leyndell_erdtree": 1,
        "enirilim_radahn": 1,
    }


def test_effective_final_boss_candidates_custom_list():
    """effective_final_boss_candidates from list format (backward compat)."""
    config = Config.from_dict(
        {"structure": {"final_boss_candidates": ["caelid_radahn", "mohgwyn_boss"]}}
    )
    assert config.structure.effective_final_boss_candidates == {
        "caelid_radahn": 1,
        "mohgwyn_boss": 1,
    }


def test_effective_final_boss_candidates_weighted():
    """effective_final_boss_candidates from weighted dict format."""
    config = Config.from_dict(
        {
            "structure": {
                "final_boss_candidates": {
                    "leyndell_erdtree": 5,
                    "stormveil_godrick": 1,
                }
            }
        }
    )
    assert config.structure.effective_final_boss_candidates == {
        "leyndell_erdtree": 5,
        "stormveil_godrick": 1,
    }


def test_resolve_final_boss_candidates_explicit_dict():
    """resolve_final_boss_candidates returns explicit dict unchanged."""
    all_zones = {"zone_a", "zone_b", "zone_c"}
    candidates = {"zone_a": 3, "zone_b": 1}
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == {"zone_a": 3, "zone_b": 1}


def test_resolve_final_boss_candidates_all_keyword():
    """resolve_final_boss_candidates expands 'all' to all zones with weight 1."""
    all_zones = {"zone_a", "zone_b", "zone_c"}
    candidates = {"all": 1}
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == {"zone_a": 1, "zone_b": 1, "zone_c": 1}


def test_resolve_final_boss_candidates_empty_dict():
    """resolve_final_boss_candidates returns empty dict unchanged."""
    all_zones = {"zone_a", "zone_b"}
    candidates: dict[str, int] = {}
    result = resolve_final_boss_candidates(candidates, all_zones)
    assert result == {}


def test_starting_items_defaults():
    """StartingItemsConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.starting_items.academy_key is True
    assert config.starting_items.pureblood_medal is False
    assert config.starting_items.drawing_room_key is True
    assert config.starting_items.lantern is True
    assert config.starting_items.physick_flask is True
    assert config.starting_items.whetstone_knife is True
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
    assert 250 in goods  # Flask of Wondrous Physick
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
    """lantern, whetstone_knife, and whetblades are parsed from config dict."""
    config = Config.from_dict(
        {
            "starting_items": {
                "lantern": False,
                "whetstone_knife": False,
                "whetblades": False,
            }
        }
    )
    assert config.starting_items.lantern is False
    assert config.starting_items.whetstone_knife is False
    assert config.starting_items.whetblades is False
    goods = config.starting_items.get_starting_goods()
    assert 2070 not in goods  # Lantern disabled
    assert 8590 not in goods  # Whetstone Knife disabled
    assert 8970 not in goods  # Iron Whetblade disabled


def test_starting_items_whetstone_knife_without_whetblades():
    """whetstone_knife can be enabled independently of whetblades."""
    config = Config.from_dict(
        {"starting_items": {"whetstone_knife": True, "whetblades": False}}
    )
    goods = config.starting_items.get_starting_goods()
    assert 8590 in goods  # Whetstone Knife enabled
    assert 8970 not in goods  # Iron Whetblade disabled
    assert 8974 not in goods  # Black Whetblade disabled


def test_starting_items_whetblades_without_whetstone_knife():
    """whetblades can be enabled independently of whetstone_knife."""
    config = Config.from_dict(
        {"starting_items": {"whetstone_knife": False, "whetblades": True}}
    )
    goods = config.starting_items.get_starting_goods()
    assert 8590 not in goods  # Whetstone Knife disabled
    assert 8970 in goods  # Iron Whetblade enabled
    assert 8974 in goods  # Black Whetblade enabled


def test_starting_items_physick_flask_from_dict():
    """physick_flask is parsed from config dict and can be disabled."""
    config = Config.from_dict({"starting_items": {"physick_flask": False}})
    assert config.starting_items.physick_flask is False
    goods = config.starting_items.get_starting_goods()
    assert 250 not in goods  # Flask of Wondrous Physick disabled


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
    assert 2160 not in goods  # Pureblood Knight's Medal (disabled by default)
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
    assert config.item_randomizer.item_preset is True
    assert config.item_randomizer.item_preset_path == ""


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


def test_item_randomizer_item_preset_from_toml(tmp_path):
    """item_preset and item_preset_path parse from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[item_randomizer]
item_preset = false
item_preset_path = "/custom/preset.yaml"
""")
    config = Config.from_toml(config_file)
    assert config.item_randomizer.item_preset is False
    assert config.item_randomizer.item_preset_path == "/custom/preset.yaml"


def test_item_randomizer_validation_difficulty():
    """difficulty must be 0-100."""
    import pytest

    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": 101}})
    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": -1}})


def test_structure_start_tier_from_toml(tmp_path):
    """start_tier can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
start_tier = 5
final_tier = 20
""")
    config = Config.from_toml(config_file)
    assert config.structure.start_tier == 5
    assert config.structure.final_tier == 20


def test_structure_start_tier_default():
    """start_tier defaults to 1."""
    config = Config.from_dict({})
    assert config.structure.start_tier == 1


def test_structure_start_tier_validation():
    """start_tier must be 1-28 and <= final_tier."""
    import pytest

    with pytest.raises(ValueError, match="start_tier must be 1-28"):
        Config.from_dict({"structure": {"start_tier": 0}})
    with pytest.raises(ValueError, match="start_tier must be 1-28"):
        Config.from_dict({"structure": {"start_tier": 29}})
    with pytest.raises(ValueError, match="start_tier.*must be <= final_tier"):
        Config.from_dict({"structure": {"start_tier": 20, "final_tier": 10}})


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


def test_run_complete_message_list_from_toml(tmp_path):
    """run_complete_message can be a list of strings in TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
run_complete_message = ["A", "B", "C"]
""")
    config = Config.from_toml(config_file)
    assert config.run_complete_message == ["A", "B", "C"]


def test_run_complete_message_empty_list_rejected():
    """run_complete_message must not be an empty list."""
    with pytest.raises(ValueError, match="run_complete_message list must not be empty"):
        Config.from_dict({"run": {"run_complete_message": []}})


def test_run_complete_message_non_string_list_rejected():
    """run_complete_message list must contain only strings."""
    with pytest.raises(TypeError, match="list must contain only strings"):
        Config.from_dict({"run": {"run_complete_message": ["ok", 42]}})


def test_run_complete_message_wrong_type_rejected():
    """run_complete_message must be a string or a list."""
    with pytest.raises(TypeError, match="must be a string or a list of strings"):
        Config.from_dict({"run": {"run_complete_message": 42}})


def test_resolve_run_complete_message_string_passthrough():
    """A scalar run_complete_message is returned unchanged."""
    config = Config.from_dict({"run": {"run_complete_message": "STATIC"}})
    assert config.resolve_run_complete_message(42) == "STATIC"


def test_resolve_run_complete_message_single_element_list():
    """A single-element list always resolves to its only entry."""
    config = Config.from_dict({"run": {"run_complete_message": ["ONLY"]}})
    assert config.resolve_run_complete_message(0) == "ONLY"
    assert config.resolve_run_complete_message(999) == "ONLY"


def test_resolve_run_complete_message_deterministic():
    """Same seed yields the same message; different seeds can yield different ones."""
    messages = ["A", "B", "C", "D", "E"]
    config = Config.from_dict({"run": {"run_complete_message": messages}})

    first = config.resolve_run_complete_message(12345)
    second = config.resolve_run_complete_message(12345)
    assert first == second
    assert first in messages

    picks = {config.resolve_run_complete_message(s) for s in range(50)}
    assert len(picks) > 1


def test_structure_final_tier_valid_range():
    """final_tier accepts valid values 1-28."""
    # Test boundary values
    config_low = Config.from_dict({"structure": {"final_tier": 1}})
    assert config_low.structure.final_tier == 1

    config_high = Config.from_dict({"structure": {"final_tier": 28}})
    assert config_high.structure.final_tier == 28

    config_mid = Config.from_dict({"structure": {"final_tier": 15}})
    assert config_mid.structure.final_tier == 15


def test_tier_curve_defaults():
    """tier_curve defaults to linear with exponent 0.6."""
    config = Config.from_dict({})
    assert config.structure.tier_curve == "linear"
    assert config.structure.tier_curve_exponent == 0.6


def test_tier_curve_from_toml(tmp_path):
    """tier_curve settings can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
tier_curve = "power"
tier_curve_exponent = 1.5
""")
    config = Config.from_toml(config_file)
    assert config.structure.tier_curve == "power"
    assert config.structure.tier_curve_exponent == 1.5


def test_tier_curve_invalid_name():
    """tier_curve must be 'linear' or 'power'."""
    with pytest.raises(ValueError, match="tier_curve must be"):
        Config.from_dict({"structure": {"tier_curve": "sigmoid"}})


def test_tier_curve_exponent_must_be_positive():
    """tier_curve_exponent must be > 0."""
    with pytest.raises(ValueError, match="tier_curve_exponent must be > 0"):
        Config.from_dict({"structure": {"tier_curve_exponent": 0}})
    with pytest.raises(ValueError, match="tier_curve_exponent must be > 0"):
        Config.from_dict({"structure": {"tier_curve_exponent": -1.0}})


def test_chapel_grace_from_toml(tmp_path):
    """chapel_grace can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
chapel_grace = false
""")
    config = Config.from_toml(config_file)
    assert config.chapel_grace is False


def test_sentry_torch_shop_from_toml(tmp_path):
    """sentry_torch_shop can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
sentry_torch_shop = false
""")
    config = Config.from_toml(config_file)
    assert config.sentry_torch_shop is False


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


def test_min_branch_age_default():
    """min_branch_age defaults to 0."""
    config = Config.from_dict({})
    assert config.structure.min_branch_age == 0


def test_min_branch_age_from_toml(tmp_path):
    """min_branch_age can be set from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
min_branch_age = 3
""")
    config = Config.from_toml(config_file)
    assert config.structure.min_branch_age == 3


def test_min_branch_age_validation():
    """min_branch_age must be >= 0."""
    with pytest.raises(ValueError, match="min_branch_age must be >= 0"):
        Config.from_dict({"structure": {"min_branch_age": -1}})


def test_enemy_config_defaults():
    """EnemyConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.enemy.randomize_bosses == "none"
    assert config.enemy.ignore_arena_size is False
    assert config.enemy.swap_boss is False


def test_enemy_config_from_dict():
    """EnemyConfig parses from config dict."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    assert config.enemy.randomize_bosses == "all"


def test_enemy_config_from_toml(tmp_path):
    """EnemyConfig parses from TOML file."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[enemy]
randomize_bosses = "minor"
""")
    config = Config.from_toml(config_file)
    assert config.enemy.randomize_bosses == "minor"


def test_enemy_config_partial():
    """EnemyConfig uses defaults for missing fields."""
    config = Config.from_dict({"enemy": {"randomize_bosses": "all"}})
    assert config.enemy.randomize_bosses == "all"
    assert config.enemy.ignore_arena_size is False
    assert config.enemy.swap_boss is False


def test_enemy_config_swap_boss():
    """EnemyConfig parses swap_boss from config dict."""
    config = Config.from_dict({"enemy": {"swap_boss": True}})
    assert config.enemy.swap_boss is True
    assert config.enemy.randomize_bosses == "none"


def test_enemy_config_legacy_bool():
    """EnemyConfig accepts legacy boolean values."""
    config_true = Config.from_dict({"enemy": {"randomize_bosses": True}})
    assert config_true.enemy.randomize_bosses == "all"
    config_false = Config.from_dict({"enemy": {"randomize_bosses": False}})
    assert config_false.enemy.randomize_bosses == "none"


def test_enemy_config_invalid_value():
    """EnemyConfig rejects invalid randomize_bosses values."""
    import pytest

    with pytest.raises(ValueError, match="randomize_bosses"):
        Config.from_dict({"enemy": {"randomize_bosses": "invalid"}})


def test_crosslinks_default():
    """crosslinks defaults to False (disabled)."""
    config = Config.from_dict({})
    assert config.structure.crosslinks is False


def test_crosslinks_from_toml():
    """crosslinks is parsed from structure section."""
    config = Config.from_dict({"structure": {"crosslinks": True}})
    assert config.structure.crosslinks is True


def test_max_exits_entrances_default_to_max_branches():
    """max_exits and max_entrances default to max_branches when not set."""
    config = Config.from_dict({"structure": {"max_branches": 4}})
    assert config.structure.max_exits == 4
    assert config.structure.max_entrances == 4


def test_max_exits_entrances_explicit_override():
    """Explicit max_exits/max_entrances override max_branches independently."""
    config = Config.from_dict(
        {"structure": {"max_branches": 3, "max_exits": 4, "max_entrances": 2}}
    )
    assert config.structure.max_exits == 4
    assert config.structure.max_entrances == 2
    assert config.structure.max_branches == 3  # Unchanged


def test_max_exits_partial_override():
    """Setting only max_exits leaves max_entrances defaulting to max_branches."""
    config = Config.from_dict({"structure": {"max_branches": 3, "max_exits": 4}})
    assert config.structure.max_exits == 4
    assert config.structure.max_entrances == 3


def test_max_exits_validation():
    """max_exits must be >= 1."""
    with pytest.raises(ValueError, match="max_exits must be >= 1"):
        Config.from_dict({"structure": {"max_exits": 0}})


def test_max_entrances_validation():
    """max_entrances must be >= 1."""
    with pytest.raises(ValueError, match="max_entrances must be >= 1"):
        Config.from_dict({"structure": {"max_entrances": 0}})


def test_max_exits_cross_validation():
    """max_exits >= 2 requires max_parallel_paths >= 2."""
    with pytest.raises(ValueError, match="max_parallel_paths must be >= 2"):
        Config.from_dict(
            {"structure": {"max_parallel_paths": 1, "max_branches": 1, "max_exits": 2}}
        )


def test_max_exits_entrances_from_toml(tmp_path):
    """max_exits and max_entrances parse from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
max_exits = 4
max_entrances = 2
""")
    config = Config.from_toml(config_file)
    assert config.structure.max_exits == 4
    assert config.structure.max_entrances == 2


def test_max_branches_mutation_updates_properties():
    """Mutating max_branches updates max_exits/max_entrances (fallback behavior)."""
    config = Config.from_dict(
        {"structure": {"max_parallel_paths": 1, "max_branches": 1}}
    )
    assert config.structure.max_exits == 1
    assert config.structure.max_entrances == 1
    config.structure.max_branches = 2
    config.structure.max_parallel_paths = 3
    assert config.structure.max_exits == 2
    assert config.structure.max_entrances == 2


def test_explicit_override_survives_max_branches_mutation():
    """Explicit max_exits is not overridden by later max_branches mutation."""
    config = Config.from_dict({"structure": {"max_exits": 4, "max_entrances": 2}})
    config.structure.max_branches = 1
    config.structure.max_parallel_paths = 1
    assert config.structure.max_exits == 4
    assert config.structure.max_entrances == 2


def test_max_branch_spacing_default():
    """max_branch_spacing defaults to 4."""
    config = Config.from_dict({})
    assert config.structure.max_branch_spacing == 4


def test_max_branch_spacing_from_toml(tmp_path):
    """max_branch_spacing parsed from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
max_branch_spacing = 6
""")
    config = Config.from_toml(config_file)
    assert config.structure.max_branch_spacing == 6


def test_max_branch_spacing_disabled():
    """max_branch_spacing = 0 disables enforcement."""
    config = Config.from_dict({"structure": {"max_branch_spacing": 0}})
    assert config.structure.max_branch_spacing == 0


def test_max_branch_spacing_validation():
    """min_branch_age >= max_branch_spacing raises ValueError."""
    with pytest.raises(ValueError, match="min_branch_age"):
        Config.from_dict(
            {
                "structure": {
                    "min_branch_age": 4,
                    "max_branch_spacing": 4,
                }
            }
        )


def test_max_branch_spacing_negative():
    """Negative max_branch_spacing raises ValueError."""
    with pytest.raises(ValueError, match="max_branch_spacing"):
        Config.from_dict({"structure": {"max_branch_spacing": -1}})


def test_death_markers_default_true():
    config = Config.from_dict({})
    assert config.death_markers is True


def test_death_markers_explicit_false():
    config = Config.from_dict({"run": {"death_markers": False}})
    assert config.death_markers is False


def test_max_weight_tolerance_default():
    """max_weight_tolerance defaults to 3."""
    config = Config.from_dict({})
    assert config.structure.max_weight_tolerance == 3


def test_max_weight_tolerance_from_toml(tmp_path):
    """max_weight_tolerance parsed from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
max_weight_tolerance = 5
""")
    config = Config.from_toml(config_file)
    assert config.structure.max_weight_tolerance == 5


def test_max_weight_tolerance_disabled():
    """max_weight_tolerance = 0 disables weight matching."""
    config = Config.from_dict({"structure": {"max_weight_tolerance": 0}})
    assert config.structure.max_weight_tolerance == 0


def test_max_weight_tolerance_negative_raises():
    """Negative max_weight_tolerance raises ValueError."""
    with pytest.raises(ValueError, match="max_weight_tolerance"):
        Config.from_dict({"structure": {"max_weight_tolerance": -1}})


class TestAllowedTypes:
    """Tests for RequirementsConfig.allowed_types."""

    def test_default_contains_all_four_types(self):
        req = RequirementsConfig()
        assert set(req.allowed_types) == {
            "legacy_dungeon",
            "mini_dungeon",
            "boss_arena",
            "major_boss",
        }

    def test_custom_subset(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=5,
            mini_dungeons=0,
            major_bosses=3,
        )
        assert req.allowed_types == ["boss_arena", "major_boss"]

    def test_empty_list_raises(self):
        with pytest.raises(ValueError, match="allowed_types must be non-empty"):
            RequirementsConfig(allowed_types=[])

    def test_unknown_type_raises(self):
        with pytest.raises(ValueError, match="invalid cluster type"):
            RequirementsConfig(allowed_types=["boss_arena", "dragons"])

    def test_duplicate_entries_raises(self):
        with pytest.raises(ValueError, match="duplicate"):
            RequirementsConfig(allowed_types=["boss_arena", "boss_arena", "major_boss"])

    def test_required_count_for_allowed_type(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=7,
            mini_dungeons=0,
            major_bosses=2,
        )
        assert req.required_count("boss_arena") == 7
        assert req.required_count("major_boss") == 2

    def test_required_count_for_excluded_type_returns_zero(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=3,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        # mini_dungeon not in allowed_types, even though default min is 5
        assert req.required_count("mini_dungeon") == 0
        # legacy_dungeon not in allowed_types, even with explicit 3
        assert req.required_count("legacy_dungeon") == 0

    def test_nonzero_min_for_excluded_type_emits_warning(self, recwarn):
        RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=3,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        messages = [str(w.message) for w in recwarn.list]
        assert any(
            "legacy_dungeons" in m and "not in allowed_types" in m for m in messages
        )

    def test_zero_min_for_excluded_type_no_warning(self, recwarn):
        RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        assert len(recwarn.list) == 0


def test_config_from_dict_parses_allowed_types():
    config = Config.from_dict(
        {
            "requirements": {
                "allowed_types": ["boss_arena", "major_boss"],
                "legacy_dungeons": 0,
                "bosses": 10,
                "mini_dungeons": 0,
                "major_bosses": 3,
            }
        }
    )
    assert config.requirements.allowed_types == ["boss_arena", "major_boss"]
    assert config.requirements.bosses == 10


def test_config_from_dict_default_allowed_types():
    config = Config.from_dict({})
    assert set(config.requirements.allowed_types) == {
        "legacy_dungeon",
        "mini_dungeon",
        "boss_arena",
        "major_boss",
    }


def test_first_layer_type_must_be_in_allowed_types():
    with pytest.raises(ValueError, match="first_layer_type.*not in allowed_types"):
        Config.from_dict(
            {
                "requirements": {
                    "allowed_types": ["boss_arena", "major_boss"],
                    "legacy_dungeons": 0,
                    "mini_dungeons": 0,
                },
                "structure": {"first_layer_type": "legacy_dungeon"},
            }
        )


def test_first_layer_type_in_allowed_types_ok():
    config = Config.from_dict(
        {
            "requirements": {
                "allowed_types": ["boss_arena", "major_boss"],
                "legacy_dungeons": 0,
                "mini_dungeons": 0,
            },
            "structure": {"first_layer_type": "boss_arena"},
        }
    )
    assert config.structure.first_layer_type == "boss_arena"


def test_first_layer_type_none_is_ok():
    config = Config.from_dict(
        {
            "requirements": {"allowed_types": ["boss_arena", "major_boss"]},
        }
    )
    assert config.structure.first_layer_type is None
