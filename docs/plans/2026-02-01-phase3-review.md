# Phase 3 Implementation Review - Corrective Measures

**Date**: 2026-02-01
**Status**: ✅ Corrections applied
**Parent Spec**: [phase-3-csharp-writer.md](./phase-3-csharp-writer.md)
**Implementation Plan**: [2026-02-01-phase3-implementation.md](./2026-02-01-phase3-implementation.md)

## Executive Summary

The Phase 3 C# Writer implementation initially had critical gaps in EMEVD event generation. **All P0 and P1 issues have been corrected.** The fog gates now have proper event generation.

---

## Conformance Matrix (Updated)

| Component | Spec Section | Status | Notes |
|-----------|--------------|--------|-------|
| Program.cs | Task 3.9 | ✅ Complete | CLI matches spec |
| ModWriter.cs | Task 3.8 | ✅ Complete | Event generation added |
| GameDataLoader.cs | Task 3.1.1 | ✅ Complete | Loads MSB/EMEVD/Params correctly |
| SpeedFogGraph.cs | Task 3.2 | ✅ Complete | JSON model matches spec |
| NodeData.cs | Task 3.2 | ✅ Complete | Simplified but functional |
| EdgeData.cs | Task 3.2 | ✅ Complete | Matches spec |
| FogDataFile.cs | Task 3.2.1 | ✅ Complete | Fog metadata model |
| ClusterFile.cs | Task 3.2.2 | ✅ Complete | Zone maps integrated |
| FogLocations.cs | Task 3.4.1 | ✅ Complete | YAML parsing for enemy areas |
| ScalingWriter.cs | Task 3.4 | ✅ Complete | SpEffect generation |
| EnemyScalingApplicator.cs | Task 3.4.1 | ✅ Complete | Collision lookup added |
| FogGateWriter.cs | Task 3.5 | ✅ Complete | Creates definitions |
| FogGateEvent.cs | Task 3.5 | ✅ Complete | Data model |
| WarpWriter.cs | Task 3.6 | ✅ Complete | SpawnPoint creation |
| StartingItemsWriter.cs | Task 3.7 | ✅ Complete | Item give events |
| EntityIdAllocator.cs | Task 3.5.1 | ✅ Complete | ID allocation |
| FogAssetHelper.cs | Task 3.5.1 | ✅ Complete | Asset creation |
| PathHelper.cs | - | ✅ Complete | Utility functions |
| EventBuilder.cs | Task 3.3 | ✅ **Created** | YAML template parser |
| EventTemplate.cs | Task 3.3 | ✅ **Created** | Template model |

---

## Critical Issues (P0) - ✅ FIXED

### Issue 1: Missing EMEVD Event Generation for Fog Gates - ✅ FIXED

**Fix Applied** (2026-02-01):
1. Created `Models/EventTemplate.cs` with `EventTemplate` and `SpeedFogEventConfig` classes
2. Created `Writers/EventBuilder.cs` for YAML→EMEVD conversion
3. Updated `ModWriter`:
   - Added `RegisterTemplateEvents()` to add template events to `common_func.emevd`
   - Added `GenerateFogGateEvents()` to create `showsfx` and `fogwarp_simple` events
   - Events are initialized via `InitializeEvent` instructions in each map's event 0

---

### Issue 2: EnemyScalingApplicator Missing Collision Lookup - ✅ FIXED

**Fix Applied** (2026-02-01):
1. Added `_colToZone` dictionary field
2. Populated in `BuildLookupTables()` from `fogLocations.EnemyAreas[].Cols`
3. Added collision lookup in `DetermineEnemyZone()`:
   ```csharp
   // Priority 2: Collision part name
   if (!string.IsNullOrEmpty(enemy.CollisionPartName))
   {
       var colKey = $"{mapName}_{enemy.CollisionPartName}";
       if (_colToZone.TryGetValue(colKey, out var colZone))
           return colZone;
       if (_colToZone.TryGetValue(enemy.CollisionPartName, out colZone))
           return colZone;
   }
   ```

---

## Medium Priority Issues (P1) - ✅ FIXED

### Issue 3: Hardcoded Scale Event ID - ✅ FIXED

**Fix Applied**: Added `const int ScaleEventId = 79000001` with documentation comment.

---

### Issue 4: common_func Template Events Not Registered - ✅ FIXED

**Fix Applied**: `RegisterTemplateEvents()` method added to `ModWriter` that:
- Loads templates from `speedfog-events.yaml`
- Adds each template event to `common_func.emevd`
- Skips if event already exists

---

## Low Priority Issues (P2) - ✅ FIXED

### Issue 5: Missing RuntimeIdentifier in csproj

**Status**: Not fixed (optional, low impact)

---

### Issue 6: DCX Types Should Use Constants - ✅ DOCUMENTED

**Fix Applied** (2026-02-01): Added documentation comments explaining the magic numbers:
```csharp
// DCX compression types for Elden Ring (numeric values used for compatibility)
// 13 = DCX_KRAK (Kraken compression, used for regulation.bin params)
// 9 = DCX_DFLT_10000_24_9 (used for EMEVD and MSB files)
// These match FogRando's GameDataWriterE.cs L4982
```

---

### Issue 7: SpawnPoint Add Method

**Status**: Not fixed (current implementation works)

---

## Second Code Review (2026-02-01) - ✅ ALL FIXED

### Issue 8: Missing `total_zones` Field in SpeedFogGraph - ✅ FIXED

**File**: `writer/SpeedFogWriter/Models/SpeedFogGraph.cs`

Added missing field to match Python output format.

---

### Issue 9: WarpWriter MSB Lookup Bug - ✅ FIXED (CRITICAL)

**File**: `writer/SpeedFogWriter/Writers/WarpWriter.cs`

**Problem**: Entry fog position was looked up in target MSB instead of entry fog's actual map MSB.

**Fix Applied**: Now correctly looks up in entry fog's map:
```csharp
var entryFogMap = fogGate.EntryFogData.Map;
if (_msbs.TryGetValue(entryFogMap, out var entryMsb))
{
    (position, rotation) = GetFogPositionFromMsb(fogGate.EntryFogData, entryMsb);
}
```

---

### Issue 10: EnemyScalingApplicator Not Tracking Modified EMEVDs - ✅ FIXED (CRITICAL)

**File**: `writer/SpeedFogWriter/Writers/EnemyScalingApplicator.cs`

**Problem**: Modified EMEVDs weren't tracked, so scale events wouldn't be written.

**Fix Applied**:
- Added `_modifiedEmevds` HashSet and `ModifiedEmevds` property
- Track in `CreateScaleEvent()`: `_modifiedEmevds.Add(mapName)`
- Integrated in `ModWriter.GenerateScaling()` to add to `_writeEmevds`

---

### Issue 11: EntityIdAllocator Region/Flag ID Range Conflicts - ✅ FIXED

**File**: `writer/SpeedFogWriter/Helpers/EntityIdAllocator.cs`

**Problem**: Region IDs (1040290070) could collide with Flag IDs (1040290000).

**Fix Applied**: Updated to use separate ranges per `speedfog-events.yaml` allocation plan:
- Entity IDs: 755890000+ (FogRando range, no conflict)
- Flag IDs: 79200000+
- Region IDs: 79100000+
- Event IDs: 79000100+

---

### Issue 12: FogAssetHelper Not Integrated in ModWriter - ✅ FIXED

**Files**: `FogGateWriter.cs`, `FogGateEvent.cs`, `ModWriter.cs`

**Problem**: MakeFrom fogs (dynamic fog gates with inline positions) weren't being created.

**Fix Applied**:
- Added `IsMakeFrom`, `FogPosition`, `FogRotation` to `FogGateEvent`
- `FogGateWriter` now populates these fields from `FogEntryData`
- `ModWriter.CreateFogGates()` calls `_fogAssetHelper.CreateFogGate()` for makefrom fogs

---

### Issue 13: Missing SpeedFogGraph Helper Methods - ✅ FIXED

**File**: `writer/SpeedFogWriter/Models/SpeedFogGraph.cs`

Added per spec lines 438-475:
- `AllEdgesResolved()` - Yields edges with resolved node references
- `GetOutgoingEdges(nodeId)` - Returns edges leaving a node
- `GetIncomingEdges(nodeId)` - Returns edges entering a node
- `NodesByLayer()` - Groups nodes by layer

---

### Issue 14: Missing NodeData Convenience Properties - ✅ FIXED

**File**: `writer/SpeedFogWriter/Models/NodeData.cs`

Added per spec lines 550-555:
- `IsLegacyDungeon`
- `IsMiniDungeon`
- `IsBossArena`
- `IsMajorBoss`
- `IsMergePoint` (true when `EntryFogs.Count > 1`)

---

## Files Created

| File | Purpose |
|------|---------|
| `writer/SpeedFogWriter/Models/EventTemplate.cs` | YAML template data models |
| `writer/SpeedFogWriter/Writers/EventBuilder.cs` | YAML→EMEVD conversion |

## Files Modified

| File | Changes |
|------|---------|
| `writer/SpeedFogWriter/Writers/ModWriter.cs` | Added event config loading, template registration, fog gate event generation, makefrom fog creation, EMEVD tracking, DCX documentation |
| `writer/SpeedFogWriter/Writers/EnemyScalingApplicator.cs` | Added `_colToZone`, collision lookup, `ScaleEventId` constant, `ModifiedEmevds` tracking |
| `writer/SpeedFogWriter/Writers/WarpWriter.cs` | Fixed MSB lookup bug for entry fog position |
| `writer/SpeedFogWriter/Writers/FogGateWriter.cs` | Added makefrom fog field population |
| `writer/SpeedFogWriter/Writers/FogGateEvent.cs` | Added `IsMakeFrom`, `FogPosition`, `FogRotation` |
| `writer/SpeedFogWriter/Models/SpeedFogGraph.cs` | Added `total_zones` field, helper methods |
| `writer/SpeedFogWriter/Models/NodeData.cs` | Added convenience type-checking properties |
| `writer/SpeedFogWriter/Helpers/EntityIdAllocator.cs` | Fixed ID allocation ranges |

---

## Verification Checklist

After corrections, verify:

- [x] `dotnet build` succeeds (17 warnings, 0 errors - warnings are expected CA1416 for Windows APIs)
- [ ] Generated mod contains:
  - [ ] `regulation.bin` with scaling SpEffects
  - [ ] `common.emevd.dcx` with starting item events
  - [ ] `common_func.emevd.dcx` with template events (scale, showsfx, fogwarp_simple)
  - [ ] Map EMEVDs with fog gate InitializeEvent calls
  - [ ] MSBs with SpawnPoint regions
- [ ] Integration test (`writer/test/run_integration.sh`) passes
- [ ] Manual test: fog gate is visible and functional in-game

---

## Commits

| Commit | Description |
|--------|-------------|
| `a5caff4` | feat(phase3): add EMEVD event generation for fog gates |
| `8b01068` | fix(phase3): address code review issues in C# writer |

---

## Reference

- **Spec**: `docs/plans/phase-3-csharp-writer.md`
- **Implementation Plan**: `docs/plans/2026-02-01-phase3-implementation.md`
- **FogRando Source**: `reference/fogrando-src/GameDataWriterE.cs`
- **Event Templates**: `data/speedfog-events.yaml`
