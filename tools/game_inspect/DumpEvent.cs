using SoulsFormats;

/// <summary>
/// EMEVD helpers that work through the old SoulsFormats under Wine. The
/// richer dump_emevd_warps runs natively but needs Oodle for the KRAK-compressed
/// vanilla EMEVDs (FogMod output is DFLT and reads fine natively), so these two
/// are the way to read game files on Linux:
///   dump-event <emevd> <event-id>   print every instruction of one event
///   find-int <emevd> <value>        list every 4-byte aligned argument slot
///                                   holding the value (flag and entity lookups)
/// </summary>
static class DumpEvent
{
    public static int RunDump(string[] args)
    {
        if (args.Length < 3 || !long.TryParse(args[2], out long eventId))
        {
            Console.Error.WriteLine("Usage: game_inspect dump-event <emevd> <event-id>");
            return 1;
        }
        if (!TryRead(args[1], out var emevd)) return 1;
        var ev = emevd.Events.Find(e => e.ID == eventId);
        if (ev == null)
        {
            Console.Error.WriteLine($"Event {eventId} not found in {Path.GetFileName(args[1])}");
            return 1;
        }
        Console.WriteLine($"=== Event {ev.ID} ({ev.Instructions.Count} instructions, rest {ev.RestBehavior}) ===");
        for (int i = 0; i < ev.Instructions.Count; i++)
        {
            var ins = ev.Instructions[i];
            Console.WriteLine($"  [{i}] {ins.Bank}[{ins.ID}] {FormatArgs(ins.ArgData)}");
        }
        if (ev.Parameters.Count > 0)
        {
            // Same X{offset}_{size} notation as fogevents.txt: the source offset
            // counts from the first argument after the event ID in the initializer.
            Console.WriteLine($"  parameters ({ev.Parameters.Count}):");
            foreach (var prm in ev.Parameters.OrderBy(x => x.InstructionIndex).ThenBy(x => x.TargetStartByte))
                Console.WriteLine($"    X{prm.SourceStartByte}_{prm.ByteCount} -> [{prm.InstructionIndex}] byte {prm.TargetStartByte}");
        }
        return 0;
    }

    public static int RunFind(string[] args)
    {
        // Arguments are scanned as raw 32-bit slots, so accept anything that
        // fits in int32 or uint32 and compare the bit pattern.
        if (args.Length < 3 || !long.TryParse(args[2], out long value) || value < int.MinValue || value > uint.MaxValue)
        {
            Console.Error.WriteLine("Usage: game_inspect find-int <emevd> <value>   (value must fit in int32 or uint32)");
            return 1;
        }
        uint pattern = unchecked((uint)value);
        if (!TryRead(args[1], out var emevd)) return 1;
        int hits = 0;
        foreach (var ev in emevd.Events)
        {
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var ins = ev.Instructions[i];
                for (int off = 0; off + 4 <= ins.ArgData.Length; off += 4)
                {
                    if (BitConverter.ToUInt32(ins.ArgData, off) != pattern) continue;
                    Console.WriteLine($"  event {ev.ID} [{i}] {ins.Bank}[{ins.ID}] at byte {off}: {FormatArgs(ins.ArgData)}");
                    hits++;
                }
            }
        }
        Console.WriteLine($"{hits} argument slots hold {value} in {Path.GetFileName(args[1])}");
        return 0;
    }

    static bool TryRead(string path, out EMEVD emevd)
    {
        try
        {
            emevd = EMEVD.Read(path);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read EMEVD: {ex.Message}");
            emevd = null!;
            return false;
        }
    }

    static string FormatArgs(byte[] data)
    {
        var ints = new List<string>();
        for (int off = 0; off + 4 <= data.Length; off += 4)
            ints.Add(BitConverter.ToInt32(data, off).ToString());
        return $"ints=({string.Join(",", ints)}) hex={Convert.ToHexString(data)}";
    }
}
