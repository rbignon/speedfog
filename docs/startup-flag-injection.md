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
| m35_00_00_00 | 35008542 | `AEG027_031_0500` | Sewer one-way door near barred gate 1, at (-129, -99, -185). No lever: the flag is the door's own ObjAct EventFlagID (ObjAct 27031), the engine renders the door open when it is ON (same mechanism as the Enir-Ilim entry). Also ends common event 90005515, the "won't open from this side" prompt. |
| m35_00_00_00 | 35008544 | `AEG027_031_0501` | Sewer one-way door near barred gate 2, at (-56, -99, -127). Same ObjAct-flag mechanism; this one has no EMEVD reference at all (not even the wrong-side prompt). |
| m10_00_00_00 | 10000500 | `AEG219_050_0500` | Stormveil barred gate near (-111, 21, 23). Map event 10002500 reads this flag and animates the gate plus its winch (`AEG219_030_0500`, EntityID 10001501). |
| m20_00_00_00 | 20008540 | `AEG417_011_0500` | Belurat main gate next to the first grace, at (-94, 44, 191). "Does not open from this side" from both sides: its ObjAct 417011 requires SpEffect 4150, which nothing grants around it. Map event 20002610 reads this flag, poses the gate open (asset anim 2) and disables the gate and lever ObjActs; the lever `AEG417_009_0500` behind it (MSB name "main gate SC") is the vanilla shortcut trigger and sets this flag. The gate's own ObjAct flag 20000540 has no EMEVD reference and is not injected (fallback candidate if the in-game check fails). Behind the gate, the elevator to the town (common event 90005500, flags 20000510/20000511) starts at the bottom, so the run skips the lower-streets loop through the Small Private Altar. |
| m20_01_00_00 | 20018540 | `AEG417_012_0501` | Enir-Ilim door before Spiral Rise stairs (map-split fog 2). No lever, no EMEVD reference: the flag is the door's own ObjAct EventFlagID (ObjAct 417012), the engine renders the door open when it is ON. |
| m61_47_44_00 | 2047448500 | `AEG464_015_2000` | Castle Ensis barred gate near (81, 360, 52). Parameterized map event 2047442500 reads this flag (also the mechanism's ObjAct EventFlagID) and animates the gate plus its mechanism (`AEG464_016_2000`, ObjAct 464016). |
| m61_47_44_10 | 2047448500 | `AEG464_015_2000` | Same gate: `_10` is a duplicate tile EMEVD (byte-identical to `_00` in vanilla, FogRando `dupeMsbs`), dupe-written by FogMod at Write time before the injector runs, so it needs its own entry. |

## Finding a Flag for a New Gate

When you spot a gate that blocks a SpeedFog path, the goal is to find the flag whose `ON` state makes the gate render in the open position at map load.

### Step 0. Check whether FogMod already opens it

`fog.txt` entrances can carry a `DoorName` field (the MSB name of a door next to
the fog gate, optionally followed by its map). For those, FogMod reads the door's
`ObjAct.EventFlagID` and emits the `setflag` common event (`fogevents.txt` ID
9005772, "Just set saved flag X0_4. Used for opening a door via event flag in
objact") into the map's Event 0, which is the same mechanism as this injector
(`reference/fogrando-src/GameDataWriterE.cs:554-575`, flushed at L3201-3212).
So before hunting a flag, grep `DoorName` in `data/fog.txt`: those doors are
already handled in our output, and they double as worked examples of the flag
pattern used by their neighbours. The other branch of the same code deletes
the door asset (and its ObjAct) instead, when the door has no ObjAct, no
`EventFlagID`, or the `cellar` tag, so a `DoorName` door missing from a map is
expected rather than a bug.

```bash
grep -n "DoorName" data/fog.txt
```

### Step 1. Locate the asset by position

Use `dump_emevd_warps objacts` on the relevant MSB to list all `Part.Asset` entries with their (rounded) positions, then grep for the target coordinates:

```bash
cd tools/dump_emevd_warps
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
wine publish/win-x64/dump_emevd_warps.exe objacts \
  /path/to/Game/map/mapstudio/<map>.msb.dcx \
  | grep -E "<X rounded>\s+<Y rounded>\s+<Z rounded>"
```

Practice-tool coordinates are world-space; MSB positions are local to the map's tile. For legacy dungeons (m10 ... m19, m35, and the DLC ones m20/m21) the offset is zero, so the values match. For open-world tiles (m60_AA_BB_CC) you need to subtract the tile origin.

### Step 2. Inspect the ObjAct entry, if any

The same `objacts` command also dumps `MSB.Event.ObjAct` records:

```
PartName  EventFlagID  EntityID  ObjActID  Position  Name
```

Read the `Name` column before settling on a door: it states the door's role in
Japanese (`両開き扉レバー　正門SC` = "double door lever, main gate shortcut",
`両開き扉　2層→沼地` = "double door, layer 2 → swamp"; the separator is the
full-width space U+3000). The Wine run on a vanilla MSB garbles it to `?`; run
the tool natively on a FogMod-output MSB (DFLT compression, no Oodle needed) to
get UTF-8, still from `tools/dump_emevd_warps`:

```bash
dotnet run -- objacts ../../seeds/<seed>/mods/fogmod/map/mapstudio/<map>.msb.dcx
```

`game_inspect near <msb> X Y Z --radius 30` then places the door relative to
the graces (`AEG099_060_*`), play regions and invasion points, which is how
you tell a door on the run's path from one in a side area.

If the asset name appears here with `EventFlagID > 0`, that flag controls the ObjAct interaction directly. Add it to `[[startup_flags]]` in `data/game_tweaks.toml` and you're done.

If the asset has no ObjAct entry (or `EventFlagID = 0`), continue to step 3.

`ObjActParam` (row = the `ObjActID` column) tells what kind of door it is
without launching the game: `actionFailedMsgId` 4010 is "Does not open from this
side" (one-way door), 4020 is "Locked" (key or flag required, see
`spQualifiedType`/`spQualifiedId`), and `playerAnimId` distinguishes the
two-handed push of a big double door (60180/60190) from a small door (60000)
or a lever (60200 pull, 60231 push, the animation of the Stormveil and Castle
Ensis winches above).

```bash
cd tools/game_inspect
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> ObjActParam \
  --row 417012 --defs ../../writer/FogModWrapper/eldendata/Defs
```

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

Add the entry to `[[startup_flags]]` in `data/game_tweaks.toml`, run `speedfog` end-to-end, and confirm the gate is open from the player's first approach. If only one of several candidate flags actually opens the gate visually, drop the others — extra flags are harmless functionally but pollute the diff.

## Pitfalls

- **Wrong entity field.** ObjActs use `Part.Asset.EntityID`, not `ObjActEntityID`. Mixing them yields zero matches in EMEVD search.
- **Lever vs gate.** Levers and gates are separate assets with separate EntityIDs. Tracing the lever leads to its own ObjAct flag (e.g., `10008501`), which is **not** the flag you want — the gate flag is set later in the open branch.
- **Asset removal as a shortcut.** Deleting the gate asset from the MSB does not work when an event drives the gate state: the event still spawns visual or collision data, and removal can crash the map. Set the flag instead.
- **Open-world coordinates.** Practice-tool coordinates for m60 tiles need conversion (subtract tile origin) before grepping the MSB output.
- **Wrong door.** Big doors share models (Belurat has two `AEG417_012` and two `AEG417_013`), so a model match is not an identification. The first pick for Belurat (2026-09-03) was `AEG417_012_0505` (flag 20000548, "layer 2 → swamp"): the flag was right for that door, but the door sits at the end of the bridge over the swamp and opens onto the Enir-Ilim annex (asset group 20006650 and collisions 20004650-20004655) that map event 20002150 disables while flag 330 is OFF, a flag SpeedFog forces OFF, so no run ever reaches it. Check the ObjAct name and the far side before adding a flag.

## References

- Injector: `writer/FogModWrapper/StartupFlagInjector.cs`
- Flag entries: `data/game_tweaks.toml` (`[[startup_flags]]`, loaded by `GameTweaksLoader`)
- Call site: `writer/FogModWrapper/Program.cs` (step 7j3)
- Tests: `writer/FogModWrapper.Tests/StartupFlagInjectorTests.cs`
- Tool: `tools/dump_emevd_warps/`
- Related: `docs/alternate-warp-patching.md`, `docs/vanilla-warp-removal.md`
