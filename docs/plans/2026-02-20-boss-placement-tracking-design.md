# Boss Placement Tracking in graph.json

## Goal

When `enemy.randomize_bosses` is enabled, track which boss the randomizer placed in each arena and expose this information per-node in `graph.json` for spoiler/visualization use.

## Data Flow

```
ItemRandomizerWrapper (C#)
  |
  +-- Calls Randomizer.Randomize()
  +-- Captures stdout during execution (Console.SetOut)
  +-- Parses "Replacing {name} (#{target_id}) in ...: {name} (#{source_id}) from ..." lines
  +-- Writes boss_placements.json to output dir:
       {
         "14000850": {"name": "Rennala Queen of the Full Moon", "entity_id": 12010800},
         ...
       }
       Key = arena entity_id (original boss), value = placed boss info

Python (main.py)
  |
  +-- Reads boss_placements.json if present
  +-- For each DAG node: if node.cluster.defeat_flag in boss_placements,
      sets node["randomized_boss"] = placed boss name
  +-- Writes enriched graph.json
```

## graph.json Format (per node)

```json
{
  "nodes": {
    "stormveil_godrick": {
      "type": "major_boss",
      "display_name": "Stormveil Castle",
      "randomized_boss": "Rennala Queen of the Full Moon",
      ...
    }
  }
}
```

`randomized_boss` is absent when boss shuffling is disabled or the boss stayed vanilla.

## Matching Logic

The randomizer's `fullName()` outputs `{name} (#{entity_id}) in {location}`. The entity ID in the `Replacing` line corresponds to the `defeat_flag` in `clusters.json` for most bosses. Special cases:

- Radahn, Fire Giant: `defeat_flag = entity_id + 200_000_000` (already handled in BuildEnemyPreset)
- DLC bosses (>= 2B): `defeat_flag == entity_id`
- All others: `defeat_flag == entity_id`

Python matching: check `defeat_flag` directly, then try `defeat_flag - 200_000_000` as fallback.

## Implementation Changes

### C# - ItemRandomizerWrapper/Program.cs

- Redirect `Console.Out` to a `StringWriter` during `Randomize()`
- Parse `Replacing` lines with regex: `Replacing .+ \(#(\d+)\) .+: (.+) \(#(\d+)\) from`
- Write `boss_placements.json` to output dir
- Restore `Console.Out` and re-log captured output

### Python - speedfog/output.py

- `dag_to_dict()` accepts optional `boss_placements: dict[str, dict]` parameter
- For each node, if `cluster.defeat_flag` matches a key (or key + 200M), add `randomized_boss` field

### Python - speedfog/main.py

- After `run_item_randomizer()`, read `boss_placements.json` from output dir
- Pass placements to `dag_to_dict()` / `export_json()`

### Python - speedfog/output.py (spoiler log)

- `export_spoiler_log()` accepts optional boss_placements
- Add "BOSS PLACEMENTS" section listing arena -> boss

## What Does NOT Change

- `GraphData.cs` does not need to read `randomized_boss` (display-only, unknown fields ignored)
- No new fields in `clusters.json`
- Generation flow unchanged (Python -> ItemRandomizer -> FogModWrapper)
