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

Each entrance in fog.txt has a **unique Region** — either a pre-existing vanilla entity or a FogMod-allocated entity from FOGMOD_ENTITY_BASE (755890000). This region is unique per entrance gate, but shared across connections that use `DuplicateEntrance` to reuse the same entrance. It is preserved exactly in compiled events and available before compilation.

### Approach: pre-build region-to-flags mapping from FogMod's Graph

**Phase 1 — Build mapping (inside InjectAndExtract(), before Write()):**

Build the mapping inside `ConnectionInjector.InjectAndExtract()`, where both the `Connection` object (with `flag_id`) and the resolved entrance edge (with `Link.Side.Warp.Region`) are already available. This avoids a fragile post-hoc Graph walk that would need to re-match `edge.Name` → connection — a non-trivial mapping when `allow_entry_as_exit` creates multiple connections from the same exit gate.

For each connection, after `Graph.Connect()` or `Graph.DuplicateEntrance()`:

1. Get `flag_id` from the connection
2. Extract `entranceEdge.Side.Warp.Region` — the primary destination region (same Side chain that FogMod will read during event compilation)
3. Record `region → flag_id` in a `Dictionary<int, List<int>>`. If the region already exists, append the new flag_id to the list.
4. If `entranceEdge.Side.AlternateSide?.Warp?.Region` exists (AlternateFlag warps like flag 300/330), register that alternate region with the same flag_id
5. **Assertion**: after building the mapping, verify that all flag_ids for the same region map to the same cluster in `event_map`. If not, throw a diagnostic error naming both connections. This is a safety net — it should never fire (see Shared Entrance Analysis below).

For `IgnorePair` connections (entry-as-exit via `allow_entry_as_exit`), the entrance edge's Side comes from the fog gate's original entrance definition, so `Side.Warp.Region` is still the correct destination region.

For shared entrances (DuplicateEntrance), `Graph.DuplicateEntrance()` creates a new Edge but reuses the same `Side` object (`Graph.cs:408`), so both entrance edges share the same `Warp.Region`. Two connections sharing the same entrance will have **different flag_ids** (flags are allocated per-connection in output.py, not per-cluster). Both flag_ids are added to the list for that region.

**Phase 2 — Scan and inject (simplified, after Write()):**

For each warp instruction (WarpPlayer 2003:14, PlayCutsceneToPlayerAndWarp 2002:11/12) in compiled EMEVDs:

1. Extract the region parameter from instruction arguments (always a literal value — see "Region values are literal" below)
2. Look up region in the `regionToFlags` dictionary
3. If found, inject `SetEventFlag(flag_id, ON)` for **each** flag_id in the list, before the warp instruction
4. If not found, skip (not one of our connections' warps)

In the common case (no shared entrance), the list has exactly one flag_id. For shared entrances, all flag_ids in the list map to the same destination cluster. See [Shared entrance analysis](#shared-entrance-analysis) for the semantic implications and [Consumer Impact](#consumer-impact) for how event consumers should handle multi-flag injection.

**Phase 3 — Validation (unchanged):**

Compare injected flags against expected flags. Abort if any connection's flag was not injected.

### What this replaces

| Current (heuristic) | Proposed (region lookup) |
|---------------------|-------------------------|
| 5 matching strategies (0, R, 1, 2, 3) | 1 dictionary lookup (multi-flag for shared entrances) |
| EntityCandidate, RegionCandidate structs | Removed |
| Compound key lookup + collision tracking | Removed |
| Dest-only lookup + collision tracking | Removed |
| Common event lookup (WarpBonfire special case) | Removed |
| FOGMOD_ENTITY_BASE filter | Implicit (region not in dict = skip) |
| IfActionButtonInArea pre-scan + parameterized entity resolution | Removed |
| InitializeEvent args parsing | Removed |
| ~800 lines of matching logic | ~80-100 lines of region lookup + multi-flag injection |

**Retained code** (reused as-is): `TryExtractWarpInfo` + `WarpInfo` struct (region extraction from warp instructions), `UnpackMapId` + `FormatMap` (diagnostics), `InjectBossDeathEvent` (boss death monitor), Phase 3 validation (flag completeness check).

**Removed code** (dead after this change): `ParseGateActionEntity`, `EntityCandidate` + `RegionCandidate` structs, `ResolveEntityCandidate`, `ResolveRegionCandidate`, `TryMatchEntityCandidates`, `RegisterEntity`, `RegisterCommonEventKeys`, compound/dest-only/common event lookup dictionaries and collision tracking.

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

### Region values are literal

In compiled EMEVD output, WarpPlayer region arguments are **always literal integer values** baked into ArgData, never parameterized. This is true for all FogMod warp code paths:

- **Template fogwarp events** (9005777): EventEditor.Process() resolves template parameters (X8_4 etc.) into literal values at compile time. Per-instance events (IDs ~1040290xxx) contain baked region values.
- **Manual fogwarp events** (L3490-3546): `list53.Add(region6)` adds the literal region to event args.
- **WarpFlag events** (L3389-3435): `list49.Add(region6)` bakes the literal region.

Only `IfActionButtonInArea` entity IDs can be parameterized in manual events (entity_id=0 in instruction, resolved via InitializeEvent args). The region-based approach doesn't depend on IfActionButtonInArea at all, so parameterization is irrelevant.

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
| Shared entrance (DuplicateEntrance) | Entity disambiguation by dest map | Multiple flag_ids per region, all injected. All map to same cluster — semantically correct (see analysis below). |
| WarpCharacterAndCopyFloorWithFadeout (2003:74) | Not handled (not a fog gate warp) | Same: not handled. This instruction is used for boss arena/dungeon internal warps (L3044: `spawnPoint2.EntityID`), not fog gate connections. Fogwarp template uses WarpPlayer (2003:14). |

### Data flow

```
graph.json connections + event_map
        │
        ▼
ConnectionInjector.InjectAndExtract()
        │  connects edges in FogMod Graph
        │  [NEW] builds regionToFlags mapping inline:
        │    connection.flag_id + entranceEdge.Side.Warp.Region
        │    region → List<flag_id> (multi-flag for shared entrances)
        │    asserts same-cluster invariant via event_map
        ▼
GameDataWriterE.Write()
        │  compiles fogwarp events (uses same Region values)
        ▼
ZoneTrackingInjector.Inject(regionToFlags, ...)
        │  scan EMEVDs, extract region, lookup in dict
        │  inject SetEventFlag for each flag_id in list
        ▼
SetEventFlag injected before each matched warp
```

### Shared entrance analysis

When two connections share the same entrance gate (DuplicateEntrance), they share the same `Warp.Region` but have **different flag_ids** (flags are allocated per-connection in output.py). The mapping becomes `region → [flag_A, flag_B]`.

**Why all flags for the same region always map to the same cluster:**

An entrance gate is a physical entity in a specific zone. Zones belong to exactly one cluster. Two connections sharing the same entrance_gate are two different exit gates (from potentially different source clusters) that both lead into the same destination zone — and therefore the same destination cluster. In `event_map`, both flag_A and flag_B map to the same cluster_id.

`allow_entry_as_exit` does not change this: it allows using an entrance fog as an EXIT for another connection, which creates a new exit edge, not a shared entrance. `DuplicateEntrance` is only called when two connections have the same `entrance_gate` in ConnectionInjector.

**Approach: inject all flags for the region.**

When a warp targets region R with flag_ids [F1, F2], inject both `SetEventFlag(F1, ON)` and `SetEventFlag(F2, ON)`. Both flags map to the same cluster in `event_map`, so all event consumers correctly identify the destination zone regardless of which exit gate the player used.

**Semantic change for shared entrances:** This changes the flag semantic from **per-connection** ("flag F means connection C was traversed") to **per-cluster** ("flag F means cluster X was entered") for connections that share an entrance gate. In the common case (no shared entrance), the list has exactly one flag_id and the semantics are identical. For shared entrances, all flags for the region fire simultaneously on any traversal — the consumer cannot determine which specific exit gate was used.

This is acceptable because:

1. **The per-connection semantic was never consumed.** The racing mod (`handle_event_flag` in speedfog-racing) immediately resolves `flag_id → node_id` via `event_map.get(str(flag_id))` and discards the flag. It tracks `node_id` (cluster-level), not connection-level information. The comment in `output.py:288-290` ("detect re-entry from a different branch") described an allocated capability that no consumer ever implemented.

2. **The lost information is recoverable from context.** Shared entrance means two exit gates (in different source clusters) connecting to the same entrance gate. The racing mod already tracks the sequence of cluster entries in `zone_history` — the previous entry identifies which source cluster the player came from, which determines the branch.

3. **Event flags are one-shot.** EMEVD flags are set once and never reset. Even with per-connection flags, re-entry to the same cluster from a different branch is indistinguishable from the first entry once the flag is already ON. The per-connection granularity only matters for the very first traversal, and only if the consumer inspects individual flags rather than resolving to nodes.

See [Consumer Impact](#consumer-impact) for detailed analysis of the effects on event consumers.

**Safety assertion**: during mapping construction (Phase 1 step 5), verify that all flag_ids for the same region resolve to the same cluster in `event_map`. If this assertion ever fires, it indicates an architectural invariant violation that needs investigation. No Python-side guard is needed because the invariant is structural, not configuration-dependent.

## Consumer Impact

### Semantic contract

The flag contract changes for shared entrances:

| Aspect | Before (heuristic) | After (region-based) |
|--------|-------------------|---------------------|
| Non-shared entrance | Flag F fires when connection C is traversed | Same — one flag per region, identical semantic |
| Shared entrance | Flag F_A fires only when exit gate A is used; F_B only for gate B | F_A **and** F_B both fire on any traversal through the shared entrance |
| `event_map` resolution | `flag → node_id` (unchanged) | `flag → node_id` (unchanged) |

For non-shared entrances (the vast majority of connections), behavior is identical. The change only affects shared entrances where `DuplicateEntrance` creates a `region → [F_A, F_B]` mapping.

### speedfog-racing analysis

`handle_event_flag` in `speedfog_racing/websocket/mod.py` processes flags as follows:

```python
node_id = event_map.get(str(flag_id))   # resolve flag → node
is_first_visit = not any(entry.get("node_id") == node_id for entry in old_history)
participant.current_zone = node_id       # node-level tracking
participant.zone_history = [*old_history, {"node_id": node_id, "igt_ms": igt, "type": "fog"}]
```

**Key observation:** the `flag_id` is discarded immediately after resolving to `node_id`. All downstream logic (`current_zone`, `current_layer`, `zone_history`, `build_leader_splits`, `get_layer_entry_igt`, death attribution) operates on node_id, not flag_id. There is no per-connection logic anywhere in the consumer.

**With multi-flag injection on shared entrances**, the mod sends two `event_flag` messages in rapid succession (F_A and F_B, same EMEVD frame). The server processes them sequentially:

| Call | flag_id | node_id | is_first_visit | Effect |
|------|---------|---------|----------------|--------|
| 1st | F_A | N | True | Append `{N, T, "fog"}` to history. Broadcast leaderboard. |
| 2nd | F_B | N | False | Append `{N, T, "fog"}` (duplicate). Broadcast player_update. |

Downstream functions handle the duplicate gracefully:
- `build_leader_splits`: `if layer not in splits` — first entry wins, duplicate ignored
- `get_layer_entry_igt`: returns first match — duplicate invisible
- `current_layer`: high watermark, idempotent
- Death attribution: uses `current_zone` (node-level) — unaffected

**The only observable effect is a duplicate entry in `zone_history`** — two identical `{"node_id": N, "igt_ms": T, "type": "fog"}` entries. This is cosmetic (no functional impact) but should be cleaned up.

### Recommended consumer-side deduplication

Event consumers that receive flags from SpeedFog EMEVD injection should handle the case where multiple flags resolve to the same node at the same timestamp. The recommended guard for speedfog-racing:

```python
# In handle_event_flag, before appending to zone_history:
if (old_history
    and old_history[-1].get("node_id") == node_id
    and old_history[-1].get("igt_ms") == igt):
    return  # duplicate from multi-flag shared entrance
```

This dedup guard is optional but recommended for clean `zone_history` data. It should be implemented as a separate change in speedfog-racing, independent of the C# changes in this spec.

### General consumer guidelines

Any system consuming SpeedFog event flags should:

1. **Resolve flags via `event_map`** — treat flags as opaque identifiers that resolve to node_ids, not as connection identifiers.
2. **Handle duplicate node arrivals** — multiple flags may fire for the same node in the same frame. Consumers should be idempotent on `(node_id, timestamp)`.
3. **Do not assume flag uniqueness per traversal** — a single fog gate traversal may set 1 or N flags (N > 1 for shared entrances). All resolve to the same node.

### Python-side documentation update

`output.py:288-290` should be updated to reflect the actual consumer contract:

```python
# Allocate event flag IDs per connection (for zone tracking).
# Each connection gets a unique flag_id, but for shared entrances (DuplicateEntrance),
# multiple flags map to the same destination node in event_map.
# Consumers resolve flags to nodes via event_map — per-connection identity is not guaranteed
# to be distinguishable at the EMEVD level (see region-based-zone-tracking spec).
```

## Changes Required

### C# changes

| File | Change |
|------|--------|
| `ConnectionInjector.cs` / `InjectionResult` | After injecting connections, iterate Graph edges to build `Dictionary<int, List<int>>` regionToFlags. Add it to InjectionResult. Handle AlternateSide regions. Assert same-cluster invariant for shared regions. |
| `ZoneTrackingInjector.cs` | Replace Phase 1 (build lookups) and Phase 2 (multi-strategy scan) with region-based lookup. Inject all flag_ids per region for shared entrances. Remove EntityCandidate, RegionCandidate, compound/dest-only/common event lookups. Keep TryExtractWarpInfo (region extraction) and InjectBossDeathEvent (unchanged). |
| `Program.cs` | Pass `injectionResult.RegionToFlags` to ZoneTrackingInjector.Inject(). Remove areaMaps construction. Simplify Inject() signature. |
| `FogModWrapper.Tests/ZoneTrackingTests.cs` | Replace entity/region/compound/common event unit tests with region-lookup tests. |

### Python changes (follow-up, not in initial scope)

Once the C# side is validated:

| File | Change |
|------|--------|
| `validator.py` | Remove zone tracking collision check and compound collision check entirely. No shared-entrance guard needed — the same-cluster invariant is structural, not configuration-dependent. |
| `crosslinks.py` | Remove `_build_collision_index`, `_build_compound_index`, `_would_collide`, `_would_compound_collide` and associated filtering |
| `tests/test_validator.py` | Remove collision test cases |
| `tests/test_crosslinks.py` | Remove collision filtering test cases |
| `output.py` | Remove `has_common_event` emission (dead after C# change). Update comment at L288-290 to reflect per-node (not per-connection) consumer contract. |
| `generate_clusters.py` | Remove `warp_bonfire` propagation to cluster exit_fogs (only used for `has_common_event`) |

### Test plan

**Prerequisite — Region stability verification** (run before implementation):

0. **Region survives Connect()**: load a real FogMod Graph from game data, read `entranceEdge.Side.Warp.Region` before `Graph.Connect()`, read it again after. Assert the values are identical. Repeat for `Graph.DuplicateEntrance()`. This validates the critical hypothesis that Connect() does not modify Warp data. If this test fails, the entire approach must be reconsidered.

**Unit tests** (ZoneTrackingTests.cs — replace existing tests):

1. **Region mapping construction**: given a mock Graph with connected edges and a list of connections, verify that `BuildRegionToFlags()` produces the expected `region → List<flag_id>` dictionary.
2. **AlternateFlag regions**: verify that both primary and alternate regions are registered with the same flag_id when `AlternateSide.Warp.Region` exists.
3. **Shared entrance, same cluster**: verify that two connections sharing an entrance Region (different flag_ids, same cluster in event_map) produce a mapping with both flag_ids in the list. No error.
4. **Shared entrance, different clusters (safety assertion)**: verify that two connections sharing an entrance Region but mapping to different clusters in event_map trigger a diagnostic error during mapping construction. (This case is architecturally impossible in current DAG generation — the test verifies the safety net.)
5. **Region lookup, single flag**: given a WarpPlayer instruction with a known region that maps to one flag_id, verify that one SetEventFlag is injected before it. Verify that an unknown region is skipped.
6. **Region lookup, multiple flags (shared entrance)**: given a WarpPlayer instruction with a region that maps to two flag_ids, verify that both SetEventFlag instructions are injected before the warp.
7. **AlternateFlag with shared entrance**: verify that a shared entrance with an `AlternateSide` registers both primary and alternate regions with all flag_ids from the list.

**Integration test** (run_integration.sh):

- Full build with a known seed, verify all expected flags are injected (Phase 3 validation passes).
- Compare injected flag count against the previous heuristic approach to confirm equivalence.

### speedfog-racing follow-up (separate repo)

Add a deduplication guard in `handle_event_flag` to prevent duplicate `zone_history` entries from shared-entrance multi-flag injection. This is cosmetic (no functional impact without it) but produces cleaner history data for spectators and analytics.

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

- **Critical hypothesis — Region stability through Connect()**: the entire approach depends on `entranceEdge.Side.Warp.Region` being readable and correct after `Graph.Connect()` and `Graph.DuplicateEntrance()`. The claim that `Graph.Connect()` only sets Link/From/To references without modifying Warp data is based on analysis of `Graph.cs:415-458` (decompiled, not authoritative source). If FogMod modified Warp.Region during connection (e.g., entity ID reallocation), the mapping would be silently wrong. **Mitigation (required before implementation)**: write a verification test that reads `Side.Warp.Region` on an entrance edge before and after `Graph.Connect()` on a real FogMod Graph loaded from game data. This transforms the hypothesis into a verified fact. Phase 3 validation provides a second safety net (missing flags abort the build), but silent wrong-region mapping could produce wrong flags rather than missing ones — the verification test catches this.

- **Missing edges**: if a connection's exit gate doesn't match any Graph edge Name (typo, FogMod internal renaming), the region won't be captured and the flag will be missing. Phase 3 validation catches this as a fatal error, same as today. **Mitigation**: log warnings during mapping construction for unmatched connections.

- **Warp instructions not covered**: if FogMod introduces a new warp instruction type (beyond WarpPlayer and PlayCutsceneToPlayerAndWarp) that uses a different region parameter layout, the region extraction would miss it. **Mitigation**: Phase 3 validation catches missing flags. This is the same risk as today.

### Transition risk

- **Rewrite scope**: replacing ~800 lines of working (if complex) matching logic with ~80-100 lines of region lookup. The existing unit tests (ZoneTrackingTests.cs) must be rewritten in parallel. During the transition, test coverage is temporarily reduced. **Mitigation**: keep the old implementation available on a branch or as a commented reference until 3+ integration runs with different seeds confirm equivalence. Do not delete old tests until new tests cover all edge cases in the test plan.

### Minimal risk

- **Python-side over-rejection**: until the Python collision validators are removed, seeds that would now succeed in C# are still rejected in Python. This is harmless (valid seeds rejected, not invalid ones accepted) and is addressed in the follow-up scope.

## Expected Gains

### Correctness

- **Zero collision-induced build failures**: the region mapping handles shared entrances by injecting all associated flags (all semantically correct). No DAG configuration can cause an unresolvable ambiguity.
- **Simpler mental model**: one lookup replaces five strategies with complex interaction rules.

### Performance (Python side, after follow-up)

- **Higher seed acceptance rate**: removing Python collision validators eliminates false rejections, especially for dense DAGs with many cross-links.
- **Broader seed diversity**: configurations previously rejected (two connections from same source to same dest map) become valid.

### Maintainability

- **~800 lines of matching logic removed**: EntityCandidate, RegionCandidate, ParseGateActionEntity, RegisterEntity, RegisterCommonEventKeys, compound/dest-only/common event lookups and all their interaction rules.
- **No special cases**: WarpBonfire, parameterized entities, AEG099 gates, adjacent map tiles — all handled uniformly by region lookup.
- **Fewer Python/C# coupling points**: `has_common_event`, `exit_entity_id`, and collision prevention in crosslinks.py/validator.py become unnecessary.

### Quantified impact

Current rejection causes (from Python validator):
- Zone tracking collisions: triggered when `allow_entry_as_exit` creates shared-exit-gate + same-dest-map pairs
- Compound collisions: triggered when cross-links create same-source + same-dest-map pairs

After this change, **all** Python collision guards can be removed — no remaining constraints. The region-based approach handles every configuration that the current heuristics cannot. The exact rejection rate improvement depends on DAG density but is expected to be significant for configs with `crosslinks > 0`.
