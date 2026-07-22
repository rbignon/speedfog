# Vanilla Warp Removal

**Date:** 2026-02-26
**Status:** Active

Workaround for a FogMod bug where vanilla one-way warps (coffins, DLC transitions) persist in the game world despite being marked for removal.

## Problem

FogMod tags unique warps with `"remove"` in its graph data, but its removal logic in `GameDataWriterE` compares `o.Name == e.Name` where `e.Name` is an entity ID string (e.g., `"2046402020"`), not an MSB Part.Asset name (e.g., `"AEG099_060_9000"`). The comparison always fails, so vanilla warp assets remain in the MSB and the player can use them to bypass the randomized graph.

## Solution

`VanillaWarpRemover` runs as a post-processing step after FogMod writes its output. It removes Part.Asset entries from MSB files by matching on `EntityID` (an integer field), not the string name.

## Data Flow

1. **Python** (`graph_export.py`): Collects `unique_exit_fogs` and unused regular exit fogs from each cluster. Emits `remove_entities` in `graph.json` as `[{"map": "m12_05_00_00", "entity_id": 2046402020}, ...]`.
2. **C#** (`GraphData.RemoveEntities`): Deserializes the list into `List<RemoveEntity>`.
3. **C#** (`VanillaWarpRemover.Remove()`): Groups entities by map, reads each MSB once, removes matching Part.Asset entries, and writes the MSB back.

## Implementation Details

- **Group by map**: Avoids reading/writing the same MSB multiple times when several entities share a map.
- **ObjAct cleanup**: ObjAct events reference part names. If a removed asset is referenced by an ObjAct, `MSB.Write()` would fail. The remover also removes these ObjAct events (same pattern as FogRando `GameDataWriterE.cs:574`).
- **MSB directory casing**: Handles both `MapStudio` (vanilla) and `mapstudio` (Wine/FogMod) directory names.
- **Missing maps**: Maps not in the mod output (not part of this seed's graph) are silently skipped.

## Matching by EntityGroup (`match_group`)

Besides the data-driven `remove_entities` from `graph.json`, `data/game_tweaks.toml`
carries a small `[[remove_entities]]` list (map + entity_id + optional
`match_group`) that is concatenated onto `graph.json`'s list before the
`VanillaWarpRemover.Remove` call in `Program.cs`:

- `m20_01_00_00 / 20006662` (by EntityGroup, `match_group = true`): the Outer Wall
  thorns barrier in Enir-Ilim (part of the map-splits climb, see
  `docs/map-splits.md`). FogMod CREATES this barrier as two assets
  (`AEG410_901`, `AEG410_905`) sharing EntityGroup `20006662`, and only disables
  it when flag 330 is ON, which SpeedFog keeps off. It has no per-asset EntityID
  to match (FogMod assigns it dynamically during its own write pass), so the
  removal uses `RemoveEntity.MatchGroup` to match `EntityGroupIDs` instead of
  `EntityID`.

`RemoveEntity.MatchGroup` (`writer/FogModWrapper.Core/Models/GraphData.cs`)
selects between the two matching strategies in `VanillaWarpRemover.RemoveFromMap`:
entries with `MatchGroup = false` (the `graph.json`-sourced default) are matched
by `EntityID`; entries with `MatchGroup = true` are matched against the asset's
`EntityGroupIDs` array instead. Both matching strategies run in the same
`Remove()` pass, since `ctx.Tweaks.RemoveEntities` (from `GameTweaksLoader`) is
concatenated onto `graph.json`'s `RemoveEntities` before the call (`Program.cs`).

This runs post-`Write`, i.e. after FogMod's `GameDataWriterE.Write()` has
already created the thorns assets: `VanillaWarpRemover` operates on the MSBs
FogMod just wrote, not on `AnnotationData` before construction.

This is a no-op when `m20_01` is absent from the seed (`VanillaWarpRemover`
skips maps not present in the output).

## Future

If FogMod fixes the upstream `o.Name == e.Name` comparison to use EntityID, this workaround can be removed entirely.
