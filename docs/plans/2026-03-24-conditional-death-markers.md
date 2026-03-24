# Conditional Death Markers Implementation Plan (speedfog)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make death markers conditional on event flags, controllable by the speedfog-racing mod based on real-time death counts. Add a config toggle to disable the feature.

**Architecture:** Python allocates 3 death flags per cluster and emits `death_flags` in graph.json (or `{}` when disabled). C# reads `death_flags` and changes DeathMarkerInjector from unconditional activation (event 0) to conditional EMEVD events that wait for flags. The racing mod (separate plan) sets these flags based on server-broadcasted death counts.

**Tech Stack:** Python 3.10+ (config, graph generation), C# .NET 8.0 (FogModWrapper, SoulsFormats/SoulsIds)

**Spec:** `docs/specs/2026-03-24-conditional-death-markers.md`

---

## File Map

| File | Change | Purpose |
|------|--------|---------|
| `speedfog/config.py` | Modify | Add `death_markers: bool` to `Config` |
| `config.example.toml` | Modify | Document `death_markers` option |
| `speedfog/output.py` | Modify | Allocate death flags, emit `death_flags` in graph.json |
| `speedfog/main.py` | Modify | Pass `death_markers` to `export_json` |
| `tests/test_config.py` | Modify | Test `death_markers` config parsing |
| `tests/test_output.py` | Modify | Test `death_flags` allocation and structure |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | Modify | Add `DeathFlags` property |
| `writer/FogModWrapper/DeathMarkerInjector.cs` | Modify | Conditional EMEVD events per cluster flag |
| `writer/FogModWrapper/Program.cs` | Modify | Pass `DeathFlags` + `EventMap` to injector |

---

### Task 1: Add `death_markers` config option (Python)

**Files:**
- Modify: `speedfog/config.py:447-462` (Config dataclass)
- Modify: `speedfog/config.py:464-591` (Config.from_dict)
- Modify: `config.example.toml`
- Test: `tests/test_config.py`

- [ ] **Step 1: Write test for death_markers config**

In `tests/test_config.py`, add:

```python
def test_death_markers_default_true():
    config = Config.from_dict({})
    assert config.death_markers is True


def test_death_markers_explicit_false():
    config = Config.from_dict({"run": {"death_markers": False}})
    assert config.death_markers is False
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_config.py::test_death_markers_default_true -v`
Expected: FAIL (AttributeError: 'Config' has no attribute 'death_markers')

- [ ] **Step 3: Add death_markers to Config dataclass and from_dict**

In `speedfog/config.py`, add to Config dataclass (after `sentry_torch_shop`):

```python
    death_markers: bool = True
```

In `Config.from_dict()`, add after the `sentry_torch_shop` line:

```python
            death_markers=run_section.get("death_markers", True),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_config.py -k death_markers -v`
Expected: 2 PASSED

- [ ] **Step 5: Add to config.example.toml**

In `config.example.toml`, add after the `sentry_torch_shop` comment in the `[run]` section:

```toml
# Place bloodstain markers at fog gates for racing death count display.
# When enabled, graph.json includes death_flags for the racing mod.
# Default: true
# death_markers = true
```

- [ ] **Step 6: Commit**

```bash
git add speedfog/config.py config.example.toml tests/test_config.py
git commit -m "feat: add death_markers config option (default true)"
```

---

### Task 2: Allocate death flags in graph.json (Python)

**Files:**
- Modify: `speedfog/output.py:295-560` (dag_to_dict, export_json)
- Modify: `speedfog/main.py:247-263` (export_json call)
- Test: `tests/test_output.py`

- [ ] **Step 1: Write tests for death_flags allocation**

In `tests/test_output.py`, add:

```python
def test_death_flags_present_when_enabled():
    """death_flags maps each non-start cluster to 3 flags."""
    result = _make_result(death_markers=True)
    assert "death_flags" in result
    death_flags = result["death_flags"]
    # Should have entries for all clusters except start
    node_ids = set(result["nodes"].keys())
    start_ids = {nid for nid, n in result["nodes"].items() if n["type"] == "start"}
    expected_ids = node_ids - start_ids
    assert set(death_flags.keys()) == expected_ids
    for cluster_id, flags in death_flags.items():
        assert len(flags) == 3
        assert all(isinstance(f, int) for f in flags)
        assert all(f >= 1040292400 for f in flags)


def test_death_flags_empty_when_disabled():
    """death_flags is empty dict when death_markers=False."""
    result = _make_result(death_markers=False)
    assert result["death_flags"] == {}


def test_death_flags_are_unique():
    """All death flag IDs are unique and don't overlap with connection flags."""
    result = _make_result(death_markers=True)
    connection_flags = {c["flag_id"] for c in result["connections"]}
    death_flag_ids = set()
    for flags in result["death_flags"].values():
        for f in flags:
            assert f not in connection_flags, f"death flag {f} overlaps connection flag"
            assert f not in death_flag_ids, f"duplicate death flag {f}"
            death_flag_ids.add(f)


def test_death_flags_after_finish_event():
    """Death flags are allocated after finish_event."""
    result = _make_result(death_markers=True)
    if result["death_flags"]:
        min_death_flag = min(f for flags in result["death_flags"].values() for f in flags)
        assert min_death_flag > result["finish_event"]
```

- [ ] **Step 2: Update `_make_result` helper to accept `death_markers` parameter**

Find `_make_result` in test_output.py and add `death_markers=True` as a parameter, passing it through to `dag_to_dict`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `pytest tests/test_output.py -k death_flags -v`
Expected: FAIL

- [ ] **Step 4: Implement death_flags allocation in output.py**

In `dag_to_dict()`, add `death_markers: bool = True` parameter.

After the `finish_event` allocation (line ~471), add:

```python
    # Allocate death marker flags: 3 per cluster (low/med/high)
    # for racing mod to control bloodstain visibility by death count.
    death_flags: dict[str, list[int]] = {}
    if death_markers:
        start_cluster_id = dag.nodes[dag.start_id].cluster.id
        for node in dag.nodes.values():
            cluster_id = node.cluster.id
            if cluster_id == start_cluster_id:
                continue
            flags = [
                EVENT_FLAG_BASE + flag_counter + i
                for i in range(3)
            ]
            flag_counter += 3
            death_flags[cluster_id] = flags
```

Add `"death_flags": death_flags,` to the returned dict (after `"finish_boss_defeat_flag"`).

Update the budget check to account for death flags:

```python
    if flag_counter > 600:
        raise ValueError(
            f"Event flag budget exceeded: {flag_counter} flags allocated "
            f"(max 600 in range 1040292400-1040292999)"
        )
```

- [ ] **Step 5: Thread death_markers through export_json and main.py**

In `export_json()`, add `death_markers: bool = True` parameter. Pass it to `dag_to_dict()`.

In `main.py`, update the `export_json` call to pass `death_markers=config.death_markers`.

- [ ] **Step 6: Run tests**

Run: `pytest tests/test_output.py -k death_flags -v`
Expected: 4 PASSED

Run: `pytest tests/ -v`
Expected: all pass (no regressions)

- [ ] **Step 7: Commit**

```bash
git add speedfog/output.py speedfog/main.py tests/test_output.py
git commit -m "feat: allocate death_flags per cluster in graph.json"
```

---

### Task 3: Add DeathFlags to C# GraphData model

**Files:**
- Modify: `writer/FogModWrapper.Core/Models/GraphData.cs`

- [ ] **Step 1: Add DeathFlags property to GraphData**

After the `RemoveEntities` property (line ~121), add:

```csharp
    /// <summary>
    /// Death marker flags per cluster: cluster_id -> [flag_low, flag_med, flag_high].
    /// When empty, death markers are placed unconditionally (no racing integration).
    /// Allocated by Python after connection flags and finish_event.
    /// </summary>
    [JsonPropertyName("death_flags")]
    public Dictionary<string, List<int>> DeathFlags { get; set; } = new();
```

- [ ] **Step 2: Build**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper.Core/Models/GraphData.cs
git commit -m "feat: add DeathFlags property to GraphData model"
```

---

### Task 4: Make DeathMarkerInjector conditional

**Files:**
- Modify: `writer/FogModWrapper/DeathMarkerInjector.cs`
- Modify: `writer/FogModWrapper/Program.cs`

This is the core task. The injector changes from:
- Current: 3 bloodstains per gate, unconditional enable+SFX in event 0
- New: 1 bloodstain per gate per death flag level, conditional EMEVD events

- [ ] **Step 1: Update Inject() signature and collection logic**

Change the `Inject` method signature to accept the new data:

```csharp
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections, Events events,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags)
```

Change `CollectGatesByMap` to return cluster association info. Replace the simple `HashSet<string>` with a struct that tracks which cluster each gate belongs to and which death flag tier it corresponds to.

New collection logic:

```csharp
    /// <summary>
    /// Info about a bloodstain to place at a gate for a specific death flag.
    /// </summary>
    private readonly struct BloodstainSpec
    {
        public readonly string PartName;
        public readonly int DeathFlag;  // The event flag controlling this bloodstain
        public readonly int TierIndex;  // 0=low, 1=med, 2=high (for offset generation)

        public BloodstainSpec(string partName, int deathFlag, int tierIndex)
        {
            PartName = partName;
            DeathFlag = deathFlag;
            TierIndex = tierIndex;
        }
    }
```

New `CollectGatesByMap`:

```csharp
    private static Dictionary<string, List<BloodstainSpec>> CollectConditionalGatesByMap(
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags)
    {
        var result = new Dictionary<string, List<BloodstainSpec>>();

        // For each connection, the destination cluster is eventMap[flagId].
        // Both the exit gate and entrance gate get bloodstains controlled by
        // that cluster's death flags.
        foreach (var conn in connections)
        {
            if (!eventMap.TryGetValue(conn.FlagId.ToString(), out var clusterId))
                continue;
            if (!deathFlags.TryGetValue(clusterId, out var flags))
                continue;

            foreach (var gate in new[] { conn.ExitGate, conn.EntranceGate })
            {
                var (mapId, partName) = ParseGateFullName(gate);
                if (!result.TryGetValue(mapId, out var specs))
                {
                    specs = new List<BloodstainSpec>();
                    result[mapId] = specs;
                }

                for (int tier = 0; tier < flags.Count; tier++)
                {
                    // Deduplicate: same gate + same flag = same bloodstain
                    if (!specs.Any(s => s.PartName == partName && s.DeathFlag == flags[tier]))
                    {
                        specs.Add(new BloodstainSpec(partName, flags[tier], tier));
                    }
                }
            }
        }

        return result;
    }
```

- [ ] **Step 2: Update Inject() to dispatch between conditional and unconditional mode**

```csharp
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections, Events events,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags)
    {
        Console.WriteLine("Injecting death markers at fog gates...");

        bool conditional = deathFlags.Count > 0;
        uint nextEntityId = FindMaxFogModEntityId(modDir) + 1;
        int totalAssets = 0;
        int totalMaps = 0;

        if (conditional)
        {
            var specsByMap = CollectConditionalGatesByMap(connections, eventMap, deathFlags);
            foreach (var (mapId, specs) in specsByMap)
            {
                var (count, nextId) = InjectMapConditional(
                    modDir, gameDir, events, mapId, specs, nextEntityId);
                if (count > 0) { totalAssets += count; totalMaps++; }
                nextEntityId = nextId;
            }
        }
        else
        {
            // Unconditional mode (death_markers disabled or no death_flags)
            var gatesByMap = CollectGatesByMap(connections);
            foreach (var (mapId, partNames) in gatesByMap)
            {
                var (count, nextId) = InjectMap(
                    modDir, gameDir, events, mapId, partNames, nextEntityId);
                if (count > 0) { totalAssets += count; totalMaps++; }
                nextEntityId = nextId;
            }
        }

        Console.WriteLine($"  Placed {totalAssets} bloodstain markers across {totalMaps} maps" +
            (conditional ? " (conditional)" : " (unconditional)"));
    }
```

- [ ] **Step 3: Implement InjectMapConditional**

Add a new method that places 1 bloodstain per spec and creates conditional EMEVD events
grouped by death flag:

```csharp
    private const int DEATH_MARKER_EVENT_BASE = 755862100;
    private static int _nextEventOffset = 0;

    private static (int Count, uint NextEntityId) InjectMapConditional(
        string modDir, string gameDir, Events events,
        string mapId, List<BloodstainSpec> specs, uint nextEntityId)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = FindMsbPath(modDir, msbFileName) ?? FindMsbPath(gameDir, msbFileName);
        if (msbPath == null) return (0, nextEntityId);

        var msb = MSBE.Read(msbPath);
        if (msb.Parts.MapPieces.Count == 0) return (0, nextEntityId);

        EnsureAssetModel(msb, BLOODSTAIN_MODEL);

        // Place bloodstain assets, grouped by death flag for EMEVD event creation
        var entityIdsByFlag = new Dictionary<int, List<uint>>();

        foreach (var spec in specs)
        {
            var gateAsset = msb.Parts.Assets.Find(a => a.Name == spec.PartName);
            if (gateAsset == null && uint.TryParse(spec.PartName, out uint eidLookup))
                gateAsset = msb.Parts.Assets.Find(a => a.EntityID == eidLookup);
            if (gateAsset == null) continue;

            var baseAsset = FindNearestVanillaAsset(msb, gateAsset.Position);
            if (baseAsset == null) continue;

            var drawGroups = GetDrawGroupsAtPosition(msb, gateAsset.Position);

            // Generate 1 offset for this tier (use tier index to get a different offset)
            var allOffsets = GenerateOffsets(gateAsset.EntityID, gateAsset.Rotation.Y);
            var offset = allOffsets[spec.TierIndex % allOffsets.Length];

            // DeepCopy workaround (save/restore)
            var savedDrawGroups = baseAsset.Unk1.DrawGroups.ToArray();
            var savedDisplayGroups = baseAsset.Unk1.DisplayGroups.ToArray();
            var savedCollisionMask = baseAsset.Unk1.CollisionMask.ToArray();
            var savedEntityGroupIDs = baseAsset.EntityGroupIDs.ToArray();
            var savedUnkPartNames = baseAsset.UnkPartNames.ToArray();
            var savedUnkT54 = baseAsset.UnkT54PartName;

            var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
            bloodstain.ModelName = BLOODSTAIN_MODEL;
            bloodstain.Name = GeneratePartName(
                msb.Parts.Assets.Select(a => a.Name), BLOODSTAIN_MODEL);
            SetNameIdent(bloodstain);
            bloodstain.Position = gateAsset.Position + offset;
            bloodstain.Rotation = new Vector3(0f, 0f, 0f);
            bloodstain.EntityID = nextEntityId;
            bloodstain.AssetSfxParamRelativeID = -1;
            for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                bloodstain.UnkPartNames[j] = null;
            bloodstain.UnkT54PartName = null;
            Array.Clear(bloodstain.EntityGroupIDs);
            if (drawGroups != null) ApplyDrawGroups(bloodstain, drawGroups);

            msb.Parts.Assets.Add(bloodstain);

            if (!entityIdsByFlag.TryGetValue(spec.DeathFlag, out var list))
            {
                list = new List<uint>();
                entityIdsByFlag[spec.DeathFlag] = list;
            }
            list.Add(nextEntityId);
            nextEntityId++;

            // Restore source arrays
            Array.Copy(savedDrawGroups, baseAsset.Unk1.DrawGroups, savedDrawGroups.Length);
            Array.Copy(savedDisplayGroups, baseAsset.Unk1.DisplayGroups, savedDisplayGroups.Length);
            Array.Copy(savedCollisionMask, baseAsset.Unk1.CollisionMask, savedCollisionMask.Length);
            Array.Copy(savedEntityGroupIDs, baseAsset.EntityGroupIDs, savedEntityGroupIDs.Length);
            Array.Copy(savedUnkPartNames, baseAsset.UnkPartNames, savedUnkPartNames.Length);
            baseAsset.UnkT54PartName = savedUnkT54;
        }

        int count = entityIdsByFlag.Values.Sum(l => l.Count);
        if (count == 0) return (0, nextEntityId);

        // Write MSB
        var writePath = FindMsbPath(modDir, msbFileName) ?? FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        // EMEVD: create conditional events per death flag
        var emevdFileName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdFileName);
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
            if (!File.Exists(gameEmevdPath)) return (count, nextEntityId);
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null) return (count, nextEntityId);

        foreach (var (deathFlag, entityIds) in entityIdsByFlag)
        {
            var eventId = DEATH_MARKER_EVENT_BASE + _nextEventOffset++;
            var evt = new EMEVD.Event(eventId);

            // Wait for the death flag to be set by the racing mod
            evt.Instructions.Add(events.ParseAdd(
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {deathFlag})"));

            // Enable and show SFX for all bloodstains controlled by this flag
            foreach (var entityId in entityIds)
            {
                evt.Instructions.Add(events.ParseAdd(
                    $"ChangeAssetEnableState({entityId}, Enabled)"));
                evt.Instructions.Add(events.ParseAdd(
                    $"CreateAssetfollowingSFX({entityId}, {SFX_DUMMY_POLY}, {SFX_ID})"));
            }

            emevd.Events.Add(evt);

            // Register in event 0
            var initArgs = new byte[8];
            BitConverter.GetBytes(0).CopyTo(initArgs, 0);
            BitConverter.GetBytes(eventId).CopyTo(initArgs, 4);
            initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));
        }

        emevd.Write(emevdPath);
        return (count, nextEntityId);
    }
```

- [ ] **Step 4: Reset _nextEventOffset at start of Inject**

Add at the top of `Inject()`:

```csharp
        _nextEventOffset = 0;
```

- [ ] **Step 5: Update Program.cs to pass new parameters**

In `Program.cs`, update the DeathMarkerInjector call (around line 507):

```csharp
        // 7h2. Death markers at fog gates
        DeathMarkerInjector.Inject(
            modDir, config.GameDir, graphData.Connections, events,
            graphData.EventMap, graphData.DeathFlags);
```

- [ ] **Step 6: Build**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add writer/FogModWrapper/DeathMarkerInjector.cs writer/FogModWrapper/Program.cs
git commit -m "feat: conditional death markers controlled by per-cluster flags"
```

---

### Task 5: End-to-end test

- [ ] **Step 1: Run Python tests**

Run: `pytest tests/ -v`
Expected: all pass

- [ ] **Step 2: Generate a seed with death_markers enabled (default)**

```bash
uv run speedfog config.toml --spoiler
```

Verify graph.json contains `death_flags` with 3 flags per cluster.

- [ ] **Step 3: Run FogModWrapper**

Build and run. Verify output includes:

```
Injecting death markers at fog gates...
  Placed N bloodstain markers across M maps (conditional)
```

- [ ] **Step 4: Generate a seed with death_markers disabled**

Add `death_markers = false` to config.toml `[run]` section, regenerate.
Verify graph.json has `"death_flags": {}`.
Verify FogModWrapper output shows `(unconditional)` or places markers without conditional events.

- [ ] **Step 5: In-game verification**

Launch with death_markers enabled. Bloodstains should NOT be visible initially
(flags are OFF, no racing mod running). If you set a death flag manually via CE
(`VirtualMemoryFlag` tree, set the flag ON), the bloodstains should appear.

- [ ] **Step 6: Commit any fixes**
