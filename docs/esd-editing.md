# ESD (Talk Script) Editing Reference

**Date:** 2026-02-26
**Status:** Active

How SpeedFog edits ESD files to add custom dialog options at Sites of Grace.

## What Are ESDs?

ESD (Event Script Data) files are finite state machines that control NPC dialog trees in Elden Ring. Each NPC or interactable object has a `.esd` file packed inside a `.talkesdbnd.dcx` BND archive. States have entry commands (actions executed on entering the state) and conditions (expressions that trigger transitions to other states).

The SoulsIds library provides `ESDEdits` and `AST` helper classes for programmatic ESD manipulation.

## Grace Talk Script

The Site of Grace dialog lives at:

```
m00_00_00_00.talkesdbnd.dcx -> t000001000.esd
```

This single ESD controls the menu for all Sites of Grace in the game. FogRando, Item Randomizer, and SpeedFog all inject entries into this menu.

### Loading

```csharp
var bnd = BND4.Read(bndPath);
var binderFile = bnd.Files.Find(f => f.Name.Contains("t000001000"));
var esd = ESD.Read(binderFile.Bytes);
```

## Anchor Pattern

To find the right state machine within the ESD, anchor on a known message ID. The grace menu uses the "Memorize spell" FMG entry (ID 15000390):

```csharp
var machines = ESDEdits.FindMachinesWithTalkData(esd, 15000390);
// Returns exactly 1 machine for the grace menu
var graceMachine = esd.StateGroups[machines[0]];
```

This is the same anchor used by FogRando (`GameDataWriterE.cs:3901`).

## ConsistentID Allocation

`ConsistentID` determines the ordering and identity of custom menu entries. Each mod uses a unique ID to prevent conflicts when multiple mods edit the same ESD:

| ID | Mod | Menu Entry |
|----|-----|------------|
| 10 | FogRando | Shadow Mart (DLC) |
| 70 | FogRando | Repeat warp / Go to next |
| 72 | Item Randomizer | (reserved) |
| 73 | SpeedFog | Rebirth |

IDs must not collide. When adding new entries, pick an unused ID.

## ModifyCustomTalkEntry

Adds a custom menu option to a state machine and returns the ID of the new state:

```csharp
var talkData = new ESDEdits.CustomTalkData
{
    Msg = 22000000,           // FMG message ID for menu text
    LeaveMsg = 20000009,      // "Leave" option text
    ConsistentID = 73,        // Unique per mod
    // Optional:
    Condition = someExpr,     // When to show the entry (null = always)
    HighlightCondition = ..., // When to highlight/glow
};

// FogRando signature uses `ref long`; SoulsIds newer builds use `out long`
ESDEdits.ModifyCustomTalkEntry(graceMachine, talkData, true, true, out long menuStateId);
```

The two `bool` params control menu positioning. The returned `menuStateId` is the state entered when the player selects the option. From there, build a custom state machine for the dialog flow.

### Post-menu Pattern

After `ModifyCustomTalkEntry`, the new state has a single default condition returning to the grace idle state. To add custom branching logic, save that return target and clear the conditions:

```csharp
long returnId = menuState.Conditions[0].TargetState ?? baseId;
menuState.Conditions.Clear();
// Now build custom state machine branching from menuState
```

This matches FogRando's pattern at `GameDataWriterE.cs:3928-3929`.

## Key ESD Functions and Commands

### Functions (Expressions)

| Call | Purpose |
|------|---------|
| `f47(3, goodId, ComparisonType, 0, 0)` | Check inventory: does player have Goods item? Type 3 = Goods. |
| `f28(2)` | Check if rebirth was performed (returns 1 if yes) |
| `f103()` | Get elapsed time (used by FogRando for warp timing) |

```csharp
// Inventory check: player has Larval Tear (Good 8185)?
AST.MakeFunction("f47", new object[] { 3, 8185, (int)ESDEdits.ComparisonType.Greater, 0, 0 });

// Combine expressions
var hasAnyTear = AST.Binop(hasTear, "||", hasDlcTear);
```

### Commands (Actions)

| Command | Purpose |
|---------|---------|
| `MakeCommand(1, 52, itemType, itemId, qty)` | Give/take item. qty=-1 consumes one. |
| `MakeCommand(1, 113)` | Open rebirth (stat reallocation) menu |
| `MakeCommand(1, 35)` | Trigger rebirth |
| `MakeCommand(1, 11, flag, 1)` | Set event flag ON (used by Item Randomizer for whetblade flags) |
| `MakeCommand(1, 145, ...)` | Open shop (used by FogRando for Shadow Mart) |

Item type constants for command (1, 52): 0=Weapon, 1=Armor, 2=Ring, 3=Goods.

### State Machine Helpers

| Helper | Purpose |
|--------|---------|
| `AST.AllocateState(machine, ref id)` | Create a new state, returns `(stateId, state)` |
| `AST.SimpleBranch(machine, state, expr, ref id)` | Branch on expression, returns `(trueState, falseState)` |
| `AST.CallState(state, targetId)` | Unconditional transition to target state |
| `ESDEdits.ShowDialog(state, returnId, msgId)` | Show a message dialog, return to `returnId` |
| `ESDEdits.ShowConfirmationDialog(state, nextId, msgId)` | Show Yes/No dialog |
| `ESDEdits.CheckDialogResult(1)` | Expression: did player press "Yes"? |
| `ESDEdits.MenuCloseExpr(menuType)` | Expression: has menu closed? (19=rebirth, 29=shop) |

## Rebirth State Machine

The `RebirthInjector` builds this dialog flow (source: `RandomizerCommon/CharacterWriter.cs:2595-2623`):

```
[Grace Menu] -> "Rebirth"
  +-- has Larval Tear?
  |   +-- NO  -> "No Larval Tear" dialog -> [return]
  |   +-- YES -> "Are you sure?" confirmation
  |       +-- NO  -> [return]
  |       +-- YES -> open rebirth menu (1,113) + trigger (1,35)
  |           +-- wait for menu close (MenuCloseExpr 19)
  |               +-- f28(2)==1 (performed) -> consume tear (1,52 qty=-1) -> [return]
  |               +-- not performed -> "Cancelled" dialog -> [return]
```

Tear check covers both base game (Good 8185) and DLC (Good 2008033).

## Filesystem Path Variants

Talk BND archives live under `script/` but the subdirectory casing varies:

| Environment | Path |
|-------------|------|
| Windows (vanilla) | `script/Talk/m00_00_00_00.talkesdbnd.dcx` |
| Wine/Linux (FogMod output) | `script/talk/m00_00_00_00.talkesdbnd.dcx` |

Always try both variants when loading:

```csharp
private static readonly string[] TALK_DIR_VARIANTS = { "talk", "Talk" };
```

When writing, use lowercase `talk` (FogMod convention).

## References

- Rebirth injection: `writer/FogModWrapper/RebirthInjector.cs`
- FogRando grace menu edits: `reference/fogrando-src/GameDataWriterE.cs:3900-3954`
- FogRando shadow mart: `reference/fogrando-src/GameDataWriterE.cs:4098-4147`
- Item Randomizer rebirth logic: `RandomizerCommon/CharacterWriter.cs:2595-2623`
