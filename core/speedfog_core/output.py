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
            "entry_fog": node.entry_fog,
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

    # Sort layers
    sorted_layers = sorted(nodes_by_layer.keys())

    # Fixed column width and name truncation for consistent alignment
    col_width = 24
    max_name_len = col_width - 2  # Leave some padding

    # Find max branches across all layers for consistent total width
    max_branches = max(len(nodes_by_layer[layer]) for layer in sorted_layers)
    total_width = col_width * max_branches

    # Build ASCII graph visualization
    for layer_idx, layer in enumerate(sorted_layers):
        node_ids = sorted(nodes_by_layer[layer])
        n_nodes = len(node_ids)

        # Draw connection lines from previous layer
        if layer_idx > 0:
            prev_layer = sorted_layers[layer_idx - 1]
            prev_count = len(nodes_by_layer[prev_layer])

            if prev_count < n_nodes:
                # Split: draw branching lines
                split_sym = (
                    "┌"
                    + "─" * (col_width // 2 - 1)
                    + "┴"
                    + "─" * (col_width // 2 - 1)
                    + "┐"
                )
                lines.append(split_sym.center(total_width))
            elif prev_count > n_nodes:
                # Merge: draw converging lines
                merge_sym = (
                    "└"
                    + "─" * (col_width // 2 - 1)
                    + "┬"
                    + "─" * (col_width // 2 - 1)
                    + "┘"
                )
                lines.append(merge_sym.center(total_width))
            else:
                # Continue: straight lines for each branch
                pipe_line = "".join("│".center(col_width) for _ in range(n_nodes))
                lines.append(pipe_line.center(total_width))

        # Draw cluster names (truncated)
        name_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            name = node.cluster.id[:max_name_len]
            name_parts.append(name.center(col_width))
        lines.append("".join(name_parts).center(total_width))

        # Draw cluster type
        type_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            type_str = f"[{node.cluster.type}]"
            type_parts.append(type_str.center(col_width))
        lines.append("".join(type_parts).center(total_width))

        # Draw weights and tier info
        info_parts = []
        for node_id in node_ids:
            node = dag.nodes[node_id]
            info = f"(w:{node.cluster.weight} t:{node.tier})"
            info_parts.append(info.center(col_width))
        lines.append("".join(info_parts).center(total_width))

        # Draw vertical lines to next layer (except for last layer)
        if layer_idx < len(sorted_layers) - 1:
            pipe_line = "".join("│".center(col_width) for _ in range(n_nodes))
            lines.append(pipe_line.center(total_width))

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
