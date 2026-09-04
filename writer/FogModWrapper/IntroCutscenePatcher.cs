using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes the new-game intro cutscene (the Tarnished waking up at the Chapel
/// of Anticipation) from event 10010020 ("Game start") of m10_01_00_00.emevd.
///
/// The PlayCutsceneToPlayerWithWeatherAndTime instruction is replaced by a
/// WaitFixedTime(0) NOP; the rest of the event (respawn point, save request,
/// flags 100 and slot 0) is preserved. The instruction also set the clock to
/// 23:45 through its change-time argument; that side effect goes with it,
/// the new-game clock is left to the engine default.
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

    /// <summary>
    /// NOP the intro cutscene instruction in event 10010020 of the provided
    /// EMEVD (expected to be m10_01_00_00.emevd). Returns the number of
    /// instructions NOPed.
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

            evt.Instructions[i] = AlternateFlagPatcher.MakeWaitFixedTime(0f);
            // Remove Parameter entries that targeted this instruction (none in
            // vanilla; same defensive cleanup as HeavyDoorMessagePatcher).
            evt.Parameters.RemoveAll(p => p.InstructionIndex == i);
            total++;
        }

        if (total > 0)
            Console.WriteLine($"Intro cutscene: NOPed {total} PlayCutscene instruction(s) in event {GAME_START_EVENT_ID}");
        else
            Console.WriteLine($"Warning: PlayCutscene({INTRO_CUTSCENE_ID}) not found in event {GAME_START_EVENT_ID}, intro cutscene not removed");

        return total;
    }
}
