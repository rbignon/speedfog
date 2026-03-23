# Death Markers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Place 3 bloodstain visuals (AEG099_090 + SFX 42) at each fog gate exit and entrance in the DAG, unconditionally.

**Architecture:** A new `DeathMarkerInjector` class in FogModWrapper, called from `Program.cs` after FogMod writes. It reads compiled MSBs to find fog gate positions, places bloodstain assets at random offsets, and adds EMEVD instructions to enable them. Also remove the test bloodstain code from ChapelGraceInjector.

**Tech Stack:** C# / .NET 8.0, SoulsFormats (MSB/EMEVD), SoulsIds (event parsing)

**Spec:** `docs/specs/2026-03-23-death-markers.md`

---

### Task 1: Create DeathMarkerInjector with FullName parsing and offset generation

**Files:**
- Create: `writer/FogModWrapper/DeathMarkerInjector.cs`

- [ ] **Step 1: Create DeathMarkerInjector.cs with helper methods**

```csharp
using System.Numerics;
using SoulsFormats;
using SoulsIds;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Places bloodstain visual markers (AEG099_090 + SFX 42) at fog gate exits and
/// entrances throughout the DAG. Each gate gets 3 bloodstains at random offsets.
/// </summary>
public static class DeathMarkerInjector
{
    private const string BLOODSTAIN_MODEL = "AEG099_090";
    private const int SFX_ID = 42;
    private const int SFX_DMYPOLY = 100;
    private const uint ENTITY_ID_BASE = 755895000;
    private const int BLOODSTAINS_PER_GATE = 3;
    private const float RADIUS_MIN = 1.5f;
    private const float RADIUS_MAX = 3.0f;

    private static readonly string[] MSB_DIR_VARIANTS = { "mapstudio", "MapStudio" };

    /// <summary>
    /// Parse a connection gate FullName into map ID and part name.
    /// "m10_01_00_00_AEG099_001_9000" -> ("m10_01_00_00", "AEG099_001_9000")
    /// </summary>
    internal static (string mapId, string partName) ParseGateFullName(string fullName)
    {
        // Map ID is always 4 segments: mXX_YY_ZZ_WW
        var parts = fullName.Split('_');
        var mapId = string.Join("_", parts[0], parts[1], parts[2], parts[3]);
        var partName = string.Join("_", parts.Skip(4));
        return (mapId, partName);
    }

    /// <summary>
    /// Generate 3 offset positions around a gate, dispersed randomly with
    /// minimum 60-degree angular separation. PRNG seeded on gateEntityId
    /// for deterministic placement.
    /// </summary>
    internal static Vector3[] GenerateOffsets(uint gateEntityId)
    {
        var rng = new Random(gateEntityId.GetHashCode());
        var positions = new Vector3[BLOODSTAINS_PER_GATE];

        for (int i = 0; i < BLOODSTAINS_PER_GATE; i++)
        {
            // Each bloodstain gets a 120-degree sector
            int sectorStart = i * 120;
            int angle = sectorStart + rng.Next(0, 120);
            float radius = RADIUS_MIN + (float)rng.NextDouble() * (RADIUS_MAX - RADIUS_MIN);
            float rad = angle * MathF.PI / 180f;

            positions[i] = new Vector3(
                MathF.Sin(rad) * radius,
                0f,
                MathF.Cos(rad) * radius
            );
        }

        return positions;
    }

    /// <summary>
    /// Find an MSB file under a base directory, trying both mapstudio variants.
    /// </summary>
    private static string? FindMsbPath(string baseDir, string mapId)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        foreach (var dirName in MSB_DIR_VARIANTS)
        {
            var path = Path.Combine(baseDir, "map", dirName, msbFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/DeathMarkerInjector.cs
git commit -m "feat: add DeathMarkerInjector skeleton with parsing and offset helpers"
```

---

### Task 2: Implement the Inject method (MSB modification)

**Files:**
- Modify: `writer/FogModWrapper/DeathMarkerInjector.cs`

- [ ] **Step 1: Add the Inject method and MSB processing**

Add to `DeathMarkerInjector.cs`:

```csharp
    /// <summary>
    /// Inject bloodstain markers at all fog gate exits and entrances.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections, Events events)
    {
        Console.WriteLine("Injecting death markers at fog gates...");

        // Collect all gate FullNames (deduplicated: a gate can be used by multiple connections)
        var gatesByMap = new Dictionary<string, HashSet<string>>();
        foreach (var conn in connections)
        {
            foreach (var fullName in new[] { conn.ExitGate, conn.EntranceGate })
            {
                var (mapId, partName) = ParseGateFullName(fullName);
                if (!gatesByMap.ContainsKey(mapId))
                    gatesByMap[mapId] = new HashSet<string>();
                gatesByMap[mapId].Add(partName);
            }
        }

        uint nextEntityId = ENTITY_ID_BASE;
        // map -> list of bloodstain entity IDs placed in that map
        var bloodstainsByMap = new Dictionary<string, List<uint>>();
        int totalPlaced = 0;

        // Phase 1: Place bloodstain assets in MSBs
        foreach (var (mapId, partNames) in gatesByMap)
        {
            var msbPath = FindMsbPath(modDir, mapId) ?? FindMsbPath(gameDir, mapId);
            if (msbPath == null)
            {
                Console.WriteLine($"  Warning: MSB not found for {mapId}, skipping {partNames.Count} gates");
                continue;
            }

            var msb = MSBE.Read(msbPath);
            EnsureAssetModel(msb, BLOODSTAIN_MODEL);

            var baseAsset = msb.Parts.Assets.FirstOrDefault();
            if (baseAsset == null)
            {
                Console.WriteLine($"  Warning: No asset parts in {mapId} MSB to clone from");
                continue;
            }

            var entityIds = new List<uint>();

            foreach (var partName in partNames)
            {
                var gateAsset = msb.Parts.Assets.Find(a => a.Name == partName);
                if (gateAsset == null)
                {
                    Console.WriteLine($"  Warning: gate asset {partName} not found in {mapId}");
                    continue;
                }

                var offsets = GenerateOffsets(gateAsset.EntityID);

                for (int i = 0; i < BLOODSTAINS_PER_GATE; i++)
                {
                    var entityId = nextEntityId++;
                    var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
                    bloodstain.ModelName = BLOODSTAIN_MODEL;
                    bloodstain.Name = $"{BLOODSTAIN_MODEL}_{entityId % 10000:D4}";
                    SetNameIdent(bloodstain);
                    bloodstain.Position = gateAsset.Position + offsets[i];
                    bloodstain.Rotation = new Vector3(0f, gateAsset.Rotation.Y, 0f);
                    bloodstain.EntityID = entityId;
                    bloodstain.AssetSfxParamRelativeID = -1;
                    for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                        bloodstain.UnkPartNames[j] = null;
                    bloodstain.UnkT54PartName = null;
                    Array.Clear(bloodstain.EntityGroupIDs);
                    msb.Parts.Assets.Add(bloodstain);
                    entityIds.Add(entityId);
                }
            }

            if (entityIds.Count > 0)
            {
                // Write to mod dir (always)
                var writePath = FindOrCreateMsbPath(modDir, mapId);
                Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
                msb.Write(writePath);
                bloodstainsByMap[mapId] = entityIds;
                totalPlaced += entityIds.Count;
            }
        }

        Console.WriteLine($"  MSB: placed {totalPlaced} bloodstain assets across {bloodstainsByMap.Count} maps");

        // Phase 2: Add EMEVD instructions
        InjectEmevd(modDir, gameDir, events, bloodstainsByMap);
    }

    private static void EnsureAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = modelName });
    }

    private static void SetNameIdent(MSBE.Part part)
    {
        var segments = part.Name.Split('_');
        if (segments.Length > 0 && int.TryParse(segments[^1], out var ident))
            part.Unk08 = ident;
    }

    private static string FindOrCreateMsbPath(string modDir, string mapId)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var mapDir = Path.Combine(modDir, "map");
        if (Directory.Exists(mapDir))
        {
            foreach (var dirName in MSB_DIR_VARIANTS)
            {
                var dir = Path.Combine(mapDir, dirName);
                if (Directory.Exists(dir))
                    return Path.Combine(dir, msbFileName);
            }
        }
        return Path.Combine(mapDir, "mapstudio", msbFileName);
    }
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/DeathMarkerInjector.cs
git commit -m "feat: DeathMarkerInjector MSB phase (place bloodstain assets at gates)"
```

---

### Task 3: Implement EMEVD injection

**Files:**
- Modify: `writer/FogModWrapper/DeathMarkerInjector.cs`

- [ ] **Step 1: Add InjectEmevd method**

Add to `DeathMarkerInjector.cs`:

```csharp
    /// <summary>
    /// Add ChangeAssetEnableState + CreateAssetfollowingSFX for each bloodstain
    /// in the appropriate map EMEVD's event 0.
    /// </summary>
    private static void InjectEmevd(
        string modDir, string gameDir,
        Events events, Dictionary<string, List<uint>> bloodstainsByMap)
    {
        int totalInjected = 0;

        foreach (var (mapId, entityIds) in bloodstainsByMap)
        {
            var emevdPath = Path.Combine(modDir, "event", $"{mapId}.emevd.dcx");

            if (!File.Exists(emevdPath))
            {
                var gameEmevdPath = Path.Combine(gameDir, "event", $"{mapId}.emevd.dcx");
                if (!File.Exists(gameEmevdPath))
                {
                    Console.WriteLine($"  Warning: EMEVD not found for {mapId}, skipping {entityIds.Count} bloodstains");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
                File.Copy(gameEmevdPath, emevdPath);
            }

            var emevd = EMEVD.Read(emevdPath);
            var initEvent = emevd.Events.Find(e => e.ID == 0);
            if (initEvent == null)
            {
                Console.WriteLine($"  Warning: Event 0 not found in {mapId}.emevd, skipping");
                continue;
            }

            foreach (var entityId in entityIds)
            {
                initEvent.Instructions.Add(events.ParseAdd(
                    $"ChangeAssetEnableState({entityId}, Enabled)"));
                initEvent.Instructions.Add(events.ParseAdd(
                    $"CreateAssetfollowingSFX({entityId}, {SFX_DMYPOLY}, {SFX_ID})"));
            }

            emevd.Write(emevdPath);
            totalInjected += entityIds.Count;
        }

        Console.WriteLine($"  EMEVD: activated {totalInjected} bloodstains across {bloodstainsByMap.Count} maps");
    }
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add writer/FogModWrapper/DeathMarkerInjector.cs
git commit -m "feat: DeathMarkerInjector EMEVD phase (enable + SFX for bloodstains)"
```

---

### Task 4: Wire into Program.cs and clean up test code

**Files:**
- Modify: `writer/FogModWrapper/Program.cs` (add call after ChapelGraceInjector)
- Modify: `writer/FogModWrapper/ChapelGraceInjector.cs` (remove test bloodstain code)

- [ ] **Step 1: Add DeathMarkerInjector call in Program.cs**

After the ChapelGraceInjector block (around line 505), add:

```csharp
        // 7h2. Death markers at fog gates
        DeathMarkerInjector.Inject(modDir, config.GameDir, graphData.Connections, events);
```

- [ ] **Step 2: Remove test bloodstain code from ChapelGraceInjector.cs**

Remove the `// TEST: Bloodstain asset (AEG099_090)` block in `InjectMsb` (the block placing the test asset with entity 755899900).

Remove the `// TEST: Enable the bloodstain test asset` block in `InjectEmevd` (the `ChangeAssetEnableState` and `CreateAssetfollowingSFX` lines for entity 755899900).

- [ ] **Step 3: Build**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add writer/FogModWrapper/Program.cs writer/FogModWrapper/ChapelGraceInjector.cs
git commit -m "feat: wire DeathMarkerInjector into pipeline, remove test bloodstain"
```

---

### Task 5: End-to-end test

- [ ] **Step 1: Generate a seed**

```bash
uv run speedfog config.toml --spoiler
```

- [ ] **Step 2: Run FogModWrapper**

Build and run FogModWrapper on the generated seed. Verify output includes:

```
Injecting death markers at fog gates...
  MSB: placed N bloodstain assets across M maps
  EMEVD: activated N bloodstains across M maps
```

Where N should be roughly `connections * 2 gates * 3 bloodstains`.

- [ ] **Step 3: Launch the game and visually verify**

- Spawn at Chapel of Anticipation
- Traverse a fog gate
- Verify bloodstains are visible near fog gates on both sides (exit and entrance)
- Verify they are spread out (not stacked)

- [ ] **Step 4: Final commit if any fixes were needed**
