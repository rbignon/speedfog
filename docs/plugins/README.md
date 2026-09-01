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
2. `output.py` serialises it verbatim into `graph.json` under `plugins`
   (graph.json v4.4+).
3. C# `GraphData.Plugins` + `IsPluginEnabled(name)` gate each feature in
   `FogModWrapper/Program.cs`.

## Deliberately NOT built (yet)

No plugin runtime, registry, or `IPlugin` interface, and no migration of
existing injectors. A "plugin" here is a config convention plus a
self-contained feature class. The `plugins` map + `IsPluginEnabled` are the
seed of a future framework; extract a shared interface only once two or more
opt-in plugins exist (rule of three). Note that a single theme can span
processes (e.g. text in FogModWrapper, lighting in StaticModBuilder), so the
cohesive unit is the config namespace, read independently where each piece
runs. Halloween is the current example spanning four features under one
namespace: `TextTheme` (boss/UI text reskin), `AmbientSpawnInjector`
(dungeon entrance greeters/ambushes), `GateDecorInjector` (catalogue
decorations), and `HalloweenIconInjector` (item icon redirect, plus its
own bootstrap-time overlay builder and packaging conditional), each
reading `[plugin.halloween]` independently.

## Plugins

- [summer-theme.md](summer-theme.md) - cosmetic summer text reskin.
- [halloween-theme.md](halloween-theme.md) - cosmetic halloween text reskin.
- [halloween-ambient.md](halloween-ambient.md) - Halloween ambient dungeon spawns and gate decorations.
- [halloween-icons.md](halloween-icons.md) - Halloween item icon redirect (Golden Seed, Larval Tear, Sacred Tear).
- [weather.md](weather.md) - force a fixed weather, pin the clock hour.
