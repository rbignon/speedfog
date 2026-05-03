# Opensplit Warp Overrides

**Status:** Active

How SpeedFog promotes selected FogMod unique warps to FogMod's `opensplit`
behaviour at runtime, exposing their core side as a usable graph edge.

## Problem

In FogMod's crawl mode, a unique warp where one side carries the `open` tag
is non-core (`Graph.cs:1167-1172`). When a unique warp has one core and one
non-core side, FogMod's default behaviour is to mark the entire warp `unused`
and `remove` (`Graph.cs:1267-1271`), so neither side produces an edge in the
graph and the destination becomes unreachable through that warp.

`opensplit` is FogMod's escape hatch (`Graph.cs:1257-1266`): when an entrance
carries the `opensplit` tag, only the non-core side is dropped (added to
`Ignore`); the core side keeps its edge. Several FogRando warps already
carry `opensplit` upstream (e.g., `1037462650`, Sending Gate to Church of
Vows). Some do not, even though the same logic applies and would be useful
for SpeedFog's randomized DAG.

The motivating case is `15002600` (Sending Gate to Haligtree, Ordina ->
haligtree). Without `opensplit`, FogMod drops the entire warp and the
`haligtree` zone has no usable entrance edge other than the bidirectional
Loretta fog. The cluster `haligtree_f5b5` ends up with the same fog as
both entry and exit, never producing a real traversal.

## Solution

`data/zone_metadata.toml` carries the override list:

```toml
[warps."15002600"]
opensplit = true
```

Two consumers read the same list to keep Python cluster generation and the
C# FogMod runtime in sync:

| Consumer | Code | Role |
|----------|------|------|
| Python cluster gen | `tools/generate_clusters.py` (`extract_opensplit_warp_ids`, `classify_fogs`) | When `is_warp_edge_active` would skip a unique warp because of the open side, re-classify it: keep the core side as an entry/exit, drop the non-core side. |
| C# FogModWrapper | `OpenSplitOverrideLoader` + `OpenSplitInjector` (`writer/FogModWrapper.Core/`, `writer/FogModWrapper/`) | After `AnnotationData.LoadLiteConfig`, before `Graph.Construct`, add the `opensplit` tag to every entrance whose `Name` is in the override list. |

Tag injection happens in `Program.cs` directly after the phantom catalog
load, so FogMod's `IsCore`/opensplit handling sees the tag during graph
construction.

## Effects

For `15002600` after the override:

- ASide (`snowfield`, `open`): non-core, marked `unused`/`remove`, added to
  `Ignore` -> no exit edge from snowfield via this warp.
- BSide (`haligtree`, no optional tags): core, kept as an entrance edge in
  `Nodes["haligtree"].From`.
- The vanilla source (snowfield -> haligtree) is gone. SpeedFog does not
  rely on it: runs start at Chapel of Anticipation, never at snowfield.
- `clusters.json`'s `haligtree_f5b5` gains `15002600` as a second
  `entry_fogs` entry alongside `AEG099_003_9001` (Loretta front), so the
  cluster represents a real traversal of Haligtree Town.

## Adding a new override

1. Run `python tools/inventory_open_warps.py` to list candidate unique warps
   with exactly one `open` side and no `opensplit` upstream.
2. Decide per-warp whether the resulting traversal is desirable (Mohgwyn's
   `12052020` was deliberately *not* added: `mohgwyn_9ac0` already has the
   Pureblood Knight's Medal entrance and the snowfield-side approach would
   add a long, low-value section).
3. Append a `[warps."<id>"] opensplit = true` block to
   `data/zone_metadata.toml` with a comment explaining the motivation.
4. Regenerate `data/clusters.json` and verify the targeted cluster gains
   the expected entry fog.

## Reference points

- `reference/fogrando-src/Graph.cs:1167-1272`: IsCore + opensplit handling
- `tools/generate_clusters.py`: `_is_side_core`, `is_warp_edge_active`,
  `classify_fogs`, `extract_opensplit_warp_ids`
- `writer/FogModWrapper.Core/OpenSplitOverrideLoader.cs`: TOML reader
- `writer/FogModWrapper/OpenSplitInjector.cs`: tag injection
- `data/zone_metadata.toml`: source of truth for overrides
