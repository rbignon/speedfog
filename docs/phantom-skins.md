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

1. `PhantomCatalogLoader` reads and validates the TOML (id range, uniqueness).
2. `PhantomCatalogInjector` adds three rows per skin to `regulation.bin`, copying templates from PhantomParam[260], SpEffectVfxParam[51508], SpEffectParam[13177].
3. `speedfog/packaging.py` copies the TOML into the seed output as `<seed_dir>/phantom_skins.toml`.

If the catalog file is absent, both steps are silent no-ops.

## Runtime Contract (speedfog-racing)

The Rust mod is expected to:

1. Load `<seed_dir>/phantom_skins.toml` at startup.
2. Read the local per-player config to obtain a `name`.
3. Resolve `name -> id` via the loaded catalog.
4. Apply `SpEffectParam[id]` to the player `ChrIns` after spawn or save load.
5. Re-apply on save reload if the SpEffect template proves non-persistent.

The catalog file is the only contract surface; the Rust mod does not depend on the speedfog source layout.

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
