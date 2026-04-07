# Boss Arena Exit Locking

**Date:** 2026-04-07
**Status:** Active

How SpeedFog locks boss arena exit fog gates by setting TrapFlag before the player warps into the arena.

## Problem

Boss arenas with `BossTrapName` in fog.txt allow escape through the exit fog gate when the boss hasn't been triggered yet. The fogwarp exit template (event 9005777) checks `TrapFlag` (parameter X20_4, resolved from `BossTrapName: area` to `area.TrapFlag`):

- **TrapFlag OFF**: exit fog gate is usable (player can warp out)
- **TrapFlag ON**: exit is locked behind `DefeatFlag` (must kill boss first)

In vanilla FogRando, `startboss` events monitor trigger regions positioned between the arena entrance and the boss. When the player walks through, `BossTrigger` is set, which activates the vanilla boss event, which sets `TrapFlag`. But when SpeedFog randomizes connections, the player may enter from a direction that bypasses these trigger regions, leaving TrapFlag OFF and the exit usable.

### Affected arenas

47 boss arenas have TrapFlag (and thus BossTrapName on their fog gate Sides). These include: Margit, Rennala, Godfrey/Gideon (Leyndell), Mohg, Godskin Duo, Fire Giant, Valiant Gargoyles, Dragonkin of Nokstella, Godskin Noble, Radahn, Bayle, and others.

86 boss arenas do NOT have TrapFlag. Their fogwarp exits have X20_4=0, so the trap check is skipped and the exit is strictly locked behind DefeatFlag regardless. These arenas are unaffected.

### Key flag distinction

These are three separate flags per boss area in fog.txt:

| Flag | Purpose | Example (Margit) |
|------|---------|-------------------|
| `DefeatFlag` | Set when boss dies | 10000850 |
| `BossTrigger` | Set when boss fight starts (activates boss AI) | 10002855 |
| `TrapFlag` | Checked by fogwarp exit to lock the gate | 10000851 |

The fogwarp exit checks **TrapFlag**, not BossTrigger. Setting BossTrigger alone does not lock the exit.

## Solution

Inject `SetEventFlag(TrapFlag, ON)` before `WarpPlayer` instructions that target boss arena warp regions. The flag is set while the player is still in the source area, so by the time they arrive in the boss arena, the exit is already locked.

This uses the same warp-patching pattern as ZoneTrackingInjector: scan all EMEVD files for WarpPlayer instructions, match destination regions against a lookup, and insert SetEventFlag before matching warps.

### Data flow

```
ConnectionInjector.InjectAndExtract()
        |  saves (flag_id, entranceEdge) references
        v
GameDataWriterE.Write()
        |  populates Side.Warp from MSB data
        v
BossTriggerInjector.BuildRegionToTrapFlag()
        |  reads entranceEdge.Side.Warp.Region
        |  looks up area.TrapFlag from graph.Areas
        |  builds region -> TrapFlag dictionary
        |  skips areas with TrapFlag <= 0
        v
Program.cs EMEVD scan loop
        |  for each EMEVD file, calls BossTriggerInjector.PatchEmevdFile()
        |  extracts region from warp instructions via TryExtractWarpInfo()
        |  if region matches, inserts SetEventFlag(TrapFlag, ON) before warp
        v
TrapFlag set before WarpPlayer -> exit locked on arrival
```

### Why set TrapFlag directly?

An earlier approach set BossTrigger instead, relying on the chain: BossTrigger -> vanilla boss event -> TrapFlag. This did not work reliably because:

1. BossTrigger and TrapFlag are different flags
2. The chain depends on vanilla boss events processing BossTrigger, which may not happen before the player can interact with the exit
3. Setting TrapFlag directly is simpler and guaranteed to lock the exit

### Interaction with ZoneTrackingInjector

Both patchers scan EMEVD files for WarpPlayer and insert SetEventFlag before it. ZoneTrackingInjector runs first, then BossTriggerInjector. Each runs its own scan on the (already-modified) EMEVD. Both SetEventFlag instructions end up before the WarpPlayer. Order does not matter since they set independent flags.

## File References

| File | Role |
|------|------|
| `writer/FogModWrapper/BossTriggerInjector.cs` | Region-to-TrapFlag mapping + EMEVD patching |
| `writer/FogModWrapper.Tests/BossTriggerInjectorTests.cs` | Unit tests for BuildRegionToTrapFlag |
| `writer/FogModWrapper/Program.cs` | Wiring (EMEVD scan loop + common.emevd) |
| `data/fog.txt` | Area definitions with DefeatFlag, BossTrigger, TrapFlag |
| `data/fogevents.txt` | fogwarp template (event 9005777) showing TrapFlag check |
