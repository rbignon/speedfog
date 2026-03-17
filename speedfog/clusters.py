"""Load and manage clusters from clusters.json."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path


def parse_qualified_fog_id(qualified: str) -> tuple[str | None, str]:
    """Parse 'zone:fog_id' or plain 'fog_id'. Returns (zone_or_none, fog_id)."""
    if ":" in qualified:
        zone, fog_id = qualified.split(":", 1)
        return zone, fog_id
    return None, qualified


def fog_matches_spec(fog_id: str, fog_zone: str, spec: str) -> bool:
    """Check if a fog matches a qualified ('zone:fog_id') or plain spec.

    Args:
        fog_id: The fog gate identifier.
        fog_zone: The zone the fog belongs to.
        spec: Either 'fog_id' (matches any zone) or 'zone:fog_id'.

    Returns:
        True if the fog matches the spec.
    """
    spec_zone, spec_fog = parse_qualified_fog_id(spec)
    return spec_fog == fog_id and (spec_zone is None or spec_zone == fog_zone)


def _filter_fogs_by_allowed(fogs: list[dict], allowed: list[str]) -> list[dict]:
    """Filter fog list to only fogs matching allowed specifiers.

    Each specifier is either a plain fog_id (matches any zone) or
    'zone:fog_id' (matches only that zone).
    """
    return [
        fog
        for fog in fogs
        if any(fog_matches_spec(fog["fog_id"], fog["zone"], spec) for spec in allowed)
    ]


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
    allow_shared_entrance: bool = False  # Multiple branches can share one entry fog
    allow_entry_as_exit: bool = (
        False  # Entry fog's return direction used as forward exit
    )
    requires: str = ""  # Zone that must be defeated before this cluster
    proximity_groups: list[list[str]] = field(
        default_factory=list
    )  # Fogs spatially close — entry and exit cannot share a group
    display_name: str = ""  # Pre-computed display name from clusters.json

    @classmethod
    def from_dict(cls, data: dict) -> ClusterData:
        """Create ClusterData from a dictionary."""
        all_exits = data.get("exit_fogs", [])
        entry_fogs = data.get("entry_fogs", [])
        exit_fogs = [f for f in all_exits if not f.get("unique")]

        # Filter by allowed_entries/allowed_exits at load time so all
        # capacity checks automatically respect the constraints.
        allowed_entries = data.get("allowed_entries", [])
        if allowed_entries:
            entry_fogs = _filter_fogs_by_allowed(entry_fogs, allowed_entries)
        allowed_exits = data.get("allowed_exits", [])
        if allowed_exits:
            exit_fogs = _filter_fogs_by_allowed(exit_fogs, allowed_exits)

        return cls(
            id=data["id"],
            zones=data["zones"],
            type=data["type"],
            weight=data["weight"],
            entry_fogs=entry_fogs,
            exit_fogs=exit_fogs,
            unique_exit_fogs=[f for f in all_exits if f.get("unique")],
            defeat_flag=data.get("defeat_flag", 0),
            allow_shared_entrance=data.get("allow_shared_entrance", False),
            allow_entry_as_exit=data.get("allow_entry_as_exit", False),
            requires=data.get("requires", ""),
            proximity_groups=data.get("proximity_groups", []),
            display_name=data.get("display_name", ""),
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
    zone_conflicts: dict[str, list[str]] = field(default_factory=dict)

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
        """Get the display name for a cluster.

        Uses the pre-computed display_name from clusters.json when available.
        Falls back to zone_names lookup (first zone with a name), then
        cluster.id.

        Args:
            cluster: The cluster to get a display name for

        Returns:
            Display name for the cluster
        """
        if cluster.display_name:
            return cluster.display_name
        for zone in cluster.zones:
            if zone in self.zone_names:
                return self.zone_names[zone]
        return cluster.id

    def get_conflicting_zones(self, zones: list[str]) -> set[str]:
        """Get all zones that conflict with the given zones.

        Args:
            zones: List of zone IDs to check.

        Returns:
            Set of zone IDs that conflict with any of the input zones.
        """
        result: set[str] = set()
        for zone in zones:
            if zone in self.zone_conflicts:
                result.update(self.zone_conflicts[zone])
        return result

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

    def filter_passant_incompatible(self) -> list[ClusterData]:
        """Remove clusters that can never serve as passant nodes.

        A cluster is passant-incompatible if consuming any single entry
        leaves zero exits. This happens when it has 1 bidirectional
        entry and 1 exit (same fog gate, same zone).

        Start and final_boss clusters are exempt (they don't need
        passant capability).

        Returns:
            List of removed clusters.
        """
        from speedfog.generator import can_be_passant_node

        exempt_types = {"start", "final_boss"}
        to_remove = [
            c
            for c in self.clusters
            if c.type not in exempt_types and not can_be_passant_node(c)
        ]

        for cluster in to_remove:
            self.clusters.remove(cluster)
            del self.by_id[cluster.id]
            if cluster.type in self.by_type:
                type_list = self.by_type[cluster.type]
                if cluster in type_list:
                    type_list.remove(cluster)

        return to_remove

    @classmethod
    def from_json(cls, path: Path) -> ClusterPool:
        """Load cluster pool from JSON file."""
        with open(path, encoding="utf-8") as f:
            data = json.load(f)

        pool = cls()
        pool.zone_maps = data.get("zone_maps", {})
        pool.zone_names = data.get("zone_names", {})
        pool.zone_conflicts = data.get("zone_conflicts", {})

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
