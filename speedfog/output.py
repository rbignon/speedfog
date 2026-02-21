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

from speedfog.care_package import CarePackageItem
from speedfog.clusters import ClusterPool
from speedfog.dag import Dag, DagNode, FogRef


def load_vanilla_tiers(path: Path) -> dict[str, int]:
    """Load vanilla scaling tiers from foglocations2.txt EnemyAreas section.

    Parses the YAML-like file format to extract zone name → ScalingTier mapping.

    Args:
        path: Path to foglocations2.txt

    Returns:
        Dictionary of zone_name → scaling_tier (int)
    """
    tiers: dict[str, int] = {}
    if not path.exists():
        return tiers

    current_name: str | None = None
    in_enemy_areas = False

    with open(path, encoding="utf-8") as f:
        for line in f:
            stripped = line.strip()
            if stripped == "EnemyAreas:":
                in_enemy_areas = True
                continue
            if not in_enemy_areas:
                continue
            # A new top-level section (non-indented, non-list line with colon)
            if stripped and not line[0].isspace() and not line.startswith("-"):
                break
            if stripped.startswith("- Name:"):
                current_name = stripped.split(":", 1)[1].strip()
            elif stripped.startswith("ScalingTier:") and current_name is not None:
                tiers[current_name] = int(stripped.split(":", 1)[1].strip())
                current_name = None

    return tiers


def _effective_type(node: DagNode, dag: Dag) -> str:
    """Return the node's effective type, overriding to 'final_boss' for the end node."""
    if node.id == dag.end_id:
        return "final_boss"
    return node.cluster.type


# =============================================================================
# V4 Format for FogModWrapper, visualization, and racing zone tracking
# =============================================================================


def _get_fog_text_from_list(fogs: list[dict[str, str]], fog_ref: FogRef) -> str:
    """Get the human-readable text for a fog gate from a list of fog dicts.

    Prefers exact (fog_id, zone) match, then falls back to fog_id-only match.
    Prefers side_text (zone-specific description) over gate-level text.

    Args:
        fogs: List of fog dicts (entry_fogs or exit_fogs)
        fog_ref: The FogRef to find

    Returns:
        Text string, or fog_id itself as fallback
    """
    for fog in fogs:
        if fog["fog_id"] == fog_ref.fog_id and fog["zone"] == fog_ref.zone:
            return str(fog.get("side_text", fog.get("text", fog_ref.fog_id)))
    # Fallback: match just fog_id
    for fog in fogs:
        if fog["fog_id"] == fog_ref.fog_id:
            return str(fog.get("side_text", fog.get("text", fog_ref.fog_id)))
    return fog_ref.fog_id


def _get_fog_text(node: DagNode, fog_ref: FogRef) -> str:
    """Get the human-readable text for a fog gate from a node's exit_fogs."""
    return _get_fog_text_from_list(node.cluster.exit_fogs, fog_ref)


def _get_entry_fog_text(node: DagNode, fog_ref: FogRef) -> str:
    """Get the human-readable text for a fog gate from a node's entry_fogs."""
    return _get_fog_text_from_list(node.cluster.entry_fogs, fog_ref)


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
    is_entry: bool = False,
) -> str:
    """Convert a fog_id to FogMod FullName format: {map}_{fog_id}.

    Args:
        fog_id: The fog ID (e.g., "AEG099_001_9000" or "1035452610")
        zone: The zone the fog connects to
        clusters: ClusterPool with zone_maps
        fog_data: Optional fog_data.json lookup for map resolution
        is_entry: Whether this is an entrance gate (affects warp resolution)

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

        For warps at cross-map boundaries, the entity in fog_data may be on
        the wrong side for the operation. Entry gates need the external-side
        entity (FogMod From edge), exit gates need the internal-side entity
        (FogMod To edge). When the entity is on the wrong side, we look up
        the paired entity in the destination map.
    """
    # For warps (numeric IDs), fog_data has the authoritative map
    if fog_data and fog_id in fog_data and fog_id.isdigit():
        data = fog_data[fog_id]
        map_id = data.get("map")

        # For cross-map boundary warps, check if the entity is on the wrong
        # side. Entry needs external (zones[0] != zone), exit needs internal
        # (zones[0] == zone). If on wrong side, find the paired entity.
        if map_id:
            dest_map = data.get("destination_map")
            fog_zones = data.get("zones", [])
            # zones[0] is always the ASide zone — the zone where the entity
            # physically exists (per extract_fog_data.py FogEntry.zones).
            is_internal = fog_zones and fog_zones[0] == zone
            on_wrong_side = (is_entry and is_internal) or (
                not is_entry and not is_internal
            )
            if dest_map and dest_map != map_id and on_wrong_side:
                fog_zones_set = set(fog_zones)
                for key, fdata in fog_data.items():
                    if (
                        not key.startswith("m")
                        and fdata.get("map") == dest_map
                        and set(fdata.get("zones", [])) == fog_zones_set
                        and key != fog_id
                    ):
                        return f"{dest_map}_{key}"
                side = "entry" if is_entry else "exit"
                print(
                    f"Warning: No paired {side} entity for cross-map warp "
                    f"{fog_id} (dest_map={dest_map})"
                )

        if map_id:
            return f"{map_id}_{fog_id}"

    # Get zone's map
    zone_map = clusters.get_map(zone)

    if fog_data:
        # Strategy 1: Try fully-qualified name with zone's map
        # Verify the zone is actually in this fog entry's zones, since the same
        # fog_id (e.g., AEG099_002_9000) can exist on many different maps.
        if zone_map:
            fullname = f"{zone_map}_{fog_id}"
            if fullname in fog_data:
                entry_zones = fog_data[fullname].get("zones", [])
                if zone in entry_zones:
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
    care_package: list[CarePackageItem] | None = None,
    run_complete_message: str = "RUN COMPLETE",
    chapel_grace: bool = True,
    starting_larval_tears: int = 10,
    vanilla_tiers: dict[str, int] | None = None,
) -> dict[str, Any]:
    """Convert a DAG to v4 JSON-serializable dictionary.

    The v4 format extends v3 with event flag tracking for racing support:
    - `event_map`: mapping of flag_id (str) -> cluster_id for zone tracking
    - `finish_event`: flag_id for final boss death detection
    - Each connection includes a `flag_id` for its destination node

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
        care_package: List of CarePackageItem for randomized starting build
        run_complete_message: Text for the golden banner after final boss defeat
        chapel_grace: Whether to add a Site of Grace at Chapel of Anticipation
        starting_larval_tears: Larval Tears to give at start (for rebirth at graces)
        vanilla_tiers: Optional zone_name → ScalingTier mapping from foglocations2.txt.
            When provided, each node gets an original_tier field (max ScalingTier of its zones).

    Returns:
        Dictionary with the following structure:
        - version: "4.0"
        - seed: int
        - total_layers, total_nodes, total_zones, total_paths: metadata
        - options: dict of FogMod options
        - nodes: dict of cluster_id -> {type, display_name, zones, layer, tier, weight}
        - edges: list of {from, to}
        - connections: list of {exit_area, exit_gate, entrance_area, entrance_gate, flag_id}
        - area_tiers: dict of zone -> tier
        - event_map: dict of str(flag_id) -> cluster_id
        - finish_event: int flag_id for final boss node
        - starting_goods, starting_runes, etc.
    """
    if options is None:
        options = {
            "scale": True,
            "shuffle": True,
        }

    # Allocate event flag IDs per connection (for racing zone tracking).
    # Each connection gets a unique flag_id so the racing mod can detect re-entry
    # to the same cluster from a different branch (e.g., shared entrance merges).
    # Must land in a category pre-allocated by FogRando in VirtualMemoryFlag.
    # FogRando's category 1040292 uses offsets ~100-299; we use 800-999 (200 flags).
    EVENT_FLAG_BASE = 1040292800
    flag_counter = 0
    event_map: dict[str, str] = {}  # str(flag_id) -> cluster_id
    final_node_flag = 0  # first flag targeting the end node

    # Build connections list — zone info comes directly from FogRef
    connections: list[dict[str, str | int]] = []
    end_cluster_id = dag.nodes[dag.end_id].cluster.id
    for edge in dag.edges:
        source_node = dag.nodes.get(edge.source_id)
        target_node = dag.nodes.get(edge.target_id)

        if source_node is None or target_node is None:
            continue

        exit_zone = edge.exit_fog.zone
        entry_zone = edge.entry_fog.zone

        # Handle final boss edge case: empty entry_fog means use first zone of target
        if not entry_zone and not edge.entry_fog.fog_id:
            if target_node.cluster.zones:
                entry_zone = target_node.cluster.zones[0]

        # Skip if zones not found (shouldn't happen in valid DAG)
        if not exit_zone or not entry_zone:
            print(
                f"Warning: Could not find zones for edge {edge.source_id} -> "
                f"{edge.target_id}: exit_fog={edge.exit_fog}, entry_fog={edge.entry_fog}"
            )
            continue

        # Handle empty entry_fog by using exit_fog (for one-way connections)
        effective_entry_fog = (
            edge.entry_fog.fog_id if edge.entry_fog.fog_id else edge.exit_fog.fog_id
        )

        flag_id = EVENT_FLAG_BASE + flag_counter
        flag_counter += 1

        exit_gate_str = _make_fullname(
            edge.exit_fog.fog_id,
            exit_zone,
            clusters,
            fog_data,
            is_entry=False,
        )

        # Look up entity_id from fog_data for entity-based disambiguation
        # in ZoneTrackingInjector (resolves compound key collisions).
        exit_entity_id = 0
        if fog_data and exit_gate_str in fog_data:
            exit_entity_id = fog_data[exit_gate_str].get("entity_id", 0)

        conn_dict: dict[str, str | int | bool] = {
            "exit_area": exit_zone,
            "exit_gate": exit_gate_str,
            "entrance_area": entry_zone,
            "entrance_gate": _make_fullname(
                effective_entry_fog,
                entry_zone,
                clusters,
                fog_data,
                is_entry=True,
            ),
            "flag_id": flag_id,
            "exit_entity_id": exit_entity_id,
        }
        if target_node.cluster.allow_entry_as_exit:
            conn_dict["ignore_pair"] = True
        connections.append(conn_dict)

        cluster_id = target_node.cluster.id
        event_map[str(flag_id)] = cluster_id

        if cluster_id == end_cluster_id and final_node_flag == 0:
            final_node_flag = flag_id

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
        # Compute original_tier: max ScalingTier of the node's zones
        original_tier: int | None = None
        if vanilla_tiers:
            zone_tiers = [
                vanilla_tiers[z] for z in node.cluster.zones if z in vanilla_tiers
            ]
            if zone_tiers:
                original_tier = max(zone_tiers)

        nodes[node.cluster.id] = {
            "type": _effective_type(node, dag),
            "display_name": clusters.get_display_name(node.cluster),
            "zones": node.cluster.zones,
            "layer": node.layer,
            "tier": node.tier,
            "original_tier": original_tier,
            "weight": node.cluster.weight,
            "exits": [],
            "entrances": [],
        }

    # Populate exits from DAG edges
    for edge in dag.edges:
        source_node = dag.nodes.get(edge.source_id)
        target_node = dag.nodes.get(edge.target_id)
        if source_node is None or target_node is None:
            continue
        source_cluster_id = source_node.cluster.id
        target_cluster_id = target_node.cluster.id
        text = _get_fog_text(source_node, edge.exit_fog)
        from_zone = edge.exit_fog.zone
        exit_entry: dict[str, str] = {
            "fog_id": edge.exit_fog.fog_id,
            "text": text,
        }
        if from_zone:
            exit_entry["from"] = from_zone
            from_text = clusters.zone_names.get(from_zone)
            if from_text:
                exit_entry["from_text"] = from_text
        exit_entry["to"] = target_cluster_id
        nodes[source_cluster_id]["exits"].append(exit_entry)

    # Populate entrances from DAG edges (mirror of exits)
    for edge in dag.edges:
        source_node = dag.nodes.get(edge.source_id)
        target_node = dag.nodes.get(edge.target_id)
        if source_node is None or target_node is None:
            continue
        source_cluster_id = source_node.cluster.id
        target_cluster_id = target_node.cluster.id
        text = _get_entry_fog_text(target_node, edge.entry_fog)
        to_zone = edge.entry_fog.zone
        # Handle final boss edge case: empty entry_fog means use first zone of target
        if not to_zone and not edge.entry_fog.fog_id:
            if target_node.cluster.zones:
                to_zone = target_node.cluster.zones[0]
        entrance_entry: dict[str, str] = {
            "text": text,
            "from": source_cluster_id,
        }
        if to_zone:
            entrance_entry["to"] = to_zone
            to_text = clusters.zone_names.get(to_zone)
            if to_text:
                entrance_entry["to_text"] = to_text
        nodes[target_cluster_id]["entrances"].append(entrance_entry)

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

    # finish_event: a SEPARATE flag for final boss death detection.
    # Must not reuse a zone-tracking flag, otherwise traversing the fog gate
    # into the final zone would prematurely trigger "RUN COMPLETE".
    finish_event = EVENT_FLAG_BASE + flag_counter
    flag_counter += 1

    if flag_counter > 200:
        raise ValueError(
            f"Event flag budget exceeded: {flag_counter} flags allocated "
            f"(max 200 in range 1040292800-1040292999)"
        )

    # finish_boss_defeat_flag: the DefeatFlag for the final boss, propagated from
    # fog.txt through clusters.json. Used by C# as primary source for boss death
    # detection, with FogMod Graph extraction as fallback.
    end_node = dag.nodes[dag.end_id]
    finish_boss_defeat_flag = end_node.cluster.defeat_flag

    # Collect vanilla warp entities to remove from MSBs.
    # Unique exits (coffins, DLC warps) are one-way teleporters that FogMod marks
    # as "remove" but can't actually delete (name mismatch). We propagate entity IDs
    # so C# can remove them from the MSB, preventing vanilla warps from persisting.
    remove_entities: list[dict[str, str | int]] = []
    seen_entities: set[tuple[str, int]] = set()
    for node in dag.nodes.values():
        for fog in node.cluster.unique_exit_fogs:
            location = fog.get("location")
            if location is None:
                continue
            zone = fog["zone"]
            map_id = clusters.get_map(zone)
            if map_id is None:
                continue
            key = (map_id, location)
            if key not in seen_entities:
                seen_entities.add(key)
                remove_entities.append({"map": map_id, "entity_id": location})

    # Also remove vanilla entities for regular exits that have a location
    # but are not used in any connection (e.g., end node drops all exits,
    # or usable unique exits that weren't picked by the generator).
    used_exit_keys: set[tuple[str, str, str]] = set()
    for edge in dag.edges:
        source = dag.nodes.get(edge.source_id)
        if source:
            used_exit_keys.add((source.id, edge.exit_fog.fog_id, edge.exit_fog.zone))

    for node in dag.nodes.values():
        for fog in node.cluster.exit_fogs:
            location = fog.get("location")
            if location is None:
                continue
            if (node.id, fog["fog_id"], fog["zone"]) in used_exit_keys:
                continue  # Used as connection — FogMod handles redirection
            zone = fog["zone"]
            map_id = clusters.get_map(zone)
            if map_id is None:
                continue
            key = (map_id, location)
            if key not in seen_entities:
                seen_entities.add(key)
                remove_entities.append({"map": map_id, "entity_id": location})

    return {
        "version": "4.0",
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
        "event_map": event_map,
        "final_node_flag": final_node_flag,
        "finish_event": finish_event,
        "finish_boss_defeat_flag": finish_boss_defeat_flag,
        "run_complete_message": run_complete_message,
        "chapel_grace": chapel_grace,
        "starting_item_lots": starting_item_lots or [],
        "starting_goods": starting_goods or [],
        "starting_runes": starting_runes,
        "starting_golden_seeds": starting_golden_seeds,
        "starting_sacred_tears": starting_sacred_tears,
        "starting_larval_tears": starting_larval_tears,
        "care_package": [
            {"type": item.type, "id": item.id, "name": item.name}
            for item in (care_package or [])
        ],
        "remove_entities": remove_entities,
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
    care_package: list[CarePackageItem] | None = None,
    run_complete_message: str = "RUN COMPLETE",
    chapel_grace: bool = True,
    starting_larval_tears: int = 10,
    vanilla_tiers: dict[str, int] | None = None,
) -> None:
    """Export a DAG to v4 formatted JSON file.

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
        care_package: List of CarePackageItem for randomized starting build
        run_complete_message: Text for the golden banner after final boss defeat
        chapel_grace: Whether to add a Site of Grace at Chapel of Anticipation
        starting_larval_tears: Larval Tears to give at start (for rebirth at graces)
        vanilla_tiers: Optional zone_name → ScalingTier mapping from foglocations2.txt
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
        care_package,
        run_complete_message,
        chapel_grace,
        starting_larval_tears,
        vanilla_tiers,
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


def export_spoiler_log(
    dag: Dag,
    output_path: Path,
    care_package: list[CarePackageItem] | None = None,
) -> None:
    """Export human-readable spoiler log with ASCII graph visualization.

    Args:
        dag: The DAG to export
        output_path: Path to write the spoiler log
        care_package: Optional care package items to include in spoiler
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
            type_str = f"[{_effective_type(node, dag)}]"
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

    # Build edge lookup: source_id -> list of (target_id, exit_fog FogRef)
    outgoing_edges: dict[str, list[tuple[str, FogRef]]] = {}
    for edge in dag.edges:
        if edge.source_id not in outgoing_edges:
            outgoing_edges[edge.source_id] = []
        outgoing_edges[edge.source_id].append((edge.target_id, edge.exit_fog))

    # Print node details sorted by tier then by cluster ID
    sorted_nodes = sorted(dag.nodes.values(), key=lambda n: (n.tier, n.cluster.id))
    for node in sorted_nodes:
        lines.append("")
        lines.append(f"[{node.cluster.id}]")
        lines.append(f"  Type: {_effective_type(node, dag)}")
        lines.append(f"  Zones: {', '.join(node.cluster.zones)}")
        lines.append(f"  Tier: {node.tier}")
        lines.append(f"  Layer: {node.layer}")
        lines.append(f"  Weight: {node.cluster.weight}")

        # Exits with fog_id and text
        exits = outgoing_edges.get(node.id, [])
        if exits:
            lines.append("  Exits:")
            for target_id, fog_ref in exits:
                target_node = dag.nodes.get(target_id)
                target_name = target_node.cluster.id if target_node else target_id
                text = _get_fog_text(node, fog_ref)
                fog_display = fog_ref.fog_id
                if text and text != fog_display:
                    lines.append(f"    -> {target_name} via {fog_display} ({text})")
                else:
                    lines.append(f"    -> {target_name} via {fog_display}")

    # Care package section
    if care_package:
        lines.append("")
        lines.append("=" * 60)
        lines.append("CARE PACKAGE (starting build)")
        lines.append("=" * 60)
        type_names = {0: "Weapon", 1: "Armor", 2: "Talisman", 3: "Spell/Item"}
        for item in care_package:
            type_label = type_names.get(item.type, "Unknown")
            lines.append(f"  [{type_label}] {item.name} (id={item.id})")

    # Write to file
    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
