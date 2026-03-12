# Region-Based Zone Tracking

**Date:** 2026-03-12
**Status:** Proposed

## Problem

ZoneTrackingInjector post-processes FogMod's compiled EMEVD files to inject SetEventFlag instructions before fog gate warps. Each flag maps to a DAG node so the racing mod can track player zone progression.

The current approach works by **reverse-engineering** compiled events: it scans all EMEVDs for warp instructions and tries to match them back to graph.json connections using five heuristic strategies (entity, region suffix, compound key, dest-only, common event). This is inherently fragile because connection identity is lost during FogMod's compilation.

### Failure modes

When the heuristics can't unambiguously match an event to a connection, the build aborts (Phase 3 validation). Two collision types cause this:

1. **Zone tracking collision** — Two connections share the same exit fog gate AND their entrance zones resolve to the same destination map. Entity disambiguation fails for AEG099 gates because FogMod allocates new entity IDs (755890xxx) that aren't in the static lookup.

2. **Compound key collision** — Two connections from the same source node target zones on the same entrance map, regardless of exit fog gate. The compound key `(source_map, dest_map)` is identical for both, making them indistinguishable.

### Cascading conservatism

To prevent these C# build failures (~2 minutes per attempt), the Python side rejects DAG configurations that might cause collisions:

- `validator.py` rejects DAGs with zone tracking or compound collisions
- `crosslinks.py` refuses cross-link pairs that would introduce collisions
- These checks are intentionally conservative (false rejections preferred over false acceptances)

As the number of connections per DAG increases (more cross-links, denser graphs), the rejection rate grows. Each rejected seed costs only milliseconds, but the constraint space shrinks, limiting seed diversity and occasionally preventing valid configurations from being generated.

### Root cause

The fundamental issue is architectural: the injector tries to reconstruct connection identity from compiled instruction data, which is a lossy process. No combination of heuristics can be 100% reliable because the information needed (which graph.json connection produced this event) was never preserved.

## Proposed Solution

### Key insight

After `ConnectionInjector.InjectAndExtract()` connects edges in FogMod's Graph and **before** `GameDataWriterE.Write()` compiles events, the Graph contains exactly the data needed to build a deterministic mapping:

- `edge.Name` == `conn.ExitGate` (identifies the connection)
- `edge.Link.Side.Warp.Region` == the destination region entity that FogMod will write into the compiled WarpPlayer instruction

Each entrance in fog.txt has a **unique Region** — either a pre-existing vanilla entity or a FogMod-allocated entity from FOGMOD_ENTITY_BASE (755890000). This region is an ideal key: unique per entrance, preserved exactly in compiled events, and available before compilation.

### Approach: pre-build region-to-flag mapping from FogMod's Graph

**Phase 1 — Build mapping (new, before Write()):**

After ConnectionInjector connects all edges, iterate `graph.Nodes.Values` → `node.To` edges. For each linked edge:

1. Match `edge.Name` to a connection's `ExitGate` to find the `flag_id`
2. Extract `edge.Link.Side.Warp.Region` — the primary destination region
3. Record `region → flag_id` in a `Dictionary<int, int>`. If the region already exists with a **different** flag_id, throw a diagnostic error naming both connections — this indicates a shared-entrance-to-different-clusters case that the Python guard (option 3) should have prevented.
4. If `edge.Link.Side.AlternateSide?.Warp?.Region` exists (AlternateFlag warps like flag 300/330), register that alternate region with the same flag_id

For shared entrances (DuplicateEntrance), `Graph.DuplicateEntrance()` creates a new Edge but reuses the same `Side` object (`Graph.cs:408`), so both entrance edges share the same `Warp.Region`. When two connections share the same entrance and target the same cluster, both exit edges map to the same `(region, flag_id)` — the second TryAdd is a no-op (same value). When they target different clusters (different flag_ids), the assert in step 3 fires.

**Phase 2 — Scan and inject (simplified, after Write()):**

For each warp instruction (WarpPlayer 2003:14, PlayCutsceneToPlayerAndWarp 2002:11/12) in compiled EMEVDs:

1. Extract the region parameter from instruction arguments
2. Look up region in the `regionToFlag` dictionary
3. If found, inject `SetEventFlag(flag_id, ON)` before the warp instruction
4. If not found, skip (not one of our connections' warps)

**Phase 3 — Validation (unchanged):**

Compare injected flags against expected flags. Abort if any connection's flag was not injected.

### What this replaces

| Current (heuristic) | Proposed (region lookup) |
|---------------------|-------------------------|
| 5 matching strategies (0, R, 1, 2, 3) | 1 dictionary lookup |
| EntityCandidate, RegionCandidate structs | Removed |
| Compound key lookup + collision tracking | Removed |
| Dest-only lookup + collision tracking | Removed |
| Common event lookup (WarpBonfire special case) | Removed |
| FOGMOD_ENTITY_BASE filter | Implicit (region not in dict = skip) |
| IfActionButtonInArea pre-scan + parameterized entity resolution | Removed |
| InitializeEvent args parsing | Removed |
| ~550 lines of matching logic | ~50 lines of region lookup |

**Retained code** (reused as-is): `TryExtractWarpInfo` + `WarpInfo` struct (region extraction from warp instructions), `UnpackMapId` + `FormatMap` (diagnostics), `InjectBossDeathEvent` (boss death monitor), Phase 3 validation (flag completeness check).

### Why the region is a reliable key

FogMod compiles warp destinations at GameDataWriterE.cs L3328:
```csharp
int region6 = warp5.Region;  // = link2.Side.Warp.Region
```

This same value appears in all warp code paths:
- **Template fogwarp events** (9005777): EventEditor.Process() fills region from entrance Warp.Region. The `warpToSide` closure (L1775, passed as `gameFuncs.WarpCmds`) reads `Side.Warp.Region` — the same Side chain we read during mapping construction.
- **Manual fogwarp events** (L3490-3546): `list53.Add(region6)` bakes the region into event args
- **WarpFlag events** (L3391-3435): `list49.Add(region6)` for WarpBonfire repeat warps
- **WarpBonfire vanilla events** in common.emevd: EventEditor replaces region/map in PlayCutsceneToPlayerAndWarp
- **warpToSide helper** (used at L2534, L2597, L2787, L3409, L3523): always receives `link.Side` and reads `Side.Warp.Region`, the same data path

Both template-compiled and manual event paths read from `edge.Link.Side.Warp.Region`, which is exactly what we capture during mapping construction. `Graph.Connect()` (Graph.cs:415-458) only sets Link/From/To references — it does not modify Warp data.

The region is unique per entrance because:
- Vanilla entrances have pre-existing unique region entities from game MSB data
- FogMod-created entrances get sequential entity IDs from FOGMOD_ENTITY_BASE (755890000)

### Edge cases handled

| Case | Current approach | Region approach |
|------|-----------------|-----------------|
| AEG099 gates (FogMod-allocated entities) | hasFogModEntity flag + compound/dest-only fallback | Region in dict = match. No entity lookup needed |
| Lie-down warps (Placidusax, vanilla region IDs) | Entity pre-scan + special FOGMOD_ENTITY_BASE bypass | Region in dict = match. FOGMOD_ENTITY_BASE irrelevant |
| WarpBonfire in common.emevd (no IfActionButtonInArea) | Strategy 3 with dedicated common event lookup | Region in dict = match. No special case |
| Parameterized entity_id=0 (InitializeEvent args) | Parse Parameter list + init args to resolve | Not needed. Region matching is instruction-local |
| AlternateFlag warps (flag 300/330, two regions) | Both regions → same event, matched by one strategy | Both regions registered in dict with same flag_id |
| Adjacent map tile warps (dest map mismatch) | Strategy R (region suffix) bypasses dest map | Region lookup has no dest map dependency |
| Two connections, same exit gate, same dest map | Currently unresolvable (build abort) | Different entrance regions → different dict entries |
| ErdtreeWarpPatcher / SealingTreeWarpPatcher | Run after ZoneTrackingInjector, no conflict | Same: run order unchanged. Flag injection is independent of subsequent warp destination patching — SetEventFlag doesn't reference the warp's region/map. |
| Unlinked edges (ForceUnlinked=true) | Filtered by FOGMOD_ENTITY_BASE or no entity match | No events created for unlinked edges (L3267: skipped) |

### Data flow

```
graph.json connections
        │
        ▼
ConnectionInjector.InjectAndExtract()
        │  connects edges in FogMod Graph
        ▼
[NEW] Build regionToFlag mapping
        │  iterate Graph.Nodes → edge.Link.Side.Warp.Region
        │  match edge.Name → connection.ExitGate → flag_id
        ▼
GameDataWriterE.Write()
        │  compiles fogwarp events (uses same Region values)
        ▼
ZoneTrackingInjector.Inject(regionToFlag, ...)
        │  scan EMEVDs, extract region, lookup in dict
        ▼
SetEventFlag injected before each matched warp
```

### Shared entrance disambiguation

When two connections share the same entrance gate (DuplicateEntrance), they share the same entrance Region. The mapping `region → flag_id` would be ambiguous.

However, shared entrances mean two exit gates converge to the same zone. Both connections' flags represent "player entered cluster X" — and both are correct. The racing mod maps flag → cluster, and both connections point to the same destination cluster.

If the two connections point to **different** clusters (edge case with allow_entry_as_exit), disambiguation is needed. Options:

1. **Enrich the mapping**: `region → List<(flagId, exitGateName)>`, then during EMEVD scan check `IfActionButtonInArea` entity or EMEVD filename to pick the right candidate. This reintroduces minimal entity matching but only for the shared-entrance case (rare).
2. **Accept both flags**: inject both SetEventFlag instructions. The racing mod sees two flags set but only one is meaningful (the one in event_map). Harmless.
3. **Prevent at DAG level**: the Python validator already prevents shared entrances to different clusters (compound collision check). If we keep this single check, the ambiguity never arises in practice.

Recommendation: start with option 3 (keep the minimal Python guard for shared-entrance-to-different-clusters), which is the simplest and preserves correctness. If the Python guard causes excessive rejections in practice, switch to option 1.

## Changes Required

### C# changes

| File | Change |
|------|--------|
| `ConnectionInjector.cs` / `InjectionResult` | After injecting connections, iterate Graph edges to build `Dictionary<int, int>` regionToFlag. Add it to InjectionResult. Handle AlternateSide regions. |
| `ZoneTrackingInjector.cs` | Replace Phase 1 (build lookups) and Phase 2 (multi-strategy scan) with region-based lookup. Remove EntityCandidate, RegionCandidate, compound/dest-only/common event lookups. Keep TryExtractWarpInfo (region extraction) and InjectBossDeathEvent (unchanged). |
| `Program.cs` | Pass `injectionResult.RegionToFlag` to ZoneTrackingInjector.Inject(). Remove areaMaps construction. Simplify Inject() signature. |
| `FogModWrapper.Tests/ZoneTrackingTests.cs` | Replace entity/region/compound/common event unit tests with region-lookup tests. |

### Python changes (follow-up, not in initial scope)

Once the C# side is validated:

| File | Change |
|------|--------|
| `validator.py` | Remove zone tracking collision check and compound collision check (or keep only the shared-entrance-different-cluster guard per disambiguation option 3) |
| `crosslinks.py` | Remove `_build_collision_index`, `_build_compound_index`, `_would_collide`, `_would_compound_collide` and associated filtering |
| `tests/test_validator.py` | Remove collision test cases |
| `tests/test_crosslinks.py` | Remove collision filtering test cases |

### Test plan

**Unit tests** (ZoneTrackingTests.cs — replace existing tests):

1. **Region mapping construction**: given a mock Graph with connected edges and a list of connections, verify that `BuildRegionToFlag()` produces the expected `region → flag_id` dictionary.
2. **AlternateFlag regions**: verify that both primary and alternate regions are registered with the same flag_id when `AlternateSide.Warp.Region` exists.
3. **Duplicate region assertion**: verify that two connections with the same entrance Region but different flag_ids trigger a diagnostic error during mapping construction.
4. **Same region, same flag (shared entrance, same cluster)**: verify that two connections sharing an entrance Region with the same flag_id produce no error (TryAdd is no-op).
5. **Region lookup in EMEVD scan**: given a crafted WarpPlayer instruction with a known region, verify that SetEventFlag is injected before it. Verify that an unknown region is skipped.

**Integration test** (run_integration.sh):

- Full build with a known seed, verify all expected flags are injected (Phase 3 validation passes).
- Compare injected flag count against the previous heuristic approach to confirm equivalence.

### Acceptance criteria for Python follow-up

After validating the C# changes across 3+ integration runs with different seeds and zero zone-tracking build failures, remove the Python collision guards (validator.py, crosslinks.py). This prevents them from lingering indefinitely and unnecessarily limiting seed diversity.

### graph.json / GraphData changes

None. The `has_common_event` field on Connection becomes unused by C# but can remain for backward compatibility (ignored).

## Regression Risks

### Low risk

- **Region extraction from warp instructions**: reuses existing `TryExtractWarpInfo()` which already extracts region. Only the lookup mechanism changes.
- **Boss death monitor**: `InjectBossDeathEvent()` is completely independent and unchanged.
- **Build order**: ZoneTrackingInjector still runs after Write() and before ErdtreeWarpPatcher/SealingTreeWarpPatcher. No ordering change.
- **Flag allocation**: Python-side flag_id assignment (output.py) is unchanged. event_map format unchanged.

### Medium risk

- **Graph iteration correctness**: the region mapping depends on iterating FogMod's Graph edges after connection injection. If FogMod's Graph.Connect() or DuplicateEntrance() modifies Warp.Region (it shouldn't — it only sets Link references), the mapping would be wrong. **Mitigation**: unit test that verifies region values survive connection injection; integration test that runs a full build and verifies all flags are injected.

- **Missing edges**: if a connection's exit gate doesn't match any Graph edge Name (typo, FogMod internal renaming), the region won't be captured and the flag will be missing. Phase 3 validation catches this as a fatal error, same as today. **Mitigation**: log warnings during mapping construction for unmatched connections.

- **Warp instructions not covered**: if FogMod introduces a new warp instruction type (beyond WarpPlayer and PlayCutsceneToPlayerAndWarp) that uses a different region parameter layout, the region extraction would miss it. **Mitigation**: Phase 3 validation catches missing flags. This is the same risk as today.

### Minimal risk

- **Python-side over-rejection**: until the Python collision validators are removed, seeds that would now succeed in C# are still rejected in Python. This is harmless (valid seeds rejected, not invalid ones accepted) and is addressed in the follow-up scope.

## Expected Gains

### Correctness

- **Zero collision-induced build failures**: the region mapping is deterministic and collision-free by construction. No DAG configuration can cause an unresolvable ambiguity.
- **Simpler mental model**: one lookup replaces five strategies with complex interaction rules.

### Performance (Python side, after follow-up)

- **Higher seed acceptance rate**: removing Python collision validators eliminates false rejections, especially for dense DAGs with many cross-links.
- **Broader seed diversity**: configurations previously rejected (two connections from same source to same dest map) become valid.

### Maintainability

- **~500 lines of matching logic removed**: EntityCandidate, RegionCandidate, compound/dest-only/common event lookups and all their interaction rules.
- **No special cases**: WarpBonfire, parameterized entities, AEG099 gates, adjacent map tiles — all handled uniformly by region lookup.
- **Fewer Python/C# coupling points**: `has_common_event`, `exit_entity_id`, and collision prevention in crosslinks.py/validator.py become unnecessary.

### Quantified impact

Current rejection causes (from Python validator):
- Zone tracking collisions: triggered when `allow_entry_as_exit` creates shared-exit-gate + same-dest-map pairs
- Compound collisions: triggered when cross-links create same-source + same-dest-map pairs

After this change, only the shared-entrance-different-cluster guard remains (option 3), which is a much narrower constraint. The exact rejection rate improvement depends on DAG density but is expected to be significant for configs with `crosslinks > 0`.
