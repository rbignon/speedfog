# Allowed Cluster Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `requirements.allowed_types` whitelist so configs like boss-rush can exclude cluster types entirely from the DAG.

**Architecture:** A new optional field on `RequirementsConfig` lists the cluster types active for the run. The planner iterates over this list instead of the hardcoded four types, the generator filters its pool snapshots through it, and the convergence picker receives a fallback from it. Minimums for types outside the list are silently ignored (warning if non-zero). The final boss is unaffected: it is still selected from `final_boss_candidates` and placed as a terminal node regardless of `allowed_types`.

**Tech Stack:** Python 3.10+ with `dataclasses`, `tomllib`, `warnings` (stdlib), `pytest`.

**Spec:** `docs/specs/2026-04-13-allowed-types-design.md`

---

## File Structure

- `speedfog/config.py` — add `allowed_types` field, validation, `required_count()` helper, `Config.__post_init__` cross-validation, wire through `Config.from_dict`.
- `speedfog/planner.py` — change `plan_layer_types` to iterate over `allowed_types`; add `fallback` parameter to `pick_weighted_type`.
- `speedfog/generator.py` — filter `pool_sizes` at planner call, filter `conv_pool_sizes` at convergence, filter `_FALLBACK_TYPES` usage in `pick_cluster_with_type_fallback` and `_compute_fallback_pool`; pass fallback to `pick_weighted_type`.
- `speedfog/validator.py` — skip per-type minimum check for excluded types; add zone-type reachability check.
- `config.example.toml` — document `allowed_types` with example modes.
- `docs/dag-generation.md` — short section on `allowed_types`.
- `tests/test_config.py` — validation tests, warning test.
- `tests/test_planner.py` — `plan_layer_types` honors `allowed_types`, `pick_weighted_type` uses `fallback`.
- `tests/test_generator.py` — end-to-end boss-rush generation.
- `tests/test_validator.py` — excluded types skipped, unreachable zones flagged.

---

## Task 1: Add `allowed_types` field with basic validation

**Files:**
- Modify: `speedfog/config.py` (`RequirementsConfig`)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing tests**

Add to `tests/test_config.py`:

```python
import pytest
from speedfog.config import RequirementsConfig


class TestAllowedTypes:
    """Tests for RequirementsConfig.allowed_types."""

    def test_default_contains_all_four_types(self):
        req = RequirementsConfig()
        assert set(req.allowed_types) == {
            "legacy_dungeon",
            "mini_dungeon",
            "boss_arena",
            "major_boss",
        }

    def test_custom_subset(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=5,
            mini_dungeons=0,
            major_bosses=3,
        )
        assert req.allowed_types == ["boss_arena", "major_boss"]

    def test_empty_list_raises(self):
        with pytest.raises(ValueError, match="allowed_types must be non-empty"):
            RequirementsConfig(allowed_types=[])

    def test_unknown_type_raises(self):
        with pytest.raises(ValueError, match="invalid cluster type"):
            RequirementsConfig(allowed_types=["boss_arena", "dragons"])

    def test_duplicate_entries_raises(self):
        with pytest.raises(ValueError, match="duplicate"):
            RequirementsConfig(
                allowed_types=["boss_arena", "boss_arena", "major_boss"]
            )
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_config.py::TestAllowedTypes -v`
Expected: FAIL — `RequirementsConfig` has no `allowed_types` argument.

- [ ] **Step 3: Add the field and validation**

In `speedfog/config.py`, modify `RequirementsConfig`:

```python
_VALID_CLUSTER_TYPES = (
    "legacy_dungeon",
    "mini_dungeon",
    "boss_arena",
    "major_boss",
)


@dataclass
class RequirementsConfig:
    """Zone requirements configuration."""

    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5
    major_bosses: int = 8
    zones: list[str] = field(default_factory=list)
    allowed_types: list[str] = field(
        default_factory=lambda: list(_VALID_CLUSTER_TYPES)
    )

    def __post_init__(self) -> None:
        if not self.allowed_types:
            raise ValueError("allowed_types must be non-empty")
        seen: set[str] = set()
        for t in self.allowed_types:
            if t not in _VALID_CLUSTER_TYPES:
                raise ValueError(
                    f"invalid cluster type in allowed_types: {t!r} "
                    f"(valid: {_VALID_CLUSTER_TYPES})"
                )
            if t in seen:
                raise ValueError(f"duplicate entry in allowed_types: {t!r}")
            seen.add(t)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_config.py::TestAllowedTypes -v`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full config test suite to check no regressions**

Run: `uv run pytest tests/test_config.py -v`
Expected: PASS (all existing tests still pass).

- [ ] **Step 6: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "config: add requirements.allowed_types with validation"
```

---

## Task 2: Add `required_count()` helper + warning for ignored minima

**Files:**
- Modify: `speedfog/config.py` (`RequirementsConfig`)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing tests**

Append to `TestAllowedTypes` in `tests/test_config.py`:

```python
    def test_required_count_for_allowed_type(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=7,
            mini_dungeons=0,
            major_bosses=2,
        )
        assert req.required_count("boss_arena") == 7
        assert req.required_count("major_boss") == 2

    def test_required_count_for_excluded_type_returns_zero(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=3,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        # mini_dungeon not in allowed_types, even though default min is 5
        assert req.required_count("mini_dungeon") == 0
        # legacy_dungeon not in allowed_types, even with explicit 3
        assert req.required_count("legacy_dungeon") == 0

    def test_nonzero_min_for_excluded_type_emits_warning(self, recwarn):
        RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=3,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        messages = [str(w.message) for w in recwarn.list]
        assert any(
            "legacy_dungeons" in m and "not in allowed_types" in m
            for m in messages
        )

    def test_zero_min_for_excluded_type_no_warning(self, recwarn):
        RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=5,
            mini_dungeons=0,
            major_bosses=2,
        )
        assert len(recwarn.list) == 0
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_config.py::TestAllowedTypes -v`
Expected: FAIL — `required_count` attribute error, no warning emitted.

- [ ] **Step 3: Implement the helper and warning**

In `speedfog/config.py`, add at the top:

```python
import warnings
```

Extend `RequirementsConfig` with the type-to-field mapping and helper, and emit the warning in `__post_init__`:

```python
_CLUSTER_TYPE_TO_FIELD = {
    "legacy_dungeon": "legacy_dungeons",
    "mini_dungeon": "mini_dungeons",
    "boss_arena": "bosses",
    "major_boss": "major_bosses",
}


@dataclass
class RequirementsConfig:
    # ... existing fields ...

    def __post_init__(self) -> None:
        # (existing allowed_types validation)

        # Warn about non-zero minima on excluded types
        for cluster_type, field_name in _CLUSTER_TYPE_TO_FIELD.items():
            if cluster_type in self.allowed_types:
                continue
            value = getattr(self, field_name)
            if value > 0:
                warnings.warn(
                    f"requirements.{field_name} = {value} ignored: "
                    f"'{cluster_type}' not in allowed_types",
                    UserWarning,
                    stacklevel=2,
                )

    def required_count(self, cluster_type: str) -> int:
        """Return minimum count for a cluster type, 0 if excluded."""
        if cluster_type not in self.allowed_types:
            return 0
        field_name = _CLUSTER_TYPE_TO_FIELD[cluster_type]
        return getattr(self, field_name)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_config.py::TestAllowedTypes -v`
Expected: PASS (8 tests total in the class).

- [ ] **Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "config: add RequirementsConfig.required_count + warn on ignored minima"
```

---

## Task 3: Wire `allowed_types` through `Config.from_dict`

**Files:**
- Modify: `speedfog/config.py` (`Config.from_dict`)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing test**

Append to `tests/test_config.py`:

```python
def test_config_from_dict_parses_allowed_types():
    config = Config.from_dict(
        {
            "requirements": {
                "allowed_types": ["boss_arena", "major_boss"],
                "legacy_dungeons": 0,
                "bosses": 10,
                "mini_dungeons": 0,
                "major_bosses": 3,
            }
        }
    )
    assert config.requirements.allowed_types == ["boss_arena", "major_boss"]
    assert config.requirements.bosses == 10


def test_config_from_dict_default_allowed_types():
    config = Config.from_dict({})
    assert set(config.requirements.allowed_types) == {
        "legacy_dungeon",
        "mini_dungeon",
        "boss_arena",
        "major_boss",
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest tests/test_config.py::test_config_from_dict_parses_allowed_types -v`
Expected: FAIL — `allowed_types` ignored by `from_dict`.

- [ ] **Step 3: Wire the field through**

In `speedfog/config.py`, modify `Config.from_dict`. In the `RequirementsConfig(...)` constructor call, add:

```python
requirements=RequirementsConfig(
    legacy_dungeons=requirements_section.get("legacy_dungeons", 1),
    bosses=requirements_section.get("bosses", 5),
    mini_dungeons=requirements_section.get("mini_dungeons", 5),
    major_bosses=requirements_section.get("major_bosses", 8),
    zones=requirements_section.get("zones", []),
    allowed_types=requirements_section.get(
        "allowed_types",
        list(_VALID_CLUSTER_TYPES),
    ),
),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_config.py -v`
Expected: PASS (new tests + all existing tests).

- [ ] **Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "config: parse allowed_types from TOML via from_dict"
```

---

## Task 4: Cross-validate `first_layer_type` against `allowed_types`

**Files:**
- Modify: `speedfog/config.py` (`Config`)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing test**

Append to `tests/test_config.py`:

```python
def test_first_layer_type_must_be_in_allowed_types():
    with pytest.raises(ValueError, match="first_layer_type.*not in allowed_types"):
        Config.from_dict(
            {
                "requirements": {
                    "allowed_types": ["boss_arena", "major_boss"],
                    "legacy_dungeons": 0,
                    "mini_dungeons": 0,
                },
                "structure": {"first_layer_type": "legacy_dungeon"},
            }
        )


def test_first_layer_type_in_allowed_types_ok():
    config = Config.from_dict(
        {
            "requirements": {
                "allowed_types": ["boss_arena", "major_boss"],
                "legacy_dungeons": 0,
                "mini_dungeons": 0,
            },
            "structure": {"first_layer_type": "boss_arena"},
        }
    )
    assert config.structure.first_layer_type == "boss_arena"


def test_first_layer_type_none_is_ok():
    config = Config.from_dict(
        {
            "requirements": {"allowed_types": ["boss_arena", "major_boss"]},
        }
    )
    assert config.structure.first_layer_type is None
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest tests/test_config.py::test_first_layer_type_must_be_in_allowed_types -v`
Expected: FAIL — no cross-validation yet.

- [ ] **Step 3: Implement `Config.__post_init__`**

In `speedfog/config.py`, add a `__post_init__` to `Config`:

```python
@dataclass
class Config:
    # ... existing fields ...

    def __post_init__(self) -> None:
        first = self.structure.first_layer_type
        if first is not None and first not in self.requirements.allowed_types:
            raise ValueError(
                f"structure.first_layer_type = {first!r} not in "
                f"requirements.allowed_types = {self.requirements.allowed_types!r}"
            )
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_config.py -v`
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "config: validate first_layer_type against allowed_types"
```

---

## Task 5: Update `pick_weighted_type` to accept an explicit fallback

**Files:**
- Modify: `speedfog/planner.py` (`pick_weighted_type`)
- Test: `tests/test_planner.py`

- [ ] **Step 1: Write failing test**

Add to `tests/test_planner.py`:

```python
class TestPickWeightedTypeFallback:
    """Tests for pick_weighted_type fallback parameter."""

    def test_fallback_used_when_all_exhausted(self):
        # All pool sizes exhausted by used_counts
        result = pick_weighted_type(
            pool_sizes={"boss_arena": 3, "major_boss": 2},
            used_counts={"boss_arena": 3, "major_boss": 2},
            rng=random.Random(42),
            fallback="boss_arena",
        )
        assert result == "boss_arena"

    def test_normal_pick_ignores_fallback(self):
        # Pool not exhausted, fallback is not picked
        result = pick_weighted_type(
            pool_sizes={"boss_arena": 10, "major_boss": 2},
            used_counts={},
            rng=random.Random(42),
            fallback="legacy_dungeon",
        )
        assert result in {"boss_arena", "major_boss"}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest tests/test_planner.py::TestPickWeightedTypeFallback -v`
Expected: FAIL — `pick_weighted_type()` got an unexpected keyword `fallback`.

- [ ] **Step 3: Add the `fallback` parameter**

In `speedfog/planner.py`, modify `pick_weighted_type`:

```python
def pick_weighted_type(
    pool_sizes: dict[str, int],
    used_counts: dict[str, int],
    rng: random.Random,
    *,
    fallback: str = "mini_dungeon",
) -> str:
    """Pick a type weighted by remaining pool capacity.

    Args:
        pool_sizes: Total available clusters per type.
        used_counts: How many of each type have been consumed so far.
        rng: Random number generator.
        fallback: Type returned when every pool is exhausted. Caller
            should pass a type known to be allowed in the current run.

    Returns:
        A type string chosen proportionally to remaining capacity, or
        `fallback` if every pool is empty.
    """
    remaining = {
        t: max(0, pool - used_counts.get(t, 0)) for t, pool in pool_sizes.items()
    }
    candidates = {t: r for t, r in remaining.items() if r > 0}

    if not candidates:
        return fallback

    types_list = list(candidates.keys())
    weights = [candidates[t] for t in types_list]
    return rng.choices(types_list, weights=weights, k=1)[0]
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_planner.py -v`
Expected: PASS (new tests + all existing planner tests — `fallback` has a default so existing callers still work).

- [ ] **Step 5: Commit**

```bash
git add speedfog/planner.py tests/test_planner.py
git commit -m "planner: add fallback parameter to pick_weighted_type"
```

---

## Task 6: Update `plan_layer_types` to iterate over `allowed_types`

**Files:**
- Modify: `speedfog/planner.py` (`plan_layer_types`)
- Test: `tests/test_planner.py`

- [ ] **Step 1: Write failing tests**

Add to `tests/test_planner.py`:

```python
class TestPlanLayerTypesAllowedTypes:
    """Tests for plan_layer_types honoring allowed_types."""

    def test_only_allowed_types_appear(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena", "major_boss"],
            legacy_dungeons=0,
            bosses=6,
            mini_dungeons=0,
            major_bosses=2,
        )
        pool_sizes = {
            "mini_dungeon": 60,
            "boss_arena": 80,
            "legacy_dungeon": 28,
        }
        result = plan_layer_types(req, 10, random.Random(42), pool_sizes)
        assert set(result) <= {"boss_arena", "major_boss"}

    def test_excluded_type_min_ignored_even_if_nonzero(self):
        # mini_dungeons=5 must be ignored when mini_dungeon not allowed
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            req = RequirementsConfig(
                allowed_types=["boss_arena", "major_boss"],
                legacy_dungeons=0,
                bosses=3,
                mini_dungeons=5,  # must be ignored
                major_bosses=2,
            )
        pool_sizes = {"mini_dungeon": 60, "boss_arena": 80}
        result = plan_layer_types(req, 8, random.Random(42), pool_sizes)
        assert "mini_dungeon" not in result

    def test_padding_filtered_by_allowed_types(self):
        req = RequirementsConfig(
            allowed_types=["boss_arena"],
            legacy_dungeons=0,
            bosses=2,
            mini_dungeons=0,
            major_bosses=0,
        )
        # Pool caller may still pass all types, but planner must filter
        pool_sizes = {
            "mini_dungeon": 60,
            "boss_arena": 80,
            "legacy_dungeon": 28,
        }
        result = plan_layer_types(req, 10, random.Random(42), pool_sizes)
        assert set(result) == {"boss_arena"}

    def test_default_allowed_types_reproduces_old_behavior(self):
        req = RequirementsConfig(
            legacy_dungeons=1,
            bosses=3,
            mini_dungeons=2,
            major_bosses=1,
        )
        pool_sizes = {
            "mini_dungeon": 60,
            "boss_arena": 80,
            "legacy_dungeon": 28,
        }
        result = plan_layer_types(req, 10, random.Random(42), pool_sizes)
        # All four types should be eligible with default allowed_types
        assert result.count("legacy_dungeon") >= 1
        assert result.count("boss_arena") >= 3
        assert result.count("mini_dungeon") >= 2
        assert result.count("major_boss") >= 1
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_planner.py::TestPlanLayerTypesAllowedTypes -v`
Expected: FAIL — excluded types still appear.

- [ ] **Step 3: Rewrite `plan_layer_types` to use `allowed_types`**

Replace `plan_layer_types` in `speedfog/planner.py` with:

```python
def plan_layer_types(
    requirements: RequirementsConfig,
    total_layers: int,
    rng: random.Random,
    pool_sizes: dict[str, int] | None = None,
) -> list[str]:
    """Plan sequence of cluster types for each layer.

    Iterates over requirements.allowed_types; types outside it are
    excluded entirely (no minimum count, no padding, no convergence
    pick). The minimum for each allowed type is read via
    requirements.required_count().

    Args:
        requirements: Configuration with per-type minimums and
            allowed_types whitelist.
        total_layers: Total number of layers to plan.
        rng: Random number generator.
        pool_sizes: Available clusters per type. Filtered by
            allowed_types inside this function.

    Returns:
        List of cluster type strings, one per layer.
    """
    layer_types: list[str] = []
    for cluster_type in requirements.allowed_types:
        layer_types.extend(
            [cluster_type] * requirements.required_count(cluster_type)
        )

    # Trim if we have too many requirements
    if len(layer_types) > total_layers:
        rng.shuffle(layer_types)
        layer_types = layer_types[:total_layers]
    else:
        padding_needed = total_layers - len(layer_types)
        if padding_needed > 0:
            if pool_sizes is not None:
                # Filter pool_sizes by allowed_types, excluding major_boss
                # from padding (final boss is terminal, handled separately).
                filtered_pool = {
                    t: size
                    for t, size in pool_sizes.items()
                    if t in requirements.allowed_types and t != "major_boss"
                }
                required_counts = {
                    t: requirements.required_count(t)
                    for t in filtered_pool
                }
                if filtered_pool:
                    layer_types.extend(
                        _distribute_padding(
                            padding_needed, required_counts, filtered_pool, rng
                        )
                    )
                else:
                    # No non-major-boss type allowed: fall back to first
                    # allowed type for padding.
                    fallback = requirements.allowed_types[0]
                    layer_types.extend([fallback] * padding_needed)
            else:
                # Legacy path: pad with mini_dungeon if it's allowed,
                # otherwise with the first allowed type.
                pad_type = (
                    "mini_dungeon"
                    if "mini_dungeon" in requirements.allowed_types
                    else requirements.allowed_types[0]
                )
                layer_types.extend([pad_type] * padding_needed)

    rng.shuffle(layer_types)
    return layer_types
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `uv run pytest tests/test_planner.py -v`
Expected: PASS (new tests + all existing planner tests).

- [ ] **Step 5: Commit**

```bash
git add speedfog/planner.py tests/test_planner.py
git commit -m "planner: plan_layer_types iterates over allowed_types"
```

---

## Task 7: Filter pools in generator call sites

**Files:**
- Modify: `speedfog/generator.py` (lines ~1994, ~2577, ~612, ~573)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing integration test**

Add to `tests/test_generator.py` (assumes existing test fixtures for clusters and config):

```python
class TestAllowedTypesIntegration:
    """Integration tests for allowed_types filtering in generator."""

    def test_boss_rush_produces_only_allowed_types(self, clusters_fixture):
        """With allowed_types=[boss_arena, major_boss], no legacy/mini
        appears in any intermediate node."""
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            config = _make_test_config(
                allowed_types=["boss_arena", "major_boss"],
                legacy_dungeons=0,
                bosses=6,
                mini_dungeons=0,
                major_bosses=2,
                min_layers=5,
                max_layers=10,
            )
        dag = generate_dag(config, clusters_fixture, seed=42)
        intermediate_types = {
            node.cluster.type
            for node in dag.nodes.values()
            if node.id not in ("start", dag.get_end_node_id())
        }
        assert intermediate_types <= {"boss_arena", "major_boss"}
```

Note: adapt `_make_test_config`, `clusters_fixture`, and `generate_dag` to match the patterns already in use in `tests/test_generator.py`. If no such fixture exists, add a similar check inside `tests/test_integration.py` using the existing end-to-end generation path.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest tests/test_generator.py::TestAllowedTypesIntegration -v`
Expected: FAIL — generator still hits legacy/mini during padding or convergence.

- [ ] **Step 3: Filter the planner-call pool**

In `speedfog/generator.py` near line 1994, replace:

```python
    pool_sizes = {
        t: len(clusters.get_by_type(t))
        for t in ("mini_dungeon", "boss_arena", "legacy_dungeon")
    }
```

with:

```python
    pool_sizes = {
        t: len(clusters.get_by_type(t))
        for t in ("mini_dungeon", "boss_arena", "legacy_dungeon")
        if t in config.requirements.allowed_types
    }
```

- [ ] **Step 4: Filter the convergence pool and pass fallback**

In `speedfog/generator.py` near line 2577, replace:

```python
    conv_pool_sizes = {t: len(clusters.get_by_type(t)) for t in _FALLBACK_TYPES}
```

with:

```python
    conv_pool_sizes = {
        t: len(clusters.get_by_type(t))
        for t in _FALLBACK_TYPES
        if t in config.requirements.allowed_types
    }
    conv_fallback = config.requirements.allowed_types[0]
```

Also replace the convergence pool snapshot loop near line 2583:

```python
        for t in _FALLBACK_TYPES:
            all_conv_clusters.extend(clusters.get_by_type(t))
```

with:

```python
        for t in _FALLBACK_TYPES:
            if t in config.requirements.allowed_types:
                all_conv_clusters.extend(clusters.get_by_type(t))
```

And replace the `pick_weighted_type` call near line 2596:

```python
        conv_layer_type = pick_weighted_type(conv_pool_sizes, conv_used, rng)
```

with:

```python
        conv_layer_type = pick_weighted_type(
            conv_pool_sizes, conv_used, rng, fallback=conv_fallback
        )
```

- [ ] **Step 5: Filter the type-fallback cluster picker**

In `speedfog/generator.py` around line 612 inside `pick_cluster_with_type_fallback`, the function receives `preferred_type` but no config. Change its signature to accept `allowed_types` and filter:

```python
def pick_cluster_with_type_fallback(
    clusters: ClusterPool,
    preferred_type: str,
    used_zones: set[str],
    rng: random.Random,
    *,
    reserved_zones: frozenset[str] = frozenset(),
    allowed_types: tuple[str, ...] | None = None,
) -> ClusterData | None:
    # ... existing preferred-type attempt unchanged ...

    # Fallback: weighted random among allowed types only
    effective_allowed = (
        allowed_types if allowed_types is not None else _FALLBACK_TYPES
    )
    fallback_types = [
        t for t in _FALLBACK_TYPES
        if t != preferred_type and t in effective_allowed
    ]
    # ... rest unchanged ...
```

Then at every call site of `pick_cluster_with_type_fallback` inside `generator.py`, add the argument. Find call sites with:

```bash
grep -n "pick_cluster_with_type_fallback" speedfog/generator.py
```

At each call, add `allowed_types=tuple(config.requirements.allowed_types)`.

- [ ] **Step 6: Filter `_compute_fallback_pool`**

In `speedfog/generator.py` near line 566, change:

```python
def _compute_fallback_pool(
    clusters: ClusterPool,
    used_zones: set[str],
    reserved_zones: frozenset[str],
) -> dict[str, int]:
    all_typed: list[ClusterData] = []
    for t in ("mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"):
        all_typed.extend(clusters.get_by_type(t))
    return compute_pool_remaining(all_typed, used_zones, reserved_zones)
```

to accept `allowed_types`:

```python
def _compute_fallback_pool(
    clusters: ClusterPool,
    used_zones: set[str],
    reserved_zones: frozenset[str],
    allowed_types: tuple[str, ...] = (
        "mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss",
    ),
) -> dict[str, int]:
    all_typed: list[ClusterData] = []
    for t in allowed_types:
        all_typed.extend(clusters.get_by_type(t))
    return compute_pool_remaining(all_typed, used_zones, reserved_zones)
```

At its call sites in the convergence/fallback path, pass `allowed_types=tuple(config.requirements.allowed_types)`.

- [ ] **Step 7: Run tests**

Run: `uv run pytest tests/ -v`
Expected: PASS (new integration test + all existing tests).

- [ ] **Step 8: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "generator: filter pools by allowed_types in planner and convergence"
```

---

## Task 8: Update validator for `allowed_types`

**Files:**
- Modify: `speedfog/validator.py` (`_check_requirements`)
- Test: `tests/test_validator.py`

- [ ] **Step 1: Write failing tests**

Add to `tests/test_validator.py`:

```python
class TestValidatorAllowedTypes:
    """Validator honors allowed_types."""

    def test_excluded_type_skipped_in_requirements_check(self):
        """When a type is excluded, no 'insufficient' error is emitted
        for it even if its count is 0 in the DAG."""
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            config = _make_test_config(
                allowed_types=["boss_arena", "major_boss"],
                legacy_dungeons=0,
                bosses=2,
                mini_dungeons=0,
                major_bosses=1,
            )
        dag = _make_test_dag_with_types(
            [("boss_arena", 2), ("major_boss", 1)]
        )
        errors: list[str] = []
        _check_requirements(dag, config, errors)
        assert not any("legacy" in e or "mini" in e for e in errors)

    def test_zone_in_excluded_type_raises(self):
        """A required zone whose cluster type is excluded is an error."""
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            config = _make_test_config(
                allowed_types=["boss_arena", "major_boss"],
                zones=["some_mini_dungeon_zone"],
                legacy_dungeons=0,
                mini_dungeons=0,
            )
        # Implementation detail: this check needs cluster type info, so
        # it may live in a separate validation pass. Adjust the call
        # target to whatever function validates this.
        from speedfog.validator import _check_zone_types_allowed
        errors: list[str] = []
        _check_zone_types_allowed(config, clusters_fixture, errors)
        assert any("some_mini_dungeon_zone" in e for e in errors)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_validator.py::TestValidatorAllowedTypes -v`
Expected: FAIL — `_check_zone_types_allowed` does not exist; `_check_requirements` does not filter.

- [ ] **Step 3: Update `_check_requirements`**

In `speedfog/validator.py`, modify `_check_requirements`:

```python
def _check_requirements(dag: Dag, config: Config, errors: list[str]) -> None:
    req = config.requirements

    type_checks = [
        ("legacy_dungeon", req.legacy_dungeons, "legacy_dungeons"),
        ("boss_arena", req.bosses, "bosses"),
        ("mini_dungeon", req.mini_dungeons, "mini_dungeons"),
    ]
    for cluster_type, required, label in type_checks:
        if cluster_type not in req.allowed_types:
            continue
        actual = dag.count_by_type(cluster_type)
        if actual < required:
            errors.append(f"Insufficient {label}: {actual} < {required}")

    if req.zones:
        all_zones: set[str] = set()
        for node in dag.nodes.values():
            all_zones.update(node.cluster.zones)
        for zone in req.zones:
            if zone not in all_zones:
                errors.append(f"Required zone missing: '{zone}'")
```

- [ ] **Step 4: Add `_check_zone_types_allowed`**

Append to `speedfog/validator.py`:

```python
def _check_zone_types_allowed(
    config: Config,
    clusters: ClusterPool,
    errors: list[str],
) -> None:
    """Ensure every required zone belongs to a cluster whose type is in
    allowed_types.

    A required zone whose cluster type is excluded is unreachable: the
    DAG can never include it. Flag this as a configuration error early.
    """
    allowed = set(config.requirements.allowed_types)
    for zone in config.requirements.zones:
        cluster = clusters.find_cluster_for_zone(zone)
        if cluster is None:
            # Zone existence is checked elsewhere; skip here.
            continue
        if cluster.type not in allowed:
            errors.append(
                f"Required zone '{zone}' has type '{cluster.type}' "
                f"which is not in allowed_types={sorted(allowed)}"
            )
```

If `ClusterPool` does not already have `find_cluster_for_zone`, use the existing lookup mechanism in the code (check `speedfog/clusters.py`). If none exists, iterate `clusters.all_clusters` and match `zone in cluster.zones`.

- [ ] **Step 5: Wire `_check_zone_types_allowed` into the validator entry point**

Find the function that runs all validation checks (top-level `validate_dag` or similar in `speedfog/validator.py`) and add a call to `_check_zone_types_allowed(config, clusters, errors)` alongside the existing requirement checks. Clusters must be passed in — extend the entry-point signature if needed, and update all callers in `generator.py` / `main.py` to pass `clusters`.

- [ ] **Step 6: Run tests**

Run: `uv run pytest tests/test_validator.py -v`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add speedfog/validator.py tests/test_validator.py
git commit -m "validator: honor allowed_types in requirements + zone reachability"
```

---

## Task 9: Documentation and example config

**Files:**
- Modify: `config.example.toml`
- Modify: `docs/dag-generation.md`

- [ ] **Step 1: Update `config.example.toml`**

In the `[requirements]` section of `/home/dev/src/games/ER/fog/speedfog/config.example.toml`, add after the `zones` comment block:

```toml
# Cluster types allowed in the DAG. Types not listed are excluded
# entirely: no minimum count, no padding, no convergence pick.
# The final boss is always a major_boss regardless of this list.
#
# Default: all four types.
# allowed_types = ["legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"]
#
# Example modes:
#   Boss rush:        ["boss_arena", "major_boss"]
#   Legacy marathon:  ["legacy_dungeon"]
#   Dungeon crawl:    ["mini_dungeon", "boss_arena", "major_boss"]
#   No minis:         ["legacy_dungeon", "boss_arena", "major_boss"]
#
# When a type is excluded, set its min_* to 0 (or leave at default; a
# warning is emitted if a non-zero min is ignored).
```

- [ ] **Step 2: Update `docs/dag-generation.md`**

Append a new section to `/home/dev/src/games/ER/fog/speedfog/docs/dag-generation.md`:

```markdown
## Allowed cluster types

By default, the DAG can include any of four cluster types:
`legacy_dungeon`, `mini_dungeon`, `boss_arena`, `major_boss`. The
`requirements.allowed_types` setting restricts this to a subset,
enabling modes such as boss-rush (`["boss_arena", "major_boss"]`) or
legacy-marathon (`["legacy_dungeon"]`).

Semantics:

- Only types listed in `allowed_types` participate in the DAG: they
  appear in the initial requirement list, in padding, and in
  convergence type selection.
- The per-type minimums (`legacy_dungeons`, `bosses`, `mini_dungeons`,
  `major_bosses`) apply only to types present in `allowed_types`.
  Minimums for excluded types are silently ignored; a warning is
  emitted if a non-zero minimum is ignored.
- The final boss is selected from `final_boss_candidates` and is
  always a major_boss, independent of `allowed_types`. A config like
  `allowed_types = ["mini_dungeon", "boss_arena"]` produces a DAG
  whose intermediate layers contain no major bosses but which still
  ends on a major-boss node.
- `structure.first_layer_type`, if set, must be in `allowed_types`.
```

- [ ] **Step 3: Commit**

```bash
git add config.example.toml docs/dag-generation.md
git commit -m "docs: document requirements.allowed_types"
```

---

## Task 10: End-to-end boss-rush seed generation

**Files:**
- Test: `tests/test_integration.py` (or `tests/test_generator.py`)

- [ ] **Step 1: Write the end-to-end test**

Append to `tests/test_integration.py` (match the existing integration style in that file):

```python
def test_boss_rush_integration(real_clusters_fixture):
    """End-to-end: a boss-rush config generates a DAG with only
    boss_arena / major_boss clusters in intermediate layers."""
    import warnings
    from speedfog.config import Config

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        config = Config.from_dict(
            {
                "requirements": {
                    "allowed_types": ["boss_arena", "major_boss"],
                    "legacy_dungeons": 0,
                    "bosses": 6,
                    "mini_dungeons": 0,
                    "major_bosses": 2,
                },
                "structure": {
                    "min_layers": 5,
                    "max_layers": 10,
                    "max_parallel_paths": 2,
                },
            }
        )

    dag = generate_dag(config, real_clusters_fixture, seed=42)

    # Every non-start, non-end node must be boss_arena or major_boss
    intermediate_types = {
        node.cluster.type
        for node in dag.nodes.values()
        if node.layer not in (0, max(n.layer for n in dag.nodes.values()))
    }
    assert intermediate_types <= {"boss_arena", "major_boss"}

    # Validator should not flag excluded-type shortages
    errors, _ = validate_dag(dag, config, real_clusters_fixture)
    excluded_errors = [
        e for e in errors if "legacy" in e or "mini_dungeons" in e
    ]
    assert excluded_errors == []
```

Adapt `generate_dag`, `validate_dag`, and `real_clusters_fixture` to match what is already exported from `speedfog` and used in the existing integration tests.

- [ ] **Step 2: Run the integration test**

Run: `uv run pytest tests/test_integration.py::test_boss_rush_integration -v`
Expected: PASS.

- [ ] **Step 3: Run the full test suite**

Run: `uv run pytest -v`
Expected: PASS (all tests).

- [ ] **Step 4: Commit**

```bash
git add tests/test_integration.py
git commit -m "test: end-to-end boss-rush integration test"
```

---

## Final Review

- [ ] **Review all commits**: `git log --oneline master..HEAD`
- [ ] **Verify spec coverage**: Each section of `docs/specs/2026-04-13-allowed-types-design.md` has at least one implementing task. Config surface (Task 1, 3), semantics (Task 2, 6), validation (Task 1, 4, 8), final boss independence (Task 10), planner (Task 5, 6), generator (Task 7), validator (Task 8), docs (Task 9), tests (all tasks).
- [ ] **Run the full suite one more time**: `uv run pytest -v`
- [ ] **Launch a code review** via the code-reviewer agent before merging, per `CLAUDE.md`.
