"""Tests for config parsing."""

import pytest
from speedfog_core.config import BudgetConfig, Config, load_config


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
zones_file = "./custom_zones.toml"
randomizer_dir = "./mods/randomizer"
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
    assert config.paths.zones_file == "./custom_zones.toml"
    assert config.paths.randomizer_dir == "./mods/randomizer"


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
    assert config.paths.zones_file == "./zones.toml"
    assert config.paths.randomizer_dir is None


def test_structure_defaults():
    """StructureConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.structure.max_parallel_paths == 3
    assert config.structure.min_layers == 6
    assert config.structure.max_layers == 10
