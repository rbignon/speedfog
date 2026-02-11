"""Tests for care package module."""

import math
from pathlib import Path

import pytest

from speedfog.care_package import (
    ITEM_TYPE_ACCESSORY,
    ITEM_TYPE_GOODS,
    ITEM_TYPE_PROTECTOR,
    ITEM_TYPE_WEAPON,
    CarePackageItem,
    _apply_weapon_upgrade,
    _format_upgrade,
    _somber_upgrade,
    load_item_pool,
    sample_care_package,
)
from speedfog.config import CarePackageConfig, Config

# =============================================================================
# Upgrade encoding tests
# =============================================================================


class TestSomberUpgrade:
    """Tests for somber upgrade conversion."""

    def test_standard_8_to_somber_3(self):
        assert _somber_upgrade(8) == 3

    def test_standard_10_to_somber_4(self):
        assert _somber_upgrade(10) == 4

    def test_standard_25_to_somber_10(self):
        assert _somber_upgrade(25) == 10

    def test_standard_0_to_somber_0(self):
        assert _somber_upgrade(0) == 0

    def test_standard_1_to_somber_0(self):
        assert _somber_upgrade(1) == 0

    def test_standard_5_to_somber_2(self):
        assert _somber_upgrade(5) == 2

    def test_uses_floor(self):
        """Verify floor division behavior."""
        for level in range(26):
            expected = math.floor(level / 2.5)
            assert _somber_upgrade(level) == expected


class TestApplyWeaponUpgrade:
    """Tests for weapon upgrade encoding."""

    def test_uchigatana_plus_8(self):
        assert _apply_weapon_upgrade(9000000, 8) == 9000008

    def test_moonveil_plus_3(self):
        assert _apply_weapon_upgrade(9060000, 3) == 9060003

    def test_no_upgrade(self):
        assert _apply_weapon_upgrade(9000000, 0) == 9000000


class TestFormatUpgrade:
    """Tests for upgrade display formatting."""

    def test_standard_weapon(self):
        assert _format_upgrade("Uchigatana", 8) == "Uchigatana +8"

    def test_somber_weapon(self):
        assert _format_upgrade("Moonveil", 3) == "Moonveil +3"

    def test_no_upgrade(self):
        assert _format_upgrade("Dagger", 0) == "Dagger"


# =============================================================================
# Item pool loading tests
# =============================================================================


class TestLoadItemPool:
    """Tests for loading care_package_items.toml."""

    def test_loads_real_pool(self):
        """Load the actual care_package_items.toml from data/."""
        pool_path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
        if not pool_path.exists():
            pytest.skip("data/care_package_items.toml not found")
        pool = load_item_pool(pool_path)
        assert "weapons" in pool
        assert "standard" in pool["weapons"]
        assert "somber" in pool["weapons"]
        assert len(pool["weapons"]["standard"]) > 0

    def test_pool_has_all_categories(self):
        """Verify all expected categories exist in the pool."""
        pool_path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
        if not pool_path.exists():
            pytest.skip("data/care_package_items.toml not found")
        pool = load_item_pool(pool_path)
        assert "shields" in pool
        assert "catalysts" in pool
        assert "armor" in pool
        assert "talismans" in pool
        assert "sorceries" in pool
        assert "incantations" in pool
        assert "crystal_tears" in pool

    def test_pool_items_have_name_and_id(self):
        """Each item must have 'name' and 'id' fields."""
        pool_path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
        if not pool_path.exists():
            pytest.skip("data/care_package_items.toml not found")
        pool = load_item_pool(pool_path)
        for weapon in pool["weapons"]["standard"]:
            assert "name" in weapon
            assert "id" in weapon
            assert isinstance(weapon["id"], int)


# =============================================================================
# Sampling tests
# =============================================================================


class TestSampleCarePackage:
    """Tests for care package sampling."""

    @pytest.fixture()
    def pool_path(self) -> Path:
        path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
        if not path.exists():
            pytest.skip("data/care_package_items.toml not found")
        return path

    def test_deterministic_same_seed(self, pool_path: Path):
        """Same seed produces identical care packages."""
        config = CarePackageConfig(enabled=True, weapons=2, talismans=2)
        items1 = sample_care_package(config, seed=42, pool_path=pool_path)
        items2 = sample_care_package(config, seed=42, pool_path=pool_path)
        assert len(items1) == len(items2)
        for a, b in zip(items1, items2, strict=False):
            assert a.type == b.type
            assert a.id == b.id
            assert a.name == b.name

    def test_different_seed_different_items(self, pool_path: Path):
        """Different seeds produce different care packages (with high probability)."""
        config = CarePackageConfig(enabled=True, weapons=2, talismans=2)
        items1 = sample_care_package(config, seed=42, pool_path=pool_path)
        items2 = sample_care_package(config, seed=99, pool_path=pool_path)
        # At least one item should differ (extremely unlikely to be identical)
        names1 = {item.name for item in items1}
        names2 = {item.name for item in items2}
        assert names1 != names2

    def test_correct_item_types(self, pool_path: Path):
        """Items have correct types for their categories."""
        config = CarePackageConfig(
            enabled=True,
            weapons=1,
            shields=1,
            catalysts=1,
            talismans=1,
            sorceries=1,
            incantations=1,
            head_armor=1,
            body_armor=1,
            arm_armor=0,
            leg_armor=0,
            crystal_tears=1,
        )
        items = sample_care_package(config, seed=42, pool_path=pool_path)

        types = [item.type for item in items]
        assert ITEM_TYPE_WEAPON in types  # weapons, shields, catalysts
        assert ITEM_TYPE_PROTECTOR in types  # armor
        assert ITEM_TYPE_ACCESSORY in types  # talismans
        assert ITEM_TYPE_GOODS in types  # sorceries, incantations, crystal tears

    def test_weapon_upgrade_applied(self, pool_path: Path):
        """Weapons should have upgrade level encoded in their ID."""
        config = CarePackageConfig(
            enabled=True,
            weapon_upgrade=8,
            weapons=1,
            shields=0,
            catalysts=0,
            talismans=0,
            sorceries=0,
            incantations=0,
            head_armor=0,
            body_armor=0,
            arm_armor=0,
            leg_armor=0,
            crystal_tears=0,
        )
        items = sample_care_package(config, seed=42, pool_path=pool_path)
        assert len(items) == 1
        weapon = items[0]
        assert weapon.type == ITEM_TYPE_WEAPON
        # Weapon could be standard (+8) or somber (+3 = floor(8/2.5))
        # Either way, the upgrade level should be encoded in the ID
        upgrade_in_id = weapon.id % 100
        assert upgrade_in_id in (
            8,
            3,
        ), f"Expected +8 (standard) or +3 (somber), got +{upgrade_in_id}"
        assert "+" in weapon.name

    def test_zero_counts_produces_empty(self, pool_path: Path):
        """All zero counts produces empty care package."""
        config = CarePackageConfig(
            enabled=True,
            weapons=0,
            shields=0,
            catalysts=0,
            talismans=0,
            sorceries=0,
            incantations=0,
            head_armor=0,
            body_armor=0,
            arm_armor=0,
            leg_armor=0,
            crystal_tears=0,
        )
        items = sample_care_package(config, seed=42, pool_path=pool_path)
        assert len(items) == 0

    def test_count_respects_pool_size(self, pool_path: Path):
        """Requesting more items than pool size gives pool size items."""
        config = CarePackageConfig(
            enabled=True,
            weapons=0,
            shields=0,
            catalysts=0,
            talismans=100,  # More than available
            sorceries=0,
            incantations=0,
            head_armor=0,
            body_armor=0,
            arm_armor=0,
            leg_armor=0,
            crystal_tears=0,
        )
        items = sample_care_package(config, seed=42, pool_path=pool_path)
        # Should get all available talismans, not crash
        assert len(items) <= 100
        assert len(items) > 0

    def test_no_upgrade_on_non_weapons(self, pool_path: Path):
        """Non-weapon items should not have upgrade encoding."""
        config = CarePackageConfig(
            enabled=True,
            weapons=0,
            shields=0,
            catalysts=0,
            talismans=1,
            sorceries=1,
            incantations=1,
            head_armor=1,
            body_armor=0,
            arm_armor=0,
            leg_armor=0,
            crystal_tears=0,
        )
        items = sample_care_package(config, seed=42, pool_path=pool_path)
        for item in items:
            # Non-weapon items should not have "+N" in name
            assert "+" not in item.name


# =============================================================================
# Config parsing tests
# =============================================================================


class TestCarePackageConfig:
    """Tests for care package config parsing."""

    def test_defaults(self):
        config = Config.from_dict({})
        assert config.care_package.enabled is False
        assert config.care_package.weapon_upgrade == 8
        assert config.care_package.weapons == 5
        assert config.care_package.shields == 2
        assert config.care_package.catalysts == 2
        assert config.care_package.talismans == 4
        assert config.care_package.sorceries == 5
        assert config.care_package.incantations == 5
        assert config.care_package.head_armor == 2
        assert config.care_package.body_armor == 2
        assert config.care_package.arm_armor == 2
        assert config.care_package.leg_armor == 2
        assert config.care_package.crystal_tears == 5

    def test_from_toml(self, tmp_path):
        config_file = tmp_path / "config.toml"
        config_file.write_text("""
[care_package]
enabled = true
weapon_upgrade = 10
weapons = 3
shields = 2
catalysts = 2
talismans = 3
sorceries = 5
incantations = 5
head_armor = 2
body_armor = 2
arm_armor = 2
leg_armor = 2
crystal_tears = 5
""")
        config = Config.from_toml(config_file)
        assert config.care_package.enabled is True
        assert config.care_package.weapon_upgrade == 10
        assert config.care_package.weapons == 3
        assert config.care_package.shields == 2
        assert config.care_package.catalysts == 2
        assert config.care_package.talismans == 3
        assert config.care_package.sorceries == 5
        assert config.care_package.incantations == 5
        assert config.care_package.head_armor == 2
        assert config.care_package.body_armor == 2
        assert config.care_package.arm_armor == 2
        assert config.care_package.leg_armor == 2
        assert config.care_package.crystal_tears == 5

    def test_validation_weapon_upgrade_too_high(self):
        with pytest.raises(ValueError, match="weapon_upgrade must be 0-25"):
            Config.from_dict({"care_package": {"weapon_upgrade": 26}})

    def test_validation_weapon_upgrade_negative(self):
        with pytest.raises(ValueError, match="weapon_upgrade must be 0-25"):
            Config.from_dict({"care_package": {"weapon_upgrade": -1}})

    def test_validation_negative_count(self):
        with pytest.raises(ValueError, match="weapons must be >= 0"):
            Config.from_dict({"care_package": {"weapons": -1}})

    def test_validation_negative_talismans(self):
        with pytest.raises(ValueError, match="talismans must be >= 0"):
            Config.from_dict({"care_package": {"talismans": -5}})


# =============================================================================
# Output serialization tests
# =============================================================================


class TestCarePackageInOutput:
    """Tests for care package serialization in dag_to_dict."""

    def test_empty_care_package(self):
        """dag_to_dict with no care_package produces empty list."""
        from speedfog.clusters import ClusterPool
        from speedfog.output import dag_to_dict
        from tests.test_output import make_test_dag

        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert result["care_package"] == []

    def test_care_package_serialization(self):
        """dag_to_dict serializes care package items correctly."""
        from speedfog.clusters import ClusterPool
        from speedfog.output import dag_to_dict
        from tests.test_output import make_test_dag

        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )

        care_items = [
            CarePackageItem(type=0, id=9000008, name="Uchigatana +8"),
            CarePackageItem(type=1, id=50000, name="Kaiden Helm"),
            CarePackageItem(type=2, id=1040, name="Erdtree's Favor"),
            CarePackageItem(type=3, id=4000, name="Glintstone Pebble"),
        ]

        result = dag_to_dict(dag, clusters, care_package=care_items)
        assert len(result["care_package"]) == 4
        assert result["care_package"][0] == {
            "type": 0,
            "id": 9000008,
            "name": "Uchigatana +8",
        }
        assert result["care_package"][3] == {
            "type": 3,
            "id": 4000,
            "name": "Glintstone Pebble",
        }
