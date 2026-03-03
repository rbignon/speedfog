# Major Bosses as Explicit Requirement

**Date:** 2026-03-03
**Status:** Approved

## Problem

`major_boss_ratio` (float in `[structure]`) replaces layer types post-shuffle, overwriting
required types like `mini_dungeon`. With `mini_dungeons = 10` and `major_boss_ratio = 0.3`,
some mini_dungeon layers get replaced by major_boss, resulting in only ~7 mini_dungeons
per path instead of the requested 10.

## Solution

Replace `major_boss_ratio` (float, `[structure]`) with `major_bosses` (int, `[requirements]`).
Major bosses are added to the initial plan alongside other types, then shuffled together.
The post-hoc replacement block is removed entirely.

## Changes

### Config (`config.py`)

- Add `major_bosses: int = 8` to `RequirementsConfig`
- Remove `major_boss_ratio: float` from `StructureConfig`
- Update TOML loading accordingly

### Planner (`planner.py`)

- `plan_layer_types`: add `major_boss × N` to initial plan (like other types)
- Remove `major_boss_ratio` parameter
- Remove the post-hoc replacement block (lines 186-194)
- Padding continues to exclude `major_boss` (already implicit via `pool_sizes`)

### Generator (`generator.py`)

- Remove `major_boss_ratio` from `plan_layer_types` call
- Remove `major_boss_ratio` range validation (replace with `major_bosses >= 0`)

### Config files

- `config.example.toml`: replace `major_boss_ratio` with `major_bosses` in `[requirements]`
- External config (racing): update `major_boss_ratio = 0.3` → `major_bosses = 8`

### Tests

- Config tests: adapt parsing/default assertions (int instead of float)
- Planner tests: rewrite ratio-based tests to count-based
- Generator tests: rewrite ratio validation tests to int validation

### Documentation

- `docs/dag-generation.md`: update references to `major_boss_ratio`
