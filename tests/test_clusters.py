"""Tests for ClusterData fields and ClusterPool display name logic."""

import json

from speedfog.clusters import ClusterData, ClusterPool


class TestClusterDataReuseFields:
    """Tests for allow_shared_entrance and allow_entry_as_exit fields."""

    def test_default_values_false(self):
        """Reuse fields default to False when not in source dict."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [{"fog_id": "fog_a", "zone": "zone_a"}],
            "exit_fogs": [{"fog_id": "fog_b", "zone": "zone_a"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.allow_shared_entrance is False
        assert cluster.allow_entry_as_exit is False

    def test_fields_loaded_from_dict(self):
        """Reuse fields are loaded from source dict when present."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "boss_arena",
            "weight": 3,
            "entry_fogs": [
                {"fog_id": "fog_a", "zone": "zone_a"},
                {"fog_id": "fog_b", "zone": "zone_a"},
            ],
            "exit_fogs": [
                {"fog_id": "fog_c", "zone": "zone_a"},
                {"fog_id": "fog_d", "zone": "zone_a"},
            ],
            "allow_shared_entrance": True,
            "allow_entry_as_exit": True,
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.allow_shared_entrance is True
        assert cluster.allow_entry_as_exit is True


class TestClusterDataRequiresField:
    """Tests for the requires field on ClusterData."""

    def test_requires_default_empty(self):
        """requires defaults to empty string when not in source dict."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [{"fog_id": "fog_a", "zone": "zone_a"}],
            "exit_fogs": [{"fog_id": "fog_b", "zone": "zone_a"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.requires == ""

    def test_requires_loaded_from_dict(self):
        """requires field is loaded from source dict when present."""
        data = {
            "id": "erdtree_boss",
            "zones": ["leyndell_erdtree"],
            "type": "final_boss",
            "weight": 5,
            "entry_fogs": [{"fog_id": "fog_a", "zone": "leyndell_erdtree"}],
            "exit_fogs": [],
            "requires": "farumazula_maliketh",
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.requires == "farumazula_maliketh"


class TestClusterDataDisplayName:
    """Tests for display_name field on ClusterData."""

    def test_display_name_default_empty(self):
        """display_name defaults to empty string when not in source dict."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [{"fog_id": "fog_a", "zone": "zone_a"}],
            "exit_fogs": [{"fog_id": "fog_b", "zone": "zone_a"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.display_name == ""

    def test_display_name_loaded_from_dict(self):
        """display_name is loaded from source dict when present."""
        data = {
            "id": "academy_redwolf_8733",
            "zones": ["academy_redwolf", "academy_courtyard"],
            "type": "legacy_dungeon",
            "weight": 3,
            "display_name": "Academy of Raya Lucaria after Red Wolf",
            "entry_fogs": [{"fog_id": "fog_a", "zone": "academy_courtyard"}],
            "exit_fogs": [{"fog_id": "fog_b", "zone": "academy_courtyard"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.display_name == "Academy of Raya Lucaria after Red Wolf"


class TestGetDisplayName:
    """Tests for ClusterPool.get_display_name()."""

    def _make_cluster(self, cluster_id, zones, cluster_type, display_name=""):
        return ClusterData(
            id=cluster_id,
            zones=zones,
            type=cluster_type,
            weight=5,
            entry_fogs=[],
            exit_fogs=[],
            display_name=display_name,
        )

    def test_uses_precomputed_display_name(self):
        """get_display_name uses pre-computed display_name when available."""
        cluster = self._make_cluster(
            "academy_8733",
            ["academy_redwolf", "academy_courtyard"],
            "legacy_dungeon",
            display_name="Academy of Raya Lucaria after Red Wolf",
        )
        pool = ClusterPool(
            zone_names={"academy_redwolf": "Red Wolf of Radagon"},
        )
        assert (
            pool.get_display_name(cluster) == "Academy of Raya Lucaria after Red Wolf"
        )

    def test_falls_back_to_zone_names(self):
        """get_display_name falls back to zone_names when no display_name."""
        cluster = self._make_cluster(
            "stormveil_db4a",
            ["stormveil"],
            "legacy_dungeon",
        )
        pool = ClusterPool(
            zone_names={"stormveil": "Stormveil Castle after Gate"},
        )
        assert pool.get_display_name(cluster) == "Stormveil Castle after Gate"

    def test_falls_back_to_cluster_id(self):
        """get_display_name falls back to cluster.id when nothing else."""
        cluster = self._make_cluster("unknown_1234", ["unknown"], "mini_dungeon")
        pool = ClusterPool()
        assert pool.get_display_name(cluster) == "unknown_1234"


class TestZoneConflicts:
    """Tests for zone conflict loading and lookup."""

    def test_zone_conflicts_loaded_from_json(self, tmp_path):
        """zone_conflicts dict is loaded from clusters.json."""
        data = {
            "version": "1.10",
            "zone_maps": {},
            "zone_names": {},
            "zone_conflicts": {
                "stormveil_margit": ["leyndell_sanctuary"],
                "leyndell_sanctuary": ["stormveil_margit"],
            },
            "clusters": [],
        }
        path = tmp_path / "clusters.json"
        path.write_text(json.dumps(data))
        pool = ClusterPool.from_json(path)
        assert pool.zone_conflicts == {
            "stormveil_margit": ["leyndell_sanctuary"],
            "leyndell_sanctuary": ["stormveil_margit"],
        }

    def test_get_conflicting_zones(self):
        """get_conflicting_zones returns all zones conflicting with input."""
        pool = ClusterPool()
        pool.zone_conflicts = {
            "zone_a": ["zone_b", "zone_c"],
            "zone_b": ["zone_a"],
        }
        result = pool.get_conflicting_zones(["zone_a"])
        assert result == {"zone_b", "zone_c"}

    def test_get_conflicting_zones_no_conflicts(self):
        """get_conflicting_zones returns empty set when no conflicts."""
        pool = ClusterPool()
        pool.zone_conflicts = {}
        result = pool.get_conflicting_zones(["zone_x"])
        assert result == set()

    def test_zone_conflicts_defaults_empty(self, tmp_path):
        """zone_conflicts defaults to empty dict if missing from JSON."""
        data = {
            "version": "1.9",
            "zone_maps": {},
            "zone_names": {},
            "clusters": [],
        }
        path = tmp_path / "clusters.json"
        path.write_text(json.dumps(data))
        pool = ClusterPool.from_json(path)
        assert pool.zone_conflicts == {}
