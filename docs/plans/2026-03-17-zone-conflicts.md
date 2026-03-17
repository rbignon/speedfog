# Zone Conflicts (Margit/Morgott Mutual Exclusion) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent mutually exclusive boss zones (Margit/Morgott) from appearing in the same run.

**Architecture:** Declare zone-level conflicts in `zone_metadata.toml`. Bake them into `clusters.json` as a top-level `zone_conflicts` dict. Load into `ClusterPool` at runtime. After each cluster selection in the generator, add conflicting zones to `used_zones` — existing `pick_cluster_uniform()` filtering handles the rest with zero changes.

**Design notes:**
- Conflicts are NOT transitive: A↔B and B↔C does not imply A↔C. Each pair must be declared explicitly.
- Both sides must declare the conflict (symmetrical). This is intentional for explicitness.

**Tech Stack:** Python 3.10+, TOML, JSON

---

## Chunk 1: Data and Loading

### Task 1: Add `conflicts_with` to zone_metadata.toml

**Files:**
- Modify: `data/zone_metadata.toml:124-128`

- [ ] **Step 1: Add the conflict declarations**

Add a new `[zones.stormveil_margit]` section (does not exist yet), and modify the existing `[zones.leyndell_sanctuary]` at line 124 to add `conflicts_with`:

```toml
# Margit is Morgott in disguise — killing Morgott removes Margit from his arena.
# These zones cannot coexist in the same run.
[zones.stormveil_margit]
conflicts_with = ["leyndell_sanctuary"]

[zones.leyndell_sanctuary]
weight = 2
conflicts_with = ["stormveil_margit"]
```

Note: `leyndell_throne` (Godfrey) is NOT conflicting — only Morgott's zone `leyndell_sanctuary`.

- [ ] **Step 2: Commit**

```bash
git add data/zone_metadata.toml
git commit -m "feat: declare Margit/Morgott zone conflict in metadata"
```

### Task 2: Bake zone_conflicts into clusters.json

**Files:**
- Modify: `tools/generate_clusters.py:1897-1904` (in `clusters_to_json()`)

- [ ] **Step 1: Write the failing test**

In `tools/test_generate_clusters.py`, add a test for conflict extraction:

```python
def test_zone_conflicts_in_json_output(self):
    """zone_conflicts from metadata are included in JSON output."""
    metadata = {
        "zones": {
            "zone_a": {"conflicts_with": ["zone_b"]},
            "zone_b": {"conflicts_with": ["zone_a"]},
        }
    }
    # Verify build_zone_conflicts extracts the mapping
    result = build_zone_conflicts(metadata)
    assert result == {"zone_a": ["zone_b"], "zone_b": ["zone_a"]}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools && uv run pytest test_generate_clusters.py::TestZoneConflictsInJsonOutput -v`
Expected: FAIL (function not defined)

- [ ] **Step 3: Write `build_zone_conflicts()` in generate_clusters.py**

Add near other `build_zone_*` functions (around line 1770):

```python
def build_zone_conflicts(metadata: dict) -> dict[str, list[str]]:
    """Extract zone conflicts from metadata.

    Returns a dict mapping zone_name → list of conflicting zone names.
    Only includes zones that have conflicts declared.
    """
    zones_meta = metadata.get("zones", {})
    conflicts: dict[str, list[str]] = {}
    for zone_name, zm in zones_meta.items():
        if isinstance(zm, dict) and "conflicts_with" in zm:
            conflicts[zone_name] = zm["conflicts_with"]
    return conflicts
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools && uv run pytest test_generate_clusters.py::TestZoneConflictsInJsonOutput -v`
Expected: PASS

- [ ] **Step 5: Add `zone_conflicts` to `clusters_to_json()` output**

In `clusters_to_json()` (line ~1897), add the field to the returned dict:

```python
    return {
        "version": "1.10",
        "generated_from": "fog.txt",
        "cluster_count": len(clusters),
        "zone_maps": zone_maps,
        "zone_names": zone_names,
        "zone_conflicts": zone_conflicts,  # NEW
        "clusters": cluster_list,
    }
```

Update the function signature to accept `metadata`:

```python
def clusters_to_json(
    clusters: list[Cluster],
    areas: dict[str, AreaData],
    metadata: dict,  # NEW
) -> dict:
    """Convert clusters to JSON-serializable format with zone→map mapping."""
    zone_maps = build_zone_maps(clusters, areas)
    zone_names = build_zone_names(clusters, areas)
    zone_conflicts = build_zone_conflicts(metadata)  # NEW
```

Update the caller in `main()` at line 2072:

```python
# Before:
output_data = clusters_to_json(clusters, areas)
# After:
output_data = clusters_to_json(clusters, areas, metadata)
```

- [ ] **Step 6: Commit**

```bash
git add tools/generate_clusters.py tools/test_generate_clusters.py
git commit -m "feat: extract zone_conflicts from metadata into clusters.json"
```

### Task 3: Load zone_conflicts in ClusterPool

**Files:**
- Modify: `speedfog/clusters.py:122-268`
- Test: `tests/test_clusters.py`

- [ ] **Step 1: Write the failing test**

```python
class TestZoneConflicts:
    """Tests for zone conflict loading and lookup."""

    def test_zone_conflicts_loaded_from_json(self, tmp_path):
        """zone_conflicts dict is loaded from clusters.json."""
        data = {
            "version": "1.10",
            "zone_maps": {},
            "zone_names": {},
            "zone_conflicts": {
                "stormveil_margit": ["leyndell_sanctuary"],
                "leyndell_sanctuary": ["stormveil_margit"],
            },
            "clusters": [],
        }
        path = tmp_path / "clusters.json"
        import json
        path.write_text(json.dumps(data))
        pool = ClusterPool.from_json(path)
        assert pool.zone_conflicts == {
            "stormveil_margit": ["leyndell_sanctuary"],
            "leyndell_sanctuary": ["stormveil_margit"],
        }

    def test_get_conflicting_zones(self):
        """get_conflicting_zones returns all zones conflicting with input."""
        pool = ClusterPool()
        pool.zone_conflicts = {
            "zone_a": ["zone_b", "zone_c"],
            "zone_b": ["zone_a"],
        }
        result = pool.get_conflicting_zones(["zone_a"])
        assert result == {"zone_b", "zone_c"}

    def test_get_conflicting_zones_no_conflicts(self):
        """get_conflicting_zones returns empty set when no conflicts."""
        pool = ClusterPool()
        pool.zone_conflicts = {}
        result = pool.get_conflicting_zones(["zone_x"])
        assert result == set()

    def test_zone_conflicts_defaults_empty(self, tmp_path):
        """zone_conflicts defaults to empty dict if missing from JSON."""
        data = {
            "version": "1.9",
            "zone_maps": {},
            "zone_names": {},
            "clusters": [],
        }
        path = tmp_path / "clusters.json"
        import json
        path.write_text(json.dumps(data))
        pool = ClusterPool.from_json(path)
        assert pool.zone_conflicts == {}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_clusters.py::TestZoneConflicts -v`
Expected: FAIL

- [ ] **Step 3: Implement zone_conflicts in ClusterPool**

In `clusters.py`, modify `ClusterPool`:

1. Add field to dataclass:
```python
zone_conflicts: dict[str, list[str]] = field(default_factory=dict)
```

2. Add method:
```python
def get_conflicting_zones(self, zones: list[str]) -> set[str]:
    """Get all zones that conflict with the given zones.

    Args:
        zones: List of zone IDs to check.

    Returns:
        Set of zone IDs that conflict with any of the input zones.
    """
    result: set[str] = set()
    for zone in zones:
        if zone in self.zone_conflicts:
            result.update(self.zone_conflicts[zone])
    return result
```

3. Load in `from_json()`:
```python
pool.zone_conflicts = data.get("zone_conflicts", {})
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_clusters.py::TestZoneConflicts -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/clusters.py tests/test_clusters.py
git commit -m "feat: load zone_conflicts in ClusterPool with lookup method"
```

## Chunk 2: Generator Integration

### Task 4: Apply conflicts in DAG generation

**Files:**
- Modify: `speedfog/generator.py` (all `used_zones.update()` call sites)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write the failing tests**

Add `_mark_cluster_used` to the imports from `speedfog.generator` at the top of the file.

```python
class TestZoneConflicts:
    """Tests for zone conflict exclusion during DAG generation."""

    def test_conflicting_zone_excluded_after_selection(self):
        """When a cluster is selected, clusters with conflicting zones are excluded."""
        margit = make_cluster(
            "margit", zones=["stormveil_margit"],
            cluster_type="major_boss", weight=2,
        )
        morgott = make_cluster(
            "morgott", zones=["leyndell_sanctuary"],
            cluster_type="major_boss", weight=2,
        )
        other = make_cluster(
            "other", zones=["other_zone"],
            cluster_type="major_boss", weight=2,
        )

        pool = ClusterPool()
        pool.zone_conflicts = {
            "stormveil_margit": ["leyndell_sanctuary"],
            "leyndell_sanctuary": ["stormveil_margit"],
        }
        for c in [margit, morgott, other]:
            pool.add(c)

        used_zones: set[str] = set()

        # Simulate selecting margit
        used_zones.update(margit.zones)
        used_zones.update(pool.get_conflicting_zones(margit.zones))

        # Now morgott should be excluded (its zone is in used_zones)
        result = pick_cluster_uniform(
            pool.get_by_type("major_boss"), used_zones, random.Random(42)
        )
        assert result is not None
        assert result.id == "other"
```

    def test_mark_cluster_used_adds_conflicts(self):
        """_mark_cluster_used adds both cluster zones and conflicting zones."""
        margit = make_cluster(
            "margit", zones=["stormveil_margit"],
            cluster_type="major_boss", weight=2,
        )
        pool = ClusterPool()
        pool.zone_conflicts = {
            "stormveil_margit": ["leyndell_sanctuary"],
        }
        pool.add(margit)

        used_zones: set[str] = set()
        _mark_cluster_used(margit, used_zones, pool)

        assert "stormveil_margit" in used_zones
        assert "leyndell_sanctuary" in used_zones
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_generator.py::TestZoneConflicts -v`
Expected: FAIL (`_mark_cluster_used` not defined)

- [ ] **Step 3: Add helper function to generator.py**

Add a helper that wraps the common pattern (update zones + conflicts):

```python
def _mark_cluster_used(
    cluster: ClusterData,
    used_zones: set[str],
    clusters: ClusterPool,
) -> None:
    """Mark a cluster's zones as used, including conflicting zones."""
    used_zones.update(cluster.zones)
    used_zones.update(clusters.get_conflicting_zones(cluster.zones))
```

- [ ] **Step 4: Replace all `used_zones.update(cluster.zones)` with `_mark_cluster_used()`**

There are ~16 call sites (line numbers from grep: 885, 930, 1039, 1074, 1141, 1221, 1386, 1476, 1566, 1655, 1735, 1868, 1913, 1977, 2062, 2114). Each `used_zones.update(X.zones)` becomes `_mark_cluster_used(X, used_zones, clusters)`.

Some call sites use different variable names for the cluster pool (check each one — most use `clusters` but verify). The pattern is always:
```python
# Before:
used_zones.update(some_cluster.zones)
# After:
_mark_cluster_used(some_cluster, used_zones, clusters)
```

- [ ] **Step 5: Run all tests**

Run: `uv run pytest tests/ -v`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: apply zone conflicts during DAG generation"
```

### Task 5: Update documentation

**Files:**
- Modify: `CLAUDE.md` (mention conflicts_with in Zone Data section)

- [ ] **Step 1: Add brief mention to CLAUDE.md Zone Data section**

Under the existing Zone Data bullet points, add:
```
- Zone conflicts declared in `zone_metadata.toml` via `conflicts_with` (e.g., Margit/Morgott mutual exclusion)
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document zone conflicts_with mechanism"
```
