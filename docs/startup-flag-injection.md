# Startup Flag Injection

**Date:** 2026-05-01
**Status:** Active

`StartupFlagInjector` sets event flags at game startup to force the world into a desired state, primarily to keep barred gates open along the DAG path.

## Mechanism

Each entry in the injector's call site is a `(mapId, flagId, on)` tuple. The injector:

1. Groups entries by EMEVD file (one Read/Write per map).
2. Inserts `SetEventFlag(flagId, on)` instructions at the head of Event 0.
3. Shifts existing parameter `InstructionIndex` values to keep parameterized calls intact.

Event 0 runs once at map load, so flags are forced ON before any vanilla event has a chance to query them. Gate-control events typically check these flags at startup and play the "already opened" animation when ON, skipping the wait-for-trigger branch entirely.

This works only for gates where the flag check happens at the **start** of the controlling event. Gates whose state is forced by an out-of-band mechanism (e.g., character params, save-state engine flags) need a different patch (see `docs/alternate-warp-patching.md`).

## Currently-Injected Flags

| Map | Flag | Asset | Notes |
|-----|------|-------|-------|
| m35_00_00_00 | 35000565 | `AEG023_330_1000` | Sewer barred gate 1 (lever `AEG027_002_0503`). Common event 90005540 reads this flag. |
| m35_00_00_00 | 35000566 | `AEG023_330_1001` | Sewer barred gate 2 (lever `AEG027_002_0507`). Common event 90005540 reads this flag. |
| m10_00_00_00 | 10000500 | `AEG219_050_0500` | Stormveil barred gate near (-111, 21, 23). Map event 10002500 reads this flag and animates the gate plus its winch (`AEG219_030_0500`, EntityID 10001501). |

## Finding a Flag for a New Gate

When you spot a gate that blocks a SpeedFog path, the goal is to find the flag whose `ON` state makes the gate render in the open position at map load.

### Step 1. Locate the asset by position

Use `dump_emevd_warps objacts` on the relevant MSB to list all `Part.Asset` entries with their (rounded) positions, then grep for the target coordinates:

```bash
cd tools/dump_emevd_warps
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
wine publish/win-x64/dump_emevd_warps.exe objacts \
  /path/to/Game/map/mapstudio/<map>.msb.dcx \
  | grep -E "<X rounded>\s+<Y rounded>\s+<Z rounded>"
```

Practice-tool coordinates are world-space; MSB positions are local to the map's tile. For legacy dungeons (m10 ... m19, m35) the offset is zero, so the values match. For open-world tiles (m60_AA_BB_CC) you need to subtract the tile origin.

### Step 2. Inspect the ObjAct entry, if any

The same `objacts` command also dumps `MSB.Event.ObjAct` records:

```
PartName  EventFlagID  EntityID  ObjActID  Position  Name
```

If the asset name appears here with `EventFlagID > 0`, that flag controls the ObjAct interaction directly. Set it ON in `StartupFlagInjector.Inject(...)` and you're done.

If the asset has no ObjAct entry (or `EventFlagID = 0`), continue to step 3.

### Step 3. Trace the asset's EntityID through EMEVD

```bash
# Find which events reference the asset's EntityID
wine publish/win-x64/dump_emevd_warps.exe dump \
  /path/to/Game/event/<map>.emevd.dcx --event all \
  | awk '/^Event /{e=$0} /<EntityID>/{print e; print}'

# Read the candidate event's full body
wine publish/win-x64/dump_emevd_warps.exe dump \
  /path/to/Game/event/<map>.emevd.dcx --event <event_id>
```

Look for the typical "load-time gate state" pattern at the start of the event:

```
IfEventFlag(AND_01, OFF, flag=<candidate>)
[... possibly more flag checks combined with AND_01 ...]
GotoIfCondGroup(label=0, ON, AND_01)   # if all OFF -> wait-for-trigger branch
ReproduceAssetAnim(<EntityID>, anim=1) # otherwise -> "already opened" anim
End
Label 0:
  ... wait for trigger ...
  SetEventFlag(<candidate>, ON)
```

The flag set in the open branch is the one to inject ON at startup. When several events reference the same asset and each sets its own flag, prefer the one that animates **both** the gate and its visible mechanism (lever, winch). Setting only a "secondary" flag may leave the lever in its un-pulled pose, even though the gate itself is open.

### Step 4. Verify in-game

Add the entry to `StartupFlagInjector.Inject(...)` in `writer/FogModWrapper/Program.cs`, run `speedfog` end-to-end, and confirm the gate is open from the player's first approach. If only one of several candidate flags actually opens the gate visually, drop the others — extra flags are harmless functionally but pollute the diff.

## Pitfalls

- **Wrong entity field.** ObjActs use `Part.Asset.EntityID`, not `ObjActEntityID`. Mixing them yields zero matches in EMEVD search.
- **Lever vs gate.** Levers and gates are separate assets with separate EntityIDs. Tracing the lever leads to its own ObjAct flag (e.g., `10008501`), which is **not** the flag you want — the gate flag is set later in the open branch.
- **Asset removal as a shortcut.** Deleting the gate asset from the MSB does not work when an event drives the gate state: the event still spawns visual or collision data, and removal can crash the map. Set the flag instead.
- **Open-world coordinates.** Practice-tool coordinates for m60 tiles need conversion (subtract tile origin) before grepping the MSB output.

## References

- Injector: `writer/FogModWrapper/StartupFlagInjector.cs`
- Call site: `writer/FogModWrapper/Program.cs` (step 7j3)
- Tests: `writer/FogModWrapper.Tests/StartupFlagInjectorTests.cs`
- Tool: `tools/dump_emevd_warps/`
- Related: `docs/alternate-warp-patching.md`, `docs/vanilla-warp-removal.md`
