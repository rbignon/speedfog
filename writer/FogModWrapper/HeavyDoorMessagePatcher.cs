using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Suppresses the "Somewhere, a heavy door has opened" popup (text ID 4200)
/// in common_func.emevd events 90005650 and 90005652.
///
/// These events handle lever-activated and flag-activated heavy doors in
/// catacombs and Hero's Graves. The door mechanics (lever interaction, flag
/// setting, door animation) are preserved; only the dialog/message display
/// instructions are replaced with WaitFixedTime(0) NOPs.
/// </summary>
public static class HeavyDoorMessagePatcher
{
    private const int TEXT_ID_HEAVY_DOOR = 4200;

    // DisplayGenericDialog = bank 2007, index 1
    // Args: [MessageID(4), DialogType(4), NumberOfOptions(4), EntityID(4), DisplayDistance(4)]
    private const int BANK_DISPLAY = 2007;
    private const int ID_GENERIC_DIALOG = 1;

    // DisplayBlinkingMessage = bank 2007, index 4
    // Args: [MessageID(4)]
    private const int ID_BLINKING_MESSAGE = 4;

    /// <summary>
    /// NOP all DisplayGenericDialog(4200) and DisplayBlinkingMessage(4200)
    /// instructions in the provided EMEVD (expected to be common_func.emevd).
    /// Returns the number of instructions NOPed.
    /// </summary>
    public static int Patch(EMEVD emevd)
    {
        int total = 0;

        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                if (instr.Bank != BANK_DISPLAY || instr.ArgData.Length < 4)
                    continue;

                if (instr.ID != ID_GENERIC_DIALOG && instr.ID != ID_BLINKING_MESSAGE)
                    continue;

                int textId = BitConverter.ToInt32(instr.ArgData, 0);
                if (textId == TEXT_ID_HEAVY_DOOR)
                {
                    evt.Instructions[i] = AlternateFlagPatcher.MakeWaitFixedTime(0f);
                    // Remove Parameter entries that targeted this instruction
                    // (e.g. event 90005650's DisplayGenericDialog has parameterized EntityID)
                    evt.Parameters.RemoveAll(p => p.InstructionIndex == i);
                    total++;
                }
            }
        }

        if (total > 0)
            Console.WriteLine($"Heavy door message fix: NOPed {total} display instruction(s) in common_func.emevd");

        return total;
    }
}
