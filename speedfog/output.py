"""Output module for DAG export to JSON and spoiler logs.

This module provides functions to export the generated DAG to:
- JSON format for consumption by the C# writer and visualization tools
- Human-readable spoiler log for players
"""

from __future__ import annotations

import json
from collections.abc import Callable
from datetime import datetime
from pathlib import Path
from typing import Any

from speedfog.clusters import ClusterPool
from speedfog.dag import Dag, DagNode

# =============================================================================
# V3 Format for FogModWrapper and visualization
# =============================================================================


def _get_fog_zone(node: DagNode, fog_id: str, is_entry: bool) -> str | None:
    """Get the zone containing a fog ID in a node's entry/exit fogs.

    Args:
        node: The node to search
        fog_id: The fog ID to find
        is_entry: True to search entry_fogs, False for exit_fogs

    Returns:
        Zone name, or None if not found
    """
    fogs = node.cluster.entry_fogs if is_entry else node.cluster.exit_fogs
    for fog in fogs:
        if fog["fog_id"] == fog_id:
            return str(fog["zone"])
    return None


def load_fog_data(path: Path) -> dict[str, dict[str, Any]]:
    """Load fog_data.json for fog→map lookups.

    Args:
        path: Path to fog_data.json

    Returns:
        Dictionary of fog_id → fog data (with "map" field)
    """
    if not path.exists():
        return {}
    with open(path, encoding="utf-8") as f:
        data: dict[str, Any] = json.load(f)
    fogs: dict[str, dict[str, Any]] = data.get("fogs", {})
    return fogs


def _make_fullname(
    fog_id: str,
    zone: str,
    clusters: ClusterPool,
    fog_data: dict[str, dict[str, Any]] | None = None,
    is_entry: bool = False,  # noqa: ARG001 - kept for API compatibility
) -> str:
    """Convert a fog_id to FogMod FullName format: {map}_{fog_id}.

    Args:
        fog_id: The fog ID (e.g., "AEG099_001_9000" or "1035452610")
        zone: The zone the fog connects to
        clusters: ClusterPool with zone_maps
        fog_data: Optional fog_data.json lookup for map resolution
        is_entry: Unused, kept for API compatibility

    Returns:
        FogMod FullName (e.g., "m10_01_00_00_AEG099_001_9000")

    Note:
        fog_data.json stores fogs with both short names and fully-qualified
        names (e.g., "AEG099_230_9000" and "m60_43_50_00_AEG099_230_9000").
        For dungeon entrances, the fog gate may be in a different map than
        the destination zone (e.g., overworld entrance to a dungeon).
        We search fog_data for a fullname that contains the destination zone.

        FogMod edge names are always based on the map where the asset physically
        exists (the "map" field), NOT the destination_map. The destination_map
        field is informational only.
    """
    # For warps (numeric IDs), fog_data has the authoritative map
    if fog_data and fog_id in fog_data and fog_id.isdigit():
        data = fog_data[fog_id]
        map_id = data.get("map")
        if map_id:
            return f"{map_id}_{fog_id}"

    # Get zone's map
    zone_map = clusters.get_map(zone)

    if fog_data:
        # Strategy 1: Try fully-qualified name with zone's map
        if zone_map:
            fullname = f"{zone_map}_{fog_id}"
            if fullname in fog_data:
                return fullname

        # Strategy 2: Search for any fullname ending with fog_id that contains zone
        # This handles cases where the fog gate is in a different map (e.g., dungeon entrance)
        for key, data in fog_data.items():
            if key.endswith(f"_{fog_id}") and zone in data.get("zones", []):
                return key

    # Fallback to zone's map
    if zone_map:
        return f"{zone_map}_{fog_id}"

    # Last resort: check fog_data for short name
    if fog_data and fog_id in fog_data:
        map_id = fog_data[fog_id].get("map")
        if map_id:
            return f"{map_id}_{fog_id}"

    return f"unknown_{fog_id}"


def dag_to_dict(
    dag: Dag,
    clusters: ClusterPool,
    options: dict[str, bool] | None = None,
    fog_data: dict[str, dict[str, Any]] | None = None,
    starting_item_lots: list[int] | None = None,
    starting_goods: list[int] | None = None,
    starting_runes: int = 0,
    starting_golden_seeds: int = 0,
    starting_sacred_tears: int = 0,
) -> dict[str, Any]:
    """Convert a DAG to v3 JSON-serializable dictionary.

    The v3 format extends v2 with `nodes` and `edges` sections for
    visualization tools, while keeping `connections`/`area_tiers` for
    FogModWrapper compatibility.

    Args:
        dag: The DAG to convert
        clusters: ClusterPool with zone_maps and zone_names
        options: FogMod options to include (default: scale=True)
        fog_data: Optional fog_data.json lookup for accurate map IDs (esp. for warps)
        starting_item_lots: DEPRECATED - ItemLot IDs (randomized by Item Randomizer)
        starting_goods: Good IDs to award at game start (not affected by randomization)
        starting_runes: Runes to add to starting classes (via CharaInitParam)
        starting_golden_seeds: Golden Seeds to give at start
        starting_sacred_tears: Sacred Tears to give at start

    Returns:
        Dictionary with the following structure:
        - version: "3.0"
        - seed: int
        - total_layers, total_nodes, total_zones, total_paths: metadata
        - options: dict of FogMod options
        - nodes: dict of cluster_id -> {type, display_name, zones, layer, tier, weight}
        - edges: list of {from, to}
        - connections: list of {exit_area, exit_gate, entrance_area, entrance_gate}
        - area_tiers: dict of zone -> tier
        - starting_goods, starting_runes, etc.
    """
    if options is None:
        options = {
            "scale": True,
            "shuffle": True,
        }

    # Build connections list
    connections: list[dict[str, str]] = []
    for edge in dag.edges:
        source_node = dag.nodes.get(edge.source_id)
        target_node = dag.nodes.get(edge.target_id)

        if source_node is None or target_node is None:
            continue

        # Find zones for exit and entry fogs
        exit_zone = _get_fog_zone(source_node, edge.exit_fog, is_entry=False)
        entry_zone = _get_fog_zone(target_node, edge.entry_fog, is_entry=True)

        # Handle final boss edge case: empty entry_fog means use first zone of target
        if entry_zone is None and (not edge.entry_fog or edge.entry_fog == ""):
            # Use first zone from target cluster
            if target_node.cluster.zones:
                entry_zone = target_node.cluster.zones[0]

        # Skip if zones not found (shouldn't happen in valid DAG)
        if exit_zone is None or entry_zone is None:
            print(
                f"Warning: Could not find zones for edge {edge.source_id} -> "
                f"{edge.target_id}: exit_fog={edge.exit_fog}, entry_fog={edge.entry_fog}"
            )
            continue

        # Handle empty entry_fog by using exit_fog (for one-way connections)
        effective_entry_fog = edge.entry_fog if edge.entry_fog else edge.exit_fog

        connections.append(
            {
                "exit_area": exit_zone,
                "exit_gate": _make_fullname(
                    edge.exit_fog, exit_zone, clusters, fog_data, is_entry=False
                ),
                "entrance_area": entry_zone,
                "entrance_gate": _make_fullname(
                    effective_entry_fog, entry_zone, clusters, fog_data, is_entry=True
                ),
            }
        )

    # Build area_tiers: zone -> tier
    area_tiers: dict[str, int] = {}
    for node in dag.nodes.values():
        for zone in node.cluster.zones:
            area_tiers[zone] = node.tier

    # Calculate metadata
    total_layers = max((n.layer for n in dag.nodes.values()), default=-1) + 1
    total_paths = len(dag.enumerate_paths())

    # Build nodes section: cluster_id -> metadata
    nodes: dict[str, dict[str, Any]] = {}
    for node in dag.nodes.values():
        nodes[node.cluster.id] = {
            "type": node.cluster.type,
            "display_name": clusters.get_display_name(node.cluster),
            "zones": node.cluster.zones,
            "layer": node.layer,
            "tier": node.tier,
            "weight": node.cluster.weight,
        }

    # Build edges section: unique (from, to) pairs by cluster_id
    seen_edges: set[tuple[str, str]] = set()
    edges_list: list[dict[str, str]] = []
    for edge in dag.edges:
        source_node = dag.nodes.get(edge.source_id)
        target_node = dag.nodes.get(edge.target_id)
        if source_node is None or target_node is None:
            continue
        pair = (source_node.cluster.id, target_node.cluster.id)
        if pair not in seen_edges:
            seen_edges.add(pair)
            edges_list.append({"from": pair[0], "to": pair[1]})

    return {
        "version": "3.0",
        "seed": dag.seed,
        "total_layers": total_layers,
        "total_nodes": dag.total_nodes(),
        "total_zones": dag.total_zones(),
        "total_paths": total_paths,
        "options": options,
        "nodes": nodes,
        "edges": edges_list,
        "connections": connections,
        "area_tiers": area_tiers,
        "starting_item_lots": starting_item_lots or [],
        "starting_goods": starting_goods or [],
        "starting_runes": starting_runes,
        "starting_golden_seeds": starting_golden_seeds,
        "starting_sacred_tears": starting_sacred_tears,
    }


def export_json(
    dag: Dag,
    clusters: ClusterPool,
    output_path: Path,
    options: dict[str, bool] | None = None,
    fog_data: dict[str, dict[str, Any]] | None = None,
    starting_item_lots: list[int] | None = None,
    starting_goods: list[int] | None = None,
    starting_runes: int = 0,
    starting_golden_seeds: int = 0,
    starting_sacred_tears: int = 0,
) -> None:
    """Export a DAG to v3 formatted JSON file.

    Args:
        dag: The DAG to export
        clusters: ClusterPool with zone_maps and zone_names
        output_path: Path to write the JSON file
        options: FogMod options (default: scale=True, shuffle=True)
        fog_data: Optional fog_data.json lookup for accurate map IDs
        starting_item_lots: DEPRECATED - ItemLot IDs (randomized by Item Randomizer)
        starting_goods: Good IDs to award at game start
        starting_runes: Runes to add to starting classes
        starting_golden_seeds: Golden Seeds to give at start
        starting_sacred_tears: Sacred Tears to give at start
    """
    data = dag_to_dict(
        dag,
        clusters,
        options,
        fog_data,
        starting_item_lots,
        starting_goods,
        starting_runes,
        starting_golden_seeds,
        starting_sacred_tears,
    )
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def _build_connection_lines(
    dag: Dag,
    prev_node_ids: list[str],
    curr_node_ids: list[str],
    col_width: int,
    total_width: int,
) -> list[str]:
    """Build ASCII lines showing connections between two layers.

    Uses diagonal characters (╲ ╱) and box-drawing to show cross-connections.

    Visual style for cross-connections (e.g., snowfield → both flamepeak and siofra):
        caelid_gaolcave    snowfield_catacombs
             │                  │ ╲
             │    ╭─────────────╯  │
             │    │                │
        flamepeak_firegiant  siofra_nokron_mimic

    Args:
        dag: The DAG with edges
        prev_node_ids: Sorted node IDs in the previous layer
        curr_node_ids: Sorted node IDs in the current layer
        col_width: Width of each column
        total_width: Total width of the output

    Returns:
        List of strings representing the connection lines
    """
    # Build edge list as (src_idx, tgt_idx) pairs
    edges: list[tuple[int, int]] = []
    for edge in dag.edges:
        if edge.source_id in prev_node_ids and edge.target_id in curr_node_ids:
            src_idx = prev_node_ids.index(edge.source_id)
            tgt_idx = curr_node_ids.index(edge.target_id)
            if (src_idx, tgt_idx) not in edges:
                edges.append((src_idx, tgt_idx))

    n_prev = len(prev_node_ids)
    n_curr = len(curr_node_ids)

    # Calculate center positions for each column
    def col_center(idx: int, n_cols: int) -> int:
        cols_width = col_width * n_cols
        offset = (total_width - cols_width) // 2
        return offset + idx * col_width + col_width // 2

    prev_centers = [col_center(i, n_prev) for i in range(n_prev)]
    curr_centers = [col_center(i, n_curr) for i in range(n_curr)]

    # Build maps for analysis
    src_targets: dict[int, list[int]] = {i: [] for i in range(n_prev)}
    tgt_sources: dict[int, list[int]] = {i: [] for i in range(n_curr)}
    for src, tgt in edges:
        src_targets[src].append(tgt)
        tgt_sources[tgt].append(src)

    # For merges: count sources from each side and track landing positions
    # This allows spacing multiple sources from the same side
    merge_left_count: dict[int, int] = {i: 0 for i in range(n_curr)}
    merge_right_count: dict[int, int] = {i: 0 for i in range(n_curr)}
    for tgt_idx in range(n_curr):
        tgt_pos = curr_centers[tgt_idx]
        for src_idx in tgt_sources[tgt_idx]:
            src_pos = prev_centers[src_idx]
            if src_pos < tgt_pos:
                merge_left_count[tgt_idx] += 1
            elif src_pos > tgt_pos:
                merge_right_count[tgt_idx] += 1

    # Check if all connections are simple 1:1 at same positions
    is_simple = (
        n_prev == n_curr
        and all(len(targets) == 1 for targets in src_targets.values())
        and all(len(sources) == 1 for sources in tgt_sources.values())
        and all(
            prev_centers[src] == curr_centers[targets[0]]
            for src, targets in src_targets.items()
            if targets
        )
    )

    lines: list[str] = []

    if is_simple:
        # Simple case: just vertical lines
        line_chars = [" "] * total_width
        for i in range(n_prev):
            pos = prev_centers[i]
            if 0 <= pos < total_width:
                line_chars[pos] = "│"
        lines.append("".join(line_chars))
        return lines

    # Complex case with cross-connections
    # Identify diagonal connections (source goes to target in different column)
    diagonals: list[
        tuple[int, int, int, int]
    ] = []  # (src_idx, tgt_idx, src_pos, tgt_pos)
    for src_idx, targets in src_targets.items():
        src_pos = prev_centers[src_idx]
        for tgt_idx in targets:
            tgt_pos = curr_centers[tgt_idx]
            if src_pos != tgt_pos:
                diagonals.append((src_idx, tgt_idx, src_pos, tgt_pos))

    # Track how many sources from each side we've already processed per target
    # Used to space landing positions for merges with multiple sources from same side
    merge_left_placed: dict[int, int] = {i: 0 for i in range(n_curr)}
    merge_right_placed: dict[int, int] = {i: 0 for i in range(n_curr)}

    # Row 1: Vertical lines from sources
    # Always show │ at source position if it goes anywhere
    # If source splits (goes to multiple targets), show extra │ at offset for diagonals
    row1 = [" "] * total_width
    for src_idx in range(n_prev):
        src_pos = prev_centers[src_idx]
        targets = src_targets[src_idx]
        if not targets:
            continue

        has_straight = any(curr_centers[t] == src_pos for t in targets)
        has_diagonal = any(curr_centers[t] != src_pos for t in targets)
        is_split = len(targets) > 1  # Source goes to multiple targets

        # Show │ at source position if straight connection
        if has_straight and 0 <= src_pos < total_width:
            row1[src_pos] = "│"

        if has_diagonal:
            if is_split:
                # Split: show extra │ at offset for diagonal paths
                for tgt_idx in targets:
                    tgt_pos = curr_centers[tgt_idx]
                    if tgt_pos < src_pos:
                        extra_pos = src_pos - 2
                        if 0 <= extra_pos < total_width and row1[extra_pos] == " ":
                            row1[extra_pos] = "│"
                    elif tgt_pos > src_pos:
                        extra_pos = src_pos + 2
                        if 0 <= extra_pos < total_width and row1[extra_pos] == " ":
                            row1[extra_pos] = "│"
            else:
                # Single diagonal (merge or 1:1 diagonal): show │ at source position
                if 0 <= src_pos < total_width and row1[src_pos] == " ":
                    row1[src_pos] = "│"

    lines.append("".join(row1))

    # Row 2: Horizontal routing with corners
    row2 = [" "] * total_width

    # For each diagonal, draw the horizontal portion with corners
    for src_idx, tgt_idx, src_pos, tgt_pos in diagonals:
        is_split = len(src_targets[src_idx]) > 1  # Source splits to multiple targets
        is_merge = (
            len(tgt_sources[tgt_idx]) > 1
        )  # Target receives from multiple sources

        if is_split:
            # Split case: source has offset │ in row1, corner lands at offset
            # If target is also a merge, offset the arrival corner to avoid collision
            if tgt_pos < src_pos:
                # Goes left: ╭ near target, ─── horizontal, ╯ at offset
                landing_pos = src_pos - 2
                if 0 <= landing_pos < total_width:
                    row2[landing_pos] = "╯"
                # Corner near target - offset if target is also a merge
                corner_pos = tgt_pos + 2 if is_merge else tgt_pos
                if 0 <= corner_pos < total_width and row2[corner_pos] == " ":
                    row2[corner_pos] = "╭"
                # Horizontal bar from corner+1 to landing
                for p in range(corner_pos + 1, landing_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"
            else:
                # Goes right: ╰ at offset, ─── horizontal, ╮ near target
                landing_pos = src_pos + 2
                if 0 <= landing_pos < total_width:
                    row2[landing_pos] = "╰"
                # Corner near target - offset if target is also a merge
                corner_pos = tgt_pos - 2 if is_merge else tgt_pos
                if 0 <= corner_pos < total_width and row2[corner_pos] == " ":
                    row2[corner_pos] = "╮"
                # Horizontal bar from landing+1 to corner
                for p in range(landing_pos + 1, corner_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"
        elif is_merge:
            # Merge case: mirror of split - corners land at offset positions near target
            # Pattern: sources → ╯/╰ at source, ─── horizontal, ╮/╭ at offset near target
            # Space landing positions when multiple sources come from the same side
            if tgt_pos < src_pos:
                # Source is to the right, goes left toward target
                # Offset landing position based on how many from right already placed
                offset = 2 + merge_right_placed[tgt_idx] * 2
                landing_pos = tgt_pos + offset
                merge_right_placed[tgt_idx] += 1
                if 0 <= src_pos < total_width:
                    row2[src_pos] = "╯"
                if 0 <= landing_pos < total_width:
                    row2[landing_pos] = "╭"
                # Horizontal bar from landing+1 to source
                for p in range(landing_pos + 1, src_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"
            else:
                # Source is to the left, goes right toward target
                # Offset landing position based on how many from left already placed
                offset = 2 + merge_left_placed[tgt_idx] * 2
                landing_pos = tgt_pos - offset
                merge_left_placed[tgt_idx] += 1
                if 0 <= src_pos < total_width:
                    row2[src_pos] = "╰"
                if 0 <= landing_pos < total_width:
                    row2[landing_pos] = "╮"
                # Horizontal bar from source+1 to landing
                for p in range(src_pos + 1, landing_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"
        else:
            # 1:1 diagonal (no split, no merge): corner at source position
            if tgt_pos < src_pos:
                # Goes left: ╯ at source
                if 0 <= src_pos < total_width:
                    row2[src_pos] = "╯"
                for p in range(tgt_pos + 1, src_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"
            else:
                # Goes right: ╰ at source
                if 0 <= src_pos < total_width:
                    row2[src_pos] = "╰"
                for p in range(src_pos + 1, tgt_pos):
                    if 0 <= p < total_width and row2[p] == " ":
                        row2[p] = "─"

        # Handle target position corner/junction for non-merge cases
        if not is_merge and 0 <= tgt_pos < total_width:
            has_straight_incoming = any(
                prev_centers[s] == tgt_pos for s in tgt_sources[tgt_idx]
            )
            from_left = tgt_pos > src_pos
            from_right = tgt_pos < src_pos

            # Check existing character and merge appropriately
            existing = row2[tgt_pos]
            if has_straight_incoming:
                if from_right and existing in [" ", "│"]:
                    row2[tgt_pos] = "├"
                elif from_left and existing in [" ", "│"]:
                    row2[tgt_pos] = "┤"
                elif existing == "─":
                    row2[tgt_pos] = "┬"
            else:
                # Single diagonal incoming
                if existing == " ":
                    row2[tgt_pos] = "╭" if from_right else "╮"

    # Add vertical lines for straight-down connections
    for tgt_idx in range(n_curr):
        tgt_pos = curr_centers[tgt_idx]
        sources = tgt_sources[tgt_idx]
        if not sources:
            continue

        has_straight = any(prev_centers[s] == tgt_pos for s in sources)
        has_diagonal = any(prev_centers[s] != tgt_pos for s in sources)

        if 0 <= tgt_pos < total_width:
            if has_straight and has_diagonal:
                if row2[tgt_pos] == "╭":
                    row2[tgt_pos] = "├"
                elif row2[tgt_pos] == "╮":
                    row2[tgt_pos] = "┤"
                elif row2[tgt_pos] == "─":
                    row2[tgt_pos] = "┬"
                elif row2[tgt_pos] == " ":
                    row2[tgt_pos] = "│"
            elif has_straight:
                if row2[tgt_pos] == " ":
                    row2[tgt_pos] = "│"
                elif row2[tgt_pos] == "─":
                    row2[tgt_pos] = "┬"

    # Also add verticals for sources that go straight down
    for src_idx in range(n_prev):
        src_pos = prev_centers[src_idx]
        targets = src_targets[src_idx]
        has_straight = any(curr_centers[t] == src_pos for t in targets)
        if has_straight and 0 <= src_pos < total_width:
            if row2[src_pos] == " ":
                row2[src_pos] = "│"
            elif row2[src_pos] == "─":
                row2[src_pos] = "┼"

    lines.append("".join(row2))

    # Row 3: Vertical lines going down to targets
    # Mirror the split logic: if target receives from multiple sources (merge),
    # show extra │ at offset positions
    row3 = [" "] * total_width
    for tgt_idx in range(n_curr):
        tgt_pos = curr_centers[tgt_idx]
        sources = tgt_sources[tgt_idx]
        if not sources:
            continue

        is_merge = len(sources) > 1  # Target receives from multiple sources

        if is_merge:
            # Merge: show │ for each incoming branch
            # Bars align with the corners in row2
            has_straight_source = any(prev_centers[s] == tgt_pos for s in sources)

            if has_straight_source:
                if 0 <= tgt_pos < total_width:
                    row3[tgt_pos] = "│"

            # Count sources from each side and place bars at spaced positions
            left_sources = [s for s in sources if prev_centers[s] < tgt_pos]
            right_sources = [s for s in sources if prev_centers[s] > tgt_pos]

            # Place bars for sources from the left (at tgt_pos - 2, -4, -6, ...)
            for i in range(len(left_sources)):
                offset = 2 + i * 2
                bar_pos = tgt_pos - offset
                if 0 <= bar_pos < total_width and row3[bar_pos] == " ":
                    row3[bar_pos] = "│"

            # Place bars for sources from the right (at tgt_pos + 2, +4, +6, ...)
            for i in range(len(right_sources)):
                offset = 2 + i * 2
                bar_pos = tgt_pos + offset
                if 0 <= bar_pos < total_width and row3[bar_pos] == " ":
                    row3[bar_pos] = "│"
        else:
            # Single source: just one vertical line
            if 0 <= tgt_pos < total_width:
                row3[tgt_pos] = "│"

    lines.append("".join(row3))

    return lines


def export_spoiler_log(dag: Dag, output_path: Path) -> None:
    """Export human-readable spoiler log with ASCII graph visualization.

    Args:
        dag: The DAG to export
        output_path: Path to write the spoiler log
    """
    lines: list[str] = []

    # Header
    lines.append("=" * 60)
    lines.append(f"SPEEDFOG SPOILER (seed: {dag.seed})")
    lines.append(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("=" * 60)
    lines.append(f"Total zones: {dag.total_zones()}")
    paths = dag.enumerate_paths()
    lines.append(f"Total paths: {len(paths)}")
    lines.append("")

    # Group nodes by layer
    nodes_by_layer: dict[int, list[str]] = {}
    for node_id, node in dag.nodes.items():
        layer = node.layer
        if layer not in nodes_by_layer:
            nodes_by_layer[layer] = []
        nodes_by_layer[layer].append(node_id)

    # Sort layers and node IDs within each layer using barycentric ordering
    # This minimizes edge crossings by placing nodes near their parents/children
    sorted_layers = sorted(nodes_by_layer.keys())

    # Build parent map: node_id -> list of parent node_ids
    parents: dict[str, list[str]] = {nid: [] for nid in dag.nodes}
    for edge in dag.edges:
        if edge.target_id in parents:
            parents[edge.target_id].append(edge.source_id)

    # First pass: sort first layer alphabetically (no parents to reference)
    if sorted_layers:
        nodes_by_layer[sorted_layers[0]] = sorted(nodes_by_layer[sorted_layers[0]])

    # Subsequent layers: sort by average position of parents in previous layer
    for i in range(1, len(sorted_layers)):
        layer = sorted_layers[i]
        prev_layer = sorted_layers[i - 1]
        prev_nodes = nodes_by_layer[prev_layer]

        # Map parent node_id to its position in previous layer
        prev_pos = {nid: idx for idx, nid in enumerate(prev_nodes)}

        def make_sort_key(
            prev_pos: dict[str, int],
        ) -> Callable[[str], tuple[float, str]]:
            """Create sort key function that captures prev_pos."""

            def sort_key(node_id: str) -> tuple[float, str]:
                node_parents = [p for p in parents[node_id] if p in prev_pos]
                if not node_parents:
                    barycenter = float("inf")
                else:
                    barycenter = sum(prev_pos[p] for p in node_parents) / len(
                        node_parents
                    )
                return (barycenter, node_id)

            return sort_key

        # Sort by barycenter, then by node_id for stability
        nodes_by_layer[layer] = sorted(
            nodes_by_layer[layer], key=make_sort_key(prev_pos)
        )

    # Fixed column width and name truncation for consistent alignment
    col_width = 24
    max_name_len = col_width - 2  # Leave some padding

    # Find max branches across all layers for consistent total width
    max_branches = max(len(nodes_by_layer[layer]) for layer in sorted_layers)
    total_width = col_width * max_branches

    # Build ASCII graph visualization
    for layer_idx, layer in enumerate(sorted_layers):
        node_ids = nodes_by_layer[layer]
        n_nodes = len(node_ids)

        # Calculate offset to center this layer's columns
        layer_width = col_width * n_nodes
        offset = (total_width - layer_width) // 2

        # Draw connection lines from previous layer using actual edges
        if layer_idx > 0:
            prev_layer = sorted_layers[layer_idx - 1]
            prev_node_ids = nodes_by_layer[prev_layer]
            connection_lines = _build_connection_lines(
                dag, prev_node_ids, node_ids, col_width, total_width
            )
            lines.extend(connection_lines)

        # Draw cluster names (truncated)
        name_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            name = node.cluster.id[:max_name_len]
            name_parts.append(name.center(col_width))
        name_line = " " * offset + "".join(name_parts)
        lines.append(name_line)

        # Draw cluster type
        type_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            type_str = f"[{node.cluster.type}]"
            type_parts.append(type_str.center(col_width))
        type_line = " " * offset + "".join(type_parts)
        lines.append(type_line)

        # Draw weights and tier info
        info_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            info = f"(w:{node.cluster.weight} t:{node.tier})"
            info_parts.append(info.center(col_width))
        info_line = " " * offset + "".join(info_parts)
        lines.append(info_line)

    lines.append("")
    lines.append("=" * 60)
    lines.append("PATH SUMMARY")
    lines.append("=" * 60)

    for i, path in enumerate(paths, 1):
        weight = dag.path_weight(path)
        # Use cluster IDs, truncated to same length as graph
        path_str = " → ".join(
            dag.nodes[nid].cluster.id[:max_name_len] if nid in dag.nodes else nid
            for nid in path
        )
        lines.append(f"Path {i} (weight {weight}): {path_str}")

    # NODE DETAILS section
    lines.append("")
    lines.append("=" * 60)
    lines.append("NODE DETAILS")
    lines.append("=" * 60)

    # Build edge lookup: source_id -> list of (target_id, fog_id)
    outgoing_edges: dict[str, list[tuple[str, str]]] = {}
    for edge in dag.edges:
        if edge.source_id not in outgoing_edges:
            outgoing_edges[edge.source_id] = []
        outgoing_edges[edge.source_id].append((edge.target_id, edge.fog_id))

    # Print node details sorted by tier then by cluster ID
    sorted_nodes = sorted(dag.nodes.values(), key=lambda n: (n.tier, n.cluster.id))
    for node in sorted_nodes:
        lines.append("")
        lines.append(f"[{node.cluster.id}]")
        lines.append(f"  Type: {node.cluster.type}")
        lines.append(f"  Zones: {', '.join(node.cluster.zones)}")
        lines.append(f"  Tier: {node.tier}")
        lines.append(f"  Layer: {node.layer}")
        lines.append(f"  Weight: {node.cluster.weight}")

        # Exits with fog_id
        exits = outgoing_edges.get(node.id, [])
        if exits:
            lines.append("  Exits:")
            for target_id, fog_id in exits:
                target_node = dag.nodes.get(target_id)
                target_name = target_node.cluster.id if target_node else target_id
                lines.append(f"    -> {target_name} via {fog_id}")

    # Write to file
    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
