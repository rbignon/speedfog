using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for ShadowRealmBlessingRemover: removing the DLC "Shadow Realm Blessing"
/// entry (FMG EventTextForTalk_dlc01 message 20010001) from the Site of Grace menu.
///
/// The remover's input structures are built to match the real vanilla ESD shape
/// confirmed via game_inspect dump-esd: a bank-6 menu-entry command with 4 args and
/// the message id in slot 1, guarded by a nested "wait for talk call" condition.
/// </summary>
public class ShadowRealmBlessingRemoverTests
{
    private const int BLESSING_MENU_MSG = 20010001; // "Shadow Realm Blessing"
    private const int SCADUTREE_SUBMENU_MSG = 20010002; // "Scadutree Blessing" (submenu, FogRando)
    private const int TALK_COMMAND_BANK = 6;
    private const long DOWNSTREAM_STATE = 17;       // vanilla: continues to "Memorize spell" state

    private static byte[] Pass() => AST.AssembleExpression(AST.Pass);

    /// <summary>
    /// Build a grace-menu state that runs a single command and is guarded by the
    /// vanilla nested "wait for talk call" condition resolving to a downstream state.
    /// </summary>
    private static ESD.State BuildMenuState(int commandBank, object[] args)
    {
        var state = new ESD.State();
        state.EntryCommands.Add(AST.MakeCommand(commandBank, 99, args));

        // Vanilla shape: cond -> (sub) if <wait>  /  sub: cond -> 17 if 1
        var wait = new ESD.Condition();
        wait.Evaluator = Pass();
        wait.Subconditions.Add(new ESD.Condition(DOWNSTREAM_STATE, Pass()));
        state.Conditions.Add(wait);

        return state;
    }

    // Real vanilla command: cmd(6, ...) 50 | 20010001 | 2010000 | 2010100
    private static ESD.State BuildBlessingState() =>
        BuildMenuState(TALK_COMMAND_BANK, new object[] { 50, BLESSING_MENU_MSG, 2010000, 2010100 });

    [Fact]
    public void RemoveBlessingEntry_neutralizes_state_and_links_to_downstream_target()
    {
        var machine = new Dictionary<long, ESD.State> { [41] = BuildBlessingState() };

        int removed = ShadowRealmBlessingRemover.RemoveBlessingEntry(machine);

        Assert.Equal(1, removed);
        var state = machine[41];
        // The command that added the blessing option is gone.
        Assert.Empty(state.EntryCommands);
        // State becomes a passthrough: a single immediate transition to the original
        // downstream target (no async wait left dangling).
        Assert.Single(state.Conditions);
        Assert.Equal(DOWNSTREAM_STATE, state.Conditions[0].TargetState);
        Assert.Empty(state.Conditions[0].Subconditions);
    }

    [Fact]
    public void RemoveBlessingEntry_returns_zero_and_leaves_machine_untouched_when_absent()
    {
        // A grace state with an unrelated talk entry (no bank-6 blessing command).
        var other = BuildMenuState(1, new object[] { 4, 15000390, -1 });
        var machine = new Dictionary<long, ESD.State> { [17] = other };

        int removed = ShadowRealmBlessingRemover.RemoveBlessingEntry(machine);

        Assert.Equal(0, removed);
        Assert.Single(machine[17].EntryCommands);
    }

    [Fact]
    public void RemoveBlessingEntry_ignores_the_scadutree_submenu_entry()
    {
        // The submenu "Scadutree Blessing" (20010002) lives in a different machine and
        // must not be matched: only the top-level "Shadow Realm Blessing" is removed.
        var submenu = BuildMenuState(TALK_COMMAND_BANK, new object[] { 1, SCADUTREE_SUBMENU_MSG, 0, 0 });
        var machine = new Dictionary<long, ESD.State> { [8] = submenu };

        int removed = ShadowRealmBlessingRemover.RemoveBlessingEntry(machine);

        Assert.Equal(0, removed);
        Assert.Single(machine[8].EntryCommands);
    }

    [Fact]
    public void RemoveBlessingEntry_ignores_non_bank6_command_carrying_the_message()
    {
        // The blessing message in a bank-1 command (e.g. a plain AddTalkListData) is
        // not a DLC blessing menu entry; only bank 6 qualifies.
        var state = BuildMenuState(1, new object[] { 4, BLESSING_MENU_MSG, -1, 0 });
        var machine = new Dictionary<long, ESD.State> { [5] = state };

        int removed = ShadowRealmBlessingRemover.RemoveBlessingEntry(machine);

        Assert.Equal(0, removed);
        Assert.Single(machine[5].EntryCommands);
    }

    [Fact]
    public void RemoveBlessingEntry_ignores_message_in_wrong_argument_slot()
    {
        // The message id must be in argument slot 1 (FogRando idiom). A bank-6 command
        // with the value in another slot is not a blessing menu entry.
        var state = BuildMenuState(TALK_COMMAND_BANK, new object[] { BLESSING_MENU_MSG, 50, 0, 0 });
        var machine = new Dictionary<long, ESD.State> { [9] = state };

        int removed = ShadowRealmBlessingRemover.RemoveBlessingEntry(machine);

        Assert.Equal(0, removed);
        Assert.Single(machine[9].EntryCommands);
    }
}
