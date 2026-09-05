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

    /// <summary>
    /// SetCurrentTime(23, 45, 0, false, false, false, 0, 0, 0): bank 2001, id 4,
    /// [hours(1), minutes(1), seconds(1), fade(1), wait(1), showClock(1), pad(2),
    ///  startupDelay(4 float), moveTime(4 float), finishDelay(4 float)]
    /// </summary>
    private static void AssertIsSetCurrentTime2345(EMEVD.Instruction instr)
    {
        Assert.Equal(2001, instr.Bank);
        Assert.Equal(4, instr.ID);
        var expected = new byte[20];
        expected[0] = 23;
        expected[1] = 45;
        Assert.Equal(expected, instr.ArgData);
    }

    /// <summary>
    /// ChangeWeather(Weather.Default, 3600, true): bank 2003, id 68,
    /// [weather(1), pad(3), lifespan(4 float), immediate(1), pad(3)]
    /// </summary>
    private static void AssertIsChangeWeatherDefault(EMEVD.Instruction instr)
    {
        Assert.Equal(2003, instr.Bank);
        Assert.Equal(68, instr.ID);
        var expected = new byte[12];
        BitConverter.GetBytes(3600f).CopyTo(expected, 4);
        expected[8] = 1;
        Assert.Equal(expected, instr.ArgData);
    }

    private static EMEVD MakeEmevd(params EMEVD.Event[] events)
    {
        var emevd = new EMEVD();
        emevd.Events.AddRange(events);
        return emevd;
    }

    [Fact]
    public void Patch_ReplacesIntroCutsceneWithTimeAndWeather()
    {
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(MakeFiller());                              // [0]
        evt.Instructions.Add(MakeIntroCutscene(INTRO_CUTSCENE_ID));      // [1]
        evt.Instructions.Add(MakeFiller());                              // [2]

        int count = IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(1, count);
        Assert.Equal(4, evt.Instructions.Count);
        Assert.Equal(2006, evt.Instructions[0].Bank);
        AssertIsSetCurrentTime2345(evt.Instructions[1]);
        AssertIsChangeWeatherDefault(evt.Instructions[2]);
        Assert.Equal(2006, evt.Instructions[3].Bank);
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
    public void Patch_RemovesOrphanedParameterEntriesAndShiftsLaterOnes()
    {
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.Add(MakeFiller());                              // [0]
        evt.Instructions.Add(MakeIntroCutscene(INTRO_CUTSCENE_ID));      // [1] - replaced by two instructions
        evt.Instructions.Add(MakeFiller());                              // [2] - becomes [3]
        evt.Parameters.Add(new EMEVD.Parameter(1, 8, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(2, 0, 0, 4));

        IntroCutscenePatcher.Patch(MakeEmevd(evt));

        Assert.Equal(2, evt.Parameters.Count);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 0);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 3);
        Assert.DoesNotContain(evt.Parameters, p => p.InstructionIndex is 1 or 2);
    }
}
