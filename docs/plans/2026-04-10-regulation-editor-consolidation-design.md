# Regulation Editor Consolidation Design

## Problem

`regulation.bin` is decrypted and re-encrypted **four times per seed** in the
post-FogMod pipeline. Each cycle runs `SFUtil.DecryptERRegulation` and
`SFUtil.EncryptERRegulation` on the full ~40 MB encrypted BND4 archive, plus
re-parses any PARAMs it touches.

The four offenders, all invoked from `writer/FogModWrapper/Program.cs`:

| Injector | Params modified | Location |
|---|---|---|
| `ShopInjector` | `ShopLineupParam` | `ShopInjector.cs:79`, `:153` |
| `WeaponUpgradeInjector` | `EquipParamWeapon` (read), `EquipParamCustomWeapon`, `CharaInitParam` | `WeaponUpgradeInjector.cs:111`, `:231` |
| `StartingRuneInjector` | `CharaInitParam` | `StartingRuneInjector.cs:50`, `:95` |
| `ChapelGraceInjector` (inside `InjectBonfireWarpParam`) | `BonfireWarpParam` | `ChapelGraceInjector.cs:292`, `:377` |

Additional waste:

- `CharaInitParam` is loaded, paramdef-applied, and re-serialized **twice**
  (once by `WeaponUpgradeInjector`, once by `StartingRuneInjector`).
- Each injector independently loads the same `eldendata/Defs/*.xml` paramdefs.
- Each injector duplicates the same ~30 lines of decrypt/encrypt/error-handling
  boilerplate.

Measured timing context (from a representative run):

```
Generate DAG      0.58s  ( 0.5%)
Item Randomizer  73.72s  (59.3%)
Build mod        49.93s  (40.2%)
```

The regulation cycles are a subset of "Build mod" (49.93s). Their exact share
is unmeasured and will be captured empirically before and after the refactor.

## Solution

Introduce a single `RegulationEditor` that opens `regulation.bin` once, caches
parsed `PARAM` objects, and re-encrypts the BND4 once at the end. All four
injectors take the editor as a parameter instead of re-opening the file.

## Design

### New class: `RegulationEditor`

New file: `writer/FogModWrapper/RegulationEditor.cs`.

```csharp
public sealed class RegulationEditor
{
    private readonly string? _regulationPath;  // null in test fixtures
    private readonly BND4 _bnd;
    private readonly string _defsDir;
    private readonly Dictionary<string, PARAM> _params = new();
    private readonly Dictionary<string, PARAMDEF> _defs = new();

    // Test-visible constructor. Path is null: Save() will serialize PARAMs
    // back into the BND4 but will not invoke encryption.
    internal RegulationEditor(BND4 bnd, string defsDir, string? path = null) { ... }

    /// <summary>
    /// Opens regulation.bin from modDir and decrypts it. Returns null if the
    /// file is missing or decryption fails (logs a warning in both cases).
    /// </summary>
    public static RegulationEditor? Open(string modDir);

    /// <summary>
    /// Returns the parsed PARAM for the given short name (e.g. "CharaInitParam").
    /// Cached: subsequent calls return the same instance. Returns null if the
    /// param file or paramdef is missing (logs a warning).
    /// </summary>
    public PARAM? GetParam(string name);

    /// <summary>
    /// Re-serializes every accessed PARAM back into the BND4, then re-encrypts
    /// regulation.bin to the path given at Open() time. No-op if no PARAM was
    /// accessed. Skips the encryption step (but still serializes the PARAMs)
    /// when the editor was constructed without a path (test fixtures only).
    /// </summary>
    public void Save();
}
```

Key properties:

- **Open()** does the decrypt once; returns null on missing-file or decrypt
  failure (same graceful behavior as current injectors).
- **GetParam()** caches by short name. `BinderFile` lookup uses the same
  `EndsWith("{name}.param")` pattern already used by the injectors.
  Paramdefs are loaded from `{AppDomain.CurrentDomain.BaseDirectory}/eldendata/Defs/{name}.xml`
  on first access and cached in `_defs`.
- **Save()** writes back every PARAM that was accessed (we assume "accessed =
  intended to modify", which is true for all current injectors). No separate
  dirty tracking, no footgun. `if (_params.Count == 0) return;` skips the
  re-encrypt entirely when no injector ran.
- **No `IDisposable`**: the editor holds no unmanaged resources, only in-memory
  state. `Save()` is explicit.

### Shared `CharaInitParam` is intentional

`WeaponUpgradeInjector` and `StartingRuneInjector` both call
`GetParam("CharaInitParam")`. They receive the **same object reference**. The
second injector observes the first's modifications in its row fields. This is
safe because:

- `WeaponUpgradeInjector` writes `equip_Wep_*` / `wepParamType_*` fields.
- `StartingRuneInjector` writes the `soul` field.
- The field sets are disjoint, so no mutation is lost.

This invariant is documented in a comment on `GetParam`.

### Refactor of the four injectors

Each injector's public API changes from `Inject(modDir, ...)` to
`ApplyTo(RegulationEditor reg, ...)`. The body loses ~25 lines of boilerplate
(file existence check, paramdef load, decrypt, encrypt). The calculation
helpers already extracted (e.g. `WeaponUpgradeInjector.UpgradeWeaponId`,
`SomberUpgrade`, `CustomWeaponUpgradeLevel`) are untouched.

| Before | After |
|---|---|
| `ShopInjector.Inject(string modDir, bool includeSentryTorch)` | `ShopInjector.ApplyTo(RegulationEditor reg, bool includeSentryTorch)` |
| `WeaponUpgradeInjector.Inject(string modDir, int weaponUpgrade)` | `WeaponUpgradeInjector.ApplyTo(RegulationEditor reg, int weaponUpgrade)` |
| `StartingRuneInjector.Inject(string modDir, int startingRunes)` | `StartingRuneInjector.ApplyTo(RegulationEditor reg, int startingRunes)` |

Each injector keeps its existing early-return guards
(`if (weaponUpgrade <= 0) return;`) so it remains self-guarding: the caller can
pass it through unconditionally.

The body of each `ApplyTo` replaces the load/parse/apply sequence with a single
`GetParam` call:

```csharp
// Before
var charaFile = regulation.Files.Find(f => f.Name.EndsWith("CharaInitParam.param"));
if (charaFile == null) { ... return; }
var charaParam = PARAM.Read(charaFile.Bytes);
charaParam.ApplyParamdef(charaDef);

// After
var charaParam = reg.GetParam("CharaInitParam");
if (charaParam == null) return;
```

### ChapelGraceInjector integration

`ChapelGraceInjector.Inject(modDir, gameDir, events)` currently executes three
phases internally: MSB patching → `InjectBonfireWarpParam` (which does its own
decrypt/encrypt) → EMEVD injection. The phases have data dependencies
(`bonfireEntityId` from MSB → param; `bonfireFlag` from param → EMEVD), so they
must stay ordered within a single call.

The refactor changes only the middle phase: instead of re-opening
`regulation.bin`, it uses the shared editor. The public `Inject` method gains a
`RegulationEditor` parameter:

```csharp
public static void Inject(string modDir, string gameDir, Events events, RegulationEditor reg)
{
    var msbResult = InjectMsbInternal(modDir, gameDir);
    if (msbResult == null) return;

    var bonfireFlag = ApplyBonfireParamInternal(reg, msbResult.BonfireEntityId);
    if (bonfireFlag == null) return;

    InjectEmevdInternal(modDir, gameDir, events, msbResult, bonfireFlag.Value);
}
```

The EMEVD phase writes to `.emevd.dcx` files and is independent of
`regulation.bin`, so it happily runs inside the `RegulationEditor` scope.

### Program.cs orchestration

The four regulation-modifying steps are consolidated into a single block in
`writer/FogModWrapper/Program.cs` (replacing the current scattered calls around
lines 660-689):

```csharp
// Consolidated regulation.bin modifications:
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

All other per-map / EMEVD injectors (`RebirthInjector`, `StartupFlagInjector`,
`VanillaWarpRemover`, `DeathMarkerInjector`, etc.) stay exactly where they are.
Only the four regulation-touching calls move into this block.

## Error Handling

| Case | Behavior |
|---|---|
| `regulation.bin` missing | `Open` logs warning, returns null. Program.cs logs single "skipping all regulation injections" message and continues. |
| `SFUtil.DecryptERRegulation` throws | `Open` catches, logs warning, returns null. Same as above. |
| Paramdef XML missing | `GetParam` logs warning, returns null. Calling injector skips that param (but other params and injectors continue). |
| `X.param` not found in BND4 | `GetParam` logs warning, returns null. Same as above. |
| Injector throws during `ApplyTo` | Unhandled, propagates. Program.cs crashes. Matches current behavior: bugs are fatal. |
| `Save()` throws | Propagates. No cleanup, no partial write. |

We deliberately do **not** wrap the injectors in try/catch. The current code
doesn't, and adding it would hide bugs.

## Testing

### Preserved

- `WeaponUpgradeTests.cs` tests only pure helpers (`UpgradeWeaponId`,
  `SomberUpgrade`, `CustomWeaponUpgradeLevel`). These helpers are untouched by
  this refactor. **No test changes required.**
- No unit tests exist today for `ShopInjector`, `StartingRuneInjector`, or
  `ChapelGraceInjector`.

### New unit tests for `RegulationEditor`

Strategy: expose an `internal RegulationEditor(BND4 bnd, string defsDir)`
constructor. The existing `InternalsVisibleTo("FogModWrapper.Tests")` entry in
`writer/FogModWrapper/FogModWrapper.csproj:13` already makes this visible to
the test project. Build a minimal in-memory BND4 with a stub PARAM file as a
fixture. This avoids any dependency on a real decryptable `regulation.bin` in
CI.

To test `Save()` without touching the file system or calling
`SFUtil.EncryptERRegulation`, split the save logic: `Save()` calls an internal
`SerializeAccessedParams()` method that writes each accessed PARAM back into
its `BinderFile.Bytes`, then calls `EncryptERRegulation` only if the in-memory
file path is not null. Tests use a constructor variant with `path = null` so
`Save()` exercises the serialization path without encrypting.

New file: `writer/FogModWrapper.Tests/RegulationEditorTests.cs`. Tests:

- `GetParam_ReturnsNull_WhenFileMissing`: lookup of an unknown short name logs
  warning, returns null.
- `GetParam_ReturnsCachedInstance_OnSecondCall`: two calls to `GetParam("X")`
  return the same reference; paramdef is applied exactly once.
- `Save_WritesAccessedParamsBack`: after `GetParam + mutate`, `Save` updates
  the corresponding `BinderFile.Bytes` in the in-memory BND4.
- `Save_SkipsEncryption_WhenNothingAccessed`: with zero `GetParam` calls,
  `Save` is a no-op (no `BinderFile.Bytes` modified, no exception even with
  null path).

### Integration test

The existing integration test (`writer/test/run_integration.sh`) continues to
validate the end-to-end pipeline against a real `regulation.bin`.

### Manual regression check

After the refactor, a real end-to-end run must verify in-game:

- Shop: smithing stones and Sentry's Torch present at Twin Maiden Husks.
- WeaponUpgrade: starting class weapon at the configured upgrade level.
- StartingRune: starting rune count on each class.
- ChapelGrace: Site of Grace functional, fast travel registers, initial spawn
  at the chapel works.

Ideally, diff the output `regulation.bin` byte-for-byte against a pre-refactor
run with identical seed and config: the bytes should be identical. If they
differ, investigate whether the shared `PARAM` instance causes any
serialization divergence in SoulsFormats.

## Expected Gains

### Performance

Bounded by the fraction of "Build mod" (49.93s) spent on regulation
decrypt/encrypt/parse. The share is currently unmeasured. The refactor reduces
regulation work from **8 cycles** (4 decrypts + 4 encrypts) plus duplicated
PARAM parsing to **2 cycles** (1 decrypt + 1 encrypt) with deduplicated PARAM
parsing: a **~75% reduction** of the regulation-bound work.

**Action:** add a `Stopwatch` around the current regulation block before
implementation, to establish a baseline. Measure again after the refactor. This
produces a real before/after number rather than a guess.

### Code quality (bonus)

- Each of the four injectors loses ~25 lines of boilerplate (file-exists,
  decrypt try/catch, paramdef load, encrypt).
- `ChapelGraceInjector.InjectBonfireWarpParam` (~110 lines) shrinks to ~60
  lines.
- Adding a fifth regulation modification in the future becomes a one-method
  change: add a new injector with an `ApplyTo(reg, ...)` signature and call it
  from the consolidated block.

## Risks

- **Shared `CharaInitParam` reference**: both `WeaponUpgradeInjector` and
  `StartingRuneInjector` mutate the same cached `PARAM` object. The field sets
  are disjoint today, but any future injector that touches CharaInitParam must
  respect this invariant. Documented in `GetParam` XML doc comment.
- **SoulsFormats serialization idempotence**: uncertain whether
  `PARAM.Write()` on a PARAM that was loaded, mutated, and is about to be
  written gives bytes identical to the sequential load/mutate/write pattern
  from before. The byte-diff in the manual regression check catches this.
- **Silent regression**: only one of the four injectors might break during the
  refactor if the integration test does not exercise every knob. Manual
  in-game verification of all four features is required before merging.

## Out of Scope

- Per-map MSB/EMEVD caching across injectors (a separate optimization
  identified in the same performance review).
- `MsbHelper` directory-detection caching.
- Parallelizing `DeathMarkerInjector` per map.
- Any optimization of `Item Randomizer` (the actual 59.3% bottleneck), which
  lives in a different codebase.
