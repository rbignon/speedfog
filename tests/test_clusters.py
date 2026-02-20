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
