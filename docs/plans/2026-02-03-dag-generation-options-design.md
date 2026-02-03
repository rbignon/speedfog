# DAG Generation Options Design

## Overview

Add configuration options to control DAG generation:
1. Force first layer after chapel_start to be legacy_dungeons
2. Allow random final boss selection from configurable candidates
3. Define intermediate major boss ratio

## Configuration

New options in `[structure]` section of `config.toml`:

```toml
[structure]
# Type of cluster for the first layer after start
# Options: "legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"
# If omitted or empty: current behavior (random based on requirements)
first_layer_type = "legacy_dungeon"

# Ratio of intermediate layers that can be major_boss
# 0.0 = none, 0.2 = ~20%, 1.0 = all (not recommended)
major_boss_ratio = 0.2

# Candidates for final boss (zone names, not cluster IDs)
# One will be chosen randomly. If omitted/empty: ["leyndell_erdtree"]
final_boss_candidates = [
    "leyndell_erdtree",
    "caelid_radahn",
    "haligtree_malenia",
    "mohgwyn_boss",
    "volcano_rykard",
    "farumazula_maliketh",
    "flamepeak_firegiant",
]
```

### Defaults (backward compatible)

| Option | Default | Behavior |
|--------|---------|----------|
| `first_layer_type` | `null` | Random based on requirements |
| `major_boss_ratio` | `0.0` | No intermediate major bosses |
| `final_boss_candidates` | `[]` | Radagon only |

## Implementation

### 1. config.py

Update `StructureConfig`:

```python
@dataclass
class StructureConfig:
    # ... existing fields ...

    first_layer_type: str | None = None
    major_boss_ratio: float = 0.0
    final_boss_candidates: list[str] = field(default_factory=list)

    @property
    def effective_final_boss_candidates(self) -> list[str]:
        """Return candidates or default if empty."""
        return self.final_boss_candidates or ["leyndell_erdtree"]
```

### 2. planner.py

Update `plan_layer_types()` to accept `major_boss_ratio`:

```python
def plan_layer_types(
    requirements: RequirementsConfig,
    num_layers: int,
    rng: random.Random,
    major_boss_ratio: float = 0.0,
) -> list[str]:
    # Calculate how many layers can be major_boss
    num_major_boss_slots = int(num_layers * major_boss_ratio)

    # Randomly select which layers will be major_boss
    # (avoid last layer, reserved for merge to final_boss)
    eligible_indices = list(range(num_layers - 1))
    major_boss_indices = set(rng.sample(
        eligible_indices,
        min(num_major_boss_slots, len(eligible_indices))
    ))

    # Build layer types list
    for i in range(num_layers):
        if i in major_boss_indices:
            layer_types.append("major_boss")
        else:
            # Existing logic for other types
            ...
```

### 3. generator.py

#### First layer handling

After creating start node and initializing branches:

```python
current_layer = 1
if config.structure.first_layer_type:
    first_type = config.structure.first_layer_type
    tier = compute_tier(current_layer, estimated_total)

    branches = execute_passant_layer(
        dag, branches, current_layer, tier,
        first_type,
        clusters, used_zones, rng,
    )
    current_layer += 1
```

#### Final boss selection

Replace fixed final_boss lookup:

```python
final_candidates = config.structure.effective_final_boss_candidates
end_cluster = None
rng.shuffle(final_candidates)

for zone_name in final_candidates:
    for cluster in clusters.get_by_type("major_boss") + clusters.get_by_type("final_boss"):
        if zone_name in cluster.zones:
            if not any(z in used_zones for z in cluster.zones):
                end_cluster = cluster
                break
    if end_cluster:
        break

if end_cluster is None:
    raise GenerationError(
        f"No available final boss from candidates: {final_candidates}"
    )

# Final boss: entry mapped, NO exits mapped
end_node = DagNode(
    id="end",
    cluster=end_cluster,
    layer=current_layer,
    tier=28,
    entry_fogs=[...],
    exit_fogs=[],  # No exits - end of run
)
```

### 4. Validation

Add config validation:

```python
def validate_config(config: Config, clusters: ClusterPool) -> list[str]:
    errors = []

    valid_types = {"legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"}
    if config.structure.first_layer_type:
        if config.structure.first_layer_type not in valid_types:
            errors.append(f"Invalid first_layer_type: {config.structure.first_layer_type}")

    for zone in config.structure.effective_final_boss_candidates:
        found = any(
            zone in cluster.zones
            for cluster in clusters.get_by_type("major_boss") + clusters.get_by_type("final_boss")
        )
        if not found:
            errors.append(f"Unknown final_boss candidate zone: {zone}")

    if not 0.0 <= config.structure.major_boss_ratio <= 1.0:
        errors.append("major_boss_ratio must be 0.0-1.0")

    return errors
```

## Files to Modify

1. `speedfog/config.py` - New options + property
2. `speedfog/planner.py` - Integrate major_boss_ratio
3. `speedfog/generator.py` - First layer + final boss selection
4. `config.example.toml` - Document new options

## Edge Cases

- `first_layer_type = "legacy_dungeon"` with 3 initial branches but only 2 legacy_dungeons available: generation error (acceptable)
- All final_boss candidates already used in DAG: generation error with clear message
- Final boss with exits: exits are ignored (exit_fogs=[])
