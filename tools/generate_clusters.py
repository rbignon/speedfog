#!/usr/bin/env python3
"""
Generate clusters.json from FogRando's fog.txt.

A cluster is a group of zones connected by world connections. Once a player
enters a cluster through an entry_fog, they have access to all exit_fogs.

Usage:
    python generate_clusters.py fog.txt clusters.json [--metadata zone_metadata.toml]
    python generate_clusters.py data/fog.txt data/clusters.json --metadata data/zone_metadata.toml
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

try:
    import tomllib
except ImportError:
    import tomli as tomllib  # type: ignore[import-not-found]

import yaml

# =============================================================================
# Data Structures
# =============================================================================


@dataclass
class WorldConnection:
    """A connection between two areas via world transition (To: section)."""

    target_area: str
    text: str
    tags: list[str] = field(default_factory=list)
    cond: str | None = None

    @property
    def is_drop(self) -> bool:
        """Check if this is a one-way drop connection."""
        return "drop" in [t.lower() for t in self.tags]


@dataclass
class AreaData:
    """Parsed area from fog.txt Areas section."""

    name: str
    text: str
    maps: list[str]
    tags: list[str]
    to_connections: list[WorldConnection] = field(default_factory=list)
    has_boss: bool = False  # True if zone has BossTrigger (boss fight zone)


@dataclass
class FogSide:
    """One side (A or B) of a fog gate."""

    area: str
    text: str
    tags: list[str] = field(default_factory=list)
    cond: str | None = None

    def requires_own_zone(self) -> bool:
        """Check if this side requires already being in its own zone.

        When Cond contains the same zone as Area, it means you must already
        have access to that zone to use this side. This typically indicates
        a one-way drop or internal passage - NOT a valid entry point.

        Examples:
        - AEG099_002_9000 ASide: Area=volcano_town, Cond=volcano_town
          -> "can be reached from main town dropping down" = one-way drop
        """
        if not self.cond or not self.area:
            return False

        # Tokenize condition, removing operators and parentheses
        tokens = self.cond.replace("(", " ").replace(")", " ").split()
        condition_zones = {
            t.lower() for t in tokens if t.upper() not in ("OR", "AND", "OR3")
        }

        # If the condition contains only this zone, it's a self-requirement
        return self.area.lower() in condition_zones


@dataclass
class FogData:
    """Parsed fog gate from Entrances or Warps section."""

    name: str
    fog_id: int
    aside: FogSide
    bside: FogSide
    tags: list[str] = field(default_factory=list)
    split_from: str | None = None  # SplitFrom field (ashen alternates)

    @property
    def is_split(self) -> bool:
        """Check if this fog is a split/alternate of another fog.

        Split fogs (e.g., ashen capital versions) share the same logical
        connection as their canonical counterpart in FogMod's graph.
        They should not be used as separate entry/exit points.
        """
        return self.split_from is not None

    @property
    def is_unique(self) -> bool:
        """Unique fogs are one-way (ASide=exit, BSide=entry)."""
        return "unique" in [t.lower() for t in self.tags]

    @property
    def is_uniquegate(self) -> bool:
        """Uniquegate fogs are sending gates that can be coupled bidirectionally."""
        return "uniquegate" in [t.lower() for t in self.tags]

    @property
    def is_norandom(self) -> bool:
        """Non-randomizable fogs should be excluded."""
        tags_lower = [t.lower() for t in self.tags]
        return "norandom" in tags_lower or "unused" in tags_lower

    @property
    def is_major(self) -> bool:
        """Major fogs are significant boss fog gates (Godrick, Margit, etc.)."""
        return "major" in [t.lower() for t in self.tags]

    @property
    def zone_pair(self) -> frozenset[str]:
        """Return the pair of zones this fog connects (for grouping)."""
        return frozenset({self.aside.area, self.bside.area})


@dataclass
class ZoneFogs:
    """Entry and exit fogs for a zone."""

    entry_fogs: list[FogData] = field(default_factory=list)
    exit_fogs: list[FogData] = field(default_factory=list)


@dataclass
class Cluster:
    """A cluster of connected zones."""

    zones: frozenset[str]
    entry_fogs: list[dict] = field(default_factory=list)
    exit_fogs: list[dict] = field(default_factory=list)
    cluster_type: str = ""
    weight: int = 0
    cluster_id: str = ""


# =============================================================================
# Key Items (conditions that are items = guaranteed connection)
# =============================================================================

# Key items extracted from fog.txt KeyItems section
# When a world connection has a Cond with these items, the connection is guaranteed
# because SpeedFog gives all key items at start
KEY_ITEMS = {
    "academyglintstonekey",
    "carianinvertedstatue",
    "cursemarkofdeath",
    "darkmoonring",
    "dectusmedallionleft",
    "dectusmedallionright",
    "discardedpalacekey",
    "drawingroomkey",
    "haligtreesecretmedallionleft",
    "haligtreesecretmedallionright",
    "imbuedswordkey",
    "purebloodknightsmedal",
    "roldmedallion",
    "runegodrick",
    "runemalenia",
    "runemohg",
    "runemorgott",
    "runeradahn",
    "runerennala",
    "runerykard",
    "rustykey",
    # DLC items (excluded anyway but listed for completeness)
    "omother",
    "welldepthskey",
    "gaolupperlevelkey",
    "gaollowerlevelkey",
    "holeladennecklace",
    "messmerskindling",
    # Special pass items (always available)
    "scalepass",
    "logicpass",
}

# Tags that mark DLC content
DLC_TAGS = {"dlc", "dlc1", "dlc2", "dlconly"}

# Tags that mark overworld areas
OVERWORLD_TAG = "overworld"

# Tags to exclude areas
EXCLUDE_TAGS = {"unused", "crawlonly", "evergaol"}  # evergaols need special handling

# Zone name prefixes to exclude (these use alternative fog gates that FogMod ignores)
EXCLUDE_ZONE_PREFIXES = {"leyndell2_"}  # Ashen Leyndell - use pre-ashen instead


# =============================================================================
# Parsing Functions
# =============================================================================


def parse_tags(tags_raw: Any) -> list[str]:
    """Parse tags from fog.txt (can be string or list)."""
    if not tags_raw:
        return []
    if isinstance(tags_raw, str):
        return tags_raw.split()
    if isinstance(tags_raw, list):
        return tags_raw
    return []


def parse_world_connection(conn_data: dict) -> WorldConnection:
    """Parse a To: connection entry."""
    return WorldConnection(
        target_area=conn_data.get("Area", ""),
        text=conn_data.get("Text", ""),
        tags=parse_tags(conn_data.get("Tags")),
        cond=conn_data.get("Cond"),
    )


def parse_area(area_data: dict) -> AreaData:
    """Parse an area entry from the Areas section."""
    maps_raw = area_data.get("Maps", "")
    maps = maps_raw.split() if isinstance(maps_raw, str) else maps_raw or []

    to_connections = []
    for conn in area_data.get("To", []) or []:
        to_connections.append(parse_world_connection(conn))

    # Check if zone has a boss (BossTrigger field present)
    has_boss = "BossTrigger" in area_data

    return AreaData(
        name=area_data.get("Name", ""),
        text=area_data.get("Text", ""),
        maps=maps,
        tags=parse_tags(area_data.get("Tags")),
        to_connections=to_connections,
        has_boss=has_boss,
    )


def parse_fog_side(side_data: dict) -> FogSide:
    """Parse ASide or BSide of a fog gate."""
    return FogSide(
        area=side_data.get("Area", ""),
        text=side_data.get("Text", ""),
        tags=parse_tags(side_data.get("Tags")),
        cond=side_data.get("Cond"),
    )


def parse_fog(fog_data: dict) -> FogData:
    """Parse an entrance or warp entry.

    Note: YAML parses names like "30052840_30051890" as integers (Python allows
    underscores in numeric literals). We reconstruct the underscore from ID and
    Location fields when this happens.
    """
    name = fog_data.get("Name", "")
    fog_id = fog_data.get("ID", 0)
    location = fog_data.get("Location")

    # Reconstruct underscore-separated names that YAML parsed as integers
    # Pattern: Name = "{ID}_{Location}" for backportals and paired warps
    if isinstance(name, int) and location is not None:
        expected_concat = int(f"{fog_id}{location}")
        if name == expected_concat:
            name = f"{fog_id}_{location}"

    return FogData(
        name=str(name),
        fog_id=fog_id,
        aside=parse_fog_side(fog_data.get("ASide", {})),
        bside=parse_fog_side(fog_data.get("BSide", {})),
        tags=parse_tags(fog_data.get("Tags")),
        split_from=fog_data.get("SplitFrom"),
    )


def parse_fog_txt(path: Path) -> dict[str, Any]:
    """
    Parse fog.txt and extract areas, entrances, warps, and key items.

    Returns dict with:
        areas: dict[name, AreaData]
        entrances: list[FogData]
        warps: list[FogData]
        key_items: set[str]
    """
    with open(path, encoding="utf-8") as f:
        data = yaml.safe_load(f)

    # Parse areas
    areas: dict[str, AreaData] = {}
    for area_raw in data.get("Areas", []):
        area = parse_area(area_raw)
        if area.name:
            areas[area.name] = area

    # Parse entrances
    entrances: list[FogData] = []
    for fog_raw in data.get("Entrances", []):
        fog = parse_fog(fog_raw)
        if fog.name:
            entrances.append(fog)

    # Parse warps
    warps: list[FogData] = []
    for fog_raw in data.get("Warps", []):
        fog = parse_fog(fog_raw)
        if fog.name:
            warps.append(fog)

    # Parse key items
    key_items: set[str] = set()
    for item in data.get("KeyItems", []):
        name = item.get("Name", "")
        if name:
            key_items.add(name.lower())

    return {
        "areas": areas,
        "entrances": entrances,
        "warps": warps,
        "key_items": key_items,
    }


# =============================================================================
# World Graph Building
# =============================================================================


@dataclass
class WorldGraph:
    """Directed graph of world connections between areas."""

    # edges[from_area] = list of (to_area, is_bidirectional)
    edges: dict[str, list[tuple[str, bool]]] = field(
        default_factory=lambda: defaultdict(list)
    )

    def add_edge(self, from_area: str, to_area: str, bidirectional: bool) -> None:
        """Add an edge to the graph."""
        self.edges[from_area].append((to_area, bidirectional))
        if bidirectional:
            self.edges[to_area].append((from_area, True))

    def has_unidirectional_edge(self, from_area: str, to_area: str) -> bool:
        """Check if there's a unidirectional edge from from_area to to_area."""
        for target, bidir in self.edges[from_area]:
            if target == to_area and not bidir:
                return True
        return False

    def get_reachable(self, start: str) -> set[str]:
        """Get all areas reachable from start via outgoing edges."""
        visited: set[str] = set()
        stack = [start]

        while stack:
            current = stack.pop()
            if current in visited:
                continue
            visited.add(current)

            for target, _ in self.edges[current]:
                if target not in visited:
                    stack.append(target)

        visited.discard(start)  # Don't include start in result
        return visited


def is_condition_guaranteed(cond: str | None, key_items: set[str]) -> bool:
    """
    Check if a condition is guaranteed (no condition, or all tokens are key items).

    Condition format examples:
    - "rustykey"
    - "OR scalepass rustykey"
    - "OR ( AND scalepass imbued_base ) ( AND logicpass imbued_base_any )"
    - "academy_entrance" (zone condition - NOT guaranteed)
    """
    if cond is None:
        return True

    # Tokenize, removing parentheses
    tokens = cond.replace("(", " ").replace(")", " ").split()

    # Filter out logical operators
    items = [t.lower() for t in tokens if t.upper() not in ("OR", "AND")]

    if not items:
        return True

    # If any token looks like a zone (not in key_items), it's a zone condition
    # Zone conditions are NOT guaranteed for cluster purposes
    return all(item in key_items for item in items)


def build_world_graph(
    areas: dict[str, AreaData],
    key_items: set[str],
    allowed_zones: set[str] | None = None,
) -> WorldGraph:
    """
    Build a directed graph of world connections.

    Rules:
    - Drop tag -> unidirectional
    - Cond with zone -> skip (not relevant for clusters)
    - Cond with items only -> bidirectional (items are given)
    - No cond -> check if reverse connection exists
    - Only include edges where both zones are in allowed_zones (if specified)
    """
    graph = WorldGraph()

    # First pass: collect all connections
    connections: list[tuple[str, str, bool]] = []  # (from, to, is_drop)

    for area_name, area in areas.items():
        # Skip if source zone not in allowed set
        if allowed_zones is not None and area_name not in allowed_zones:
            continue

        for conn in area.to_connections:
            if not conn.target_area:
                continue

            # Skip if target zone not in allowed set
            if allowed_zones is not None and conn.target_area not in allowed_zones:
                continue

            # Skip connections with crawlonly tag
            if "crawlonly" in [t.lower() for t in conn.tags]:
                continue

            # Check if it's a drop (unidirectional)
            is_drop = conn.is_drop

            # Check condition
            if conn.cond:
                if not is_condition_guaranteed(conn.cond, key_items):
                    # Zone condition - skip for cluster building
                    continue

            connections.append((area_name, conn.target_area, is_drop))

    # Build lookup for reverse connections
    conn_set: set[tuple[str, str]] = {(f, t) for f, t, _ in connections}

    # Add edges to graph
    for from_area, to_area, is_drop in connections:
        if is_drop:
            # Unidirectional drop
            graph.add_edge(from_area, to_area, bidirectional=False)
        else:
            # Check if bidirectional (has reverse connection)
            has_reverse = (to_area, from_area) in conn_set
            graph.add_edge(from_area, to_area, bidirectional=has_reverse)

    return graph


# =============================================================================
# Fog Classification
# =============================================================================


def classify_fogs(
    entrances: list[FogData],
    warps: list[FogData],
) -> dict[str, ZoneFogs]:
    """
    Classify fogs as entry/exit for each zone.

    Rules:
    - Skip norandom fogs
    - Unique fogs: ASide = exit only, BSide = entry only
    - Uniquegate pairs: coupled as single bidirectional connection
    - Normal fogs: both sides are entry + exit
    """
    zone_fogs: dict[str, ZoneFogs] = defaultdict(ZoneFogs)

    all_fogs = entrances + warps

    # Group uniquegate fogs by zone pair to detect coupled sending gates
    # Each pair of zones should only produce ONE bidirectional connection
    uniquegate_by_zones: dict[frozenset[str], list[FogData]] = defaultdict(list)
    processed_uniquegate_pairs: set[frozenset[str]] = set()

    for fog in all_fogs:
        if fog.is_norandom:
            continue
        if fog.is_uniquegate and not fog.is_unique:
            # Collect uniquegate fogs for pairing
            uniquegate_by_zones[fog.zone_pair].append(fog)

    for fog in all_fogs:
        if fog.is_norandom:
            continue

        # Skip split/alternate fogs (e.g., ashen capital versions)
        # They share the same logical connection as their canonical counterpart
        if fog.is_split:
            continue

        tags_lower = [t.lower() for t in fog.tags]
        aside_tags_lower = [t.lower() for t in fog.aside.tags]
        bside_tags_lower = [t.lower() for t in fog.bside.tags]

        # Skip crawlonly fogs (we're not in crawl mode)
        if "crawlonly" in tags_lower:
            continue

        # Skip fogs with minorwarp at the fog level (not on sides)
        # These are transporter chests/sending gates without AEG099 fog gate models
        if "minorwarp" in tags_lower:
            continue

        aside_area = fog.aside.area
        bside_area = fog.bside.area

        if not aside_area or not bside_area:
            continue

        # Check for minorwarp tag on sides (transporter chest/warp)
        # In fog.txt, warps with minorwarp follow the standard ASide/BSide convention:
        # - ASide = "using the chest" = source (exit)
        # - BSide = "arriving" = destination (entry)
        # The minorwarp tag indicates this specific warp is one-way (you take the chest
        # and arrive somewhere). Paired warps (PairWith) may provide the return path.
        #
        # These warps are exit-only from ASide.Area, entry-only at BSide.Area
        if "minorwarp" in aside_tags_lower or "minorwarp" in bside_tags_lower:
            # ASide is source (exit), BSide is destination (entry)
            zone_fogs[aside_area].exit_fogs.append(fog)
            zone_fogs[bside_area].entry_fogs.append(fog)
            continue

        # Backportals become selfwarps in the boss room only
        # With req_backportal=true, BSide.Area = ASide.Area
        if "backportal" in tags_lower:
            # Selfwarp: only add to ASide (the boss room)
            zone_fogs[aside_area].entry_fogs.append(fog)
            zone_fogs[aside_area].exit_fogs.append(fog)
            continue

        if fog.is_unique:
            # Unique fogs are one-way warps (sending gates, abductors, etc.)
            # ASide is exit only - FogMod can redirect where the warp sends you
            # BSide is NOT an entry_fog - there's no physical fog gate at destination
            zone_fogs[aside_area].exit_fogs.append(fog)
        elif fog.is_uniquegate:
            # Uniquegate: check if this pair was already processed
            zone_pair = fog.zone_pair
            if zone_pair in processed_uniquegate_pairs:
                continue  # Already added as part of the pair

            # Mark as processed and add as single bidirectional connection
            processed_uniquegate_pairs.add(zone_pair)

            # Use the first fog of the pair as representative
            representative = uniquegate_by_zones[zone_pair][0]
            # Apply same Cond logic as bidirectional fogs
            if not representative.aside.requires_own_zone():
                zone_fogs[aside_area].entry_fogs.append(representative)
            zone_fogs[aside_area].exit_fogs.append(representative)
            if not representative.bside.requires_own_zone():
                zone_fogs[bside_area].entry_fogs.append(representative)
            zone_fogs[bside_area].exit_fogs.append(representative)
        else:
            # Bidirectional: both sides are entry + exit
            # EXCEPT when a side has Cond that requires its own zone (e.g., drops)
            #
            # Example: AEG099_002_9000 (volcano_town -> volcano_abductors)
            #   ASide: Area=volcano_town, Cond=volcano_town
            #   -> You must already be in volcano_town to use ASide (it's a drop)
            #   -> This fog is NOT a valid entry into volcano_town from outside
            #   -> But it IS still an exit from volcano_town (you can drop down)
            if not fog.aside.requires_own_zone():
                zone_fogs[aside_area].entry_fogs.append(fog)
            zone_fogs[aside_area].exit_fogs.append(fog)

            if not fog.bside.requires_own_zone():
                zone_fogs[bside_area].entry_fogs.append(fog)
            zone_fogs[bside_area].exit_fogs.append(fog)

    return dict(zone_fogs)


# =============================================================================
# Cluster Generation
# =============================================================================


def generate_clusters(
    zones: set[str],
    world_graph: WorldGraph,
) -> list[Cluster]:
    """
    Generate all possible clusters using flood-fill.

    For each zone, compute all reachable zones and create a cluster.
    Deduplicate by cluster zones.
    """
    seen_clusters: set[frozenset[str]] = set()
    clusters: list[Cluster] = []

    for zone in sorted(zones):  # Sort for determinism
        reachable = world_graph.get_reachable(zone)
        cluster_zones = frozenset({zone} | reachable)

        if cluster_zones not in seen_clusters:
            seen_clusters.add(cluster_zones)
            clusters.append(Cluster(zones=cluster_zones))

    return clusters


def compute_cluster_fogs(
    cluster: Cluster,
    world_graph: WorldGraph,
    zone_fogs: dict[str, ZoneFogs],
) -> None:
    """
    Compute entry_fogs and exit_fogs for a cluster.

    Entry zones: zones without unidirectional incoming edges from other cluster zones
    entry_fogs: union of entry_fogs from entry zones
    exit_fogs: union of exit_fogs from all zones
    """
    # Find entry zones (no unidirectional incoming edge from cluster)
    entry_zones = set(cluster.zones)

    for zone in cluster.zones:
        for other_zone in cluster.zones:
            if other_zone != zone:
                if world_graph.has_unidirectional_edge(other_zone, zone):
                    entry_zones.discard(zone)
                    break

    # Collect entry fogs from entry zones
    seen_fog_ids: set[int] = set()
    cluster.entry_fogs = []

    for zone in sorted(entry_zones):
        if zone not in zone_fogs:
            continue
        for fog in zone_fogs[zone].entry_fogs:
            if fog.fog_id not in seen_fog_ids:
                seen_fog_ids.add(fog.fog_id)
                cluster.entry_fogs.append(
                    {
                        "fog_id": str(fog.name),  # Always string for consistency
                        "zone": zone,
                    }
                )

    # Collect exit fogs from all zones
    seen_fog_ids = set()
    cluster.exit_fogs = []

    for zone in sorted(cluster.zones):
        if zone not in zone_fogs:
            continue
        for fog in zone_fogs[zone].exit_fogs:
            if fog.fog_id not in seen_fog_ids:
                seen_fog_ids.add(fog.fog_id)
                fog_entry = {
                    "fog_id": str(fog.name),  # Always string for consistency
                    "zone": zone,
                }
                if fog.is_unique:
                    fog_entry["unique"] = True
                cluster.exit_fogs.append(fog_entry)


# =============================================================================
# Filtering and Enrichment
# =============================================================================


def should_exclude_area(
    area: AreaData, exclude_dlc: bool, exclude_overworld: bool
) -> bool:
    """Check if an area should be excluded from cluster generation."""
    tags_lower = {t.lower() for t in area.tags}

    # Check exclude tags
    if tags_lower & EXCLUDE_TAGS:
        return True

    # Check DLC
    if exclude_dlc and (tags_lower & DLC_TAGS):
        return True

    # Check overworld
    if exclude_overworld and OVERWORLD_TAG in tags_lower:
        return True

    # Check excluded zone prefixes (e.g., ashen Leyndell uses alternative fog gates)
    for prefix in EXCLUDE_ZONE_PREFIXES:
        if area.name.startswith(prefix):
            return True

    return False


def get_major_zones(
    entrances: list[FogData],
    warps: list[FogData],
) -> set[str]:
    """
    Get zones connected to fog gates with the 'major' tag.

    These are significant boss fights (Godrick, Margit, Radahn, etc.).
    """
    major_zones: set[str] = set()

    for fog in entrances + warps:
        if fog.is_major:
            if fog.aside.area:
                major_zones.add(fog.aside.area)
            if fog.bside.area:
                major_zones.add(fog.bside.area)

    return major_zones


def get_fortress_zones(warps: list[FogData]) -> set[str]:
    """
    Get zones with 'fortressonly legacy' warps.

    These are mini-fortresses that should be treated as legacy dungeons:
    - Caria Manor (liurnia_manor)
    - Shaded Castle (altus_shaded)
    - Castle Redmane (caelid_redmane)
    - Castle Sol (mountaintops_sol)
    """
    fortress_zones: set[str] = set()

    for fog in warps:
        tags_lower = [t.lower() for t in fog.tags]
        if "fortressonly" in tags_lower and "legacy" in tags_lower:
            if fog.aside.area:
                fortress_zones.add(fog.aside.area)
            if fog.bside.area:
                fortress_zones.add(fog.bside.area)

    return fortress_zones


def get_zone_type(
    area: AreaData,
    major_zones: set[str] | None = None,
    fortress_zones: set[str] | None = None,
) -> str:
    """
    Derive zone type from area data.

    Returns one of these types:
    - "start": Starting zone (chapel_start)
    - "final_boss": End zone (leyndell_erdtree, leyndell2_erdtree)
    - "major_boss": Boss arena with major fog gate (Godrick, Margit, etc.)
    - "boss_arena": Boss arena without major fog gate (evergaols, minidungeon bosses)
    - "legacy_dungeon": Large dungeons (Stormveil, Academy, mini-fortresses)
    - "mini_dungeon": Catacombs, caves, tunnels, gaols
    - "underground": Underground areas (Siofra, Ainsel, Deeproot, Mohgwyn) - filtered out
    - "other": Unclassified (divine towers, overworld tiles, colosseums)
    """
    if major_zones is None:
        major_zones = set()
    if fortress_zones is None:
        fortress_zones = set()

    tags_lower = {t.lower() for t in area.tags}
    name_lower = area.name.lower()

    # Special zones
    if "start" in tags_lower or name_lower == "chapel_start":
        return "start"

    if name_lower in ("leyndell_erdtree", "leyndell2_erdtree"):
        return "final_boss"

    # Boss zones (have BossTrigger in fog.txt)
    if area.has_boss:
        if area.name in major_zones:
            return "major_boss"
        return "boss_arena"

    # Trivial zones are pass-through areas, not significant dungeons
    # Check after special zones (start, final_boss, boss_arena) but before map-based classification
    if "trivial" in tags_lower:
        return "other"

    # Mini-fortresses (Caria Manor, Shaded Castle, Castle Redmane, Castle Sol)
    if area.name in fortress_zones:
        return "legacy_dungeon"

    if not area.maps:
        return "other"  # No maps = unknown

    primary_map = area.maps[0]

    # Legacy dungeon map prefixes (m10=Stormveil, m11=Academy, etc.)
    legacy_prefixes = ["m10_", "m11_", "m13_", "m14_", "m15_", "m16_"]
    if any(primary_map.startswith(p) for p in legacy_prefixes):
        return "legacy_dungeon"

    # Underground areas (m12): Siofra, Ainsel, Deeproot, Mohgwyn, Lake of Rot
    if primary_map.startswith("m12"):
        return "underground"

    # Mini-dungeons: catacombs, caves, tunnels, gaols, sewers
    # m30=catacombs, m31=caves, m32=tunnels, m35=sewers, m39=gaols
    mini_dungeon_prefixes = ["m30", "m31", "m32", "m35", "m39"]
    if any(primary_map.startswith(p) for p in mini_dungeon_prefixes):
        return "mini_dungeon"

    # Check minidungeon tag as fallback
    if "minidungeon" in tags_lower:
        return "mini_dungeon"

    # Everything else: divine towers (m34), colosseums (m45), overworld tiles (m60), etc.
    return "other"


def load_metadata(path: Path | None) -> dict:
    """Load zone metadata from TOML file."""
    if path is None or not path.exists():
        return {
            "defaults": {
                "legacy_dungeon": 10,
                "mini_dungeon": 4,
                "major_boss": 3,
                "boss_arena": 2,
                "underground": 6,
                "start": 1,
                "final_boss": 4,
                "other": 2,
            },
            "zones": {},
        }

    with open(path, "rb") as f:
        return tomllib.load(f)


def get_zone_weight(
    zone: str,
    zone_type: str,
    metadata: dict,
) -> int:
    """Get weight for a zone from metadata."""
    # Check zone-specific override
    zones_meta = metadata.get("zones", {})
    if zone in zones_meta:
        zone_meta = zones_meta[zone]
        if isinstance(zone_meta, dict) and "weight" in zone_meta:
            return zone_meta["weight"]
        if isinstance(zone_meta, int):
            return zone_meta

    # Use default for type
    defaults = metadata.get("defaults", {})
    return defaults.get(zone_type, 4)


def generate_cluster_id(zones: frozenset[str]) -> str:
    """Generate a unique cluster ID from zones."""
    # Sort zones for determinism
    sorted_zones = sorted(zones)
    primary_zone = sorted_zones[0]

    # Generate short hash
    hash_input = ",".join(sorted_zones).encode("utf-8")
    short_hash = hashlib.md5(hash_input).hexdigest()[:4]

    return f"{primary_zone}_{short_hash}"


def filter_and_enrich_clusters(
    clusters: list[Cluster],
    areas: dict[str, AreaData],
    metadata: dict,
    major_zones: set[str],
    fortress_zones: set[str],
    exclude_dlc: bool,
    exclude_overworld: bool,
) -> list[Cluster]:
    """
    Filter out invalid clusters and enrich with type/weight/id.

    Excludes:
    - Clusters with DLC zones (if exclude_dlc)
    - Clusters with overworld zones (if exclude_overworld)
    - Clusters with no entry_fogs or no exit_fogs
    - Underground clusters (large empty exploration areas)
    """
    filtered: list[Cluster] = []

    for cluster in clusters:
        # Check if any zone should be excluded
        skip = False
        for zone_name in cluster.zones:
            if zone_name not in areas:
                continue
            if should_exclude_area(areas[zone_name], exclude_dlc, exclude_overworld):
                skip = True
                break

        if skip:
            continue

        # Skip empty clusters
        if not cluster.entry_fogs or not cluster.exit_fogs:
            continue

        # Determine cluster type (from primary zone)
        primary_zone = sorted(cluster.zones)[0]
        if primary_zone in areas:
            cluster.cluster_type = get_zone_type(
                areas[primary_zone], major_zones, fortress_zones
            )
        else:
            cluster.cluster_type = "unknown"

        # Skip underground clusters (large empty exploration areas)
        if cluster.cluster_type == "underground":
            continue

        # Calculate total weight
        total_weight = 0
        for zone_name in cluster.zones:
            if zone_name in areas:
                zone_type = get_zone_type(areas[zone_name], major_zones, fortress_zones)
                total_weight += get_zone_weight(zone_name, zone_type, metadata)
            else:
                total_weight += 4  # Default weight

        cluster.weight = total_weight

        # Generate ID
        cluster.cluster_id = generate_cluster_id(cluster.zones)

        filtered.append(cluster)

    return filtered


# =============================================================================
# Output
# =============================================================================


def build_zone_maps(
    clusters: list[Cluster],
    areas: dict[str, AreaData],
) -> dict[str, str]:
    """
    Build zone→map mapping for all zones in clusters.

    Takes the first (primary) map from each zone's map list.
    """
    zone_maps: dict[str, str] = {}

    # Collect all zones from clusters
    all_zones: set[str] = set()
    for cluster in clusters:
        all_zones.update(cluster.zones)

    # Build mapping
    for zone_name in sorted(all_zones):
        if zone_name in areas:
            area = areas[zone_name]
            if area.maps:
                zone_maps[zone_name] = area.maps[0]  # Primary map

    return zone_maps


def clusters_to_json(
    clusters: list[Cluster],
    areas: dict[str, AreaData],
) -> dict:
    """Convert clusters to JSON-serializable format with zone→map mapping."""
    zone_maps = build_zone_maps(clusters, areas)

    return {
        "version": "1.1",
        "generated_from": "fog.txt",
        "cluster_count": len(clusters),
        "zone_maps": zone_maps,
        "clusters": [
            {
                "id": c.cluster_id,
                "zones": sorted(c.zones),
                "type": c.cluster_type,
                "weight": c.weight,
                "entry_fogs": c.entry_fogs,
                "exit_fogs": c.exit_fogs,
            }
            for c in sorted(clusters, key=lambda x: x.cluster_id)
        ],
    }


# =============================================================================
# CLI
# =============================================================================


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Generate clusters.json from FogRando's fog.txt",
    )
    parser.add_argument(
        "fog_txt",
        type=Path,
        help="Path to fog.txt input file",
    )
    parser.add_argument(
        "output",
        type=Path,
        help="Path to output clusters.json",
    )
    parser.add_argument(
        "--metadata",
        type=Path,
        default=None,
        help="Path to zone_metadata.toml (optional)",
    )
    parser.add_argument(
        "--exclude-dlc",
        action="store_true",
        default=True,
        help="Exclude DLC zones (default: True)",
    )
    parser.add_argument(
        "--include-dlc",
        action="store_true",
        help="Include DLC zones",
    )
    parser.add_argument(
        "--exclude-overworld",
        action="store_true",
        default=True,
        help="Exclude overworld zones (default: True)",
    )
    parser.add_argument(
        "--include-overworld",
        action="store_true",
        help="Include overworld zones",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Verbose output",
    )

    args = parser.parse_args()

    # Handle include flags overriding exclude defaults
    exclude_dlc = args.exclude_dlc and not args.include_dlc
    exclude_overworld = args.exclude_overworld and not args.include_overworld

    if not args.fog_txt.exists():
        print(f"Error: Input file not found: {args.fog_txt}", file=sys.stderr)
        return 1

    # Load and parse fog.txt
    print(f"Loading {args.fog_txt}...")
    try:
        parsed = parse_fog_txt(args.fog_txt)
    except yaml.YAMLError as e:
        print(f"Error parsing YAML: {e}", file=sys.stderr)
        return 1

    areas = parsed["areas"]
    entrances = parsed["entrances"]
    warps = parsed["warps"]
    key_items = parsed["key_items"] | KEY_ITEMS  # Merge with built-in list

    print(f"Parsed {len(areas)} areas, {len(entrances)} entrances, {len(warps)} warps")
    print(f"Known key items: {len(key_items)}")

    # Get zones to process (exclude DLC/overworld at zone level)
    zones_to_process: set[str] = set()
    for name, area in areas.items():
        if not should_exclude_area(area, exclude_dlc, exclude_overworld):
            zones_to_process.add(name)

    print(f"Zones to process: {len(zones_to_process)}")

    # Build world graph (only include edges between allowed zones)
    print("Building world graph...")
    world_graph = build_world_graph(areas, key_items, allowed_zones=zones_to_process)

    if args.verbose:
        edge_count = sum(len(edges) for edges in world_graph.edges.values())
        print(f"  Graph has {len(world_graph.edges)} nodes, {edge_count} edges")

    # Classify fogs
    print("Classifying fogs...")
    zone_fogs = classify_fogs(entrances, warps)
    print(f"  Found fogs for {len(zone_fogs)} zones")

    # Identify major boss zones (connected to fog gates with 'major' tag)
    major_zones = get_major_zones(entrances, warps)
    if args.verbose:
        print(f"  Major boss zones: {len(major_zones)}")

    # Identify fortress zones (mini-fortresses with fortressonly legacy warps)
    fortress_zones = get_fortress_zones(warps)
    if args.verbose:
        print(f"  Fortress zones: {len(fortress_zones)}")

    # Generate clusters
    print("Generating clusters...")
    clusters = generate_clusters(zones_to_process, world_graph)
    print(f"  Generated {len(clusters)} raw clusters")

    # Compute fogs for each cluster
    for cluster in clusters:
        compute_cluster_fogs(cluster, world_graph, zone_fogs)

    # Load metadata
    metadata = load_metadata(args.metadata)

    # Filter and enrich
    print("Filtering and enriching clusters...")
    clusters = filter_and_enrich_clusters(
        clusters,
        areas,
        metadata,
        major_zones,
        fortress_zones,
        exclude_dlc,
        exclude_overworld,
    )
    print(f"  Final cluster count: {len(clusters)}")

    # Output statistics
    if args.verbose:
        by_type: dict[str, int] = defaultdict(int)
        for c in clusters:
            by_type[c.cluster_type] += 1
        print("  Clusters by type:")
        for t, count in sorted(by_type.items()):
            print(f"    {t}: {count}")

    # Write output
    output_data = clusters_to_json(clusters, areas)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(output_data, f, indent=2)

    print(f"Wrote {args.output}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
