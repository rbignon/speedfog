# Vanilla Warp Removal

**Date:** 2026-02-26
**Status:** Active

Workaround for a FogMod bug where vanilla one-way warps (coffins, DLC transitions) persist in the game world despite being marked for removal.

## Problem

FogMod tags unique warps with `"remove"` in its graph data, but its removal logic in `GameDataWriterE` compares `o.Name == e.Name` where `e.Name` is an entity ID string (e.g., `"2046402020"`), not an MSB Part.Asset name (e.g., `"AEG099_060_9000"`). The comparison always fails, so vanilla warp assets remain in the MSB and the player can use them to bypass the randomized graph.

## Solution

`VanillaWarpRemover` runs as a post-processing step after FogMod writes its output. It removes Part.Asset entries from MSB files by matching on `EntityID` (an integer field), not the string name.

## Data Flow

1. **Python** (`output.py`): Collects `unique_exit_fogs` and unused regular exit fogs from each cluster. Emits `remove_entities` in `graph.json` as `[{"map": "m12_05_00_00", "entity_id": 2046402020}, ...]`.
2. **C#** (`GraphData.RemoveEntities`): Deserializes the list into `List<RemoveEntity>`.
3. **C#** (`VanillaWarpRemover.Remove()`): Groups entities by map, reads each MSB once, removes matching Part.Asset entries, and writes the MSB back.

## Implementation Details

- **Group by map**: Avoids reading/writing the same MSB multiple times when several entities share a map.
- **ObjAct cleanup**: ObjAct events reference part names. If a removed asset is referenced by an ObjAct, `MSB.Write()` would fail. The remover also removes these ObjAct events (same pattern as FogRando `GameDataWriterE.cs:574`).
- **MSB directory casing**: Handles both `MapStudio` (vanilla) and `mapstudio` (Wine/FogMod) directory names.
- **Missing maps**: Maps not in the mod output (not part of this seed's graph) are silently skipped.

## Future

If FogMod fixes the upstream `o.Name == e.Name` comparison to use EntityID, this workaround can be removed entirely.
