# Weather Plugin

Forces a fixed weather and optionally pins the in-game clock to a fixed hour
(periodic re-set, not a frozen clock), so every run happens under identical
conditions (default: cloudless noon). Opt-in via config:

```toml
[plugin.weather]
enabled = true
weather = "cloudless"   # optional, default "cloudless"
hour = 12               # optional, 0-23, default 12
freeze_time = true      # optional, default true
```

## Accepted weather names

Snake_case names of the game's Weather enum (`er-common.emedf.json`,
instruction 2003[68]). `None` (-1) and `Unknown 18-23` are not exposed.

| name | value | name | value |
|------|-------|------|-------|
| `default` | 0 | `windy_fog` | 9 |
| `rain` | 1 | `heavy_snow` | 10 |
| `snow` | 2 | `heavy_fog` | 11 |
| `windy_rain` | 3 | `windy_puffy_clouds` | 12 |
| `fog` | 4 | `default_2` | 13 |
| `cloudless` | 5 | `default_3` | 14 |
| `flat_clouds` | 6 | `rainy_heavy_fog` | 15 |
| `puffy_clouds` | 7 | `snowy_heavy_fog` | 16 |
| `rainy_clouds` | 8 | `scattered_rain` | 17 |

Validation is strict (`WeatherInjector.Parse`): unknown parameter keys,
unknown weather names, wrong types, or an hour outside 0-23 abort the build.

## Mechanism

`WeatherInjector` (FogModWrapper) adds one looping event to `common.emevd`
(ID from `SpeedFogIds.WeatherEvents`, base 755865000), registered in event 0:

```
SetCurrentTime(hour, 0, 0, false, false, false, 0, 0, 0)   # if freeze_time
ChangeWeather(Weather.X, -1, true)
WaitFixedTimeSeconds(10)
EndUnconditionally(EventEndType.Restart)
```

- `ChangeWeather` lifespan -1 means "until the next change".
- The 10 s loop is insurance against vanilla events and cutscenes that
  change weather or change time (e.g. cutscene instructions 2002[10]/[12]
  carry their own weather/time). Re-applications are idempotent, hence
  invisible when nothing drifted.
- `SetCurrentTime` is also re-applied every restart: re-setting the same
  hour is a visual no-op, so any drift (a cutscene, the grace "pass time"
  menu) is corrected within one interval.
- `freeze_time` pins the hour by re-setting it every interval instead of
  `FreezeTime(true)`: a frozen clock keeps engine flag 2200 ("world clock
  stopped") permanently ON, which breaks external tools reading that byte
  as a loading-screen indicator (the racing mod's zone reveal) and stalls
  the makestable load-end gate (`MakestablePulsePatcher` waits for 2200 to
  drop before its stable-position pulse; a 5 s timeout limits the damage,
  see `docs/quitout-respawn.md`). The clock
  drifts up to ~3-4 in-game minutes between re-sets, invisible at noon;
  night stays unreachable, so night-only spawns are still excluded.
- The loop uses `EndUnconditionally(EventEndType.Restart)` (the event
  restarts from instruction 0 every interval), the same idiom as FogRando's
  `scale` template. EMEVD `Goto` only jumps forward, so a backward
  Goto-to-label loop would silently end the event after one pass.
- Common events keep running across map loads, so one event covers the
  whole run.

Vanilla precedent for the pattern: the Chapel of Anticipation (m18) sets
10:30 and freezes time; the Ranni night sequence in common.emevd sets 22:30
and forces weather 7. FogRando itself emits
`ChangeWeather(Weather.PuffyClouds, -1, ...)` in its evergaol events
(`GameDataWriterE.cs:3704`).

## Known limitations

- If a cutscene or the grace "pass time" menu changes the hour, it is
  corrected within 10 s (one loop interval); the off-hour window is at most
  that long.
- Night-only overworld spawns (Night's Cavalry, Deathbird) never appear
  with a pinned day. For racing this is a consistency feature.
- Underground skyboxes (Siofra, Ainsel): verify visually when changing the
  weather; not yet confirmed in-game (checklist step 4 below).
- The pinned clock still drifts up to one interval's worth of in-game
  minutes before snapping back; at hours near dawn/dusk the periodic
  re-set may be noticeable in the sky lighting. Verified invisible at the
  default noon.

## In-game verification checklist

1. Generate a seed with the plugin enabled (defaults).
2. At Chapel spawn: clear sky, noon lighting, clock pinned (hour stays put
   across a few minutes of play).
3. Traverse several zones including an overworld cluster: no rain/snow.
4. Enter an underground map (Siofra-type starry sky): visuals intact.
5. Quit-out and reload: weather and hour re-forced within 10 s.
6. One run with `freeze_time = false`: clock advances, weather still forced.
