#!/usr/bin/env python3
"""Simulate the "cluster-first" approach to DAG generation.

Instead of: decide operation → find compatible cluster
Do: pick random cluster → determine what operation it supports → apply

Compare DAG structure and zone distribution with current approach.
"""

from __future__ import annotations

import random
import sys
from collections import Counter, defaultdict
from pathlib import Path

project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from speedfog.clusters import ClusterData, ClusterPool, load_clusters  # noqa: E402
from speedfog.config import Config, resolve_final_boss_candidates  # noqa: E402
from speedfog.dag import Branch, Dag, DagNode, FogRef  # noqa: E402
from speedfog.generator import (  # noqa: E402
    GenerationError,
    _find_valid_merge_indices,
    _has_valid_merge_pair,
    _pick_entry_and_exits_for_node,
    can_be_merge_node,
    can_be_passant_node,
    can_be_split_node,
    compute_net_exits,
    execute_forced_merge,
    generate_dag,
    pick_cluster,
    pick_cluster_with_filter,
    select_entries_for_merge,
)
from speedfog.planner import compute_tier, plan_layer_types  # noqa: E402


def load_racing_standard_config() -> Config:
    return Config.from_dict(
        {
            "run": {"seed": 0},
            "budget": {"tolerance": 5},
            "requirements": {
                "legacy_dungeons": 1,
                "bosses": 10,
                "mini_dungeons": 5,
                "major_bosses": 8,
            },
            "structure": {
                "max_parallel_paths": 3,
                "min_layers": 25,
                "max_layers": 30,
                "final_tier": 20,
                "split_probability": 0.5,
                "merge_probability": 0.5,
                "max_branches": 3,
                "first_layer_type": "legacy_dungeon",
                "final_boss_candidates": ["all"],
            },
        }
    )


def pick_cluster_uniform(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
) -> ClusterData | None:
    """Pick a random cluster with no filter other than zone overlap."""
    available = [c for c in candidates if not any(z in used_zones for z in c.zones)]
    if not available:
        return None
    return rng.choice(available)


def generate_dag_cluster_first(
    config: Config,
    clusters: ClusterPool,
    seed: int,
    split_bias: float = 1.0,
    merge_bias: float = 1.0,
) -> Dag:
    """Generate DAG with cluster-first selection.

    For each layer/branch:
    1. Pick a random cluster uniformly from available pool
    2. Determine what operations it supports
    3. Choose operation based on capabilities + current state

    split_bias/merge_bias: multiplier on the probability of choosing
    split/merge when available. Higher = more likely to branch.
    """
    rng = random.Random(seed)
    dag = Dag(seed=seed)
    used_zones: set[str] = set()

    # Start node (same as original)
    start_candidates = clusters.get_by_type("start")
    if not start_candidates:
        raise GenerationError("No start cluster found")
    start_cluster = pick_cluster(start_candidates, used_zones, rng, require_exits=False)
    if start_cluster is None:
        raise GenerationError("Could not pick start cluster")

    start_node = DagNode(
        id="start",
        cluster=start_cluster,
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef(f["fog_id"], f["zone"]) for f in start_cluster.exit_fogs],
    )
    dag.add_node(start_node)
    dag.start_id = "start"
    used_zones.update(start_cluster.zones)

    start_exits = start_node.exit_fogs
    num_initial = min(
        len(start_exits),
        config.structure.max_parallel_paths,
        config.structure.max_exits,
    )
    if num_initial == 0:
        raise GenerationError("Start cluster has no exits")
    rng.shuffle(start_exits)
    branches = [Branch(f"b{i}", "start", start_exits[i]) for i in range(num_initial)]

    # First layer if forced type
    current_layer = 1
    if config.structure.first_layer_type:
        first_type = config.structure.first_layer_type
        tier = compute_tier(current_layer, 10, config.structure.final_tier)
        # Use cluster-first for first layer too
        candidates = clusters.get_by_type(first_type)
        new_branches = []
        for i, branch in enumerate(branches):
            cluster = pick_cluster_uniform(candidates, used_zones, rng)
            if cluster is None:
                raise GenerationError(f"No cluster for first layer branch {i}")
            if not can_be_passant_node(cluster):
                raise GenerationError(
                    f"First layer cluster {cluster.id} can't be passant"
                )
            used_zones.update(cluster.zones)
            entry_fog, exit_fogs = _pick_entry_and_exits_for_node(cluster, 1, rng)
            node_id = f"node_{current_layer}_{chr(97 + i)}"
            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=current_layer,
                tier=tier,
                entry_fogs=[entry_fog],
                exit_fogs=exit_fogs,
            )
            dag.add_node(node)
            dag.add_edge(
                branch.current_node_id, node_id, branch.available_exit, entry_fog
            )
            new_branches.append(Branch(branch.id, node_id, rng.choice(exit_fogs)))
        branches = new_branches
        current_layer += 1

    # Plan layer types
    num_layers = rng.randint(config.structure.min_layers, config.structure.max_layers)
    if config.structure.first_layer_type:
        num_layers = max(1, num_layers - 1)
    layer_types = plan_layer_types(config.requirements, num_layers, rng)

    first_layer_offset = 1 if config.structure.first_layer_type else 0
    estimated_total = len(layer_types) + 2 + first_layer_offset

    # Execute layers with cluster-first logic
    for layer_idx, layer_type in enumerate(layer_types):
        is_near_end = layer_idx >= len(layer_types) - 2
        tier = compute_tier(current_layer, estimated_total, config.structure.final_tier)

        # Force merge near end
        if is_near_end and len(branches) > 1:
            branches, current_layer = execute_forced_merge(
                dag,
                branches,
                current_layer,
                tier,
                layer_type,
                clusters,
                used_zones,
                rng,
                config,
            )
            continue

        candidates = clusters.get_by_type(layer_type)
        max_paths = config.structure.max_parallel_paths
        max_ex = config.structure.max_exits
        max_en = config.structure.max_entrances

        # === CLUSTER-FIRST LOGIC ===
        # For each branch, pick a cluster first, then decide operation

        if len(branches) == 1:
            # Single branch: pick cluster, check if it can split
            branch = branches[0]
            cluster = pick_cluster_uniform(candidates, used_zones, rng)
            if cluster is None:
                raise GenerationError(
                    f"No cluster for layer {current_layer} (type: {layer_type})"
                )

            # Check what this cluster can do
            room = max_paths - len(branches) + 1
            max_fan = min(max_ex, room)

            can_split = False
            actual_fan = 2
            if max_fan >= 2:
                for n in range(max_fan, 1, -1):
                    if can_be_split_node(cluster, n):
                        can_split = True
                        actual_fan = n
                        break

            # Decide: split (if possible) or passant
            do_split = False
            if can_split:
                # Probability of splitting when cluster allows it
                do_split = (
                    rng.random() < config.structure.split_probability * split_bias
                )

            if do_split:
                used_zones.update(cluster.zones)
                entry_fog, exit_fogs = _pick_entry_and_exits_for_node(
                    cluster, actual_fan, rng
                )
                node_id = f"node_{current_layer}_a"
                node = DagNode(
                    id=node_id,
                    cluster=cluster,
                    layer=current_layer,
                    tier=tier,
                    entry_fogs=[entry_fog],
                    exit_fogs=exit_fogs,
                )
                dag.add_node(node)
                dag.add_edge(
                    branch.current_node_id, node_id, branch.available_exit, entry_fog
                )
                branches = [
                    Branch(f"{branch.id}_{chr(97+j)}", node_id, exit_fogs[j])
                    for j in range(actual_fan)
                ]
            else:
                # Passant
                if not can_be_passant_node(cluster):
                    # This cluster can't even be passant — need to re-pick
                    cluster = pick_cluster_with_filter(
                        candidates, used_zones, rng, can_be_passant_node
                    )
                    if cluster is None:
                        raise GenerationError(
                            f"No passant cluster for layer {current_layer}"
                        )
                used_zones.update(cluster.zones)
                entry_fog, exit_fogs = _pick_entry_and_exits_for_node(cluster, 1, rng)
                node_id = f"node_{current_layer}_a"
                node = DagNode(
                    id=node_id,
                    cluster=cluster,
                    layer=current_layer,
                    tier=tier,
                    entry_fogs=[entry_fog],
                    exit_fogs=exit_fogs,
                )
                dag.add_node(node)
                dag.add_edge(
                    branch.current_node_id, node_id, branch.available_exit, entry_fog
                )
                branches = [Branch(branch.id, node_id, rng.choice(exit_fogs))]

        elif len(branches) >= max_paths:
            # At max: must merge or passant
            # Pick cluster for potential merge first
            cluster = pick_cluster_uniform(candidates, used_zones, rng)
            if cluster is None:
                raise GenerationError(f"No cluster for layer {current_layer}")

            can_merge = can_be_merge_node(cluster, 2) and _has_valid_merge_pair(
                branches
            )
            do_merge = (
                can_merge
                and rng.random() < config.structure.merge_probability * merge_bias
            )

            if do_merge:
                # Find how many branches to merge
                max_merge = max(min(max_en, len(branches)), 2)
                actual_merge = 2
                for n in range(max_merge, 1, -1):
                    if can_be_merge_node(cluster, n):
                        indices = _find_valid_merge_indices(branches, rng, n)
                        if indices is not None:
                            actual_merge = n
                            break

                indices = _find_valid_merge_indices(branches, rng, actual_merge)
                if indices is None:
                    # Fallback to passant
                    do_merge = False

            if do_merge:
                used_zones.update(cluster.zones)
                merge_branches_list = [branches[i] for i in indices]

                if cluster.allow_shared_entrance:
                    entries = select_entries_for_merge(cluster, 1, rng)
                    shared_entry = FogRef(entries[0]["fog_id"], entries[0]["zone"])
                    exits = compute_net_exits(cluster, entries)
                    rng.shuffle(exits)
                    exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]

                    node_id = f"node_{current_layer}_a"
                    node = DagNode(
                        id=node_id,
                        cluster=cluster,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[shared_entry],
                        exit_fogs=exit_fogs,
                    )
                    dag.add_node(node)
                    for b in merge_branches_list:
                        dag.add_edge(
                            b.current_node_id, node_id, b.available_exit, shared_entry
                        )
                else:
                    entries = select_entries_for_merge(cluster, actual_merge, rng)
                    entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
                    exits = compute_net_exits(cluster, entries)
                    rng.shuffle(exits)
                    exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]

                    node_id = f"node_{current_layer}_a"
                    node = DagNode(
                        id=node_id,
                        cluster=cluster,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=entry_fogs_list,
                        exit_fogs=exit_fogs,
                    )
                    dag.add_node(node)
                    for b, ef in zip(
                        merge_branches_list, entry_fogs_list, strict=False
                    ):
                        dag.add_edge(b.current_node_id, node_id, b.available_exit, ef)

                new_branches = [
                    Branch(f"merged_{current_layer}", node_id, rng.choice(exit_fogs))
                ]

                # Handle remaining branches as passant
                merge_set = set(indices)
                letter = 1
                for i, branch in enumerate(branches):
                    if i in merge_set:
                        continue
                    pc = pick_cluster_uniform(candidates, used_zones, rng)
                    if pc is None or not can_be_passant_node(pc):
                        pc = pick_cluster_with_filter(
                            candidates, used_zones, rng, can_be_passant_node
                        )
                    if pc is None:
                        raise GenerationError(
                            "No passant cluster for non-merged branch"
                        )
                    used_zones.update(pc.zones)
                    ef, exf = _pick_entry_and_exits_for_node(pc, 1, rng)
                    nid = f"node_{current_layer}_{chr(97 + letter)}"
                    n = DagNode(
                        id=nid,
                        cluster=pc,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[ef],
                        exit_fogs=exf,
                    )
                    dag.add_node(n)
                    dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                    new_branches.append(Branch(branch.id, nid, rng.choice(exf)))
                    letter += 1

                branches = new_branches
            else:
                # Passant all branches
                new_branches = []
                first_cluster = cluster  # reuse the one we already picked
                for i, branch in enumerate(branches):
                    if i == 0:
                        c = first_cluster
                        if not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                    else:
                        c = pick_cluster_uniform(candidates, used_zones, rng)
                        if c is None or not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                    used_zones.update(c.zones)
                    ef, exf = _pick_entry_and_exits_for_node(c, 1, rng)
                    nid = f"node_{current_layer}_{chr(97 + i)}"
                    n = DagNode(
                        id=nid,
                        cluster=c,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[ef],
                        exit_fogs=exf,
                    )
                    dag.add_node(n)
                    dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                    new_branches.append(Branch(branch.id, nid, rng.choice(exf)))
                branches = new_branches
        else:
            # 1 < branches < max_paths: can split, merge, or passant
            # Pick a cluster for the "interesting" branch (first one)
            cluster = pick_cluster_uniform(candidates, used_zones, rng)
            if cluster is None:
                raise GenerationError(f"No cluster for layer {current_layer}")

            room = max_paths - len(branches) + 1
            max_fan = min(max_ex, room)

            can_split_here = False
            actual_fan = 2
            if max_fan >= 2:
                for n in range(max_fan, 1, -1):
                    if can_be_split_node(cluster, n):
                        can_split_here = True
                        actual_fan = n
                        break

            can_merge_here = can_be_merge_node(cluster, 2) and _has_valid_merge_pair(
                branches
            )

            # Decide operation based on what the cluster supports
            roll = rng.random()
            sp = config.structure.split_probability * split_bias
            mp = config.structure.merge_probability * merge_bias

            operation = "passant"
            if can_split_here and can_merge_here:
                if roll < sp:
                    operation = "split"
                elif roll < sp + mp:
                    operation = "merge"
            elif can_split_here:
                if roll < sp:
                    operation = "split"
            elif can_merge_here:
                if roll < mp:
                    operation = "merge"

            if operation == "split":
                split_idx = rng.randrange(len(branches))
                new_branches = []
                letter = 0
                for i, branch in enumerate(branches):
                    if i == split_idx:
                        used_zones.update(cluster.zones)
                        ef, exf = _pick_entry_and_exits_for_node(
                            cluster, actual_fan, rng
                        )
                        nid = f"node_{current_layer}_{chr(97 + letter)}"
                        n = DagNode(
                            id=nid,
                            cluster=cluster,
                            layer=current_layer,
                            tier=tier,
                            entry_fogs=[ef],
                            exit_fogs=exf,
                        )
                        dag.add_node(n)
                        dag.add_edge(
                            branch.current_node_id, nid, branch.available_exit, ef
                        )
                        for j in range(actual_fan):
                            new_branches.append(
                                Branch(f"{branch.id}_{chr(97+j)}", nid, exf[j])
                            )
                        letter += 1
                    else:
                        c = pick_cluster_uniform(candidates, used_zones, rng)
                        if c is None or not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                        used_zones.update(c.zones)
                        ef, exf = _pick_entry_and_exits_for_node(c, 1, rng)
                        nid = f"node_{current_layer}_{chr(97 + letter)}"
                        n = DagNode(
                            id=nid,
                            cluster=c,
                            layer=current_layer,
                            tier=tier,
                            entry_fogs=[ef],
                            exit_fogs=exf,
                        )
                        dag.add_node(n)
                        dag.add_edge(
                            branch.current_node_id, nid, branch.available_exit, ef
                        )
                        new_branches.append(Branch(branch.id, nid, rng.choice(exf)))
                        letter += 1
                branches = new_branches

            elif operation == "merge":
                max_merge = max(min(max_en, len(branches)), 2)
                actual_m = 2
                indices = None
                for n in range(max_merge, 1, -1):
                    if can_be_merge_node(cluster, n):
                        idx = _find_valid_merge_indices(branches, rng, n)
                        if idx is not None:
                            actual_m = n
                            indices = idx
                            break
                if indices is None:
                    indices = _find_valid_merge_indices(branches, rng, 2)

                if indices is None:
                    # Fallback to passant
                    operation = "passant"
                else:
                    used_zones.update(cluster.zones)
                    merge_branches_list = [branches[i] for i in indices]

                    if cluster.allow_shared_entrance:
                        entries = select_entries_for_merge(cluster, 1, rng)
                        shared_entry = FogRef(entries[0]["fog_id"], entries[0]["zone"])
                        exits_remaining = compute_net_exits(cluster, entries)
                        rng.shuffle(exits_remaining)
                        exit_fogs = [
                            FogRef(f["fog_id"], f["zone"]) for f in exits_remaining[:1]
                        ]
                        nid = f"node_{current_layer}_a"
                        n = DagNode(
                            id=nid,
                            cluster=cluster,
                            layer=current_layer,
                            tier=tier,
                            entry_fogs=[shared_entry],
                            exit_fogs=exit_fogs,
                        )
                        dag.add_node(n)
                        for b in merge_branches_list:
                            dag.add_edge(
                                b.current_node_id, nid, b.available_exit, shared_entry
                            )
                    else:
                        entries = select_entries_for_merge(cluster, actual_m, rng)
                        efl = [FogRef(e["fog_id"], e["zone"]) for e in entries]
                        exits_remaining = compute_net_exits(cluster, entries)
                        rng.shuffle(exits_remaining)
                        exit_fogs = [
                            FogRef(f["fog_id"], f["zone"]) for f in exits_remaining[:1]
                        ]
                        nid = f"node_{current_layer}_a"
                        n = DagNode(
                            id=nid,
                            cluster=cluster,
                            layer=current_layer,
                            tier=tier,
                            entry_fogs=efl,
                            exit_fogs=exit_fogs,
                        )
                        dag.add_node(n)
                        for b, ef in zip(merge_branches_list, efl, strict=False):
                            dag.add_edge(b.current_node_id, nid, b.available_exit, ef)

                    new_branches = [
                        Branch(f"merged_{current_layer}", nid, rng.choice(exit_fogs))
                    ]
                    merge_set = set(indices)
                    letter = 1
                    for i, branch in enumerate(branches):
                        if i in merge_set:
                            continue
                        c = pick_cluster_uniform(candidates, used_zones, rng)
                        if c is None or not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                        used_zones.update(c.zones)
                        ef2, exf2 = _pick_entry_and_exits_for_node(c, 1, rng)
                        nid2 = f"node_{current_layer}_{chr(97 + letter)}"
                        n2 = DagNode(
                            id=nid2,
                            cluster=c,
                            layer=current_layer,
                            tier=tier,
                            entry_fogs=[ef2],
                            exit_fogs=exf2,
                        )
                        dag.add_node(n2)
                        dag.add_edge(
                            branch.current_node_id, nid2, branch.available_exit, ef2
                        )
                        new_branches.append(Branch(branch.id, nid2, rng.choice(exf2)))
                        letter += 1
                    branches = new_branches

            if operation == "passant":
                new_branches = []
                first = True
                for i, branch in enumerate(branches):
                    if first:
                        c = cluster
                        first = False
                        if not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                    else:
                        c = pick_cluster_uniform(candidates, used_zones, rng)
                        if c is None or not can_be_passant_node(c):
                            c = pick_cluster_with_filter(
                                candidates, used_zones, rng, can_be_passant_node
                            )
                            if c is None:
                                raise GenerationError("No passant cluster")
                    used_zones.update(c.zones)
                    ef, exf = _pick_entry_and_exits_for_node(c, 1, rng)
                    nid = f"node_{current_layer}_{chr(97 + i)}"
                    n = DagNode(
                        id=nid,
                        cluster=c,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[ef],
                        exit_fogs=exf,
                    )
                    dag.add_node(n)
                    dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                    new_branches.append(Branch(branch.id, nid, rng.choice(exf)))
                branches = new_branches

        current_layer += 1

    # Final merge
    if len(branches) > 1:
        last_type = layer_types[-1] if layer_types else "mini_dungeon"
        tier = compute_tier(current_layer, estimated_total, config.structure.final_tier)
        branches, current_layer = execute_forced_merge(
            dag,
            branches,
            current_layer,
            tier,
            last_type,
            clusters,
            used_zones,
            rng,
            config,
        )

    # End node
    all_boss = clusters.get_by_type("major_boss") + clusters.get_by_type("final_boss")
    all_boss_zones = {z for c in all_boss for z in c.zones}
    final_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    final_candidates = list(final_candidates)
    rng.shuffle(final_candidates)

    end_cluster = None
    for zone_name in final_candidates:
        for c in all_boss:
            if zone_name in c.zones and not any(z in used_zones for z in c.zones):
                end_cluster = c
                break
        if end_cluster:
            break

    if end_cluster is None:
        raise GenerationError("No available final boss")

    entry_fog_end = None
    if end_cluster.entry_fogs:
        main_e = [e for e in end_cluster.entry_fogs if e.get("main")]
        chosen = rng.choice(main_e) if main_e else rng.choice(end_cluster.entry_fogs)
        entry_fog_end = FogRef(chosen["fog_id"], chosen["zone"])

    end_node = DagNode(
        id="end",
        cluster=end_cluster,
        layer=current_layer,
        tier=config.structure.final_tier,
        entry_fogs=[entry_fog_end] if entry_fog_end else [],
        exit_fogs=[],
    )
    dag.add_node(end_node)
    dag.end_id = "end"

    branch = branches[0]
    dag.add_edge(
        branch.current_node_id,
        end_node.id,
        branch.available_exit,
        entry_fog_end or FogRef("", ""),
    )

    return dag


def analyze_dags(
    label: str,
    dags: list[Dag],
    total_attempts: int,
    successful: int,
    clusters: ClusterPool,
) -> dict[str, Counter]:
    """Analyze a set of generated DAGs."""
    cluster_counts: Counter[str] = Counter()
    type_counts: Counter[str] = Counter()
    path_counts: list[int] = []
    node_counts: list[int] = []
    max_branch_counts: list[int] = []

    for dag in dags:
        paths = dag.enumerate_paths()
        path_counts.append(len(paths))
        node_counts.append(len(dag.nodes))

        # Track max parallel branches (max nodes at same layer)
        layer_nodes: dict[int, int] = defaultdict(int)
        for node in dag.nodes.values():
            layer_nodes[node.layer] += 1
            cluster_counts[node.cluster.id] += 1
            type_counts[node.cluster.type] += 1
        max_branch_counts.append(max(layer_nodes.values()) if layer_nodes else 1)

    failed = total_attempts - successful
    print(f"\n{'='*80}")
    print(f"  {label}")
    print(f"{'='*80}")
    print(
        f"  Success: {successful}/{total_attempts} ({failed} failed, {failed/total_attempts*100:.1f}%)"
    )

    if dags:
        avg_paths = sum(path_counts) / len(path_counts)
        avg_nodes = sum(node_counts) / len(node_counts)
        avg_branches = sum(max_branch_counts) / len(max_branch_counts)
        linear = sum(1 for p in path_counts if p == 1)
        print(
            f"  Avg paths: {avg_paths:.1f}  (linear: {linear}/{len(dags)} = {linear/len(dags)*100:.0f}%)"
        )
        print(f"  Avg nodes: {avg_nodes:.1f}")
        print(f"  Avg max branches: {avg_branches:.1f}")
        print(
            f"  Path distribution: 1={sum(1 for p in path_counts if p==1)}, "
            f"2={sum(1 for p in path_counts if p==2)}, "
            f"3+={sum(1 for p in path_counts if p>=3)}"
        )

    # Distribution metrics per type
    for ctype in ["mini_dungeon", "boss_arena", "major_boss", "legacy_dungeon"]:
        type_clusters = clusters.get_by_type(ctype)
        pool_size = len(type_clusters)
        if pool_size == 0:
            continue

        counts = [cluster_counts.get(c.id, 0) for c in type_clusters]
        counts.sort()
        total = sum(counts)
        mean = total / pool_size if pool_size else 0
        appeared = sum(1 for c in counts if c > 0)
        mx = max(counts) if counts else 0
        mn = min(counts) if counts else 0

        # Gini
        if mean > 0 and pool_size > 1:
            n = len(counts)
            num = sum(abs(counts[i] - counts[j]) for i in range(n) for j in range(n))
            gini = num / (2 * n * n * mean)
        else:
            gini = 0

        # CV
        if mean > 0:
            var = sum((c - mean) ** 2 for c in counts) / len(counts)
            cv = var**0.5 / mean
        else:
            cv = 0

        ratio = mx / max(mn, 1)
        print(f"\n  {ctype} (pool={pool_size}, appeared={appeared}):")
        print(
            f"    Gini={gini:.3f}  CV={cv:.3f}  Max/Min={ratio:.1f}x  "
            f"Max={mx}  Min={mn}  Avg={mean:.0f}"
        )

        # Show top 5 and bottom 5
        ranked = [
            (c.id, cluster_counts.get(c.id, 0), clusters.get_display_name(c))
            for c in type_clusters
        ]
        ranked.sort(key=lambda x: x[1], reverse=True)
        print("    Top 5:    ", end="")
        for _, cnt, name in ranked[:5]:
            pct = cnt / max(successful, 1) * 100
            print(f"{name[:25]}={pct:.0f}%  ", end="")
        print()
        print("    Bottom 5: ", end="")
        for _, cnt, name in ranked[-5:]:
            pct = cnt / max(successful, 1) * 100
            print(f"{name[:25]}={pct:.0f}%  ", end="")
        print()

    return cluster_counts


def main():
    config = load_racing_standard_config()
    clusters_path = project_root / "data" / "clusters.json"
    clusters_orig = load_clusters(clusters_path)

    if config.structure.max_exits > 1 and config.structure.max_parallel_paths > 1:
        clusters_orig.merge_roundtable_into_start()

    all_boss = clusters_orig.get_by_type("major_boss") + clusters_orig.get_by_type(
        "final_boss"
    )
    all_boss_zones = {z for c in all_boss for z in c.zones}
    config.structure.final_boss_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )

    num_seeds = 3000
    base_rng = random.Random(42)
    seeds = [base_rng.randint(1, 999_999_999) for _ in range(num_seeds)]

    # --- Current approach ---
    print("Running CURRENT approach...")
    current_dags = []
    current_ok = 0
    for seed in seeds:
        try:
            dag = generate_dag(config, clusters_orig, seed)
            current_dags.append(dag)
            current_ok += 1
        except GenerationError:
            pass

    # --- Cluster-first approach (standard bias) ---
    print("Running CLUSTER-FIRST approach (bias=1.0)...")
    cf_dags = []
    cf_ok = 0
    for seed in seeds:
        # Reload clusters fresh each time (merge_roundtable mutates)
        clusters2 = load_clusters(clusters_path)
        clusters2.merge_roundtable_into_start()
        try:
            dag = generate_dag_cluster_first(
                config, clusters2, seed, split_bias=1.0, merge_bias=1.0
            )
            cf_dags.append(dag)
            cf_ok += 1
        except GenerationError:
            pass

    # --- Cluster-first with higher split/merge bias ---
    print("Running CLUSTER-FIRST approach (bias=2.0)...")
    cf2_dags = []
    cf2_ok = 0
    for seed in seeds:
        clusters3 = load_clusters(clusters_path)
        clusters3.merge_roundtable_into_start()
        try:
            dag = generate_dag_cluster_first(
                config, clusters3, seed, split_bias=2.0, merge_bias=2.0
            )
            cf2_dags.append(dag)
            cf2_ok += 1
        except GenerationError:
            pass

    # --- Cluster-first with even higher bias ---
    print("Running CLUSTER-FIRST approach (bias=3.0)...")
    cf3_dags = []
    cf3_ok = 0
    for seed in seeds:
        clusters4 = load_clusters(clusters_path)
        clusters4.merge_roundtable_into_start()
        try:
            dag = generate_dag_cluster_first(
                config, clusters4, seed, split_bias=3.0, merge_bias=3.0
            )
            cf3_dags.append(dag)
            cf3_ok += 1
        except GenerationError:
            pass

    # --- Analysis ---
    analyze_dags("CURRENT APPROACH", current_dags, num_seeds, current_ok, clusters_orig)
    analyze_dags("CLUSTER-FIRST (bias=1.0)", cf_dags, num_seeds, cf_ok, clusters_orig)
    analyze_dags("CLUSTER-FIRST (bias=2.0)", cf2_dags, num_seeds, cf2_ok, clusters_orig)
    analyze_dags("CLUSTER-FIRST (bias=3.0)", cf3_dags, num_seeds, cf3_ok, clusters_orig)


if __name__ == "__main__":
    main()
