# Phantom Skins

Catalog of cosmetic phantom auras applied to the player as in-game rewards by speedfog-racing. Aura-only effect: a colored outline around the player's silhouette without modifying the body or equipment textures.

## Mechanism

Each skin is materialized as three paired param rows in `regulation.bin`:

| PARAM | Role |
|-------|------|
| `PhantomParam[id]` | Defines the visual: `edgeColorR/G/B`, `edgeColorA`, `edgePower`, `glowScale`, `alpha`. All other color components zeroed for aura-only effect. |
| `SpEffectVfxParam[id]` | `phantomParamOverwriteId = id`. Binds VFX to PhantomParam. |
| `SpEffectParam[id]` | `vfxId = id`. The id applied at runtime to a character. |

All three rows share the same id. Pattern adapted from RandomizerCommon's `EnemyRandomizer.cs:1740-1771`.

## Reserved ID Range

`1450700-1450799` (100 slots). Adjacent ranges reserved by other tooling:

- `77690-77694`: EnemyRandomizer enemy tier auras
- `1450601`: EnemyRandomizer single phantom row

Do not allocate from these ranges in other speedfog injectors.

## Catalog File

`data/phantom_skins.toml` is the single source of truth. One block per skin:

```toml
[[skins]]
id = 1450700              # required, in 1450700-1450799, unique
name = "gold-aura"        # required, unique, snake-case-with-hyphens
display_name = "Golden Phantom"
edge_color = [255, 215, 0]  # RGB 0-255
edge_power = 0.5            # outline thickness
glow_scale = 0.0            # bloom around outline
alpha = 1.0                 # overall opacity 0-1
```

To add a preset: add a new `[[skins]]` block, regenerate a seed, test in-game.

Note on alpha: the V1 schema exposes a single `alpha` value that the injector writes to both `PhantomParam.alpha` (overall) and `PhantomParam.edgeColorA` (edge alpha). If a future preset needs distinct overall and edge opacities, the schema would need an explicit `edge_alpha` field.

## Build-Time Flow

1. **Python (`speedfog/output.py:load_phantom_skins_catalog`)** reads `data/phantom_skins.toml` and builds a `name -> id` mapping. The mapping is embedded in `graph.json` under the `phantom_skins` field (graph.json v4.3+).
2. **C# (`PhantomCatalogLoader`)** reads and validates the TOML (id range, uniqueness).
3. **C# (`PhantomCatalogInjector`)** adds three rows per skin to `regulation.bin`, copying templates from PhantomParam[260], SpEffectVfxParam[51508], SpEffectParam[13177].

If the catalog file is absent, all three steps are silent no-ops (graph.json gets `"phantom_skins": {}`, no params injected).

## Runtime Contract (speedfog-racing)

The Rust mod resolves the skin via the per-seed mapping shipped in `graph.json` (v4.3+). Each entry is a structured object so the schema can grow without breaking older mods:

```json
{
  "version": "4.4",
  "phantom_skins": {
    "gold-aura": { "speffects": [1450700] },
    "cyan-aura": { "speffects": [1450701] },
    "...": "..."
  }
}
```

V1 ships a single directive per skin: `speffects: [int]`. Future skins can add other directives (e.g. `fxr_ids`, `emote_id`) without breaking compatibility: older mods ignore unknown keys, newer mods see absent keys as "no-op for that mechanism".

The flow:

1. Mod loads `graph.json` at startup (already does this for connections, etc.).
2. WebSocket auth_ok from speedfog-racing server includes the user's chosen skin name.
3. Mod looks up `name` in `graph.json.phantom_skins`. Missing name = log warn, feature off.
4. Mod iterates the directives it knows. For each id in `speffects`, hook player `ChrIns` and apply `SpEffectParam[id]` after spawn / save load.
5. Re-apply on save reload if the SpEffect template proves non-persistent.

`graph.json` is the only contract surface; the Rust mod does not need to read `data/phantom_skins.toml` directly. See `speedfog-racing/docs/specs/2026-05-01-phantom-skins-integration.md` for the full integration design (server side included).

## Iterative Calibration with Cheat Engine

For tuning skin values without rebuilding regulation.bin every iteration:

1. Generate a seed: `uv run speedfog config.toml --logs`
2. Build the mod: standard pipeline (`wine FogModWrapper.exe ...`).
3. Launch the seed.
4. Open Cheat Engine, attach to the ER process, load the all-in-one cheat table.
5. Use the table's "Apply SpEffect to Player" feature with id 1450700 (or other catalog id) to apply the baked skin.
6. To experiment with values without rebuilding: locate `PhantomParam[1450700]` in the table's param browser and edit `edgeColorR/G/B`, `edgePower`, etc. live; the change is visible immediately.
7. Once satisfied with the values, port them back to `data/phantom_skins.toml` and regenerate.

## Known Limits

- The aura is rendered around the entire player silhouette, including any worn equipment. There is no clean per-armor-piece coloring.
- The SpEffect duration depends on template `13177`; if it expires in-game, the runtime mod re-applies.
