using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects a "Rebirth" (stat reallocation) option into the Site of Grace talk menu.
/// Reproduces the rebirth logic from Item Randomizer's CharacterWriter.cs (lines 2595-2623)
/// as a standalone post-processing step on the grace ESD.
///
/// The rebirth flow:
/// 1. Player selects "Rebirth" from the grace menu
/// 2. Check if player has a Larval Tear (Good 8185 or DLC variant 2008033)
/// 3. If no tear → show "No Larval Tear" dialog → return
/// 4. If has tear → show confirmation dialog
/// 5. On confirm → open rebirth menu (MakeCommand 1,113) + trigger rebirth (MakeCommand 1,35)
/// 6. Wait for menu close → check if rebirth was performed (f28)
/// 7. If performed → consume the appropriate tear → return
///
/// Based on: RandomizerCommon/CharacterWriter.cs:2595-2623 (rebirth ESD logic)
/// Grace menu anchor: FogMod/GameDataWriterE.cs:3900-3921 (FindMachinesWithTalkData)
/// </summary>
public static class RebirthInjector
{
    // Larval Tear Good IDs
    private const int LARVAL_TEAR_GOOD_ID = 8185;      // Base game Larval Tear
    private const int DLC_LARVAL_TEAR_GOOD_ID = 2008033; // DLC variant

    // Vanilla message IDs (from Rennala's rebirth dialog - no FMG editing needed)
    private const int MSG_REBIRTH = 22000000;       // "Rebirth" menu entry text
    private const int MSG_CONFIRM = 22001000;       // "Are you sure?" confirmation
    private const int MSG_CANCELLED = 22001001;     // "Rebirth cancelled" info
    private const int MSG_NO_TEAR = 22001002;       // "You don't have a Larval Tear"
    private const int MSG_LEAVE = 20000009;          // "Leave" option

    // Grace ESD identifiers
    private const int MEMORIZE_SPELL_MSG = 15000390; // Anchor: "Memorize spell" talk data
    private const int CONSISTENT_ID = 73;            // FogMod uses 70, randomizer uses 72

    // Talk script directory variants (vanilla=PascalCase, FogMod under Wine=lowercase)
    private static readonly string[] TALK_DIR_VARIANTS = { "talk", "Talk" };

    /// <summary>
    /// Inject the rebirth menu option into the grace talk ESD.
    /// </summary>
    /// <param name="modDir">FogMod output directory (mods/fogmod/)</param>
    /// <param name="gameDir">Elden Ring Game directory</param>
    public static void Inject(string modDir, string gameDir)
    {
        Console.WriteLine("Injecting rebirth option at Sites of Grace...");

        // Load the grace talk BND (FogMod always writes this)
        var bndFileName = "m00_00_00_00.talkesdbnd.dcx";
        var bndPath = FindTalkBnd(modDir, bndFileName)
                      ?? FindTalkBnd(gameDir, bndFileName);

        if (bndPath == null)
        {
            Console.WriteLine($"Warning: {bndFileName} not found, skipping rebirth injection");
            return;
        }

        var bnd = BND4.Read(bndPath);

        // Find the grace talk script (t000001000.esd)
        var binderFile = bnd.Files.Find(f => f.Name.Contains("t000001000"));
        if (binderFile == null)
        {
            Console.WriteLine("Warning: t000001000.esd not found in talk BND, skipping rebirth injection");
            return;
        }

        var esd = ESD.Read(binderFile.Bytes);

        // Find the grace state machine by anchoring on the "Memorize spell" message
        var machines = ESDEdits.FindMachinesWithTalkData(esd, MEMORIZE_SPELL_MSG);
        if (machines.Count != 1)
        {
            Console.WriteLine($"Warning: Expected 1 grace machine with talk data {MEMORIZE_SPELL_MSG}, " +
                              $"found {machines.Count}. Skipping rebirth injection.");
            return;
        }

        var graceMachine = esd.StateGroups[machines[0]];

        // Add "Rebirth" as a custom talk entry in the grace menu
        var rebirthTalkData = new ESDEdits.CustomTalkData
        {
            Msg = MSG_REBIRTH,
            LeaveMsg = MSG_LEAVE,
            ConsistentID = CONSISTENT_ID,
            // No condition - always shown (player can see it even without tears)
        };

        ESDEdits.ModifyCustomTalkEntry(graceMachine, rebirthTalkData, true, true, out long menuStateId);

        if (!graceMachine.TryGetValue(menuStateId, out var menuState))
        {
            Console.WriteLine("Warning: Could not add rebirth menu entry to grace ESD");
            return;
        }

        // Build the rebirth state machine branching from the menu state.
        // This reproduces CharacterWriter.cs lines 2595-2623.
        BuildRebirthStateMachine(graceMachine, menuState, ref menuStateId);

        // Write modified ESD back
        binderFile.Bytes = esd.Write();

        // Write the BND to modDir (always write to mod output)
        var writePath = FindTalkBnd(modDir, bndFileName) ?? CreateTalkBndPath(modDir, bndFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        bnd.Write(writePath);

        Console.WriteLine("Rebirth option injected successfully");
    }

    /// <summary>
    /// Build the rebirth state machine: check for tears, confirm, open rebirth menu,
    /// consume tear on success.
    /// </summary>
    private static void BuildRebirthStateMachine(
        Dictionary<long, ESD.State> graceMachine,
        ESD.State menuState,
        ref long baseId)
    {
        // Save the return state (the grace menu idle state) from the default condition
        // created by ModifyCustomTalkEntry, then clear existing conditions so we can
        // build our own branching logic. This matches FogRando's pattern in
        // GameDataWriterE.cs:3928-3929 for custom post-menu state machines.
        long returnId = menuState.Conditions.Count > 0
            ? menuState.Conditions[0].TargetState ?? baseId
            : baseId;
        menuState.Conditions.Clear();

        // Check: does player have a Larval Tear? (Good 8185 OR Good 2008033)
        var hasTear = HasItemExpr(LARVAL_TEAR_GOOD_ID);
        var hasDlcTear = HasItemExpr(DLC_LARVAL_TEAR_GOOD_ID);
        var hasAnyTear = AST.Binop(hasTear, "||", hasDlcTear);

        // Branch: has tear?
        var (yesTearState, noTearState) = AST.SimpleBranch(graceMachine, menuState, hasAnyTear, ref baseId);

        // No tear → show "No Larval Tear" dialog → return to menu
        ESDEdits.ShowDialog(noTearState, returnId, MSG_NO_TEAR);

        // Has tear → show confirmation dialog
        var (confirmNextId, confirmMain) = AST.AllocateState(graceMachine, ref baseId);
        ESDEdits.ShowConfirmationDialog(yesTearState, confirmNextId, MSG_CONFIRM);

        // Branch: did player press "Yes"?
        var (yesState, noState) = AST.SimpleBranch(graceMachine, confirmMain, ESDEdits.CheckDialogResult(1), ref baseId);

        // No (cancel) → return to menu
        AST.CallState(noState, returnId);

        // Yes → open rebirth menu and trigger rebirth
        var (waitStateId, waitMain) = AST.AllocateState(graceMachine, ref baseId);
        yesState.EntryCommands.Add(AST.MakeCommand(1, 113));  // Open rebirth menu
        yesState.EntryCommands.Add(AST.MakeCommand(1, 35));   // Trigger rebirth
        yesState.Conditions.Add(new ESD.Condition(waitStateId, AST.AssembleExpression(ESDEdits.MenuCloseExpr(19))));

        // After menu closes: check if rebirth was actually performed (f28(2) == 1)
        var rebirthPerformed = AST.Binop(AST.MakeFunction("f28", 2), "==", 1);
        var (performedState, notPerformedState) = AST.SimpleBranch(graceMachine, waitMain, rebirthPerformed, ref baseId);

        // Not performed → show cancelled dialog → return
        ESDEdits.ShowDialog(notPerformedState, returnId, MSG_CANCELLED);

        // Performed → consume the tear (check which type the player has)
        var (consumeBaseTear, consumeDlcTear) = AST.SimpleBranch(graceMachine, performedState, hasTear, ref baseId);

        // Has base tear → consume base tear (give -1)
        consumeBaseTear.EntryCommands.Add(GiveItemCommand(LARVAL_TEAR_GOOD_ID, -1));
        AST.CallState(consumeBaseTear, returnId);

        // No base tear → must have DLC tear → consume it (give -1)
        consumeDlcTear.EntryCommands.Add(GiveItemCommand(DLC_LARVAL_TEAR_GOOD_ID, -1));
        AST.CallState(consumeDlcTear, returnId);
    }

    /// <summary>
    /// Create an ESD expression that checks if the player has a Goods item (quantity > 0).
    /// Equivalent to CharacterWriter.HasItemExpr for Goods type.
    /// f47 params: itemType(3=Goods), itemId, comparisonType(Greater), value(0), extra(0)
    /// </summary>
    private static AST.Expr HasItemExpr(int goodId)
    {
        return AST.MakeFunction("f47", new object[]
        {
            3,                                      // ItemType.Goods
            goodId,
            (int)ESDEdits.ComparisonType.Greater,   // >
            0,                                      // quantity > 0
            0                                       // unused param
        });
    }

    /// <summary>
    /// Create an ESD command to give/take an item.
    /// Command (1, 52): Give player item. quantity=-1 means consume one.
    /// Params: itemType(3=Goods), itemId, quantity
    /// </summary>
    private static ESD.CommandCall GiveItemCommand(int goodId, int quantity)
    {
        return AST.MakeCommand(1, 52, new object[] { 3, goodId, quantity });
    }

    /// <summary>
    /// Find a talk BND file under a base directory, trying both case variants.
    /// </summary>
    private static string? FindTalkBnd(string baseDir, string bndFileName)
    {
        foreach (var dirName in TALK_DIR_VARIANTS)
        {
            var path = Path.Combine(baseDir, "script", dirName, bndFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>
    /// Create the expected path for a talk BND in the mod directory.
    /// Uses lowercase "talk" (FogMod convention under Wine).
    /// </summary>
    private static string CreateTalkBndPath(string modDir, string bndFileName)
    {
        return Path.Combine(modDir, "script", "talk", bndFileName);
    }
}
