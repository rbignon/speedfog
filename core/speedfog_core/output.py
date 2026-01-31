"""Output module for DAG export to JSON and spoiler logs.

This module provides functions to export the generated DAG to:
- JSON format for consumption by the C# writer
- Human-readable spoiler log for players
"""

from __future__ import annotations

import json
from collections.abc import Callable
from pathlib import Path
from typing import Any

from speedfog_core.dag import Dag


def dag_to_dict(dag: Dag) -> dict[str, Any]:
    """Convert a DAG to a JSON-serializable dictionary.

    Args:
        dag: The DAG to convert

    Returns:
        Dictionary with the following structure:
        - seed: int
        - total_layers: int
        - total_nodes: int
        - total_zones: int
        - total_paths: int
        - path_weights: list[int]
        - nodes: dict[str, node_dict]
        - edges: list[edge_dict]
        - start_id: str
        - end_id: str
    """
    # Build nodes dict
    nodes: dict[str, dict[str, Any]] = {}
    for node_id, node in dag.nodes.items():
        nodes[node_id] = {
            "cluster_id": node.cluster.id,
            "zones": node.cluster.zones,
            "type": node.cluster.type,
            "weight": node.cluster.weight,
            "layer": node.layer,
            "tier": node.tier,
            "entry_fogs": node.entry_fogs,
            "exit_fogs": node.exit_fogs,
        }

    # Build edges list
    edges: list[dict[str, str]] = []
    for edge in dag.edges:
        edges.append(
            {
                "source": edge.source_id,
                "target": edge.target_id,
                "fog_id": edge.fog_id,
            }
        )

    # Calculate path statistics
    paths = dag.enumerate_paths()
    path_weights = [dag.path_weight(path) for path in paths]

    # Calculate total layers (max layer + 1, or 0 if no nodes)
    if dag.nodes:
        total_layers = max(node.layer for node in dag.nodes.values()) + 1
    else:
        total_layers = 0

    return {
        "seed": dag.seed,
        "total_layers": total_layers,
        "total_nodes": dag.total_nodes(),
        "total_zones": dag.total_zones(),
        "total_paths": len(paths),
        "path_weights": path_weights,
        "nodes": nodes,
        "edges": edges,
        "start_id": dag.start_id,
        "end_id": dag.end_id,
    }


def export_json(dag: Dag, output_path: Path) -> None:
    """Export a DAG to a formatted JSON file.

    Args:
        dag: The DAG to export
        output_path: Path to write the JSON file
    """
    data = dag_to_dict(dag)
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

    # Write to file
    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
