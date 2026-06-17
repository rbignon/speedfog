using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Removes the DLC "Shadow Realm Blessing" entry from the Site of Grace menu.
///
/// "Shadow Realm Blessing" (FMG EventTextForTalk_dlc01, message 20010001) is the
/// top-level grace menu option that opens the Scadutree Blessing / Revered Spirit
/// Ash Blessing submenu. SpeedFog enables DLC areas (Program.cs: opt["dlc"]=true)
/// so the entry shows up, but SpeedFog scales enemies via FogMod tiers and never
/// uses Scadutree blessing, making the option useless and confusing.
///
/// Confirmed via game_inspect dump-esd against the vanilla t000001000.esd: in the
/// grace menu machine (anchored by "Memorize spell", message 15000390, the same
/// machine RebirthInjector edits) a single state adds the entry via a bank-6 talk
/// command carrying message 20010001 in argument slot 1:
///
///   State 41: [entry] cmd(6, ...)  50 | 20010001 | 2010000 | 2010100
///                cond -> (sub) if &lt;wait&gt; -> cond -> 17 if 1
///
/// FogRando edits the *submenu* entry (message 20010002 "Scadutree Blessing") in a
/// different machine for its Shadow Mart feature (GameDataWriterE.cs:4098); that is a
/// distinct option from this top-level one. The submenu is reachable only by selecting
/// this entry (the selection branch keys off list id 50), so neutralizing this state
/// removes the feature entirely. The state is turned into a passthrough: its entry
/// command is dropped and its wait condition is replaced by an immediate transition
/// to the original downstream state.
///
/// Detection mirrors FogRando's bank-6 menu-entry idiom (GameDataWriterE.cs:4098-4101):
/// bank 6, exactly 4 arguments, message id in argument slot 1.
/// </summary>
public static class ShadowRealmBlessingRemover
{
    // EventTextForTalk_dlc01.fmg 20010001 = "Shadow Realm Blessing".
    public const int BLESSING_MENU_MSG = 20010001;

    // Bank 6 holds the special talk commands that add DLC menu entries.
    private const int TALK_COMMAND_BANK = 6;

    // A bank-6 menu-entry command has 4 args; the message id sits in slot 1.
    private const int MENU_ENTRY_ARG_COUNT = 4;
    private const int MSG_ARG_INDEX = 1;

    /// <summary>
    /// Remove the "Shadow Realm Blessing" entry from the grace talk ESD.
    /// </summary>
    /// <param name="modDir">FogMod output directory (mods/fogmod/)</param>
    /// <param name="gameDir">Elden Ring Game directory</param>
    public static void Inject(string modDir, string gameDir)
    {
        Console.WriteLine("Removing Shadow Realm Blessing from the grace menu...");

        var grace = GraceTalkEsd.Load(modDir, gameDir);
        if (grace == null)
            return;

        int removed = RemoveBlessingEntry(grace.GraceMachine);
        if (removed == 0)
        {
            Console.WriteLine("Note: Shadow Realm Blessing entry not present in grace menu (nothing to remove)");
            return;
        }
        if (removed > 1)
            Console.WriteLine($"Warning: neutralized {removed} Shadow Realm Blessing states (expected 1)");

        grace.Save();
        Console.WriteLine("Shadow Realm Blessing removed successfully");
    }

    /// <summary>
    /// Find every state that adds the "Shadow Realm Blessing" menu entry and turn it
    /// into a passthrough to its original downstream target. Returns the number of
    /// states neutralized (0 if the entry is absent).
    /// </summary>
    public static int RemoveBlessingEntry(Dictionary<long, ESD.State> machine)
    {
        int removed = 0;
        foreach (var state in machine.Values)
        {
            if (!StateAddsBlessingEntry(state))
                continue;

            long target = ResolveDownstreamTarget(state);
            state.EntryCommands.Clear();
            state.Conditions.Clear();
            state.Conditions.Add(new ESD.Condition(target, AST.AssembleExpression(AST.Pass)));
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// True if an entry command is a bank-6 menu-entry command (4 args) whose message
    /// id argument (slot 1) is the blessing menu message. Matches FogRando's detection
    /// idiom in GameDataWriterE.cs:4098-4101.
    /// </summary>
    private static bool StateAddsBlessingEntry(ESD.State state)
    {
        foreach (var cmd in state.EntryCommands)
        {
            if (cmd.CommandBank != TALK_COMMAND_BANK || cmd.Arguments.Count != MENU_ENTRY_ARG_COUNT)
                continue;
            try
            {
                if (AST.DisassembleExpression(cmd.Arguments[MSG_ARG_INDEX]).TryAsInt(out int value)
                    && value == BLESSING_MENU_MSG)
                    return true;
            }
            catch
            {
                // Non-literal argument expression: not the message constant.
            }
        }
        return false;
    }

    /// <summary>
    /// Resolve the first concrete target state reachable from this state's
    /// conditions, descending through subcondition groups (the vanilla menu wraps
    /// the real target in a "wait for talk call" subcondition).
    /// </summary>
    private static long ResolveDownstreamTarget(ESD.State state)
    {
        foreach (var cond in state.Conditions)
        {
            long? target = FindTarget(cond);
            if (target != null)
                return target.Value;
        }
        throw new InvalidOperationException(
            "Shadow Realm Blessing state has no resolvable downstream target");
    }

    private static long? FindTarget(ESD.Condition cond)
    {
        if (cond.TargetState != null)
            return cond.TargetState;
        foreach (var sub in cond.Subconditions)
        {
            long? target = FindTarget(sub);
            if (target != null)
                return target;
        }
        return null;
    }
}
