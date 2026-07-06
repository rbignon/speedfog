using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Shared EMEVD instruction-building helpers.
/// </summary>
public static class EmevdHelper
{
    /// <summary>
    /// Build an InitializeEvent instruction (bank 2000, id 0): slot 0, the
    /// given event ID, plus optional extra int arguments.
    /// </summary>
    public static EMEVD.Instruction InitializeEvent(int eventId, params int[] args)
    {
        var bytes = new byte[8 + args.Length * 4];
        BitConverter.GetBytes(0).CopyTo(bytes, 0);        // slot = 0
        BitConverter.GetBytes(eventId).CopyTo(bytes, 4);  // eventId
        for (int i = 0; i < args.Length; i++)
        {
            BitConverter.GetBytes(args[i]).CopyTo(bytes, 8 + i * 4);
        }
        return new EMEVD.Instruction(2000, 0, bytes);
    }
}
