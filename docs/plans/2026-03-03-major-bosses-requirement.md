# Major Bosses as Explicit Requirement — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace `major_boss_ratio` (float, post-hoc replacement) with `major_bosses` (int, `[requirements]`) so major bosses are planned alongside other types and don't overwrite guaranteed requirements.

**Architecture:** Add `major_bosses` to `RequirementsConfig`, include them in the initial plan list in `plan_layer_types()`, remove the post-hoc replacement block. Padding continues to exclude `major_boss` (not in `pool_sizes`).

**Tech Stack:** Python (speedfog), pytest

---

### Task 1: Update RequirementsConfig

**Files:**
- Modify: `speedfog/config.py:34-40` (RequirementsConfig dataclass)
- Modify: `speedfog/config.py:425-429` (from_dict parsing)
- Modify: `speedfog/config.py:58` (remove major_boss_ratio from StructureConfig)
- Modify: `speedfog/config.py:443` (remove major_boss_ratio from TOML loading)
- Test: `tests/test_config.py`

**Step 1: Write the failing tests**

In `tests/test_config.py`, update existing tests:

```python
# In test_config_defaults (line 28): change assertion
def test_config_defaults():
    config = Config.from_dict({})
    # ... existing assertions ...
    assert config.requirements.major_bosses == 8  # NEW

# In test_structure_defaults (line 128): remove major_boss_ratio assertion
def test_structure_defaults():
    config = Config.from_dict({})
    # REMOVE: assert config.structure.major_boss_ratio == 0.0

# In test_structure_new_options (lines 135-151): remove major_boss_ratio
def test_structure_new_options(tmp_path):
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
first_layer_type = "legacy_dungeon"
final_boss_candidates = ["caelid_radahn", "haligtree_malenia", "leyndell_erdtree"]
""")
    config = Config.from_toml(config_file)
    assert config.structure.first_layer_type == "legacy_dungeon"
    # REMOVE: assert config.structure.major_boss_ratio == 0.2
    assert config.structure.final_boss_candidates == [
        "caelid_radahn", "haligtree_malenia", "leyndell_erdtree",
    ]

# NEW test:
def test_major_bosses_from_toml(tmp_path):
    """major_bosses is parsed from requirements section."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[requirements]
major_bosses = 5
""")
    config = Config.from_toml(config_file)
    assert config.requirements.major_bosses == 5

def test_major_bosses_default():
    """major_bosses defaults to 8."""
    config = Config.from_dict({})
    assert config.requirements.major_bosses == 8
```

**Step 2: Run tests to verify they fail**

Run: `pytest tests/test_config.py -v -k "major_boss or config_defaults or structure_defaults or structure_new_options"`
Expected: FAIL (major_bosses not defined, major_boss_ratio still present)

**Step 3: Implement the config changes**

In `speedfog/config.py`:

1. Add `major_bosses: int = 8` to `RequirementsConfig` (line 39, before `zones`):
```python
@dataclass
class RequirementsConfig:
    """Zone requirements configuration."""
    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5
    major_bosses: int = 8
    zones: list[str] = field(default_factory=list)
```

2. Remove `major_boss_ratio: float = 0.0` from `StructureConfig` (line 58)

3. Update `from_dict` requirements parsing (line 425-429) to include:
```python
major_bosses=requirements_section.get("major_bosses", 8),
```

4. Remove `major_boss_ratio=structure_section.get(...)` from structure parsing (line 443)

**Step 4: Run tests to verify they pass**

Run: `pytest tests/test_config.py -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "refactor: move major_bosses from structure ratio to requirements count"
```

---

### Task 2: Update planner

**Files:**
- Modify: `speedfog/planner.py:128-196` (plan_layer_types function)
- Test: `tests/test_planner.py`

**Step 1: Write the failing tests**

Replace the 4 ratio-based tests in `tests/test_planner.py` (lines 388-419):

```python
def test_major_bosses_zero_no_major_boss(self):
    """With major_bosses=0, no major_boss should appear."""
    reqs = RequirementsConfig(legacy_dungeons=1, bosses=2, mini_dungeons=2, major_bosses=0)
    rng = random.Random(42)
    result = plan_layer_types(reqs, total_layers=10, rng=rng)
    assert "major_boss" not in result

def test_major_bosses_included_in_plan(self):
    """major_bosses are included in the plan like other types."""
    reqs = RequirementsConfig(legacy_dungeons=1, bosses=2, mini_dungeons=2, major_bosses=3)
    rng = random.Random(42)
    result = plan_layer_types(reqs, total_layers=10, rng=rng)
    assert result.count("major_boss") >= 3

def test_major_bosses_do_not_overwrite_requirements(self):
    """major_bosses should not reduce the count of other required types."""
    reqs = RequirementsConfig(legacy_dungeons=2, bosses=5, mini_dungeons=10, major_bosses=8)
    rng = random.Random(42)
    result = plan_layer_types(reqs, total_layers=25, rng=rng)
    assert result.count("legacy_dungeon") >= 2
    assert result.count("boss_arena") >= 5
    assert result.count("mini_dungeon") >= 10
    assert result.count("major_boss") >= 8

def test_major_bosses_exact_count_no_padding(self):
    """When requirements exactly fill total_layers, counts are exact."""
    reqs = RequirementsConfig(legacy_dungeons=2, bosses=3, mini_dungeons=3, major_bosses=2)
    rng = random.Random(42)
    result = plan_layer_types(reqs, total_layers=10, rng=rng)
    assert len(result) == 10
    assert result.count("legacy_dungeon") == 2
    assert result.count("boss_arena") == 3
    assert result.count("mini_dungeon") == 3
    assert result.count("major_boss") == 2

def test_major_bosses_excluded_from_padding(self):
    """Padding should not add extra major_boss layers."""
    reqs = RequirementsConfig(legacy_dungeons=0, bosses=0, mini_dungeons=0, major_bosses=2)
    rng = random.Random(42)
    result = plan_layer_types(reqs, total_layers=10, rng=rng)
    assert result.count("major_boss") == 2  # Only the required ones
```

**Step 2: Run tests to verify they fail**

Run: `pytest tests/test_planner.py -v -k "major_boss"`
Expected: FAIL (plan_layer_types still uses major_boss_ratio parameter)

**Step 3: Implement the planner changes**

In `speedfog/planner.py`, update `plan_layer_types()`:

1. Remove `major_boss_ratio` parameter (line 132)
2. Add `major_boss` to the initial plan list (after line 160):
```python
layer_types.extend(["major_boss"] * requirements.major_bosses)
```
3. Add `major_boss` to `required_counts` dict (line 170-174):
```python
required_counts = {
    "legacy_dungeon": requirements.legacy_dungeons,
    "boss_arena": requirements.bosses,
    "mini_dungeon": requirements.mini_dungeons,
    "major_boss": requirements.major_bosses,
}
```
4. Delete the entire post-hoc replacement block (lines 186-194)
5. Update the docstring to remove ratio references

**Step 4: Run tests to verify they pass**

Run: `pytest tests/test_planner.py -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/planner.py tests/test_planner.py
git commit -m "refactor: plan major_bosses as initial requirement, remove post-hoc ratio"
```

---

### Task 3: Update generator validation

**Files:**
- Modify: `speedfog/generator.py:58-63` (validate_config)
- Modify: `speedfog/generator.py:1270-1276` (plan_layer_types call)
- Test: `tests/test_generator.py`

**Step 1: Write the failing tests**

In `tests/test_generator.py`, replace the 3 ratio-based validation tests (lines 632-656):

```python
def test_major_bosses_negative_validation(self):
    """Negative major_bosses returns error."""
    pool = make_cluster_pool()
    config = Config()
    config.requirements.major_bosses = -1
    errors = validate_config(config, pool, _boss_candidates(pool))
    assert len(errors) == 1
    assert "major_bosses" in errors[0]

def test_major_bosses_zero_valid(self):
    """major_bosses=0 is valid (no major bosses)."""
    pool = make_cluster_pool()
    config = Config()
    config.requirements.major_bosses = 0
    errors = validate_config(config, pool, _boss_candidates(pool))
    assert errors == []

def test_major_bosses_positive_valid(self):
    """Positive major_bosses is valid."""
    pool = make_cluster_pool()
    config = Config()
    config.requirements.major_bosses = 8
    errors = validate_config(config, pool, _boss_candidates(pool))
    assert errors == []
```

Also update `test_multiple_errors_returned` (line 684-692): remove `config.structure.major_boss_ratio = 2.0`, replace with `config.requirements.major_bosses = -1`, and update expected error count.

**Step 2: Run tests to verify they fail**

Run: `pytest tests/test_generator.py -v -k "major_boss"`
Expected: FAIL

**Step 3: Implement the generator changes**

In `speedfog/generator.py`:

1. Replace major_boss_ratio validation (lines 58-63) with:
```python
# Validate major_bosses
if config.requirements.major_bosses < 0:
    errors.append(
        f"major_bosses must be >= 0, got {config.requirements.major_bosses}"
    )
```

2. Remove `major_boss_ratio=config.structure.major_boss_ratio` from `plan_layer_types()` call (line 1274)

**Step 4: Run tests to verify they pass**

Run: `pytest tests/test_generator.py -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "refactor: validate major_bosses as int requirement in generator"
```

---

### Task 4: Update config files and tools

**Files:**
- Modify: `config.example.toml`
- Modify: `reduced_merge.toml`
- Modify: `placi.toml` (commented reference)
- Modify: `tools/simulate_cluster_first.py`
- Modify: `tools/analyze_correlation_and_failures.py`
- Modify: `tools/analyze_zone_distribution.py`

**Step 1: Update config.example.toml**

In `[requirements]` section (after line 33), add:
```toml
# Number of major boss encounters (Godrick, Radahn, etc.)
# in intermediate layers.
major_bosses = 8
```

Remove lines 99-102 from `[structure]` section:
```toml
# Ratio of intermediate layers that can contain a major boss (0.0-1.0).
# 0.0 = no intermediate major bosses (default)
# 0.2 = roughly 20% of layers can be major bosses
# major_boss_ratio = 0.2
```

**Step 2: Update reduced_merge.toml**

Replace `major_boss_ratio = 0.3` (line 33 in `[structure]`) with `major_bosses = 8` in `[requirements]`.

**Step 3: Update placi.toml**

Remove commented `# major_boss_ratio = 0.2` line.

**Step 4: Update tools**

In `tools/simulate_cluster_first.py`:
- Line 52: change `"major_boss_ratio": 0.3` → move to requirements as `"major_bosses": 8`
- Line 144: remove `major_boss_ratio=config.structure.major_boss_ratio` from `plan_layer_types()` call

In `tools/analyze_correlation_and_failures.py`:
- Line 35: change `"major_boss_ratio": 0.3` → move to requirements as `"major_bosses": 8`

In `tools/analyze_zone_distribution.py`:
- Line 47: change `"major_boss_ratio": 0.3` → move to requirements as `"major_bosses": 8`
- Line 114: change `major_boss_ratio` print to `major_bosses`

**Step 5: Commit**

```bash
git add config.example.toml reduced_merge.toml placi.toml tools/simulate_cluster_first.py tools/analyze_correlation_and_failures.py tools/analyze_zone_distribution.py
git commit -m "refactor: update configs and tools for major_bosses requirement"
```

---

### Task 5: Update documentation

**Files:**
- Modify: `docs/dag-generation.md`

**Step 1: Update dag-generation.md**

Line 212: change to `layer_types = plan_layer_types(requirements, num_layers, rng)`

Lines 215-219: update description to:
```
`plan_layer_types()` builds a list from:
- Required legacy dungeons, bosses, mini dungeons, major bosses (from config)
- Pad with mini_dungeons/boss_arenas/legacy_dungeons or trim to fit `num_layers`
- Shuffle for randomness
```

Line 354 in config table: replace `structure.major_boss_ratio` row with:
```
| `requirements.major_bosses` | 8 | Number of major boss layers |
```

**Step 2: Commit**

```bash
git add docs/dag-generation.md
git commit -m "docs: update dag-generation for major_bosses requirement"
```

---

### Task 6: Run full test suite and verify

**Step 1: Run all Python tests**

Run: `pytest -v`
Expected: All PASS

**Step 2: Generate a seed to verify behavior**

Run: `uv run speedfog config.example.toml --spoiler`

Verify: the spoiler output shows major_boss nodes distributed among other types, and mini_dungeon/boss_arena/legacy_dungeon counts match requirements.

**Step 3: Verify with racing config**

The racing config at `../speedfog-racing/tools/output/standard/config.toml` will need `major_boss_ratio` replaced with `major_bosses = 8` in `[requirements]`. This is an external file — note it for the user.
