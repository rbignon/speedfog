using FogModWrapper.Plugins.Halloween;
using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// HalloweenDarknessInjector builds its event through FogMod's
/// Events.ParseAdd, so the tests need a real SoulsIds.Events instance backed
/// by the gitignored data/er-common.emedf.json; they skip (early return) when
/// it is absent, like StartingItemInjectorTests.
/// </summary>
public class HalloweenDarknessInjectorTests
{
    private static string? FindDataDir()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "er-common.emedf.json")))
            return envDir;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../..", "data"));
        if (File.Exists(Path.Combine(candidate, "er-common.emedf.json")))
            return candidate;

        return null;
    }

    private static Events? BuildEvents()
    {
        var dataDir = FindDataDir();
        return dataDir == null
            ? null
            : new Events(Path.Combine(dataDir, "er-common.emedf.json"), darkScriptMode: true, paramAwareMode: true);
    }

    private static EMEVD RunInject(Events events)
    {
        var commonEmevd = new EMEVD();
        commonEmevd.Events.Add(new EMEVD.Event(0));
        HalloweenDarknessInjector.InjectEmevdEvent(commonEmevd, events);
        return commonEmevd;
    }

    private static EMEVD.Event DarknessEvent(EMEVD emevd) =>
        emevd.Events.Single(e => e.ID == SpeedFogIds.HalloweenDarknessEvents.Base);

    [Fact]
    public void Inject_ActivatesTheEvergaolOverrideLikeVanilla()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var activate = DarknessEvent(RunInject(events)).Instructions[0];

        // Activate Gparam Override (2012[11]): sub ID 0 (m00_00_0000_commonevent)
        // with no transition, byte for byte the evergaol call of vanilla
        // common_func event 90005882.
        Assert.Equal((2012, 11), (activate.Bank, activate.ID));
        Assert.Equal(new byte[8], activate.ArgData);
    }

    [Fact]
    public void Inject_LoopsWithAWaitBeforeRestarting()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var instructions = DarknessEvent(RunInject(events)).Instructions;

        // A restart without a wait would spin the event every frame.
        var wait = Assert.Single(instructions, i => (i.Bank, i.ID) == (1001, 0));
        Assert.True(BitConverter.ToSingle(wait.ArgData, 0) > 0);
        var end = instructions[^1];
        Assert.Equal((1000, 4), (end.Bank, end.ID));
        Assert.Equal(1, end.ArgData[0]); // EventEndType.Restart
    }

    [Fact]
    public void Inject_RegistersTheEventInEventZero()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var init = RunInject(events).Events.Single(e => e.ID == 0).Instructions.Single();

        Assert.Equal((2000, 0), (init.Bank, init.ID));
        Assert.Equal(SpeedFogIds.HalloweenDarknessEvents.Base, BitConverter.ToInt32(init.ArgData, 4));
    }

    // --- RepointOverrides: vanilla fast loops re-activating their own override ---

    private static EMEVD.Instruction Activate(int subId, float transition) =>
        new(2012, 11, BitConverter.GetBytes(subId).Concat(BitConverter.GetBytes(transition)).ToArray());

    private static EMEVD.Event AcademyLikeLoop(long id) => new(id)
    {
        Instructions =
        {
            new EMEVD.Instruction(1003, 101, new byte[8]),
            Activate(500, 0.5f),
            new EMEVD.Instruction(1001, 0, BitConverter.GetBytes(0.1f)),
            new EMEVD.Instruction(1000, 4, new byte[] { 1, 0, 0, 0 }),
            new EMEVD.Instruction(2012, 12, BitConverter.GetBytes(0f)),
        },
    };

    [Fact]
    public void RepointOverrides_SwitchesTheEventsActivationsToTheEvergaolSubId()
    {
        var emevd = new EMEVD();
        emevd.Events.Add(AcademyLikeLoop(14002689));

        Assert.Equal(1, HalloweenDarknessInjector.RepointOverrides(emevd, 14002689));

        var activate = emevd.Events[0].Instructions[1];
        Assert.Equal(0, BitConverter.ToInt32(activate.ArgData, 0));
        Assert.Equal(0.5f, BitConverter.ToSingle(activate.ArgData, 4)); // transition kept
    }

    [Fact]
    public void RepointOverrides_LeavesOtherInstructionsAndEventsAlone()
    {
        var emevd = new EMEVD();
        emevd.Events.Add(AcademyLikeLoop(14002689));
        emevd.Events.Add(AcademyLikeLoop(14002690));
        var before = emevd.Events.Select(e => e.Instructions.Select(i => i.ArgData.ToArray()).ToList()).ToList();

        HalloweenDarknessInjector.RepointOverrides(emevd, 14002689);

        var patched = emevd.Events[0].Instructions;
        foreach (var i in new[] { 0, 2, 3, 4 })
            Assert.Equal(before[0][i], patched[i].ArgData);
        Assert.All(emevd.Events[1].Instructions.Select((ins, i) => (ins, i)),
            x => Assert.Equal(before[1][x.i], x.ins.ArgData));
    }

    [Fact]
    public void RepointOverrides_MissingEvent_ChangesNothing()
    {
        var emevd = new EMEVD();
        emevd.Events.Add(AcademyLikeLoop(14002690));

        Assert.Equal(0, HalloweenDarknessInjector.RepointOverrides(emevd, 14002689));
        Assert.Equal(500, BitConverter.ToInt32(emevd.Events[0].Instructions[1].ArgData, 0));
    }
}
