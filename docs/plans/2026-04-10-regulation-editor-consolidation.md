# RegulationEditor Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate the four independent `regulation.bin` decrypt/encrypt cycles (`ShopInjector`, `WeaponUpgradeInjector`, `StartingRuneInjector`, `ChapelGraceInjector.InjectBonfireWarpParam`) into a single shared `RegulationEditor` that decrypts once, caches parsed `PARAM` objects, and re-encrypts once.

**Architecture:** New `RegulationEditor` class in `writer/FogModWrapper/RegulationEditor.cs` encapsulates the BND4 lifecycle. Each injector's `Inject(modDir, ...)` signature is replaced with `ApplyTo(RegulationEditor reg, ...)` that calls `reg.GetParam(name)` instead of performing its own decrypt/parse/encrypt. `Program.cs` orchestrates the four injectors inside a single `Open` / `Save` block.

**Tech Stack:** C# .NET 10.0, xUnit for unit tests, SoulsFormats (via `writer/lib/SoulsFormats.dll`) for BND4/PARAM/SFUtil.

**Related design doc:** `docs/plans/2026-04-10-regulation-editor-consolidation-design.md` (commit `4fd9dea`).

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `writer/FogModWrapper/RegulationEditor.cs` | Encapsulates BND4 lifecycle, PARAM/paramdef cache, Save() |
| Create | `writer/FogModWrapper.Tests/RegulationEditorTests.cs` | Unit tests for null cases and Save no-op |
| Modify | `writer/FogModWrapper/ShopInjector.cs` | Replace `Inject(modDir, ...)` with `ApplyTo(reg, ...)` |
| Modify | `writer/FogModWrapper/WeaponUpgradeInjector.cs` | Replace `Inject(modDir, ...)` with `ApplyTo(reg, ...)` |
| Modify | `writer/FogModWrapper/StartingRuneInjector.cs` | Replace `Inject(modDir, ...)` with `ApplyTo(reg, ...)` |
| Modify | `writer/FogModWrapper/ChapelGraceInjector.cs` | Add `RegulationEditor reg` parameter to `Inject`, refactor `InjectBonfireWarpParam` to use `reg` |
| Modify | `writer/FogModWrapper/Program.cs` | Consolidate the four calls into a single `Open`/`ApplyTo`/`Save` block around lines 660-678 |

---

## Design Notes

**Shared `CharaInitParam` reference is intentional:** `WeaponUpgradeInjector` writes `equip_Wep_*` / `wepParamType_*` fields; `StartingRuneInjector` writes the `soul` field. The field sets are disjoint, so the second accessor sees the first accessor's changes without conflict. This invariant is documented in a comment on `GetParam`.

**No `MarkDirty`:** `Save()` re-serializes every PARAM that was accessed. An injector that calls `GetParam` without mutating is rare (only in early-return paths) and re-serializing an unmodified PARAM is cheap compared to the AES cycle. Removing the API reduces footgun potential.

**No `IDisposable`:** `RegulationEditor` holds only in-memory state (no file handles). `Save()` is an explicit call.

**Test constructor exposes `path = null`:** In test fixtures, `RegulationEditor` is constructed without a path. `Save()` still serializes accessed PARAMs into the BND4 but skips `SFUtil.EncryptERRegulation` when the path is null. This enables unit testing without a real decryptable `regulation.bin`.

**Baseline/post timing:** Captured by running a known seed before and after the refactor and comparing the Python-side `Build mod` timing output. No extra in-code instrumentation is added; the measurement is operational, not code-level.

**Incremental migration:** Each of Tasks 3-6 migrates a single injector. Intermediate states are functionally equivalent to the baseline (4 cycles total, because the editor runs 1 cycle and the remaining legacy injectors still run their own). This keeps the pipeline working after every commit.

---

### Task 1: Record baseline timing

**Files:**
- No code changes. This task produces a recorded measurement for later comparison.

- [ ] **Step 1: Pick a reproducible seed and config**

Use the standard config and an explicit seed so the run is reproducible after the refactor. From the project root:

```bash
uv run speedfog config.example.toml --seed 212559448 --logs
```

Record the seed you used; the same seed must be reused in Task 7.

- [ ] **Step 2: Run the full pipeline and capture timing**

Run the complete pipeline (DAG generation + C# writer). From the project root:

```bash
uv run speedfog config.example.toml --seed 212559448 --logs 2>&1 | tee /tmp/speedfog-baseline.log
```

Expected output includes a `Timing breakdown` block near the end similar to:

```
Timing breakdown:
  Generate DAG                0.58s  ( 0.5%)
  Item Randomizer            73.72s  (59.3%)
  Build mod                  49.93s  (40.2%)
```

Record the `Build mod` value and the seed in a scratch file or commit message draft for Task 7. This is the baseline number.

- [ ] **Step 3: No commit**

No code change to commit in this task. Proceed to Task 2.

---

### Task 2: Create `RegulationEditor` class with unit tests

**Files:**
- Create: `writer/FogModWrapper/RegulationEditor.cs`
- Create: `writer/FogModWrapper.Tests/RegulationEditorTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `writer/FogModWrapper.Tests/RegulationEditorTests.cs` with three tests that exercise only the null/no-op paths (no real PARAM parsing required):

```csharp
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class RegulationEditorTests
{
    private static BND4 CreateEmptyBnd() => new BND4();

    private static BND4 CreateBndWithFile(string fileName, byte[] bytes)
    {
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, fileName, bytes));
        return bnd;
    }

    private static string EmptyDefsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"regedit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GetParam_ReturnsNull_WhenBinderFileMissing()
    {
        var editor = new RegulationEditor(CreateEmptyBnd(), EmptyDefsDir());
        var result = editor.GetParam("ShopLineupParam");
        Assert.Null(result);
    }

    [Fact]
    public void GetParam_ReturnsNull_WhenParamdefMissing()
    {
        // BND4 has the binder file, but defsDir has no matching XML.
        var bnd = CreateBndWithFile("N:/GR/data/Param/GameParam/ShopLineupParam.param", new byte[0]);
        var editor = new RegulationEditor(bnd, EmptyDefsDir());
        var result = editor.GetParam("ShopLineupParam");
        Assert.Null(result);
    }

    [Fact]
    public void Save_IsNoOp_WhenNoParamAccessed()
    {
        // No GetParam calls, no accessed PARAMs, null path so no encryption attempt.
        var editor = new RegulationEditor(CreateEmptyBnd(), EmptyDefsDir());
        var exception = Record.Exception(() => editor.Save());
        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run tests to verify compile failure**

Run:

```bash
cd writer/FogModWrapper.Tests && dotnet test --filter FullyQualifiedName~RegulationEditorTests
```

Expected: compile error, `RegulationEditor` type not found.

- [ ] **Step 3: Create the `RegulationEditor` class skeleton**

Create `writer/FogModWrapper/RegulationEditor.cs`:

```csharp
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Shared editor for regulation.bin that decrypts once, caches parsed PARAM
/// objects across injectors, and re-encrypts once on Save().
///
/// Cached PARAMs are shared by reference across GetParam calls with the same
/// name. If two injectors both modify the same PARAM (e.g. CharaInitParam),
/// their changes coexist in the same in-memory object. This is safe as long
/// as they write disjoint fields, which is a documented invariant of the
/// current consumer set (WeaponUpgradeInjector writes weapon fields;
/// StartingRuneInjector writes the soul field).
/// </summary>
public sealed class RegulationEditor
{
    private readonly string? _regulationPath;
    private readonly BND4 _bnd;
    private readonly string _defsDir;
    private readonly Dictionary<string, PARAM> _params = new();
    private readonly Dictionary<string, PARAMDEF> _defs = new();

    /// <summary>
    /// Test-visible constructor. When <paramref name="path"/> is null, Save()
    /// serializes accessed PARAMs back into the BND4 but skips the encryption
    /// step (the file is never written).
    /// </summary>
    internal RegulationEditor(BND4 bnd, string defsDir, string? path = null)
    {
        _bnd = bnd;
        _defsDir = defsDir;
        _regulationPath = path;
    }

    /// <summary>
    /// Opens and decrypts regulation.bin from <paramref name="modDir"/>.
    /// Returns null (and logs a warning) if the file is missing or decryption
    /// throws.
    /// </summary>
    public static RegulationEditor? Open(string modDir)
    {
        var path = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(path))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping regulation injections");
            return null;
        }

        BND4 bnd;
        try
        {
            bnd = SFUtil.DecryptERRegulation(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decrypt regulation.bin: {ex.Message}");
            return null;
        }

        var defsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eldendata", "Defs");
        return new RegulationEditor(bnd, defsDir, path);
    }

    /// <summary>
    /// Returns the parsed PARAM identified by its short name (e.g.
    /// "CharaInitParam"). Cached: subsequent calls return the same instance.
    /// Returns null (and logs a warning) if the matching binder file or
    /// paramdef XML cannot be found.
    /// </summary>
    public PARAM? GetParam(string name)
    {
        if (_params.TryGetValue(name, out var cached))
            return cached;

        var file = _bnd.Files.Find(f => f.Name.EndsWith($"{name}.param"));
        if (file == null)
        {
            Console.WriteLine($"Warning: {name}.param not found in regulation.bin");
            return null;
        }

        var def = LoadParamdef(name);
        if (def == null)
            return null;

        var param = PARAM.Read(file.Bytes);
        param.ApplyParamdef(def);
        _params[name] = param;
        return param;
    }

    /// <summary>
    /// Re-serializes every accessed PARAM back into the BND4, then re-encrypts
    /// regulation.bin at the path supplied to Open(). No-op when no PARAM has
    /// been accessed. Skips the encryption step entirely when the editor was
    /// constructed without a path (test fixtures only).
    /// </summary>
    public void Save()
    {
        if (_params.Count == 0)
            return;

        foreach (var kvp in _params)
        {
            var file = _bnd.Files.Find(f => f.Name.EndsWith($"{kvp.Key}.param"));
            if (file == null)
                continue;
            file.Bytes = kvp.Value.Write();
        }

        if (_regulationPath == null)
            return;

        SFUtil.EncryptERRegulation(_regulationPath, _bnd);
        Console.WriteLine($"Regulation saved: {_params.Count} param(s) modified");
    }

    private PARAMDEF? LoadParamdef(string name)
    {
        if (_defs.TryGetValue(name, out var cached))
            return cached;

        var path = Path.Combine(_defsDir, $"{name}.xml");
        if (!File.Exists(path))
        {
            Console.WriteLine($"Warning: paramdef {name}.xml not found at {path}");
            return null;
        }

        var def = PARAMDEF.XmlDeserialize(path);
        _defs[name] = def;
        return def;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
cd writer/FogModWrapper.Tests && dotnet test --filter FullyQualifiedName~RegulationEditorTests
```

Expected: 3 tests passed.

- [ ] **Step 5: Verify the full test suite still compiles and passes**

Run:

```bash
cd writer/FogModWrapper.Tests && dotnet test
```

Expected: all existing tests pass, plus the 3 new ones.

- [ ] **Step 6: Commit**

```bash
git add writer/FogModWrapper/RegulationEditor.cs writer/FogModWrapper.Tests/RegulationEditorTests.cs
git commit -m "$(cat <<'EOF'
feat: add RegulationEditor for shared regulation.bin lifecycle

New class encapsulating the decrypt/cache/encrypt cycle for regulation.bin.
Injectors will migrate to this shared editor in subsequent commits. Unit
tests cover the null/no-op paths; real PARAM serialization relies on the
integration test.
EOF
)"
```

---

### Task 3: Migrate `ShopInjector` to `RegulationEditor`

**Files:**
- Modify: `writer/FogModWrapper/ShopInjector.cs`
- Modify: `writer/FogModWrapper/Program.cs:660` (the `ShopInjector.Inject` call site)

- [ ] **Step 1: Replace the body of `ShopInjector.Inject`**

Open `writer/FogModWrapper/ShopInjector.cs`. Replace the `Inject` method (lines 49-156) with a new `ApplyTo` method. Remove the old `Inject` entirely.

New `ApplyTo` method:

```csharp
/// <summary>
/// Inject smithing stones (and optionally the Sentry's Torch) into the shop.
/// </summary>
public static void ApplyTo(RegulationEditor reg, bool includeSentryTorch = true)
{
    var shopParam = reg.GetParam("ShopLineupParam");
    if (shopParam == null)
        return;

    Console.WriteLine("Injecting smithing stones into merchant shop...");

    // Find existing IDs to avoid conflicts
    var existingIds = new HashSet<int>(shopParam.Rows.Select(r => r.ID));
    Console.WriteLine($"  ShopLineupParam has {existingIds.Count} existing entries");

    // Log existing entries in the Twin Maiden Husks range (101800-101999)
    var twinMaidenIds = shopParam.Rows
        .Where(r => r.ID >= 101800 && r.ID < 102000)
        .Select(r => r.ID)
        .OrderBy(id => id)
        .ToList();
    Console.WriteLine($"  Existing IDs in 101800-101999: {string.Join(", ", twinMaidenIds)}");

    // Always clear the full potential range to avoid stale entries on config toggle
    int maxItemCount = NormalStones.Length + SomberStones.Length + ExtraItems.Length;
    int itemCount = NormalStones.Length + SomberStones.Length + (includeSentryTorch ? ExtraItems.Length : 0);

    // Remove any existing entries in our target range
    shopParam.Rows.RemoveAll(r => r.ID >= BASE_SHOP_ID && r.ID < BASE_SHOP_ID + maxItemCount);
    Console.WriteLine($"  Cleared range {BASE_SHOP_ID}-{BASE_SHOP_ID + maxItemCount - 1} for shop items");

    // Add smithing stones
    int shopId = BASE_SHOP_ID;
    foreach (var (itemId, name, price) in NormalStones)
    {
        AddShopEntry(shopParam, shopId, itemId, price);
        Console.WriteLine($"  Added {name} for {price} runes (shop ID {shopId})");
        shopId++;
    }

    foreach (var (itemId, name, price) in SomberStones)
    {
        AddShopEntry(shopParam, shopId, itemId, price);
        Console.WriteLine($"  Added {name} for {price} runes (shop ID {shopId})");
        shopId++;
    }

    // Add extra items (weapons/tools)
    if (includeSentryTorch)
    {
        foreach (var (itemId, name, price, equipType) in ExtraItems)
        {
            AddShopEntry(shopParam, shopId, itemId, price, equipType);
            Console.WriteLine($"  Added {name} for {price} runes (shop ID {shopId})");
            shopId++;
        }
    }

    // Sort rows by ID (required for game to read correctly)
    shopParam.Rows = shopParam.Rows.OrderBy(r => r.ID).ToList();

    Console.WriteLine($"Shop items injected successfully ({itemCount} items)");
}
```

Note: the `AddShopEntry` helper at the bottom of the file is unchanged. Remove the old `Inject` body (file-exists check, paramdef load, decrypt, encrypt).

- [ ] **Step 2: Open the editor block in `Program.cs` and call `ShopInjector.ApplyTo`**

Open `writer/FogModWrapper/Program.cs`. Replace the line at `:660`:

```csharp
// 7e. Smithing stones in merchant shop (param file)
ShopInjector.Inject(modDir, graphData.SentryTorchShop);
```

with:

```csharp
// 7e. Consolidated regulation.bin modifications begin here.
// Additional injectors will migrate into this block in subsequent commits.
var reg = RegulationEditor.Open(modDir);
if (reg != null)
{
    ShopInjector.ApplyTo(reg, graphData.SentryTorchShop);
    reg.Save();
}
```

Leave `WeaponUpgradeInjector.Inject(modDir, ...)`, `StartingRuneInjector.Inject(modDir, ...)`, and `ChapelGraceInjector.Inject(modDir, ...)` calls unchanged below this block. They still run their own decrypt/encrypt, so the overall pipeline still works.

- [ ] **Step 3: Build and run the test suite**

Run:

```bash
cd writer/FogModWrapper && dotnet build
cd ../FogModWrapper.Tests && dotnet test
```

Expected: build succeeds, all tests pass.

- [ ] **Step 4: Run the C# integration test**

Run:

```bash
cd writer/test && ./run_integration.sh
```

Expected: integration test passes end-to-end. The shop injection must still produce the smithing stones in the output mod.

- [ ] **Step 5: Commit**

```bash
git add writer/FogModWrapper/ShopInjector.cs writer/FogModWrapper/Program.cs
git commit -m "$(cat <<'EOF'
refactor: migrate ShopInjector to RegulationEditor

First injector to use the shared editor. Other injectors still perform
their own decrypt/encrypt and will migrate in subsequent commits.
EOF
)"
```

---

### Task 4: Migrate `WeaponUpgradeInjector` to `RegulationEditor`

**Files:**
- Modify: `writer/FogModWrapper/WeaponUpgradeInjector.cs`
- Modify: `writer/FogModWrapper/Program.cs` (the `WeaponUpgradeInjector.Inject` call site)

- [ ] **Step 1: Replace the body of `WeaponUpgradeInjector.Inject`**

Open `writer/FogModWrapper/WeaponUpgradeInjector.cs`. Replace the `Inject` method with `ApplyTo`. The pure helpers (`SomberUpgrade`, `UpgradeWeaponId`, `CustomWeaponUpgradeLevel`) stay untouched.

New `ApplyTo` method:

```csharp
/// <summary>
/// Upgrade starting class weapons in CharaInitParam to the given level.
/// Handles both standard weapons (EquipParamWeapon, ID-encoded upgrade)
/// and custom weapons with ashes of war (EquipParamCustomWeapon, reinforceLv field).
/// </summary>
public static void ApplyTo(RegulationEditor reg, int weaponUpgrade)
{
    if (weaponUpgrade <= 0)
        return;

    // Load EquipParamWeapon to build regular/somber sets
    var weaponParam = reg.GetParam("EquipParamWeapon");
    if (weaponParam == null)
        return;

    var regularWeapons = new HashSet<int>();
    var somberWeapons = new HashSet<int>();
    foreach (var row in weaponParam.Rows)
    {
        if ((int)row["originEquipWep25"].Value > 0)
            regularWeapons.Add(row.ID);
        else if ((int)row["originEquipWep10"].Value > 0)
            somberWeapons.Add(row.ID);
    }

    // Load EquipParamCustomWeapon (for weapons with ashes of war)
    var customWepParam = reg.GetParam("EquipParamCustomWeapon");

    // Load CharaInitParam
    var charaParam = reg.GetParam("CharaInitParam");
    if (charaParam == null)
        return;

    Console.WriteLine($"Upgrading starting class weapons to +{weaponUpgrade} (somber +{SomberUpgrade(weaponUpgrade)})...");

    int upgraded = 0;

    foreach (var row in charaParam.Rows)
    {
        if (row.ID < CLASS_ROW_MIN || row.ID > CLASS_ROW_MAX)
            continue;

        foreach (var (fieldName, typeFieldName) in WeaponTypeFields)
        {
            int weaponId = (int)row[fieldName].Value;
            if (weaponId <= 0)
                continue;

            byte wepType = (byte)row[typeFieldName].Value;

            if (wepType == 1 && customWepParam != null)
            {
                // Custom weapon (has ash of war): modify reinforceLv in EquipParamCustomWeapon
                var customRow = customWepParam.Rows.Find(r => r.ID == weaponId);
                if (customRow == null)
                {
                    Console.WriteLine($"  Warning: custom weapon {weaponId} not found in EquipParamCustomWeapon");
                    continue;
                }

                int baseWepId = (int)customRow["baseWepId"].Value;
                int targetLevel = CustomWeaponUpgradeLevel(baseWepId, weaponUpgrade, regularWeapons, somberWeapons);
                if (targetLevel < 0)
                {
                    Console.WriteLine($"  Warning: base weapon {baseWepId} for custom weapon {weaponId} not found in weapon tables");
                    continue;
                }

                byte currentLevel = (byte)customRow["reinforceLv"].Value;
                if (currentLevel != (byte)targetLevel)
                {
                    customRow["reinforceLv"].Value = (byte)targetLevel;
                    upgraded++;
                }
            }
            else
            {
                // Standard weapon: upgrade via ID encoding
                int newId = UpgradeWeaponId(weaponId, weaponUpgrade, regularWeapons, somberWeapons);
                if (newId != weaponId)
                {
                    row[fieldName].Value = newId;
                    upgraded++;
                }
            }
        }
    }

    if (upgraded == 0)
    {
        Console.WriteLine("  No weapons to upgrade");
        return;
    }

    Console.WriteLine($"  Upgraded {upgraded} weapon slots across starting classes");
}
```

Note: `EquipParamWeapon` is only read (regular/somber classification) and never mutated. It still goes through the editor cache so it is serialized back on Save; the `.Write()` result is identical to the original bytes since no row was modified.

- [ ] **Step 2: Update `Program.cs` to call `ApplyTo` inside the editor block**

In `writer/FogModWrapper/Program.cs`, move the weapon upgrade call into the editor block. The block should now read:

```csharp
// 7e. Consolidated regulation.bin modifications begin here.
// Additional injectors will migrate into this block in subsequent commits.
var reg = RegulationEditor.Open(modDir);
if (reg != null)
{
    ShopInjector.ApplyTo(reg, graphData.SentryTorchShop);
    WeaponUpgradeInjector.ApplyTo(reg, graphData.WeaponUpgrade);
    reg.Save();
}
```

Delete the now-stale separate call to `WeaponUpgradeInjector.Inject(modDir, graphData.WeaponUpgrade);` that was at line 663.

- [ ] **Step 3: Build and run the test suite**

Run:

```bash
cd writer/FogModWrapper && dotnet build
cd ../FogModWrapper.Tests && dotnet test
```

Expected: build succeeds, all tests pass (including the pure helper tests in `WeaponUpgradeTests.cs` which are untouched).

- [ ] **Step 4: Run the C# integration test**

Run:

```bash
cd writer/test && ./run_integration.sh
```

Expected: integration test passes.

- [ ] **Step 5: Commit**

```bash
git add writer/FogModWrapper/WeaponUpgradeInjector.cs writer/FogModWrapper/Program.cs
git commit -m "$(cat <<'EOF'
refactor: migrate WeaponUpgradeInjector to RegulationEditor

Second injector in the shared block. CharaInitParam now lives in the shared
cache, ready to be reused by StartingRuneInjector in the next commit.
EOF
)"
```

---

### Task 5: Migrate `StartingRuneInjector` to `RegulationEditor`

**Files:**
- Modify: `writer/FogModWrapper/StartingRuneInjector.cs`
- Modify: `writer/FogModWrapper/Program.cs` (the `StartingRuneInjector.Inject` call site)

- [ ] **Step 1: Replace the body of `StartingRuneInjector.Inject`**

Open `writer/FogModWrapper/StartingRuneInjector.cs`. Replace the `Inject` method with `ApplyTo`. The class-level constants (`CLASS_ROW_MIN`, `CLASS_ROW_MAX`, `MAX_SOUL`) stay.

New `ApplyTo` method:

```csharp
/// <summary>
/// Set starting runes on all character classes in CharaInitParam.
/// </summary>
public static void ApplyTo(RegulationEditor reg, int startingRunes)
{
    if (startingRunes <= 0)
        return;

    var charaParam = reg.GetParam("CharaInitParam");
    if (charaParam == null)
        return;

    int clampedRunes = Math.Clamp(startingRunes, 0, MAX_SOUL);
    if (clampedRunes != startingRunes)
    {
        Console.WriteLine($"Warning: starting_runes capped at {MAX_SOUL:N0}");
    }

    Console.WriteLine($"Setting starting runes to {clampedRunes:N0} on all classes...");

    int updated = 0;
    foreach (var row in charaParam.Rows)
    {
        if (row.ID < CLASS_ROW_MIN || row.ID > CLASS_ROW_MAX)
            continue;

        row["soul"].Value = clampedRunes;
        updated++;
    }

    if (updated == 0)
    {
        Console.WriteLine("  No character classes found");
        return;
    }

    Console.WriteLine($"  Set {clampedRunes:N0} starting runes on {updated} classes");
}
```

The `GetParam("CharaInitParam")` call here reuses the same object already cached by `WeaponUpgradeInjector`. The `soul` field and the weapon fields are disjoint, so the in-memory object carries both mutations.

- [ ] **Step 2: Update `Program.cs` to call `ApplyTo` inside the editor block**

Extend the editor block:

```csharp
// 7e. Consolidated regulation.bin modifications begin here.
// Additional injectors will migrate into this block in subsequent commits.
var reg = RegulationEditor.Open(modDir);
if (reg != null)
{
    ShopInjector.ApplyTo(reg, graphData.SentryTorchShop);
    WeaponUpgradeInjector.ApplyTo(reg, graphData.WeaponUpgrade);
    StartingRuneInjector.ApplyTo(reg, graphData.StartingRunes);
    reg.Save();
}
```

Delete the now-stale separate call to `StartingRuneInjector.Inject(modDir, graphData.StartingRunes);` that was at line 666.

- [ ] **Step 3: Build and run the test suite**

Run:

```bash
cd writer/FogModWrapper && dotnet build
cd ../FogModWrapper.Tests && dotnet test
```

Expected: build succeeds, all tests pass.

- [ ] **Step 4: Run the C# integration test**

Run:

```bash
cd writer/test && ./run_integration.sh
```

Expected: integration test passes.

- [ ] **Step 5: Commit**

```bash
git add writer/FogModWrapper/StartingRuneInjector.cs writer/FogModWrapper/Program.cs
git commit -m "$(cat <<'EOF'
refactor: migrate StartingRuneInjector to RegulationEditor

Third injector in the shared block. Reuses the cached CharaInitParam
already loaded by WeaponUpgradeInjector: no second parse, no conflicting
mutations (soul vs weapon fields).
EOF
)"
```

---

### Task 6: Migrate `ChapelGraceInjector` bonfire param phase to `RegulationEditor`

**Files:**
- Modify: `writer/FogModWrapper/ChapelGraceInjector.cs`
- Modify: `writer/FogModWrapper/Program.cs` (the `ChapelGraceInjector.Inject` call site)

- [ ] **Step 1: Update `ChapelGraceInjector.Inject` signature**

Open `writer/FogModWrapper/ChapelGraceInjector.cs`. Change the public `Inject` method (line 82) to accept a `RegulationEditor` parameter. The method body stays structurally the same (MSB phase, then bonfire param phase, then EMEVD phase); only the middle phase changes to use the editor.

Replace the existing `Inject` method with:

```csharp
/// <summary>
/// Inject a Site of Grace at Chapel of Anticipation.
/// Skips gracefully if the grace already exists (e.g., added by Item Randomizer).
/// Also relocates the vanilla player spawn to the grace so the player starts there directly.
/// </summary>
public static void Inject(string modDir, string gameDir, Events events, RegulationEditor reg)
{
    Console.WriteLine("Injecting Chapel of Anticipation grace...");

    // Step 1: MSB - add grace parts + relocate vanilla player spawn
    var msbResult = InjectMsb(modDir, gameDir);
    if (msbResult == null)
        return;

    // Step 2: BonfireWarpParam - add fast travel entry (via shared editor)
    var bonfireFlag = InjectBonfireWarpParam(reg, msbResult.Value.BonfireEntityId);
    if (bonfireFlag == null)
        return;

    // Step 3: EMEVD - RegisterBonfire + pre-activate grace flag + redirect initial spawn + one-shot warp
    InjectEmevd(modDir, gameDir, events, bonfireFlag.Value, msbResult.Value.BonfireEntityId,
                msbResult.Value.SpawnRegionEntityId, msbResult.Value.PlayerEntityId);

    Console.WriteLine("Chapel of Anticipation grace injected successfully");
}
```

- [ ] **Step 2: Rewrite `InjectBonfireWarpParam` to use `RegulationEditor`**

Still in `writer/FogModWrapper/ChapelGraceInjector.cs`, replace the existing `InjectBonfireWarpParam` method (lines 266-381, approximately) with a version that takes a `RegulationEditor`. The allocation logic (row ID, flag ID, template copy) is unchanged:

```csharp
/// <summary>
/// Add bonfire warp entry to regulation.bin for fast travel support.
/// Returns the allocated event flag ID, or null on failure.
/// </summary>
private static uint? InjectBonfireWarpParam(RegulationEditor reg, uint bonfireEntityId)
{
    var bonfireParam = reg.GetParam("BonfireWarpParam");
    if (bonfireParam == null)
        return null;

    // Check if a row already exists for this bonfire entity
    var existingRow = bonfireParam.Rows.Find(r =>
        (uint)r["bonfireEntityId"].Value == bonfireEntityId);
    if (existingRow != null)
    {
        var existingFlag = (uint)existingRow["eventflagId"].Value;
        Console.WriteLine($"  BonfireWarpParam: row already exists for entity {bonfireEntityId} " +
                          $"(flag {existingFlag})");
        return existingFlag;
    }

    // Find template bonfire row to copy cosmetic fields from
    var templateRow = bonfireParam.Rows.Find(r =>
        (uint)r["bonfireEntityId"].Value == TEMPLATE_BONFIRE_ENTITY);
    if (templateRow == null)
    {
        Console.WriteLine("Warning: Template bonfire row (entity 10001950) not found");
        return null;
    }

    // Allocate unique row ID (increment from base if conflicts)
    var existingIds = new HashSet<int>(bonfireParam.Rows.Select(r => r.ID));
    int rowId = BONFIRE_ROW_BASE;
    while (existingIds.Contains(rowId))
        rowId++;

    // Allocate unique event flag ID (increment from template's flag if conflicts)
    var existingFlags = new HashSet<uint>(
        bonfireParam.Rows.Select(r => (uint)r["eventflagId"].Value));
    uint flagId = (uint)templateRow["eventflagId"].Value;
    while (existingFlags.Contains(flagId))
        flagId++;

    // Parse map coordinates from MAP_ID (m10_01_00_00)
    var mapParts = MAP_ID.Split('_');
    byte areaNo = byte.Parse(mapParts[0].Substring(1)); // "m10" -> 10
    byte gridX = byte.Parse(mapParts[1]);                // "01"
    byte gridZ = byte.Parse(mapParts[2]);                // "00"

    // Create new BonfireWarpParam row
    var newRow = new PARAM.Row(rowId, "", bonfireParam.AppliedParamdef);
    newRow["eventflagId"].Value = flagId;
    newRow["bonfireEntityId"].Value = bonfireEntityId;
    newRow["areaNo"].Value = areaNo;
    newRow["gridXNo"].Value = gridX;
    newRow["gridZNo"].Value = gridZ;
    newRow["posX"].Value = POS_X;
    newRow["posY"].Value = POS_Y;
    newRow["posZ"].Value = POS_Z;
    newRow["textId1"].Value = TEXT_ID;
    newRow["bonfireSubCategorySortId"].Value = (ushort)9999;

    // Copy cosmetic/display fields from template row (GameDataWriterE.cs:4796-4809)
    foreach (var field in new[]
    {
        "forbiddenIconId", "bonfireSubCategoryId", "iconId",
        "dispMask00", "dispMask01", "dispMask02",
        "noIgnitionSfxDmypolyId_0", "noIgnitionSfxId_0"
    })
    {
        newRow[field].Value = templateRow[field].Value;
    }

    bonfireParam.Rows.Add(newRow);
    bonfireParam.Rows = bonfireParam.Rows.OrderBy(r => r.ID).ToList();

    Console.WriteLine($"  BonfireWarpParam: row {rowId}, flag {flagId}, entity {bonfireEntityId}");
    return flagId;
}
```

Note: the file-existence check, paramdef load, decrypt, binder file lookup, and encrypt calls are all deleted. The method drops from ~110 lines to ~65.

- [ ] **Step 3: Update `Program.cs` to pass `reg` into the editor block**

Move the `ChapelGraceInjector.Inject` call into the editor block and pass `reg`. The final editor block reads:

```csharp
// 7e. Consolidated regulation.bin modifications:
// single decrypt, single encrypt, shared PARAM cache.
var reg = RegulationEditor.Open(modDir);
if (reg != null)
{
    ShopInjector.ApplyTo(reg, graphData.SentryTorchShop);
    WeaponUpgradeInjector.ApplyTo(reg, graphData.WeaponUpgrade);
    StartingRuneInjector.ApplyTo(reg, graphData.StartingRunes);

    if (graphData.ChapelGrace)
        ChapelGraceInjector.Inject(modDir, config.GameDir, events, reg);

    reg.Save();
}
else
{
    Console.WriteLine("Warning: regulation.bin unavailable, skipping all regulation injections");
}
```

Delete the now-stale `if (graphData.ChapelGrace) { ChapelGraceInjector.Inject(modDir, config.GameDir, events); }` block previously at lines 674-678.

- [ ] **Step 4: Build and run the test suite**

Run:

```bash
cd writer/FogModWrapper && dotnet build
cd ../FogModWrapper.Tests && dotnet test
```

Expected: build succeeds, all tests pass.

- [ ] **Step 5: Run the C# integration test**

Run:

```bash
cd writer/test && ./run_integration.sh
```

Expected: integration test passes end-to-end.

- [ ] **Step 6: Commit**

```bash
git add writer/FogModWrapper/ChapelGraceInjector.cs writer/FogModWrapper/Program.cs
git commit -m "$(cat <<'EOF'
refactor: migrate ChapelGraceInjector bonfire param to RegulationEditor

Fourth and final injector in the shared block. regulation.bin is now
decrypted and re-encrypted exactly once per seed regardless of which
injectors run.
EOF
)"
```

---

### Task 7: Measure post-refactor timing and record delta

**Files:**
- No code changes. This task validates the performance gain.

- [ ] **Step 1: Run the post-refactor pipeline with the same seed as Task 1**

Use the same seed recorded in Task 1:

```bash
uv run speedfog config.example.toml --seed 212559448 --logs 2>&1 | tee /tmp/speedfog-after.log
```

- [ ] **Step 2: Extract the new `Build mod` time**

Compare the `Timing breakdown` block in `/tmp/speedfog-after.log` against the baseline from Task 1. Record both numbers.

- [ ] **Step 3: Report the delta**

Compute `baseline_build_mod - post_build_mod` and report it, for example:

```
Build mod baseline:  49.93s
Build mod post-refactor: XX.XXs
Regulation consolidation delta: YY.YYs (-ZZ%)
```

Include this delta in the commit message for the next task or in the PR description.

- [ ] **Step 4: No commit**

No code change. Proceed to Task 8.

---

### Task 8: Manual in-game regression verification

**Files:**
- No code changes. This task validates correctness by running the mod in Elden Ring.

- [ ] **Step 1: Build and launch the mod**

Follow the standard launch procedure from the project:

```bash
# Re-run a full build if anything is stale
uv run speedfog config.example.toml --seed 212559448 --logs
./output/launch_speedfog.bat   # under Windows or Wine
```

- [ ] **Step 2: Verify `ShopInjector` output in-game**

Travel to Roundtable Hold, speak with the Twin Maiden Husks. Confirm:

- All 8 normal smithing stones ([1] through [8]) are listed for purchase with the prices defined in `ShopInjector.NormalStones`.
- All 9 somber smithing stones ([1] through [9]) are listed with the prices in `ShopInjector.SomberStones`.
- Sentry's Torch appears if `care_package.sentry_torch_shop` is enabled in the config.

- [ ] **Step 3: Verify `WeaponUpgradeInjector` output in-game**

Start a new character in a class whose starting weapon is defined in `care_package`. Confirm:

- The starting weapon is at the expected upgrade level (e.g., +8 for a regular weapon, +3 for a somber weapon if `weapon_upgrade=8`).
- Upgrade is visible in the equipment menu.

- [ ] **Step 4: Verify `StartingRuneInjector` output in-game**

On the same new character, confirm the rune count matches `care_package.starting_runes`.

- [ ] **Step 5: Verify `ChapelGraceInjector` output in-game**

Confirm:

- The player spawns at the Site of Grace in the Chapel of Anticipation (not the vanilla spawn point).
- The Site of Grace is interactive (sit/rest works).
- Fast travel from the map menu registers the Chapel grace and can warp back to it from another grace.

- [ ] **Step 6: Optional: byte-diff `regulation.bin`**

For extra safety, diff the output `regulation.bin` against a pre-refactor run with the same seed and config. From the project root with a backup of the baseline:

```bash
cmp output/mod/regulation.bin /path/to/baseline/regulation.bin && echo "identical" || echo "DIFFERS"
```

Ideally: `identical`. If the files differ, the shared `PARAM` instance may cause a serialization divergence in SoulsFormats. In that case, investigate whether `PARAM.Write()` produces different bytes depending on whether the rows were mutated or left untouched after load.

- [ ] **Step 7: No commit**

No code changes in this task. If all verifications pass, the refactor is complete.

---

## Self-Review

**Spec coverage:**
- `RegulationEditor` class → Task 2
- Shared `CharaInitParam` reference invariant → documented in Task 2 class comment
- Test constructor + path-null behavior → Task 2
- Four injectors migrated → Tasks 3-6
- Program.cs consolidation → Task 6 final block
- Unit tests → Task 2 (null/no-op cases only, integration test covers the rest)
- Integration test → runs in every migration task
- Byte-diff regression check → Task 8 Step 6
- Performance measurement (baseline + post) → Tasks 1 and 7
- Manual in-game verification of all four features → Task 8 Steps 2-5

**Out-of-scope reminders (not implemented, not tasks):**
- Per-map MSB/EMEVD caching
- `MsbHelper` directory-detection caching
- `DeathMarkerInjector` parallelization
- Item Randomizer optimization
