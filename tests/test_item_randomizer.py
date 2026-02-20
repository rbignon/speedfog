"""Tests for Item Randomizer integration."""

import json
from pathlib import Path
from unittest.mock import MagicMock

from speedfog.config import Config
from speedfog.item_randomizer import generate_item_config, run_item_randomizer


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
    assert "preset" not in result
    assert result["enemy_options"]["randomize_bosses"] is False
    assert result["enemy_options"]["lock_final_boss"] is True
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
    assert result["enemy_options"]["randomize_bosses"] is False
    assert result["enemy_options"]["lock_final_boss"] is True
    assert result["enemy_options"]["finish_boss_defeat_flag"] == 0
    # preset key should no longer be present
    assert "preset" not in result


def test_generate_item_config_enemy_options_enabled():
    """generate_item_config passes through enemy randomization settings."""
    config = Config.from_dict(
        {"enemy": {"randomize_bosses": True, "lock_final_boss": False}}
    )
    result = generate_item_config(config, 42, finish_boss_defeat_flag=1042380520)

    assert result["enemy_options"]["randomize_bosses"] is True
    assert result["enemy_options"]["lock_final_boss"] is False
    assert result["enemy_options"]["finish_boss_defeat_flag"] == 1042380520


def test_generate_item_config_finish_boss_defeat_flag():
    """generate_item_config propagates finish_boss_defeat_flag correctly."""
    config = Config.from_dict({"enemy": {"randomize_bosses": True}})
    result = generate_item_config(config, 42, finish_boss_defeat_flag=1234567890)

    assert result["enemy_options"]["finish_boss_defeat_flag"] == 1234567890
