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
    unique_exit_fogs: list[dict] = field(
        default_factory=list
    )  # unique exits (filtered out)
    defeat_flag: int = 0  # Boss defeat event flag (from fog.txt DefeatFlag)

    @classmethod
    def from_dict(cls, data: dict) -> ClusterData:
        """Create ClusterData from a dictionary."""
        all_exits = data.get("exit_fogs", [])
        return cls(
            id=data["id"],
            zones=data["zones"],
            type=data["type"],
            weight=data["weight"],
            entry_fogs=data.get("entry_fogs", []),
            exit_fogs=[f for f in all_exits if not f.get("unique")],
            unique_exit_fogs=[f for f in all_exits if f.get("unique")],
            defeat_flag=data.get("defeat_flag", 0),
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
    zone_names: dict[str, str] = field(default_factory=dict)

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

    def get_display_name(self, cluster: ClusterData) -> str:
        """Get the display name from the cluster's primary (first) zone.

        The first zone defines the cluster's identity; other zones are
        secondary (e.g. roundtable merged into chapel_start).

        Args:
            cluster: The cluster to get a display name for

        Returns:
            Display name of the first zone, or cluster.id as fallback
        """
        for zone in cluster.zones:
            if zone in self.zone_names:
                return self.zone_names[zone]
        return cluster.id

    def merge_roundtable_into_start(self) -> None:
        """Merge the roundtable cluster into the start cluster.

        Roundtable Hold is always accessible from Chapel of Anticipation
        via menu teleport (unlocked by RoundtableUnlockInjector). Merging
        makes the roundtable fog gate a second exit from the start node,
        enabling two branches from the very beginning of a run.

        The roundtable_balcony cluster (type "other") remains in the pool
        but is harmless since the generator never picks "other" clusters.
        """
        start_clusters = self.get_by_type("start")
        if not start_clusters:
            return

        start = start_clusters[0]

        # Find roundtable cluster by zone name
        roundtable: ClusterData | None = None
        for cluster in self.clusters:
            if "roundtable" in cluster.zones:
                roundtable = cluster
                break

        if roundtable is None:
            return

        # Merge roundtable data into the start cluster.
        # Weight is intentionally NOT updated: roundtable is a hub accessible
        # via menu teleport, not a dungeon the player must traverse sequentially.
        start.zones.extend(roundtable.zones)
        start.entry_fogs.extend(roundtable.entry_fogs)
        start.exit_fogs.extend(roundtable.exit_fogs)
        start.unique_exit_fogs.extend(roundtable.unique_exit_fogs)

        # Remove roundtable from the pool
        self.clusters.remove(roundtable)
        del self.by_id[roundtable.id]
        if roundtable.type in self.by_type:
            type_list = self.by_type[roundtable.type]
            if roundtable in type_list:
                type_list.remove(roundtable)

    @classmethod
    def from_json(cls, path: Path) -> ClusterPool:
        """Load cluster pool from JSON file."""
        with open(path, encoding="utf-8") as f:
            data = json.load(f)

        pool = cls()
        pool.zone_maps = data.get("zone_maps", {})
        pool.zone_names = data.get("zone_names", {})

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
