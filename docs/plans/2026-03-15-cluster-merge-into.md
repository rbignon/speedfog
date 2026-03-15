# Cluster merge_into Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `merge_into` mechanism in zone_metadata.toml so that trivial zones (like `caelid_preradahn`) are absorbed into their target cluster (like `caelid_radahn`), preventing stake-of-Marika softlocks.

**Architecture:** A new `merge_into` field in zone_metadata.toml declares that a zone's cluster should be merged into the cluster containing the target zone. `generate_clusters.py` applies these merges after flood-fill but before fog computation, so internal fog gates between merged zones become non-randomized. The merged cluster inherits the target's type, ID, and properties.

**Tech Stack:** Python (generate_clusters.py, zone_metadata.toml)

---

## Chunk 1: Core Implementation

### Task 1: Add merge_into to zone_metadata.toml

**Files:**
- Modify: `data/zone_metadata.toml`

- [ ] **Step 1: Add caelid_preradahn entry**

Add after the existing `[zones.caelid_gaolcave]` block (line ~86):

```toml
# Merge into caelid_radahn: preradahn is just an elevator + sending gate corridor.
# Without merge, the Radahn stake respawns in preradahn (outside DAG) → softlock.
[zones.caelid_preradahn]
weight = 0
merge_into = "caelid_radahn"
```

- [ ] **Step 2: Commit**

```bash
git add data/zone_metadata.toml
git commit -m "config: add merge_into for caelid_preradahn → caelid_radahn"
```

### Task 2: Write failing tests for apply_cluster_merges

**Files:**
- Create: tests in `tools/test_generate_clusters.py` (append to file)

- [ ] **Step 1: Write test for basic merge**

Append to `tools/test_generate_clusters.py`:

```python
class TestApplyClusterMerges:
    """Tests for apply_cluster_merges function."""

    def test_basic_merge(self):
        """Merging zone A's cluster into zone B's cluster unions their zones."""
        cluster_a = Cluster(zones=frozenset({"zone_a"}))
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        metadata = {"zones": {"zone_a": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_a, cluster_b], metadata)

        assert len(result) == 1
        assert result[0].zones == frozenset({"zone_a", "zone_b"})

    def test_no_merges(self):
        """Without merge_into, clusters remain unchanged."""
        cluster_a = Cluster(zones=frozenset({"zone_a"}))
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        metadata = {"zones": {}}

        result = apply_cluster_merges([cluster_a, cluster_b], metadata)

        assert len(result) == 2

    def test_merge_target_not_found(self):
        """merge_into referencing unknown zone logs warning, keeps cluster."""
        cluster_a = Cluster(zones=frozenset({"zone_a"}))
        metadata = {"zones": {"zone_a": {"merge_into": "nonexistent"}}}

        result = apply_cluster_merges([cluster_a], metadata)

        # Cluster kept as-is when target not found
        assert len(result) == 1
        assert result[0].zones == frozenset({"zone_a"})

    def test_merge_multizone_source(self):
        """Source cluster with multiple zones merges all into target."""
        cluster_a = Cluster(zones=frozenset({"zone_a", "zone_a2"}))
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        metadata = {"zones": {"zone_a": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_a, cluster_b], metadata)

        assert len(result) == 1
        assert result[0].zones == frozenset({"zone_a", "zone_a2", "zone_b"})

    def test_merge_does_not_affect_other_clusters(self):
        """Unrelated clusters are preserved."""
        cluster_a = Cluster(zones=frozenset({"zone_a"}))
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        cluster_c = Cluster(zones=frozenset({"zone_c"}))
        metadata = {"zones": {"zone_a": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_a, cluster_b, cluster_c], metadata)

        assert len(result) == 2
        merged = next(c for c in result if "zone_b" in c.zones)
        assert merged.zones == frozenset({"zone_a", "zone_b"})
        other = next(c for c in result if "zone_c" in c.zones)
        assert other.zones == frozenset({"zone_c"})

    def test_duplicate_clusters_containing_merged_zone_are_removed(self):
        """If zone_a appears in multiple clusters, all are consumed by merge."""
        cluster_a1 = Cluster(zones=frozenset({"zone_a"}))
        cluster_a2 = Cluster(zones=frozenset({"zone_a", "zone_extra"}))
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        metadata = {"zones": {"zone_a": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_a1, cluster_a2, cluster_b], metadata)

        merged = next(c for c in result if "zone_b" in c.zones)
        assert "zone_a" in merged.zones
        assert "zone_extra" in merged.zones
        assert len(result) == 1
        # No leftover cluster containing zone_a
        assert all("zone_a" not in c.zones or "zone_b" in c.zones for c in result)

    def test_already_same_cluster_is_noop(self):
        """If source and target are already in the same cluster, nothing changes."""
        cluster_ab = Cluster(zones=frozenset({"zone_a", "zone_b"}))
        metadata = {"zones": {"zone_a": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_ab], metadata)

        assert len(result) == 1
        assert result[0].zones == frozenset({"zone_a", "zone_b"})

    def test_source_zone_not_in_any_cluster(self):
        """If source zone doesn't exist in any cluster, merge is silently skipped."""
        cluster_b = Cluster(zones=frozenset({"zone_b"}))
        metadata = {"zones": {"zone_missing": {"merge_into": "zone_b"}}}

        result = apply_cluster_merges([cluster_b], metadata)

        assert len(result) == 1
        assert result[0].zones == frozenset({"zone_b"})
```

- [ ] **Step 2: Add import for apply_cluster_merges**

Add `apply_cluster_merges` to the import list at the top of `test_generate_clusters.py`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd tools && python -m pytest test_generate_clusters.py::TestApplyClusterMerges -v`
Expected: FAIL with `ImportError` (function not yet defined)

- [ ] **Step 4: Commit**

```bash
git add tools/test_generate_clusters.py
git commit -m "test: add failing tests for apply_cluster_merges"
```

### Task 3: Implement apply_cluster_merges

**Files:**
- Modify: `tools/generate_clusters.py`

- [ ] **Step 1: Add apply_cluster_merges function**

Add after `generate_clusters()` (around line 970):

```python
def apply_cluster_merges(
    clusters: list[Cluster],
    metadata: dict,
) -> list[Cluster]:
    """
    Merge clusters based on merge_into declarations in zone metadata.

    When a zone has merge_into = "target_zone", the cluster containing that zone
    is absorbed into the cluster containing the target zone. This is used for
    trivial antechamber zones (e.g. caelid_preradahn → caelid_radahn) where the
    internal fog gate should remain vanilla.

    Must be called AFTER generate_clusters() and BEFORE compute_cluster_fogs(),
    so that internal fog gates between merged zones are correctly classified.
    """
    zones_meta = metadata.get("zones", {})

    # Collect merge declarations: source_zone → target_zone
    merges: dict[str, str] = {}
    for zone_name, zm in zones_meta.items():
        if isinstance(zm, dict) and "merge_into" in zm:
            merges[zone_name] = zm["merge_into"]

    if not merges:
        return clusters

    # Build zone → cluster index
    zone_to_cluster: dict[str, Cluster] = {}
    for cluster in clusters:
        for zone in cluster.zones:
            zone_to_cluster[zone] = cluster

    # Apply merges
    consumed: set[int] = set()  # id() of clusters absorbed into others
    for source_zone, target_zone in merges.items():
        if target_zone not in zone_to_cluster:
            print(f"  Warning: merge_into target '{target_zone}' not found, skipping")
            continue

        target_cluster = zone_to_cluster[target_zone]

        if id(target_cluster) in consumed:
            print(f"  Warning: merge target '{target_zone}' is in a cluster already consumed "
                  f"by another merge — check for conflicting merge_into declarations")
            continue

        # Find all clusters containing the source zone (may be multiple due to flood-fill)
        source_clusters = [
            c for c in clusters
            if source_zone in c.zones and id(c) != id(target_cluster) and id(c) not in consumed
        ]

        for source_cluster in source_clusters:
            # Union zones into target. Existing target zones already point to
            # target_cluster via object identity, so only source zones need updating.
            target_cluster.zones = frozenset(target_cluster.zones | source_cluster.zones)
            consumed.add(id(source_cluster))

            # Update zone→cluster index for merged zones
            for zone in source_cluster.zones:
                zone_to_cluster[zone] = target_cluster

            print(f"  Merged cluster ({', '.join(sorted(source_cluster.zones))}) "
                  f"into ({', '.join(sorted(target_cluster.zones))})")

    # Return clusters excluding consumed ones
    return [c for c in clusters if id(c) not in consumed]
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd tools && python -m pytest test_generate_clusters.py::TestApplyClusterMerges -v`
Expected: All 8 tests PASS

- [ ] **Step 3: Commit**

```bash
git add tools/generate_clusters.py
git commit -m "feat: add apply_cluster_merges for zone fusion"
```

### Task 4: Wire apply_cluster_merges into main()

**Files:**
- Modify: `tools/generate_clusters.py` (main function, around line 1918)

- [ ] **Step 1: Move metadata loading before cluster fog computation**

In `main()`, the current order is:
1. `generate_clusters()` (line 1900)
2. `compute_cluster_fogs()` (line 1904)
3. `load_metadata()` (line 1919)

Move metadata loading BEFORE fog computation and insert the merge step. Replace lines 1898-1905 (cluster generation + fog computation) with the code below. The deduplication block (lines 1907-1916) and everything after stays unchanged — it just follows after the new merge step. Also remove the old `metadata = load_metadata(args.metadata)` at line 1919.

```python
    # Generate clusters
    print("Generating clusters...")
    clusters = generate_clusters(zones_to_process, world_graph)
    print(f"  Generated {len(clusters)} raw clusters")

    # Load metadata (needed for merge step before fog computation)
    metadata = load_metadata(args.metadata)

    # Apply cluster merges (must happen before fog computation)
    clusters = apply_cluster_merges(clusters, metadata)

    # Compute fogs for each cluster
    for cluster in clusters:
        compute_cluster_fogs(cluster, world_graph, zone_fogs)
```

- [ ] **Step 2: Run all generate_clusters tests**

Run: `cd tools && python -m pytest test_generate_clusters.py -v`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```bash
git add tools/generate_clusters.py
git commit -m "feat: wire apply_cluster_merges into main pipeline"
```

### Task 5: Integration test with real data

**Files:**
- Modify: `tools/test_generate_clusters.py`

- [ ] **Step 1: Write integration test**

Append to `tools/test_generate_clusters.py`:

```python
class TestApplyClusterMergesIntegration:
    """Integration tests using real fog.txt data (skipped if unavailable)."""

    @pytest.fixture
    def real_data(self):
        """Load real fog.txt and metadata if available."""
        from pathlib import Path

        fog_txt = Path(__file__).parent.parent / "data" / "fog.txt"
        metadata_path = Path(__file__).parent.parent / "data" / "zone_metadata.toml"
        if not fog_txt.exists():
            pytest.skip("data/fog.txt not available")

        parsed = parse_fog_txt(fog_txt)
        areas = parsed["areas"]
        key_items = parsed["key_items"] | KEY_ITEMS
        zones_to_process = {
            name for name, area in areas.items()
            if not should_exclude_area(area, exclude_dlc=False, exclude_overworld=True)
            and name not in get_evergaol_zones(parsed["entrances"], parsed["warps"])
        }
        world_graph = build_world_graph(areas, key_items, allowed_zones=zones_to_process)
        zone_fogs = classify_fogs(parsed["entrances"], parsed["warps"], areas)
        metadata = load_metadata(metadata_path)

        clusters = generate_clusters(zones_to_process, world_graph)
        return clusters, metadata, world_graph, zone_fogs

    def test_preradahn_merged_into_radahn(self, real_data):
        """caelid_preradahn should be merged into caelid_radahn's cluster."""
        clusters, metadata, world_graph, zone_fogs = real_data

        merged = apply_cluster_merges(clusters, metadata)

        # After merge, no cluster should contain only caelid_preradahn
        preradahn_clusters = [c for c in merged if "caelid_preradahn" in c.zones]
        assert len(preradahn_clusters) == 1
        assert "caelid_radahn" in preradahn_clusters[0].zones

    def test_sending_gate_becomes_internal_after_merge(self, real_data):
        """The sending gate 1051382300 should not be an exit/entry fog after merge."""
        clusters, metadata, world_graph, zone_fogs = real_data

        merged = apply_cluster_merges(clusters, metadata)

        # Compute fogs on the merged cluster
        for cluster in merged:
            compute_cluster_fogs(cluster, world_graph, zone_fogs)

        radahn_cluster = next(c for c in merged if "caelid_radahn" in c.zones)

        # The sending gate should not appear in exit_fogs or entry_fogs
        exit_fog_ids = {f["fog_id"] for f in radahn_cluster.exit_fogs}
        entry_fog_ids = {f["fog_id"] for f in radahn_cluster.entry_fogs}
        assert "1051382300" not in exit_fog_ids, "Sending gate should be internal, not an exit"
        assert "1051382300" not in entry_fog_ids, "Sending gate should be internal, not an entry"

    def test_radahn_cluster_has_entries(self, real_data):
        """Merged radahn cluster should still have entry fogs."""
        clusters, metadata, world_graph, zone_fogs = real_data

        merged = apply_cluster_merges(clusters, metadata)
        for cluster in merged:
            compute_cluster_fogs(cluster, world_graph, zone_fogs)

        radahn_cluster = next(c for c in merged if "caelid_radahn" in c.zones)
        assert len(radahn_cluster.entry_fogs) > 0, "Radahn cluster must have entry fogs"
```

- [ ] **Step 2: Add imports**

Add `parse_fog_txt`, `KEY_ITEMS`, and `load_metadata` to the import list at the top of the test file (if not already present).

- [ ] **Step 3: Run integration tests**

Run: `cd tools && python -m pytest test_generate_clusters.py::TestApplyClusterMergesIntegration -v`
Expected: All 3 tests PASS (or skip if fog.txt unavailable)

- [ ] **Step 4: Commit**

```bash
git add tools/test_generate_clusters.py
git commit -m "test: add integration tests for caelid_preradahn merge"
```

### Task 6: Regenerate clusters.json and verify

- [ ] **Step 1: Regenerate clusters.json**

Run: `cd /home/dev/src/games/ER/fog/speedfog && python tools/generate_clusters.py data/fog.txt data/clusters.json --metadata data/zone_metadata.toml -v`

Expected output should include:
```
  Merged cluster (caelid_preradahn) into (caelid_preradahn, caelid_radahn)
```

- [ ] **Step 2: Verify merged cluster in clusters.json**

Check that:
1. No standalone `caelid_preradahn` cluster exists
2. The `caelid_radahn_*` cluster contains both zones: `["caelid_radahn", "caelid_preradahn"]`
3. The sending gate `1051382300` does NOT appear in exit_fogs or entry_fogs of the merged cluster
4. The `AEG099_232_9000` fog gate from preradahn appears as an entry_fog (elevator entrance)

- [ ] **Step 3: Run a seed generation to verify end-to-end**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run speedfog config.toml --spoiler`

Expected: Seed generates without error. If `caelid_radahn` appears in the DAG, the spoiler should show the merged cluster.

- [ ] **Step 4: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && pytest -v`

Expected: All tests pass.
