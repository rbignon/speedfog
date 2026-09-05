using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes the new-game intro cutscene (the Tarnished waking up at the Chapel
/// of Anticipation) from event 10010020 ("Game start") of m10_01_00_00.emevd.
///
/// The PlayCutsceneToPlayerWithWeatherAndTime instruction is replaced by two
/// instructions: SetCurrentTime(23:45), the clock the cutscene call set
/// through its change-time argument, and ChangeWeather(Default, 3600 s,
/// immediate), a chosen explicit start weather (the call's change-weather
/// argument was off). The rest of the event (respawn point, save request,
/// flags 100 and slot 0) is preserved.
///
/// Instructions are built from raw bytes (as ChapelGraceInjector does) so
/// Patch stays free of the EMEDF parser and unit-testable without it.
/// </summary>
public static class IntroCutscenePatcher
{
    private const long GAME_START_EVENT_ID = 10010020;

    // Vanilla event 10010020, instruction 7:
    // PlayCutsceneToPlayerWithWeatherAndTime(10000040, Skippable, 10000, false, Default, 0, true, 23, 45, 0)
    private const int INTRO_CUTSCENE_ID = 10000040;

    // PlayCutsceneToPlayerWithWeatherAndTime = bank 2002, id 10
    // Args: [cutsceneId(4), playback(4), playerEntity(4), changeWeather(1), weather(1), pad(2),
    //        weatherLifespan(4 float), changeTime(1), hours(1), minutes(1), seconds(1)]
    private const int BANK_CUTSCENE = 2002;
    private const int ID_CUTSCENE_WEATHER_TIME = 10;

    // The clock the removed instruction set (its change-time argument).
    private const byte START_HOUR = 23;
    private const byte START_MINUTE = 45;

    // Weather.Default; the lifespan follows the vanilla precedent of the
    // Stranded Graveyard exit (m18 event 18000021: ChangeWeather(Default, 3600, false)).
    private const sbyte WEATHER_DEFAULT = 0;
    private const float WEATHER_LIFESPAN_SECONDS = 3600f;

    /// <summary>
    /// Replace the intro cutscene instruction in event 10010020 of the
    /// provided EMEVD (expected to be m10_01_00_00.emevd) by
    /// SetCurrentTime + ChangeWeather. Returns the number of instructions
    /// replaced.
    /// </summary>
    public static int Patch(EMEVD emevd)
    {
        var evt = emevd.Events.Find(e => e.ID == GAME_START_EVENT_ID);
        if (evt == null)
        {
            Console.WriteLine($"Warning: event {GAME_START_EVENT_ID} not found, intro cutscene not removed");
            return 0;
        }

        int total = 0;
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var instr = evt.Instructions[i];
            if (instr.Bank != BANK_CUTSCENE || instr.ID != ID_CUTSCENE_WEATHER_TIME
                || instr.ArgData.Length < 4)
                continue;

            if (BitConverter.ToInt32(instr.ArgData, 0) != INTRO_CUTSCENE_ID)
                continue;

            evt.Instructions[i] = MakeSetCurrentTime(START_HOUR, START_MINUTE);
            evt.Instructions.Insert(i + 1, MakeChangeWeather(WEATHER_DEFAULT, WEATHER_LIFESPAN_SECONDS));
            // Parameter entries: drop the ones that targeted the replaced
            // instruction (none in vanilla; same defensive cleanup as
            // HeavyDoorMessagePatcher), shift the later ones past the insert.
            evt.Parameters.RemoveAll(p => p.InstructionIndex == i);
            foreach (var p in evt.Parameters)
            {
                if (p.InstructionIndex > i)
                    p.InstructionIndex++;
            }
            i++; // skip the inserted ChangeWeather
            total++;
        }

        if (total > 0)
            Console.WriteLine($"Intro cutscene: replaced {total} PlayCutscene instruction(s) in event {GAME_START_EVENT_ID} " +
                              $"with SetCurrentTime({START_HOUR}:{START_MINUTE}) + ChangeWeather(Default)");
        else
            Console.WriteLine($"Warning: PlayCutscene({INTRO_CUTSCENE_ID}) not found in event {GAME_START_EVENT_ID}, intro cutscene not removed");

        return total;
    }

    /// <summary>
    /// SetCurrentTime(hours, minutes, 0, false, false, false, 0, 0, 0): bank 2001, id 4.
    /// Args: [hours(1), minutes(1), seconds(1), fade(1), wait(1), showClock(1), pad(2),
    ///        clockStartupDelay(4 float), clockMoveTime(4 float), clockFinishDelay(4 float)]
    /// </summary>
    internal static EMEVD.Instruction MakeSetCurrentTime(byte hours, byte minutes)
    {
        var args = new byte[20];
        args[0] = hours;
        args[1] = minutes;
        return new EMEVD.Instruction(2001, 4, args);
    }

    /// <summary>
    /// ChangeWeather(weather, lifespan, true): bank 2003, id 68, switched immediately.
    /// Args: [weather(1 sbyte), pad(3), lifespan(4 float), immediate(1), pad(3)]
    /// </summary>
    internal static EMEVD.Instruction MakeChangeWeather(sbyte weather, float lifespanSeconds)
    {
        var args = new byte[12];
        args[0] = (byte)weather;
        BitConverter.GetBytes(lifespanSeconds).CopyTo(args, 4);
        args[8] = 1;
        return new EMEVD.Instruction(2003, 68, args);
    }
}
