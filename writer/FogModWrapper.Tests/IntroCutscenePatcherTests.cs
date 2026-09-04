using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class IntroCutscenePatcherTests
{
    private const long GAME_START_EVENT_ID = 10010020;
    private const int INTRO_CUTSCENE_ID = 10000040;

    // PlayCutsceneToPlayerWithWeatherAndTime = bank 2002, id 10
    private const int BANK_CUTSCENE = 2002;
    private const int ID_CUTSCENE_WEATHER_TIME = 10;

    /// <summary>
    /// Build the vanilla instruction 7 of event 10010020:
    /// [cutsceneId(4), playback(4), playerEntity(4), changeWeather(1), weather(1), pad(2),
    ///  weatherLifespan(4 float), changeTime(1), hours(1), minutes(1), seconds(1)]
    /// = PlayCutsceneToPlayerWithWeatherAndTime(cutsceneId, Skippable, 10000, false, Default, 0, true, 23, 45, 0)
    /// </summary>
    private static EMEVD.Instruction MakeIntroCutscene(int cutsceneId)
    {
        var args = new byte[24];
        BitConverter.GetBytes(cutsceneId).CopyTo(args, 0);
        BitConverter.GetBytes(0).CopyTo(args, 4);       // playback: Skippable
        BitConverter.GetBytes(10000).CopyTo(args, 8);   // player entity
        BitConverter.GetBytes(0f).CopyTo(args, 16);     // weather lifespan
        args[20] = 1;                                   // change time
        args[21] = 23;
        args[22] = 45;
        return new EMEVD.Instruction(BANK_CUTSCENE, ID_CUTSCENE_WEATHER_TIME, args);
    }

    private static EMEVD.Instruction MakeFiller()
    {
        // SetWindSFX(-1) as in the vanilla event: bank 2006, id 6
        return new EMEVD.Instruction(2006, 6, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
    }

    private static void AssertIsNop(EMEVD.Instruction instr)
    {
        // WaitFixedTime(0): bank 1001, id 0, [seconds(4 float)] = 0
        Assert.Equal(1001, instr.Bank);
        Assert.Equal(0, instr.ID);
        Assert.Equal(new byte[4], instr.ArgData);
    }

    private static EMEVD MakeEmevd(params EMEVD.Event[] events)
    {
        var emevd = new EMEVD();
        emevd.Events.AddRange(events);
        return emevd;
    }

    [Fact]
    public void Patch_NopsIntroCutsceneInGameStartEvent()
    {
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(MakeFiller());                              // [0]
        evt.Instructions.Add(MakeIntroCutscene(INTRO_CUTSCENE_ID));      // [1]
        evt.Instructions.Add(MakeFiller());                              // [2]

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(1, count);
        AssertIsNop(evt.Instructions[1]);
        Assert.Equal(2006, evt.Instructions[0].Bank);
        Assert.Equal(2006, evt.Instructions[2].Bank);
    }

    [Fact]
    public void Patch_IgnoresOtherCutsceneIds()
    {
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(MakeIntroCutscene(12345));

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(0, count);
        Assert.Equal(BANK_CUTSCENE, evt.Instructions[0].Bank);
    }

    [Fact]
    public void Patch_IgnoresSiblingCutsceneInstruction()
    {
        // PlayCutsceneToPlayerAndWarpWithWeatherAndTime (bank 2002, id 12) also
        // starts with a cutscene ID; only id 10 is the intro cutscene call.
        var intro = MakeIntroCutscene(INTRO_CUTSCENE_ID);
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(new EMEVD.Instruction(BANK_CUTSCENE, 12, intro.ArgData));

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(0, count);
        Assert.Equal(12, evt.Instructions[0].ID);
    }

    [Fact]
    public void Patch_IgnoresIntroCutsceneInOtherEvents()
    {
        var evt = new EMEVD.Event(10010030);
        evt.Instructions.Add(MakeIntroCutscene(INTRO_CUTSCENE_ID));

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(0, count);
        Assert.Equal(BANK_CUTSCENE, evt.Instructions[0].Bank);
    }

    [Fact]
    public void Patch_MissingGameStartEvent_ReturnsZero()
    {
        var evt = new EMEVD.Event(0);
        evt.Instructions.Add(MakeFiller());

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(0, count);
    }

    [Fact]
    public void Patch_RemovesOrphanedParameterEntries()
    {
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(MakeFiller());                              // [0]
        evt.Instructions.Add(MakeIntroCutscene(INTRO_CUTSCENE_ID));      // [1] - will be NOP'd
        evt.Instructions.Add(MakeFiller());                              // [2]
        evt.Parameters.Add(new EMEVD.Parameter(1, 8, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(2, 0, 0, 4));

        IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(2, evt.Parameters.Count);
        Assert.DoesNotContain(evt.Parameters, p => p.InstructionIndex == 1);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 0);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 2);
    }
}
