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
processes (e.g. text in FogModWrapper, lighting in GamePatcher), so the
cohesive unit is the config namespace, read independently where each piece
runs.

## Plugins

- [summer-theme.md](summer-theme.md) - cosmetic summer text reskin.
