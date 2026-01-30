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
    """Export a human-readable spoiler log.

    The spoiler log contains:
    - Header with seed
    - Totals (nodes, zones, paths)
    - Layers section (nodes grouped by layer)
    - Paths section (all paths with weights)

    Args:
        dag: The DAG to export
        output_path: Path to write the spoiler log
    """
    lines: list[str] = []

    # Header
    lines.append("=" * 60)
    lines.append("SPEEDFOG SPOILER LOG")
    lines.append("=" * 60)
    lines.append("")
    lines.append(f"Seed: {dag.seed}")
    lines.append("")

    # Totals
    lines.append("-" * 40)
    lines.append("TOTALS")
    lines.append("-" * 40)
    lines.append(f"Total nodes: {dag.total_nodes()}")
    lines.append(f"Total zones: {dag.total_zones()}")
    paths = dag.enumerate_paths()
    lines.append(f"Total paths: {len(paths)}")
    lines.append("")

    # Layers section
    lines.append("-" * 40)
    lines.append("LAYERS")
    lines.append("-" * 40)

    # Group nodes by layer
    nodes_by_layer: dict[int, list[str]] = {}
    for node_id, node in dag.nodes.items():
        layer = node.layer
        if layer not in nodes_by_layer:
            nodes_by_layer[layer] = []
        nodes_by_layer[layer].append(node_id)

    # Output nodes by layer in order
    for layer in sorted(nodes_by_layer.keys()):
        lines.append(f"\nLayer {layer}:")
        for node_id in sorted(nodes_by_layer[layer]):
            node = dag.nodes[node_id]
            lines.append(
                f"  [{node_id}] {node.cluster.id} "
                f"({node.cluster.type}, weight={node.cluster.weight}, tier={node.tier})"
            )
            lines.append(f"    Zones: {', '.join(node.cluster.zones)}")
            if node.entry_fog:
                lines.append(f"    Entry fog: {node.entry_fog}")
            if node.exit_fogs:
                lines.append(f"    Exit fogs: {', '.join(node.exit_fogs)}")

    lines.append("")

    # Paths section
    lines.append("-" * 40)
    lines.append("PATHS")
    lines.append("-" * 40)

    for i, path in enumerate(paths, 1):
        weight = dag.path_weight(path)
        path_str = " -> ".join(path)
        lines.append(f"\nPath {i} (weight={weight}):")
        lines.append(f"  {path_str}")

    lines.append("")
    lines.append("=" * 60)

    # Write to file
    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
