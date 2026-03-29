# Erdtree Flag 300 Fix - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Neutralize `SetEventFlag(300, ON)` in Event 900 so zone tracking flags are not skipped by the fogwarp SkipIfEventFlag(flag=300).

**Architecture:** Rename `SealingTreePatcher` to `AlternateFlagPatcher` and extend it to handle both (Event 915, flag 330) and (Event 900, flag 300) using the same NOP + clear-at-startup pattern.

**Tech Stack:** C# / .NET 8.0, SoulsFormats (EMEVD), xUnit

---

### Task 1: Rename SealingTreePatcher to AlternateFlagPatcher

**Files:**
- Rename: `writer/FogModWrapper/SealingTreePatcher.cs` -> `writer/FogModWrapper/AlternateFlagPatcher.cs`
- Rename: `writer/FogModWrapper.Tests/SealingTreePatcherTests.cs` -> `writer/FogModWrapper.Tests/AlternateFlagPatcherTests.cs`
- Modify: `writer/FogModWrapper/Program.cs:620`

- [ ] **Step 1: Rename the source file and class**

Rename `writer/FogModWrapper/SealingTreePatcher.cs` to `writer/FogModWrapper/AlternateFlagPatcher.cs`. Change the class name inside:

```csharp
public static class AlternateFlagPatcher
```

Update the class-level docstring to:

```csharp
/// <summary>
/// Neutralizes vanilla EMEVD events that set AlternateFlag values (flags 300 and 330)
/// which interfere with SpeedFog's fogwarp patching and zone tracking.
///
/// Flag 300 (Erdtree burning): Set by Event 900 when the Forge bonfire transition fires.
/// If ON before the player reaches an Erdtree fogwarp, the compiled SkipIfEventFlag(300)
/// skips the zone tracking SetEventFlag injected by ZoneTrackingInjector.
/// Fix: NOP SetEventFlag(300, ON) in Event 900, clear flag 300 in Event 0.
///
/// Flag 330 (Sealing Tree burned): Set by Event 915 after Dancing Lion defeat.
/// If ON, fogwarps targeting Romina's area use the post-burning variant where Romina
/// doesn't exist. Fix: NOP SetEventFlag(330, ON) in Event 915, clear flag 330 in Event 0.
///
/// In both cases, the corresponding warp patcher (ErdtreeWarpPatcher for 300,
/// SealingTreeWarpPatcher for 330) handles the flag at the correct moment.
/// </summary>
```

- [ ] **Step 2: Rename the test file and class**

Rename `writer/FogModWrapper.Tests/SealingTreePatcherTests.cs` to `writer/FogModWrapper.Tests/AlternateFlagPatcherTests.cs`. Change the class name:

```csharp
public class AlternateFlagPatcherTests
```

Replace all `SealingTreePatcher.` references with `AlternateFlagPatcher.` in the test file (7 occurrences):

```
SealingTreePatcher.NopSetEventFlag  ->  AlternateFlagPatcher.NopSetEventFlag
SealingTreePatcher.InsertClearFlag  ->  AlternateFlagPatcher.InsertClearFlag
```

- [ ] **Step 3: Update Program.cs call site**

In `writer/FogModWrapper/Program.cs`, change line 619-620:

Old:
```csharp
        // 7j2. Neutralize vanilla Sealing Tree events to prevent flag 330 contamination.
        SealingTreePatcher.Patch(commonEmevd);
```

New:
```csharp
        // 7j2. Neutralize vanilla events that set AlternateFlag values (flags 300, 330).
        AlternateFlagPatcher.Patch(commonEmevd);
```

- [ ] **Step 4: Build and run tests**

Run: `cd writer/FogModWrapper.Tests && dotnet test -v q`
Expected: All 163 tests pass (rename only, no behavior change).

- [ ] **Step 5: Commit**

```bash
git add writer/FogModWrapper/AlternateFlagPatcher.cs writer/FogModWrapper.Tests/AlternateFlagPatcherTests.cs writer/FogModWrapper/Program.cs
git rm writer/FogModWrapper/SealingTreePatcher.cs writer/FogModWrapper.Tests/SealingTreePatcherTests.cs
git commit -m "refactor: rename SealingTreePatcher to AlternateFlagPatcher"
```

---

### Task 2: Add flag 300 / Event 900 patching

**Files:**
- Modify: `writer/FogModWrapper/AlternateFlagPatcher.cs`

- [ ] **Step 1: Add constants for Event 900 / flag 300**

In `AlternateFlagPatcher.cs`, add after the existing constants:

```csharp
    /// <summary>
    /// Flag 300 = Erdtree burning. Controls m11_00 vs m11_05 map tile loading.
    /// Set by Event 900 when the Forge bonfire transition fires (flag 9116 trigger).
    /// </summary>
    private const int ERDTREE_BURNING_FLAG = 300;

    /// <summary>
    /// Vanilla Event 900 in common.emevd -- Forge bonfire transition that sets flag 300.
    /// FogMod repurposes this event for farumazula_maliketh connections.
    /// </summary>
    private const int EVENT_900_ID = 900;
```

- [ ] **Step 2: Extend Patch() to handle Event 900**

Update the `Patch()` method to apply the same treatment to Event 900 / flag 300. Replace the entire method body with:

```csharp
    public static void Patch(EMEVD commonEmevd)
    {
        int nop330 = 0, nop300 = 0;
        bool cleared330 = false, cleared300 = false;

        // 1. NOP SetEventFlag(330, ON) in Event 915
        var evt915 = commonEmevd.Events.FirstOrDefault(e => e.ID == EVENT_915_ID);
        if (evt915 != null)
        {
            nop330 = NopSetEventFlag(evt915, SEALING_TREE_FLAG);
        }

        // 2. NOP SetEventFlag(300, ON) in Event 900
        var evt900 = commonEmevd.Events.FirstOrDefault(e => e.ID == EVENT_900_ID);
        if (evt900 != null)
        {
            nop300 = NopSetEventFlag(evt900, ERDTREE_BURNING_FLAG);
        }

        // 3. Clear both flags at game start
        var evt0 = commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (evt0 != null)
        {
            if (nop330 > 0 || evt915 != null)
            {
                InsertClearFlag(evt0, SEALING_TREE_FLAG);
                cleared330 = true;
            }
            if (nop300 > 0 || evt900 != null)
            {
                InsertClearFlag(evt0, ERDTREE_BURNING_FLAG);
                cleared300 = true;
            }
        }

        // Log results
        if (nop330 > 0 || cleared330)
        {
            Console.WriteLine($"AlternateFlag fix: NOP'd {nop330} SetEventFlag(330) in Event 915"
                + (cleared330 ? ", cleared flag 330 in Event 0" : ""));
        }
        if (nop300 > 0 || cleared300)
        {
            Console.WriteLine($"AlternateFlag fix: NOP'd {nop300} SetEventFlag(300) in Event 900"
                + (cleared300 ? ", cleared flag 300 in Event 0" : ""));
        }
        if (nop330 == 0 && nop300 == 0 && !cleared330 && !cleared300)
        {
            Console.WriteLine("AlternateFlag fix: Events 900/915 not found in common.emevd");
        }
    }
```

- [ ] **Step 3: Build**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add writer/FogModWrapper/AlternateFlagPatcher.cs
git commit -m "fix: neutralize SetEventFlag(300) in Event 900 for zone tracking"
```

---

### Task 3: Add tests for flag 300 / Event 900 patching

**Files:**
- Modify: `writer/FogModWrapper.Tests/AlternateFlagPatcherTests.cs`

- [ ] **Step 1: Add test for Event 900 NOP**

Add to `AlternateFlagPatcherTests`:

```csharp
    [Fact]
    public void Patch_NopsFlag300InEvent900()
    {
        var emevd = new EMEVD();

        // Event 0 (initialization)
        var evt0 = new EMEVD.Event(0);
        evt0.Instructions.Add(MakeFiller());
        emevd.Events.Add(evt0);

        // Event 900 with SetEventFlag(300, ON)
        var evt900 = new EMEVD.Event(900);
        evt900.Instructions.Add(MakeFiller());                    // [0]
        evt900.Instructions.Add(MakeSetEventFlag(300, true));     // [1] -- target
        evt900.Instructions.Add(MakeSetEventFlag(301, true));     // [2] -- untouched
        evt900.Instructions.Add(MakeSetEventFlag(302, false));    // [3] -- untouched
        emevd.Events.Add(evt900);

        AlternateFlagPatcher.Patch(emevd);

        // SetEventFlag(300, ON) replaced with WaitFixedTime(0)
        Assert.Equal(1001, evt900.Instructions[1].Bank);
        Assert.Equal(0, evt900.Instructions[1].ID);

        // SetEventFlag(301) and SetEventFlag(302) untouched
        Assert.Equal(2003, evt900.Instructions[2].Bank);
        Assert.Equal(301, BitConverter.ToInt32(evt900.Instructions[2].ArgData, 4));
        Assert.Equal(2003, evt900.Instructions[3].Bank);
        Assert.Equal(302, BitConverter.ToInt32(evt900.Instructions[3].ArgData, 4));
    }
```

- [ ] **Step 2: Add test for flag 300 cleared in Event 0**

```csharp
    [Fact]
    public void Patch_ClearsFlag300InEvent0()
    {
        var emevd = new EMEVD();

        var evt0 = new EMEVD.Event(0);
        evt0.Instructions.Add(MakeFiller());
        emevd.Events.Add(evt0);

        var evt900 = new EMEVD.Event(900);
        evt900.Instructions.Add(MakeSetEventFlag(300, true));
        emevd.Events.Add(evt900);

        AlternateFlagPatcher.Patch(emevd);

        // Event 0 should have a SetEventFlag(300, OFF) inserted at [0]
        var clearInstr = evt0.Instructions[0];
        Assert.Equal(2003, clearInstr.Bank);
        Assert.Equal(66, clearInstr.ID);
        Assert.Equal(300, BitConverter.ToInt32(clearInstr.ArgData, 4));
        Assert.Equal(0, clearInstr.ArgData[8]); // OFF
    }
```

- [ ] **Step 3: Add test for both flags patched together**

```csharp
    [Fact]
    public void Patch_HandlesBothFlags300And330()
    {
        var emevd = new EMEVD();

        var evt0 = new EMEVD.Event(0);
        evt0.Instructions.Add(MakeFiller());
        emevd.Events.Add(evt0);

        var evt900 = new EMEVD.Event(900);
        evt900.Instructions.Add(MakeSetEventFlag(300, true));
        emevd.Events.Add(evt900);

        var evt915 = new EMEVD.Event(915);
        evt915.Instructions.Add(MakeSetEventFlag(330, true));
        emevd.Events.Add(evt915);

        AlternateFlagPatcher.Patch(emevd);

        // Both events NOP'd
        Assert.Equal(1001, evt900.Instructions[0].Bank);
        Assert.Equal(1001, evt915.Instructions[0].Bank);

        // Event 0 has two clear flags inserted at [0] and [1]
        // InsertClearFlag inserts at index 0, so order is: clear(300), clear(330), original filler
        // (330 inserted first at [0], then 300 inserted at [0] pushing 330 to [1])
        Assert.Equal(3, evt0.Instructions.Count);

        var flags = new[] {
            BitConverter.ToInt32(evt0.Instructions[0].ArgData, 4),
            BitConverter.ToInt32(evt0.Instructions[1].ArgData, 4)
        };
        Assert.Contains(300, flags);
        Assert.Contains(330, flags);
    }
```

- [ ] **Step 4: Run all tests**

Run: `cd writer/FogModWrapper.Tests && dotnet test -v q`
Expected: All tests pass (163 existing + 3 new = 166).

- [ ] **Step 5: Commit**

```bash
git add writer/FogModWrapper.Tests/AlternateFlagPatcherTests.cs
git commit -m "test: add tests for Event 900 flag 300 patching"
```

---

### Task 4: Update documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/alternate-warp-patching.md`

- [ ] **Step 1: Update CLAUDE.md**

In the directory structure, replace:

```
│   │   ├── SealingTreePatcher.cs  # Neutralize Event 915 / clear flag 330
```

with:

```
│   │   ├── AlternateFlagPatcher.cs  # Neutralize Events 900/915, clear flags 300/330
```

In the FogModWrapper class table, replace:

```
| `SealingTreePatcher` | Neutralizes Event 915 and clears flag 330 on game start |
```

with:

```
| `AlternateFlagPatcher` | Neutralizes Events 900/915, clears AlternateFlags 300/330 on game start |
```

- [ ] **Step 2: Update alternate-warp-patching.md**

Replace the `### SealingTreePatcher (defense-in-depth)` section (lines 93-102) with:

```markdown
### AlternateFlagPatcher (defense-in-depth)

In addition to rewriting warp destinations, SpeedFog also neutralizes the EMEVD events that set AlternateFlag values:

**Flag 330 / Event 915 (Sealing Tree):**
1. NOP `SetEventFlag(330, ON)` in Event 915 (common.emevd) -- replaced with `WaitFixedTime(0)`
2. Insert `SetEventFlag(330, OFF)` in Event 0 -- clears the flag on game start for stale saves

**Flag 300 / Event 900 (Erdtree burning):**
1. NOP `SetEventFlag(300, ON)` in Event 900 (common.emevd) -- replaced with `WaitFixedTime(0)`
2. Insert `SetEventFlag(300, OFF)` in Event 0 -- clears the flag on game start

Without this, Event 900 sets flag 300 when the DAG includes `farumazula_maliketh` connections (which use the Forge WarpBonfire transition). This causes the compiled fogwarp's `SkipIfEventFlag(flag=300)` to skip the zone tracking `SetEventFlag` injected by ZoneTrackingInjector.

**Source**: `writer/FogModWrapper/AlternateFlagPatcher.cs`
```

Update the comparison table row (line 113):

Old:
```
| Companion patcher | None needed | SealingTreePatcher (neutralizes Event 915) |
```

New:
```
| Companion patcher | AlternateFlagPatcher (neutralizes Event 900) | AlternateFlagPatcher (neutralizes Event 915) |
```

Update the pipeline text (lines 117 and 122) replacing `SealingTreePatcher` with `AlternateFlagPatcher`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/alternate-warp-patching.md
git commit -m "docs: update references from SealingTreePatcher to AlternateFlagPatcher"
```

---

### Task 5: Update remaining doc references

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/fogmod-emevd-model.md`

- [ ] **Step 1: Update architecture.md**

Replace `SealingTreePatcher.cs` with `AlternateFlagPatcher.cs` and update descriptions. Two locations:

Line 76 (class table):
```
| `AlternateFlagPatcher.cs` | Neutralize Events 900/915, clear AlternateFlags 300/330 |
```

Line 181 (pipeline):
```
  - **7j2** AlternateFlagPatcher: neutralize Events 900/915, clear flags 300/330
```

- [ ] **Step 2: Update fogmod-emevd-model.md**

Replace `SealingTreePatcher` references. Two locations:

Line 200 (table row):
```
| `AlternateFlagPatcher` | Neutralizes `SetEventFlag(300/330, ON)` in Events 900/915 | Specific events in common.emevd (not a warp scanner) |
```

Line 216 (file reference):
```
| AlternateFlagPatcher | `writer/FogModWrapper/AlternateFlagPatcher.cs` |
```

- [ ] **Step 3: Commit**

```bash
git add docs/architecture.md docs/fogmod-emevd-model.md
git commit -m "docs: update remaining SealingTreePatcher references"
```
