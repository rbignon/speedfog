# Map Splits

**Status:** Active

How SpeedFog splits an oversized map into two or more DAG-sized zones by
declaring synthetic zones and fog gates in a tracked supplement file, rather
than editing the gitignored, bootstrap-regenerated `fog.txt`/`foglocations2.txt`.

## Purpose

`fog.txt` and `foglocations2.txt` are extracted from FogRando at bootstrap and
are gitignored: they can't carry SpeedFog-specific additions. But some FogRando
zones are too large for a single DAG cluster (a ~1 hour run wants clusters on
the order of a few minutes each), and Enir-Ilim's Outer Wall climb is one of
them: a single long vertical staircase from the `20012020` warp up to Radahn's
arena, with no existing fog gate midway.

`data/map_splits.toml` is a tracked "supplement" file: it declares new zones
and new fog gates (using FogMod's own `MakeFrom` mechanism, already used ~65
times in `fog.txt` for fakegaols) that carve a zone into smaller pieces. Two
independent pipelines read the same file and inject its content into their
respective in-memory structures before those structures are otherwise
finalized, so the split behaves exactly like an ordinary fog.txt zone/gate to
everything downstream (cluster generation, DAG placement, connection
injection, zone tracking, spoiler).

The Enir-Ilim climb is the first (and so far only) instance of the mechanism;
see "The Enir-Ilim Instance" below.

## File Format

`data/map_splits.toml`:

```toml
[[zones]]
name = "enirilim_upper"
map = "m20_01_00_00"
display_name = "Enir-Ilim Spiral Rise"
tags = ["dlc"]
split_from = "enirilim"
cols = []

[[fogs]]
name = "AEG099_002_9100"
map = "m20_01_00_00"
id = 20011960
text = "Spiral Rise ascent"
make_from = "AEG099_002 AEG099_002_9000 -281.566 66.082 -85.637 -159.1"
aside = { area = "enirilim", text = "climbing toward the Spiral Rise" }
bside = { area = "enirilim_upper", text = "arriving at the Spiral Rise" }
```

- `[[zones]]`: a new `AreaData`/`AnnotationData.Area`. `name`/`map`/`display_name`
  map directly to their fog.txt equivalents; `tags` is optional (fog.txt areas
  with no tags parse as `null`, not `""`, so an empty `tags` list is treated
  the same way on both sides). `split_from`/`cols` only drive the EnemyArea
  split (see below) and are unrelated to the zone's existence as a graph node.
- `[[fogs]]`: a new fog gate. `id` is the MSB EntityID FogMod assigns to the
  `MakeFrom`-created asset; it must be outside vanilla `m20_01` ranges and
  outside FogMod's own range (>= 755890000, see `FOGMOD_ENTITY_BASE` in
  CLAUDE.md). `make_from` is FogMod's own space-delimited format (`GameDataWriterE.cs`
  `Write` methods, ~L256-262): `"<model> <source-asset-to-copy> <x> <y> <z> <yaw>"`.
  The source asset must already exist in the target map (Enir-Ilim's fogs copy
  `AEG099_002_9000`, the existing Radahn-arena gate in `m20_01_00_00`).
- **One-way semantics**: `aside` is the exit side only, `bside` is the entry
  side only, matching FogRando's `unique` tag pattern (sending gates,
  abductor virgins, etc.) used throughout `fog.txt`. This is a DAG-topology
  choice, not a physical one: the `MakeFrom` gate itself is an ordinary
  walkable fog wall with no inherent direction. See "Two Injection Points"
  for how each pipeline enforces (or doesn't enforce) that one-way-ness.

## Two Injection Points

The supplement is consumed by two independent pipelines that must agree on
its topology, even though they enforce the one-way semantics through
different mechanisms:

| Consumer | Code | What it does |
|----------|------|---------------|
| Python cluster-gen | `tools/generate_clusters.py` (`load_map_splits`, `inject_map_splits`) | Adds an `AreaData` per `[[zones]]` entry and a `FogData` per `[[fogs]]` entry, tagged `["unique"]`, to the parsed fog.txt structures before cluster flood-fill. |
| C# FogModWrapper | `MapSplitsLoader` (`writer/FogModWrapper.Core/`) + `MapSplitsInjector` (`writer/FogModWrapper/`) | Adds an `AnnotationData.Area` and an **untagged**, ordinary two-sided `AnnotationData.Entrance` (both `ASide` and `BSide` populated, `Tags = null`) directly into FogMod's `AnnotationData`, before `Graph.Construct` runs (`Program.cs`, right after `OpenSplitInjector.Apply`). |

The asymmetry is deliberate and matters:

- On the **Python side**, tagging the injected `FogData` `"unique"` routes it
  through `classify_fogs`'s `is_unique` branch: `ASide`'s zone gets it as an
  `exit_fogs` entry, `BSide`'s zone gets it as an `entry_fogs` entry, and
  the reverse pairing is never produced. The DAG generator only ever builds
  `connections` (in `graph.json`) out of each zone's `entry_fogs`/`exit_fogs`
  lists, so it can never propose using the gate in reverse.
- On the **C# side**, `MapSplitsInjector` does *not* set `Tags` on the
  `Entrance` it creates: to FogMod's own `Graph.Construct`, this is an
  ordinary two-core-sided gate, structurally reversible. Nothing at the
  FogMod level prevents the reverse pairing.

In other words, the one-way behaviour of a map-splits fog is enforced
entirely by Python never asking for the reverse connection, not by anything
in the C# `AnnotationData`. This is safe as long as both sides declare the
*same* zones/fogs from the *same* file: if `classify_fogs` ever stopped
treating these fogs as `unique` (or a caller passed `warps` instead of
`entrances`), Python's DAG could hand `graph.json` a reverse-direction
connection that FogMod's untagged `Entrance` would happily wire, silently
reopening a path the split was built to seal (see "sealed Leda tail" below).

## Collision Guards

Both pipelines must check the injected fog names against the real fog.txt
gates before adding them, because FogMod keys entrances by `FullName =
Area + "_" + Name`, not by `Name` alone: the same asset-derived name (e.g.
`AEG099_002_9100`, `AEG099_002_9000`, ...) is legitimately reused across
dozens of unrelated maps. A real example: `AEG099_002_9100` already exists in
`fog.txt` at `Area: m35_00_00_00` (Subterranean Shunning-Grounds, entity
`35001850`) — a different map from the Enir-Ilim `AEG099_002_9100`
(`m20_01_00_00`, entity `20011960`), and not a conflict.

- **C#** (`MapSplitsInjector.Apply`): scopes the check to `(Area, Name)`,
  matching FogMod's `FullName` invariant exactly — `x.Name == fog.Name &&
  x.Area == fog.Map` against both `ann.Entrances` and `ann.Warps`. An earlier
  version compared `Name` alone across all areas and crashed on the
  `m20_01`/`m35` collision above; fixed to scope by area (see commit
  `5584c02`).
- **Python** (`inject_map_splits`): the parsed `FogData` has no per-entrance
  map field (only `ASide`/`BSide` zone names), so the map can't be checked
  directly. Instead it uses zone overlap as a proxy: an existing same-named
  fog is only treated as a collision if its `{aside.area, bside.area}` set
  intersects the new fog's `{aside, bside}` zones. Each injected fog is also
  registered into the lookup immediately, so later fogs in the same
  `map_splits.toml` are checked against earlier ones too (mirrors the C#
  side rechecking its own growing `Entrances` list). See commit `6252c0d`.

Both guards raise (`ValueError` / `InvalidDataException`) rather than
silently overwrite, since a real collision would mean two different physical
gates sharing an identity FogMod uses as a dictionary key.

## EnemyArea Split Mechanics

`SplitEnemyAreas` (in `MapSplitsInjector.cs`) optionally moves a subset of a
source `EnemyArea`'s enemies into a new `EnemyArea` named after the split
zone, so each half of a split map can carry its own scaling tier. It only
runs for zones with a non-empty `cols` list; `cols = []` is a documented
no-op (used today, since Enir-Ilim's col partition hasn't been captured
in-game yet).

- **Prefixed vs unprefixed collision names**: `AnnotationData.EnemyLocArea.Cols`
  (the space-separated string on the *area*, e.g. `enirilim`'s Cols) stores
  map-prefixed names (`"m20_01_00_00_h001800"`), but
  `AnnotationData.EnemyLoc.Col` (the per-enemy field) stores the bare,
  unprefixed name (`"h001800"`). `map_splits.toml`'s `cols` list is
  unprefixed (`"h004400"`, ...); `SplitEnemyAreas` prefixes each entry with
  `zone.Map` before matching against the source area's `Cols` string, and
  compares the *unprefixed* set directly against `loc.Col` for the
  per-enemy pass.
- **Tier inheritance**: the new `EnemyLocArea` copies `ScalingTier` from the
  source area (`src.ScalingTier`) at split time — this is FogRando's own
  vanilla/inherent baseline tier used in `GameDataWriterE`'s scaling formula,
  not the DAG-assigned target tier. The tier that actually differentiates the
  two halves in play comes from `graph.json`'s `area_tiers` (computed
  per-zone by the Python DAG placement), applied afterwards via
  `ConnectionInjector.ApplyAreaTiers(ctx.Graph, ...)` (`Program.cs`, after
  `Graph.Construct`) — this overwrites `Graph.AreaTiers[zone]` for any zone
  name present in `graph.json`, including the new split zone once it exists
  as its own cluster.
- **Per-enemy reassignment guarded by `ActualArea`**: an enemy is moved
  (`loc.Area = zone.Name`) only when `loc.Map == zone.Map`, `loc.Col` is in
  the (unprefixed) col set, **and** `loc.ActualArea == zone.SplitFrom`.
  `ActualArea` is `(Area ?? AArea).Split(' ')[0]` (FogRando's own computed
  property): the guard ensures only enemies whose *current* effective area is
  still the untouched source zone get reassigned, not enemies FogMod has
  already routed elsewhere.
- **`MainMap`/`Groups` stay on the source**: the new `EnemyLocArea` only sets
  `Name`, `Cols`, and `ScalingTier`; it does not copy `MainMap` or `Groups`
  from the source. These fields (used by `GameDataWriterE` to build
  area-name -> map / area-name -> group lookups) are left unset on the split
  zone — a known simplification, not yet exercised since Enir-Ilim's split
  is currently a no-op (`cols = []`).
- **Why missing tiers are safe**: if a synthetic zone somehow ends up without
  an entry in `Graph.AreaTiers` (e.g. `area_tiers` missing it in `graph.json`),
  FogRando does not throw. `GameDataWriterE`'s enemy-scaling loop looks the
  tier up with `TryGetValue` and only throws `"No tier for ..."` when
  `Feature.AllowUnlinked` is false (`GameDataWriterE.cs` ~L2126-2140); when
  true it just skips scaling for that entity instead. `AllowUnlinked` is set
  from `opt["crawl"] || opt["bossrush"] || opt["endless"]` (`Randomizer.cs:19`),
  and SpeedFog always sets `crawl = true` (`Program.cs`,
  `BuildRandomizerOptions`), so this path is always the tolerant one for
  SpeedFog.

## The Enir-Ilim Instance

The Outer Wall climb, split into two clusters by two synthetic one-way fogs:

```
warp 20012020 (Outer Wall)
   |  enirilim (lower): Outer Wall + First Rise
   v
FOG 2 (synthetic, AEG099_002_9100, id 20011960)      <- boundary between the two clusters
   |  enirilim_upper: Spiral Rise + Cleansing Chamber Anteroom
   v
FOG 1 (synthetic, AEG099_002_9101, id 20011961)      <- exit, before Leda
   :  unused tail: Leda's plateau, lift, Divine Gate, enirilim_stairs (excluded)
------
AEG099_002_9000 BSide -> enirilim_radahn (final_boss)   <- unchanged, DAG-wired
```

- `enirilim` (lower half, name kept from the original single-zone design):
  entry = warp `20012020`, exit = ASide of fog 2.
- `enirilim_upper` (new synthetic zone): entry = BSide of fog 2, exit = ASide
  of fog 1.
- Both fogs are one-way (`unique` pattern): fog 1's BSide targets
  `enirilim_stairs`, which is never a graph entry (see below), so it's not
  reachable as an *entrance* from anywhere in the randomized run.
- Each half keeps at least one Site of Grace.

**Entity IDs**: fog 2 is `AEG099_002_9100` / `20011960`; fog 1 is
`AEG099_002_9101` / `20011961`. Both copy the `AEG099_002` model from the
existing `AEG099_002_9000` asset in `m20_01_00_00`.

**Sealed Leda tail**: `enirilim_stairs` (Leda's plateau onward) is excluded
from the cluster pool via `zone_metadata.toml`'s existing `exclude = true`
mechanism (`[zones.enirilim_stairs]`), not a map-splits-specific flag — see
"zone_metadata.toml pieces" below. Behind it, the vanilla "After Leda" fog
wall (`AEG099_002_9002`, entity `20011148`, `Tags: unused` in `fog.txt`,
ASide `enirilim` / BSide `enirilim_stairs`) is left physically in place by
FogMod (tagged `unused`, not `unused remove`), sealing the tail a second
time behind the synthetic fog 1. Leda and her ambush remain unreachable; no
startup-flag hack is needed to fake "Leda defeated" state (an earlier,
unmerged branch used flags `7625`/`4902` for this before the split existed —
superseded, see the design spec).

**Thorns removal**: FogMod itself *creates* an Outer Wall thorns barrier
(two assets, `AEG410_901`/`AEG410_905`, sharing `EntityGroup 20006662`) and
only disables it when flag 330 (Sealing Tree burned) is ON, which SpeedFog
keeps OFF. `EnirilimAssetRemover` (`writer/FogModWrapper/EnirilimAssetRemover.cs`)
removes it post-`Write` by `EntityGroup` match (`MatchGroup = true`) via
`VanillaWarpRemover`, since a FogMod-created asset has no per-asset EntityID
to key on before the write pass. See `docs/vanilla-warp-removal.md` for the
`match_group` mechanism.

**Fog-2 door (pending discovery)**: a vanilla closed door blocks the passage
at fog 2, before the Spiral Rise stairs. It needs a `[[startup_flags]]` entry
in `data/game_tweaks.toml` to force it open at map load, following the
methodology in `docs/startup-flag-injection.md`. The controlling flag has not
yet been identified in-game.

**`zone_metadata.toml` pieces** (see `data/zone_metadata.toml` around
`[zones.enirilim]`/`[zones.enirilim_upper]`/`[zones.enirilim_stairs]`):

- `no_drop_to` cuts on `enirilim`: `belurat_enirilim` (unconditional drop into
  Belurat), `belurat_stairs` (treekindling-gated passage back), and
  `enirilim_stairs` (the excluded tail) — keeping the climb a standalone,
  warp-entered cluster. `belurat_stairs` also cuts `no_drop_to = ["enirilim"]`
  in the reverse direction.
- `[zones.enirilim_stairs] exclude = true` — the tail must never surface as a
  playable cluster.
- `weight` on `enirilim` (2) and `enirilim_upper` (2) are placeholders
  pending playtesting calibration (comments in `zone_metadata.toml` call
  this out explicitly).

**Remaining in-game inputs** (per the design spec):

1. The fog-2 door's controlling flag (startup-flag-injection methodology).
2. The exact `cols` partition between `enirilim` and `enirilim_upper` — which
   `h00xx00` collision groups sit below vs. above the fog 2 plane. Until
   captured, `map_splits.toml`'s `enirilim_upper.cols` stays `[]` and the
   EnemyArea split is inactive (both halves scale under the single inherited
   `enirilim` tier baseline).
3. In-game yaw validation for both `MakeFrom` gates: the yaw is derived from
   `atan2(dx, dz)` of the captured doorway segment, with the model's own axis
   convention (±90°) unverified until seen in-game.

## Reference points

- `data/map_splits.toml`: source of truth for the supplement, both zones and
  fogs, header comment cross-references this doc and the design spec.
- `tools/generate_clusters.py`: `load_map_splits`, `inject_map_splits`,
  `classify_fogs` (`is_unique` branch), `KEY_ITEMS` (`treekindling`).
- `writer/FogModWrapper.Core/MapSplitsLoader.cs`: TOML reader (`SplitZone`,
  `SplitFog` records).
- `writer/FogModWrapper/MapSplitsInjector.cs`: `Apply` (Areas/Entrances) and
  `SplitEnemyAreas` (EnemyArea split).
- `writer/FogModWrapper/EnirilimAssetRemover.cs`: hardcoded thorns removal.
- `writer/FogModWrapper/VanillaWarpRemover.cs`: `MatchGroup` removal path.
- `data/zone_metadata.toml`: `[zones.enirilim]`, `[zones.enirilim_upper]`,
  `[zones.enirilim_stairs]`.
- `reference/fogrando-src/GameDataWriterE.cs`: `MakeFrom` gate creation
  (~L256-262), enemy scaling / `AreaTiers.TryGetValue` (~L2126-2140).
- `reference/fogrando-src/AnnotationData.cs`: `EnemyLocArea`, `EnemyLoc`
  (`ActualArea`).
- `reference/fogrando-src/Randomizer.cs:19`: `Feature.AllowUnlinked` derived
  from `crawl`/`bossrush`/`endless`.
- `docs/superpowers/specs/2026-07-20-enirilim-split-synthetic-fogs-design.md`:
  design rationale, topology, in-game capture notes (French).
- `docs/startup-flag-injection.md`: methodology for the pending fog-2 door
  flag.
- `docs/vanilla-warp-removal.md`: `match_group` removal mechanism.
