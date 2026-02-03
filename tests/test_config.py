"""Tests for config parsing."""

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
clusters_file = "./custom_clusters.json"
randomizer_dir = "./mods/randomizer"
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
    assert config.paths.clusters_file == "./custom_clusters.json"
    assert config.paths.randomizer_dir == "./mods/randomizer"
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
    assert config.paths.output_dir == "./output"
    assert config.paths.clusters_file == "./data/clusters.json"
    assert config.paths.randomizer_dir is None
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
    assert config.structure.effective_final_boss_candidates == ["leyndell_erdtree"]


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
    assert config.starting_items.great_runes is True
    assert config.starting_items.golden_seeds == 0
    assert config.starting_items.sacred_tears == 0
    assert config.starting_items.starting_runes == 0


def test_starting_items_consumables(tmp_path):
    """StartingItemsConfig parses consumable starting resources."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[starting_items]
golden_seeds = 5
sacred_tears = 3
starting_runes = 50000
""")
    config = Config.from_toml(config_file)
    assert config.starting_items.golden_seeds == 5
    assert config.starting_items.sacred_tears == 3
    assert config.starting_items.starting_runes == 50000


def test_starting_items_get_item_lots():
    """get_item_lots returns correct ItemLot IDs."""
    config = Config.from_dict(
        {
            "starting_items": {
                "academy_key": True,
                "pureblood_medal": False,
                "great_runes": False,
                "rune_godrick": True,
                "rune_radahn": False,
            }
        }
    )
    lots = config.starting_items.get_item_lots()
    assert 1034450100 in lots  # Academy Key
    assert 100320 not in lots  # Pureblood Medal disabled
    assert 34100500 in lots  # Godrick's Great Rune
    assert 34130050 not in lots  # Radahn's Great Rune disabled


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
