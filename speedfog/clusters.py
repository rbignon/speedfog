"""Load and manage clusters from clusters.json."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class ClusterData:
    """A cluster loaded from clusters.json."""

    id: str
    zones: list[str]
    type: str  # start, final_boss, legacy_dungeon, mini_dungeon, boss_arena
    weight: int
    entry_fogs: list[dict]  # [{"fog_id": str, "zone": str}, ...]
    exit_fogs: list[dict]  # [{"fog_id": str, "zone": str, "unique"?: bool}, ...]

    @classmethod
    def from_dict(cls, data: dict) -> ClusterData:
        """Create ClusterData from a dictionary."""
        return cls(
            id=data["id"],
            zones=data["zones"],
            type=data["type"],
            weight=data["weight"],
            entry_fogs=data.get("entry_fogs", []),
            exit_fogs=data.get("exit_fogs", []),
        )

    def available_exits(self, used_entry: dict | None) -> list[dict]:
        """Get exit fogs available after using an entry fog.

        A fog gate has two sides. Using an entry from zone A only removes
        the exit from zone A (same side), not exits from other zones.

        Args:
            used_entry: Entry fog dict {"fog_id", "zone"}, or None.

        Returns:
            List of exit fog dicts still available.
        """
        if used_entry is None:
            return list(self.exit_fogs)

        used_key = (used_entry["fog_id"], used_entry["zone"])
        return [f for f in self.exit_fogs if (f["fog_id"], f["zone"]) != used_key]


@dataclass
class ClusterPool:
    """Collection of clusters grouped by type."""

    clusters: list[ClusterData] = field(default_factory=list)
    by_type: dict[str, list[ClusterData]] = field(default_factory=dict)
    by_id: dict[str, ClusterData] = field(default_factory=dict)
    zone_maps: dict[str, str] = field(default_factory=dict)

    def add(self, cluster: ClusterData) -> None:
        """Add a cluster to the pool."""
        self.clusters.append(cluster)
        self.by_id[cluster.id] = cluster
        if cluster.type not in self.by_type:
            self.by_type[cluster.type] = []
        self.by_type[cluster.type].append(cluster)

    def get_by_type(self, cluster_type: str) -> list[ClusterData]:
        """Get all clusters of a given type."""
        return self.by_type.get(cluster_type, [])

    def get_by_id(self, cluster_id: str) -> ClusterData | None:
        """Get a cluster by ID."""
        return self.by_id.get(cluster_id)

    def get_map(self, zone: str) -> str | None:
        """Get the map ID for a zone."""
        return self.zone_maps.get(zone)

    def get_map_for_cluster(self, cluster: ClusterData) -> str | None:
        """Get the primary map for a cluster (from first zone with a map)."""
        for zone in cluster.zones:
            map_id = self.zone_maps.get(zone)
            if map_id:
                return map_id
        return None

    @classmethod
    def from_json(cls, path: Path) -> ClusterPool:
        """Load cluster pool from JSON file."""
        with open(path, encoding="utf-8") as f:
            data = json.load(f)

        pool = cls()
        pool.zone_maps = data.get("zone_maps", {})

        for cluster_data in data.get("clusters", []):
            cluster = ClusterData.from_dict(cluster_data)
            pool.add(cluster)

        return pool


def load_clusters(path: Path) -> ClusterPool:
    """Load clusters from JSON file.

    Args:
        path: Path to clusters.json

    Returns:
        ClusterPool with all clusters loaded

    Raises:
        FileNotFoundError: If the file doesn't exist
    """
    if not path.exists():
        raise FileNotFoundError(f"Clusters file not found: {path}")
    return ClusterPool.from_json(path)
