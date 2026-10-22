# Halloween Darkness

Opt-in layer of the Halloween plugin that keeps the vanilla evergaol
lighting active over the whole run: a darker, colder image everywhere,
while torches, candles, SFX lights and the player's own light stay lit.

```toml
[plugin.halloween]
enabled = true
darkness = true   # default false
```

Parsed strictly by `HalloweenPluginSettings.Parse` (a non-boolean value
aborts the build). Off by default so that
enabling the Halloween plugin never darkens a seed by surprise; a pool
opts in explicitly.

## Mechanism

`writer/FogModWrapper/HalloweenDarknessInjector.cs` adds one looping event
to `common.emevd` (ID from `SpeedFogIds.HalloweenDarknessEvents`, base
755865500), registered in event 0:

```
ActivateGparamOverride(0, 0)     # 2012[11], sub ID 0, no transition
WaitFixedTimeSeconds(2)
EndUnconditionally(EventEndType.Restart)
```

- `Activate Gparam Override` (2012[11]) layers a partial gparam over the
  current area's own: only the groups the override file carries replace
  the area's values. Sub ID 0 is `m00_00_0000_commonevent.gparam`, the
  file the vanilla evergaol event activates on entry (common_func event
  90005882, with the same hardcoded `(0, 0)` arguments; the test pins the
  instruction bytes against that call). It overrides LightSet, ToneMap,
  LightShaft, ColorGrading, VolumetricFog and EnvironmentConfig.
- The event re-applies the override every 2 seconds, the WeatherInjector
  idiom (`EndUnconditionally(Restart)`, since EMEVD `Goto` only jumps
  forward). Vanilla events call `Deactivate Gparam Override` (see
  Interactions), and persistence across map loads is unverified: vanilla
  never relies on it, its activations live in map events that run again
  on each load. The 2 second interval keeps any bright window after a fog
  gate warp short.
- Common events keep running across map loads, so one event covers the
  whole run.
- Vanilla events that re-apply their own override in a fast loop would race
  the 2 second loop and make the image flash. `RepointFastLoopOverrides`
  (mod-dir phase, after `EventDisabler`) rewrites the sub ID of their
  `Activate Gparam Override` instructions to 0 in the mod output's map
  EMEVD, keeping the transition time; a map missing from the output is
  reported and skipped. The list (`FastLoopOverrideEvents`) holds the one
  such loop found so far, the Academy's (see Interactions).

## Interactions

- Vanilla overrides of other areas, and how they meet the 2 second loop:
  - Roundtable Hold (`m11_10` event 11100680, sub ID 1, no blend): runs
    once per load, so the evergaol look takes over within one interval.
  - Specimen Storehouse (`m21_01` event 21012847, sub ID 2) and Enir-Ilim
    (`m20_01` event 20012847, sub ID 4): activated once on entering a
    region, with a 3 second blend, until the area's boss flag. Expect a
    partial blend toward their look on entry, then a snap back to the
    evergaol look.
  - Academy (`m14_00` event 14002689, sub ID 500): a 0.1 second loop
    re-applies it while the player is inside region 14002689, until flag
    14000801 (Rennala's TrapFlag in `data/fog.txt`). Against the darkness
    loop the image flashed in Rennala's arena (seen in game, 2026-10-04),
    so this event is repointed to sub ID 0 (see Mechanism) and both loops
    apply the same override. Once the flag is on, the same event runs
    `Deactivate Gparam Override` on each Academy load: a bright window of
    up to 2 seconds, like any other deactivation.
  - Evergaols use sub ID 0 themselves, so nothing changes there.
- Common event 840 deactivates any override (3 second blend) and resets the
  weather to default whenever the player carries SpEffect 8998, then
  clears it. Both loops (darkness, weather) restore their state within
  their interval.
- `[plugin.weather]` is configured independently: it picks the weather
  gparams and the hour, the override layers on top of whatever they
  produce. At night, fog weathers lose most of their effect under it
  (2026-10-04): `heavy_fog` showed clearly with the override off and only
  faintly with it on, and `fog`, `rainy_heavy_fog` and `snowy_heavy_fog`
  did no better (`windy_fog` and daytime untested). The override's own
  VolumetricFog group is the likely cause; its darker LightSet and ToneMap
  may add to it. Darkness wins over fog visibility: of the fog weathers
  tested, `heavy_fog` keeps the most fog under the override.

## Background and rejected approaches

The feature started from Scadu Altus' Darklight Catacombs
(`m40_02_00_00`). Its darkness cannot be transplanted: it is authored into
the map, not a setting. Its collisions use the same default gparam as the
sibling DLC catacombs (`m40_00_*`, shared by all `m40` maps), its
`MapDefaultInfoParam` row matches theirs (`PlayerLigntScale` 1.2), and it
calls no gparam override. What differs is the map's own lighting: 54 static
point lights in its `.btl` against 258 (`m40_00`) and 342 (`m40_01`), plus
the baked GI. Its three levers (`AEG447_115`) only toggle
`CreateAssetfollowingSFX` on about 100 lamps (`AEG447_073`/`074`, SFX
840015/840016 from `sfxbnd_m40`, each carrying one PointLight action).

Custom override files were tried first (spike, 2026-10-04), built with
SoulsFormatsNEXT's `GPARAM` from the `m00_00_0000_commonevent` template
(an unmodified read/write round-trips byte for byte) and shipped as
`param/drawparam/*.gparam.dcx` in a mod folder, KRAK-compressed like
vanilla. Neither a new sub ID (`m00_00_0050_commonevent`) nor a replaced
vanilla one (`m00_00_0021_commonevent`) had any visible effect, while sub
ID 0 activated from the same event did. The cause is unknown (the
executable's name formats, `m%02u_%02u_%04u_CommonEvent` and
`m%02u_%02u_%04u_CommonEventMapUnique`, match those file names). It only
matters to tune the look beyond the vanilla evergaol override: solve that
first before retrying.

## In-game checks

- Done (spike, 2026-10-04, one location): the override activated from
  `common.emevd` darkens the image, and the render was approved.
- Done (2026-10-04): entering the Specimen Storehouse and Enir-Ilim looks
  fine (partial blend toward their look, then back to the evergaol).
- Done (2026-10-04, midnight): fog weathers under the override (see
  Interactions).
- Owed: no flicker when the override is re-applied every 2 seconds; the
  look indoors (a catacomb, a legacy dungeon); darkness back within 2
  seconds after leaving an evergaol, after a fog gate warp, and after a
  quit-out; no flash left in Rennala's arena once her event is repointed.
