"""Output module for DAG export to JSON and spoiler logs.

This module provides functions to export the generated DAG to:
- JSON format for consumption by the C# writer
- Human-readable spoiler log for players
"""

from __future__ import annotations

import json
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

    Uses actual DAG edges to determine which nodes connect.

    Args:
        dag: The DAG with edges
        prev_node_ids: Sorted node IDs in the previous layer
        curr_node_ids: Sorted node IDs in the current layer
        col_width: Width of each column
        total_width: Total width of the output

    Returns:
        List of strings representing the connection lines
    """
    # Build connection map: for each current node, which previous nodes connect to it
    connections: dict[int, list[int]] = {i: [] for i in range(len(curr_node_ids))}

    for edge in dag.edges:
        if edge.source_id in prev_node_ids and edge.target_id in curr_node_ids:
            src_idx = prev_node_ids.index(edge.source_id)
            tgt_idx = curr_node_ids.index(edge.target_id)
            if src_idx not in connections[tgt_idx]:
                connections[tgt_idx].append(src_idx)

    # Sort connections for each target
    for tgt_idx in connections:
        connections[tgt_idx].sort()

    # Build the connection lines
    lines: list[str] = []
    n_prev = len(prev_node_ids)
    n_curr = len(curr_node_ids)

    # Calculate center positions for each column
    def col_center(idx: int, n_cols: int) -> int:
        """Get the center position of a column."""
        cols_width = col_width * n_cols
        offset = (total_width - cols_width) // 2
        return offset + idx * col_width + col_width // 2

    # Draw vertical lines from previous layer
    line1_chars = [" "] * total_width
    for i in range(n_prev):
        pos = col_center(i, n_prev)
        if 0 <= pos < total_width:
            line1_chars[pos] = "│"
    lines.append("".join(line1_chars))

    # Determine connection pattern and draw appropriate symbols
    # Check if this is a pure split, merge, or mixed
    sources_used: set[int] = set()
    for src_indices in connections.values():
        sources_used.update(src_indices)

    # Draw horizontal connection line if needed
    if n_prev != n_curr or any(len(srcs) > 1 for srcs in connections.values()):
        # Need horizontal lines for splits/merges
        line2_chars = [" "] * total_width

        # Find the range of horizontal connections needed
        all_positions: list[int] = []
        for i in range(n_prev):
            all_positions.append(col_center(i, n_prev))
        for i in range(n_curr):
            all_positions.append(col_center(i, n_curr))

        min_pos = min(all_positions) if all_positions else 0
        max_pos = max(all_positions) if all_positions else 0

        # Draw horizontal line spanning all connections
        for pos in range(min_pos, max_pos + 1):
            line2_chars[pos] = "─"

        # Place junction characters at source positions
        for i in range(n_prev):
            pos = col_center(i, n_prev)
            if 0 <= pos < total_width:
                # Check if this source has multiple targets or is part of a merge
                targets_from_src = sum(
                    1 for tgt, srcs in connections.items() if i in srcs
                )
                if targets_from_src > 0:
                    if pos == min_pos:
                        line2_chars[pos] = "└"
                    elif pos == max_pos:
                        line2_chars[pos] = "┘"
                    else:
                        line2_chars[pos] = "┴"

        # Place junction characters at target positions
        for i in range(n_curr):
            pos = col_center(i, n_curr)
            if 0 <= pos < total_width:
                num_sources = len(connections[i])
                if num_sources > 0:
                    if pos == min_pos:
                        line2_chars[pos] = "┌"
                    elif pos == max_pos:
                        line2_chars[pos] = "┐"
                    else:
                        line2_chars[pos] = "┬"

        lines.append("".join(line2_chars))

    # Draw vertical lines to current layer
    line3_chars = [" "] * total_width
    for i in range(n_curr):
        pos = col_center(i, n_curr)
        if 0 <= pos < total_width:
            line3_chars[pos] = "│"
    lines.append("".join(line3_chars))

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

    # Sort layers and node IDs within each layer
    sorted_layers = sorted(nodes_by_layer.keys())
    for layer in sorted_layers:
        nodes_by_layer[layer] = sorted(nodes_by_layer[layer])

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
