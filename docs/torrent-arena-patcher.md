# Torrent Arena Patcher

`TorrentArenaPatcher` (in `writer/FogModWrapper/`) re-enables Torrent inside
boss arenas that vanilla blocks.

## Mechanism

Each MSBE collision part (`MSBE.Part.Collision`) carries a boolean
`DisableTorrent`. When the player stands on a collision flagged
`DisableTorrent=true`, the game forbids summoning Torrent. Boss arenas use this
on the collisions that cover the fight area.

To allow Torrent in an arena we set `DisableTorrent=false` on the targeted
collisions. This is the same pattern the FogRando randomizer uses to make the
Haligtree accessible by Torrent under its `snowfast` option
(`RandomizerCommon/MiscSetup.cs:1467-1473`).

## Patched maps

| Cluster | Map | Boss | Collisions flipped |
|---|---|---|---|
| `deeproot_boss` | `m12_03_00_00` | Fia's Champions | `h006000` |
| `ainsel_boss` | `m12_04_00_00` | Astel, Naturalborn of the Void | `h020300`, `h020400`, `h020500` |
| `siofra_boss` | `m12_08_00_00` | Ancestor Spirit | `h020300`, `h020500`, `h901000`, `h905000` |
| `siofra_nokron_boss` | `m12_09_00_00` | Regal Ancestor Spirit | `h020300`, `h020500`, `h901000`, `h905000` |

Mohg's arena (`mohgwyn_boss`, `m12_05_00_00`) is also Torrent-blocked but is
not patched because Mohg's fight design depends on staying dismounted.

## Identifying collisions

The collision names per map were found by inspecting MSBEs with the dedicated
subcommand of `tools/game_inspect/`:

```sh
wine tools/game_inspect/publish/win-x64/game_inspect.exe \
    list-collisions /path/to/Game/map/mapstudio/m12_03_00_00.msb.dcx --torrent-only
```

The collisions don't carry geometry in the MSB (positions are `(0,0,0)`); only
the name identifies them. The geometry comes from the `h<NNNNNN>` mesh file in
the map's `.mapbnd.dcx`.

## Pipeline

The patcher runs in Phase 8 (`ApplyModDirInjectors` in `Program.cs`), after
`VanillaWarpRemover`, so it sees the MSBs that FogMod has already modified for
fog gates. For each target map:

1. If FogMod wrote the MSB into the mod output, edit it in place.
2. Otherwise read the vanilla MSB from the game directory and write the patched
   version into the mod output.

This means the patch is applied regardless of whether the arena's cluster ended
up in the DAG for the current seed.

## Caveats

- `DisableTorrent` is the MSB-side block, but the game can still dismount the
  player via SpEffects triggered from EMEVD (e.g. on entering a boss region).
  Verify in-game; if Torrent is force-dismounted at fight start, the next step
  is to look at the arena's EMEVD with `tools/dump_emevd_warps`.
- `m12_04_00_00` is the shared MSB for Ainsel preboss + Astel arena, and
  `m12_05_00_00` is the shared MSB for the Mohgwyn approach + Mohg arena
  (see `StakeRemover.cs:30-39`). Flipping the named collisions there only
  enables Torrent on the specific collision cells listed, not the whole map.
