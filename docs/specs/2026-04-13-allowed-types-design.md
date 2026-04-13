# Allowed Cluster Types in Requirements

## Context

SpeedFog currently forces every DAG to include a mix of four cluster types
(legacy dungeons, mini-dungeons, boss arenas, major bosses). The
`requirements` section exposes a minimum count for each type, but there is
no way to forbid a type entirely. Roger wants a "boss rush" config that
only contains boss arenas and major bosses, and more generally a flexible
way to describe which types are in scope for a run.

## Goal

Let a config declare a whitelist of cluster types. Types outside the list
are excluded from the DAG completely: no initial requirement, no padding,
no convergence pick. The final boss node (always a major boss, chosen from
`final_boss_candidates`) is independent of this whitelist.

## Non-goals

- Soft caps on how many clusters of a given type appear (no `max_*`). The
  door remains open for this later, but no identified use case needs it
  yet.
- Preset mechanism (`run.preset = "boss_rush"`). Can be layered on top
  later if desired.
- Any change to final-boss selection, scaling tiers, or other structural
  knobs.

## Design

### Config surface

A single new field in `[requirements]`:

```toml
[requirements]
allowed_types = ["boss_arena", "major_boss"]
```

Default value: `["legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"]`
(all four types, which reproduces current behavior).

### Semantics

`allowed_types` is the source of truth for which cluster types participate
in the DAG. The minimum counts (`legacy_dungeons`, `bosses`, `mini_dungeons`,
`major_bosses`) only apply to types present in `allowed_types`. Minimums
for excluded types are silently ignored.

This means the boss-rush config is just:

```toml
[requirements]
allowed_types = ["boss_arena", "major_boss"]
bosses = 10
major_bosses = 3
```

The default `legacy_dungeons = 1` and `mini_dungeons = 5` are not applied
because those types are not in `allowed_types`. No zeroing, no error, no
special case for "default vs explicit".

### Final boss independence

The terminal major-boss node is selected via `final_boss_candidates` and
placed as a separate step in the generator. This is unchanged. A config
with `allowed_types = ["mini_dungeon", "boss_arena"]` still produces a
DAG that ends on a major boss, even though no intermediate major-boss
clusters appear. This is the intended behavior and enables use cases
like "mini-dungeon crawl ending on Radagon".

### Validation rules

Enforced at config load time:

1. `allowed_types` must be a non-empty list.
2. Every entry must be one of
   `{"legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"}`.
3. If `structure.first_layer_type` is set, it must be in `allowed_types`
   (no sense starting on a type the rest of the DAG excludes).
4. If any `min_*` is set for a type that is not in `allowed_types` and
   that minimum is non-zero, emit a warning (not an error). The minimum
   is ignored regardless.
5. Required zones (`requirements.zones`) must belong to clusters whose
   type is in `allowed_types`. Otherwise the zone is unreachable, so
   emit an error.

### Example use cases

| Mode | `allowed_types` |
|------|-----------------|
| Vanilla (today) | all 4 types (default) |
| Boss rush | `boss_arena`, `major_boss` |
| Pure boss rush (minibosses only, one final) | `boss_arena` |
| Dungeon crawl | `mini_dungeon`, `boss_arena`, `major_boss` |
| Legacy marathon | `legacy_dungeon` |
| No minis | `legacy_dungeon`, `boss_arena`, `major_boss` |

## Implementation

### `speedfog/config.py`

- Add `allowed_types: list[str]` to `RequirementsConfig`, default to the
  four-type list.
- In `RequirementsConfig.__post_init__`:
  - Validate the list is non-empty and all entries are known types.
  - Emit warnings (via `warnings.warn` or `logging`) for `min_*` > 0 on
    excluded types.
- In `Config.from_dict`, pass through the TOML value for `allowed_types`.
- Validation of `first_layer_type` vs `allowed_types` happens in
  `Config.__post_init__` (needs access to both sub-configs).
- Validation of `requirements.zones` vs `allowed_types` requires cluster
  data, so it stays in the existing zone-check path inside the
  generator / validator where clusters are loaded.

Expose a small helper for consumers:

```python
def required_count(self, cluster_type: str) -> int:
    """Return the minimum count for a cluster type, or 0 if excluded."""
    if cluster_type not in self.allowed_types:
        return 0
    mapping = {
        "legacy_dungeon": self.legacy_dungeons,
        "boss_arena": self.bosses,
        "mini_dungeon": self.mini_dungeons,
        "major_boss": self.major_bosses,
    }
    return mapping[cluster_type]
```

### `speedfog/planner.py`

- `plan_layer_types` currently builds the required list by hardcoded
  extension of four counts. Replace with an iteration over
  `allowed_types` that calls `required_count`. Types outside the list
  contribute zero elements.
- `_distribute_padding` receives `pool_sizes` that must already be
  filtered by `allowed_types` at the call site. The function itself
  stays type-agnostic, so no change needed internally.
- `pick_weighted_type` currently falls back to the literal
  `"mini_dungeon"` when every pool is empty. Change the signature to
  accept a `fallback: str` argument, and have callers pass the first
  entry of `allowed_types` (or any sensible deterministic choice from
  the allowed list).

### `speedfog/generator.py`

Two call sites need to filter pools by `allowed_types`:

1. **Planner call** (around line 2000): `pool_sizes` passed to
   `plan_layer_types` must be restricted to `allowed_types`.
2. **Convergence** (around line 2577): `conv_pool_sizes` must be
   restricted to `allowed_types`, and the new `fallback` argument to
   `pick_weighted_type` must use a type from `allowed_types`.

Additionally, `_FALLBACK_TYPES` or any other hardcoded type iteration
used during cluster selection needs to honor the whitelist.

### `speedfog/validator.py`

- `_check_requirements`: skip the per-type check for types not in
  `allowed_types` (count-of-zero is trivially satisfied, but the check
  should not emit a misleading "insufficient" error).
- `_check_requirements`: add the zone-type reachability check from rule
  5 of the validation section.

### `config.example.toml`

Document the new field with examples covering at least boss-rush and
legacy-marathon modes.

### `docs/dag-generation.md`

Add a short section explaining `allowed_types` and how it interacts
with the final boss (which remains a major boss regardless).

## Testing

New tests:

- `config`: `allowed_types` defaults to all four types; custom list is
  parsed; invalid entry raises; empty list raises; `first_layer_type`
  not in `allowed_types` raises.
- `config`: explicit `mini_dungeons = 3` with `allowed_types` excluding
  `mini_dungeon` emits a warning; generation proceeds with 0.
- `planner`: `plan_layer_types` with a restricted `allowed_types`
  produces only allowed types in both the required list and the
  padding.
- `planner`: `pick_weighted_type` fallback uses the provided argument,
  not a hardcoded constant.
- `generator`: end-to-end boss-rush seed generation produces a DAG
  where every non-final node is boss_arena or major_boss, and the
  final node is major_boss.
- `generator`: dungeon-crawl mode (`["mini_dungeon", "boss_arena",
  "major_boss"]`) produces no legacy dungeons.
- `validator`: a DAG whose intermediate layers contain a type outside
  `allowed_types` is rejected (defense in depth; planner/generator
  should never produce this, but the validator should catch
  regressions).
- `validator`: a required zone whose cluster type is excluded produces
  a clear error.

Existing tests must keep passing with the default `allowed_types`
containing all four types.

## Risks and open questions

- **Pool exhaustion**: with only boss_arena + major_boss allowed, total
  available clusters drop significantly. `min_layers` and the
  requirement counts must be compatible with the available pool.
  Existing generation-failure paths will surface this as a seed retry
  or error, which is the correct behavior.
- **Convergence fallback**: replacing the hardcoded `"mini_dungeon"`
  fallback in `pick_weighted_type` is a behavioral change for
  non-whitelisted runs too, but only in the degenerate case where every
  pool is empty, which already indicates a failed generation.
- **Warnings channel**: we need to pick a consistent mechanism for the
  "min ignored" warning. If the project already uses `logging`, use
  that; otherwise `warnings.warn` is acceptable. To verify during
  implementation.
