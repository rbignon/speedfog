"""Tests for ClusterData fog reuse fields."""

from speedfog.clusters import ClusterData


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
