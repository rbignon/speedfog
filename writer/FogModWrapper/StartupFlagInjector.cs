using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects SetEventFlag instructions at the start of Event 0 in map-specific
/// or common EMEVDs. Used to force game state at startup (e.g., opening gates,
/// unlocking doors).
///
/// Flags are grouped by EMEVD file to minimize reads/writes.
/// </summary>
public static class StartupFlagInjector
{
    /// <summary>
    /// Inject SetEventFlag instructions into Event 0 of the specified EMEVDs.
    /// </summary>
    /// <param name="modDir">Mod output directory containing event/*.emevd.dcx</param>
    /// <param name="flags">List of (mapId, flagId, on) tuples. mapId is e.g. "m35_00_00_00" or "common".</param>
    public static void Inject(string modDir, IEnumerable<(string mapId, int flagId, bool on)> flags)
    {
        // Group by EMEVD file to do a single Read/Write per file
        var grouped = flags.GroupBy(f => f.mapId);

        foreach (var group in grouped)
        {
            var mapId = group.Key;
            var emevdPath = Path.Combine(modDir, "event", $"{mapId}.emevd.dcx");
            if (!File.Exists(emevdPath))
            {
                Console.WriteLine($"Warning: {mapId}.emevd.dcx not found, skipping {group.Count()} startup flag(s)");
                continue;
            }

            var emevd = EMEVD.Read(emevdPath);
            var evt0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
            if (evt0 == null)
            {
                Console.WriteLine($"Warning: Event 0 not found in {mapId}.emevd.dcx");
                continue;
            }

            var flagList = group.ToList();
            for (int i = 0; i < flagList.Count; i++)
            {
                evt0.Instructions.Insert(i, MakeSetEventFlag(flagList[i].flagId, flagList[i].on));
            }

            // Shift parameter indices to account for inserted instructions
            foreach (var param in evt0.Parameters)
            {
                param.InstructionIndex += flagList.Count;
            }

            emevd.Write(emevdPath);

            var flagStr = string.Join(", ", flagList.Select(f => $"{f.flagId}={(@f.on ? "ON" : "OFF")}"));
            Console.WriteLine($"Startup flags: set {flagList.Count} flag(s) in {mapId} ({flagStr})");
        }
    }

    private static EMEVD.Instruction MakeSetEventFlag(int flagId, bool on)
    {
        var args = new byte[12];
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        args[8] = (byte)(on ? 1 : 0);
        return new EMEVD.Instruction(2003, 66, args);
    }
}
