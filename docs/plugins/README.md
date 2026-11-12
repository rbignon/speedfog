# Plugins

Opt-in, namespaced cosmetic/feature layers activated per seed via config.

## Config convention

```toml
[plugin.summer]
enabled = true
# feature-specific params are free-form
```

## Flow

1. Python (`config.py`) loads the whole `[plugin]` table into `Config.plugins`
   (generic, no per-plugin schema).
2. `graph_export.py` serialises it verbatim into `graph.json` under `plugins`
   (graph.json v4.4+).
3. C# `PluginRegistry.CreateEnabled` (end of `LoadInputsPhase`) creates one
   `SpeedFogPlugin` object per enabled table it knows, in execution order;
   each parses its own parameters then, so an invalid table aborts the build
   before FogMod runs. Tables with no C# plugin (yet) are ignored.
4. The pipeline phases call every plugin's hooks at fixed points (below).

## C# contract (FogModWrapper)

A plugin is the C# side of one `[plugin.<name>]` namespace: a subclass of
`SpeedFogPlugin` (`writer/FogModWrapper/Plugins/`) whose hooks are no-ops
unless overridden, registered once in `PluginRegistry`. Its state (parsed
settings, two-phase guards such as "row written in regulation, placed in
MSB only if present") lives in the plugin object, not in the pipeline's
`Context`.

| Hook | Called from | Position and why | Used by |
|---|---|---|---|
| `AfterFogModWrite` | `WriteFogModPhase` | Right after `BuildRegionToFlags`: last point where flags can join `RegionToFlags` before `PatchEmevdPhase` injects them | halloween (jumpscare plan + mirror flags) |
| `OnCommonEmevd` | `CommonEmevdPhase` | After the core injectors, before `AlternateFlagPatcher` and the single common.emevd write | weather (event), halloween (darkness event), race_items (use and removal events) |
| `OnRegulation` | `RegulationPhase` | Inside the single regulation.bin batch; not called when regulation.bin is unavailable | halloween (greeter, ambusher, jumpscare rows; icons), race_items (goods and SpEffect rows) |
| `TextThemeName` (data, not a method) | `ModDirPhase` | After `BossNameInjector` (themes reskin the ids it allocates by `boss_name`), before `DeathMarkerInjector`; the phase applies the themes in plugin order and warns when several are enabled | summer, halloween |
| `OnModDirMsb` | `ModDirPhase` | `Context.GateSides` (fog.txt gate sides) is available. After `DeathMarkerInjector`, before the Untouchable boss MSB repoint and `VanillaWarpRemover` (the decor ground estimate reads vanilla assets the remover deletes) | halloween (ambient pass: decorations, greeters, ambushes, jumpscare actors) |
| `OnModDirLate` | `ModDirPhase` | After `EventDisabler`, before `VanillaWarpRemover`: historical position, kept so per-map EMEVD rewrites stay in the same order | halloween (darkness fast-loop repoint), race_items (item texts) |

**Order.** `PluginRegistry` lists `summer`, `weather`, `halloween`, `race_items`, and that
is the execution order at every hook: weather before halloween keeps their
common.emevd events in their historical order, summer before halloween lets
the halloween text theme overwrite colliding summer entries. `race_items`
touches disjoint ids and common.emevd events, so it simply runs last.

**Adding a plugin.** Create `writer/FogModWrapper/Plugins/<Name>/` with a
`<Name>Plugin : SpeedFogPlugin` that parses its table in its constructor and
overrides the hooks it needs, put its injectors in the same folder, then add
one line to `PluginRegistry` at the position that fits the order rule. That
line is where the C# side declares the plugin's config key: the registry
assigns `SpeedFogPlugin.Name` from it (after construction, so constructors
cannot read it), and `LoadInputsPhase` logs the enabled ones. A
plugin that needs a point no hook covers gets a new hook in
`SpeedFogPlugin`, called from the phase, with its ordering reason documented
in the table above.

**Where plugin code lives.** Namespaces follow folders. A plugin's
FogModWrapper code goes in `writer/FogModWrapper/Plugins/<Name>/` (namespace
`FogModWrapper.Plugins.<Name>`); its pure data side (catalogue loaders and
their records, unit-testable without FogMod) goes in
`writer/FogModWrapper.Core/Plugins/<Name>/` under the same namespace, which
deliberately spans the two assemblies. Code shared by several plugins gets
its own folder, like `Plugins/Themes/` (the text theme used by summer and
halloween). Plugin IDs (events, flags, rows, entities) stay in the central
`SpeedFogIds` registry, whose tests catch collisions across features and
plugins.

**No FogModWrapper side.** A plugin may live entirely in another process.
`winter` has no `SpeedFogPlugin` and no `PluginRegistry` line: its static mod
is built by StaticModBuilder at bootstrap (`--winter-dir`) and copied into the
seed by `packaging.py` when `[plugin.winter]` is enabled; the table still
reaches `graph.json` through the generic passthrough.

**Across processes.** A theme can span processes (e.g. text in
FogModWrapper, an icon overlay built by StaticModBuilder at bootstrap), so
the cohesive unit stays the config namespace, read independently where each
piece runs. Halloween spans six features under one namespace: `TextTheme`
(boss/UI text reskin), `AmbientSpawnInjector` (dungeon exit gate
greeters/ambushes), `GateDecorInjector` (catalogue decorations),
`HalloweenIconInjector` (item icon redirect; the bootstrap-time overlay builder and
packaging conditional are shared with race items), `HalloweenJumpscareInjector` (an
Aging Untouchable appearing on the player in a few dungeon layers), and
`HalloweenDarknessInjector` (evergaol lighting everywhere); their
FogModWrapper side is driven by `HalloweenPlugin` (the text theme through its
`TextThemeName`).

## Plugins

- [summer-theme.md](summer-theme.md) - cosmetic summer text reskin.
- [halloween-theme.md](halloween-theme.md) - cosmetic halloween text reskin.
- [halloween-ambient.md](halloween-ambient.md) - Halloween ambient dungeon spawns and gate decorations.
- [halloween-icons.md](halloween-icons.md) - Halloween item icon redirect (Golden Seed, Sacred Tear).
- [halloween-darkness.md](halloween-darkness.md) - Halloween evergaol lighting over the whole run.
- [halloween-jumpscares.md](halloween-jumpscares.md) - Halloween jumpscares: an Aging Untouchable appearing on the player in a few dungeon layers.
- [race-items.md](race-items.md) - Mario Kart style race items (goods, SpEffects, EMEVD events, texts) for speedfog-racing.
- [weather.md](weather.md) - force a fixed weather, pin the clock hour.
- [winter.md](winter.md) - white ground and frosted foliage (static mod built at bootstrap); snowfall comes from the weather plugin.
