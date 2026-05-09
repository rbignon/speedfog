"""Integration tests for generator_v2."""

from __future__ import annotations

import random

from speedfog.clusters import ClusterData, ClusterPool


def _mk_cluster(cid: str, ctype: str, weight: int = 10) -> ClusterData:
    return ClusterData(
        id=cid,
        zones=[cid],
        type=ctype,
        weight=weight,
        entry_fogs=[{"fog_id": "E", "zone": cid}],
        exit_fogs=[{"fog_id": "X", "zone": cid}],
    )


def test_pick_layer_clusters_returns_requested_type_when_available():
    from speedfog.generator_v2 import pick_layer_clusters

    pool = ClusterPool()
    for i in range(5):
        pool.add(_mk_cluster(f"md_{i}", "mini_dungeon", weight=10))
    rng = random.Random(0)
    picked, fallbacks = pick_layer_clusters(
        width=3,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=rng,
    )
    assert len(picked) == 3
    assert all(c.type == "mini_dungeon" for c in picked)
    assert fallbacks == []


def test_pick_layer_clusters_falls_back_when_type_pool_exhausted():
    from speedfog.generator_v2 import pick_layer_clusters

    pool = ClusterPool()
    pool.add(_mk_cluster("md_0", "mini_dungeon"))
    pool.add(_mk_cluster("ba_0", "boss_arena"))
    pool.add(_mk_cluster("ba_1", "boss_arena"))
    rng = random.Random(0)
    picked, fallbacks = pick_layer_clusters(
        width=3,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=rng,
        allowed_types=("mini_dungeon", "boss_arena"),
    )
    assert len(picked) == 3
    types = sorted(c.type for c in picked)
    assert types == ["boss_arena", "boss_arena", "mini_dungeon"]
    assert len(fallbacks) == 2  # two boss_arenas were fallbacks
    assert all(f.preferred_type == "mini_dungeon" for f in fallbacks)


def test_pick_layer_clusters_fails_when_no_compatible_remaining():
    from speedfog.generator_v2 import GenerationError, pick_layer_clusters

    pool = ClusterPool()
    pool.add(_mk_cluster("md_0", "mini_dungeon"))
    rng = random.Random(0)
    import pytest

    with pytest.raises(GenerationError):
        pick_layer_clusters(
            width=3,
            layer_type="mini_dungeon",
            clusters=pool,
            used_zones=set(),
            rng=rng,
            allowed_types=("mini_dungeon",),
        )


def test_generate_dag_v2_produces_exact_layers_count():
    from pathlib import Path

    from speedfog.clusters import load_clusters
    from speedfog.config import (
        BudgetConfig,
        Config,
        RequirementsConfig,
        StructureConfig,
    )
    from speedfog.generator_v2 import generate_dag

    pool = load_clusters(Path(__file__).parent.parent / "data" / "clusters.json")
    pool.merge_roundtable_into_start()
    pool.filter_passant_incompatible()

    cfg = Config(
        seed=12345,
        requirements=RequirementsConfig(
            legacy_dungeons=1,
            bosses=3,
            mini_dungeons=3,
            major_bosses=1,
            allowed_types=[
                "mini_dungeon",
                "boss_arena",
                "legacy_dungeon",
                "major_boss",
            ],
        ),
        structure=StructureConfig(
            layers_count=15,
            max_parallel_paths=4,
            final_boss_candidates={"leyndell_throne": 1},
        ),
        budget=BudgetConfig(),
    )
    dag, log = generate_dag(cfg, pool)

    layers = {n.layer for n in dag.nodes.values()}
    assert layers == set(range(15)), f"Expected 15 layers, got {sorted(layers)}"
    # Validator
    errors = dag.validate_structure()
    assert errors == [], errors
