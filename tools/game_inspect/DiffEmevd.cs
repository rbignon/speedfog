using SoulsFormats;

/// <summary>
/// Compare two EMEVDs (the same map from two game versions). Lists events added
/// or removed by ID and common events whose instruction stream changed, with
/// the instruction count on each side. Starts with a decompressed-bytes
/// comparison so container-only changes are reported without parsing.
/// </summary>
static class DiffEmevd
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: game_inspect diff-emevd <old.emevd.dcx> <new.emevd.dcx>");
            return 1;
        }

        byte[] oldBytes, newBytes;
        try
        {
            oldBytes = DCX.Decompress(args[1]);
            newBytes = DCX.Decompress(args[2]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read EMEVD: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"=== {Path.GetFileName(args[1])} -> {Path.GetFileName(args[2])} ===");
        if (oldBytes.AsSpan().SequenceEqual(newBytes))
        {
            Console.WriteLine("  decompressed content identical (container-only change)");
            return 0;
        }
        Console.WriteLine($"  decompressed size {oldBytes.Length} -> {newBytes.Length}");

        var oldEvents = KeyedById(EMEVD.Read(oldBytes).Events);
        var newEvents = KeyedById(EMEVD.Read(newBytes).Events);
        int changes = 0;
        foreach (var id in newEvents.Keys.Except(oldEvents.Keys).OrderBy(k => newEvents[k].ID))
        {
            Console.WriteLine($"  + event {id,-12} {newEvents[id].Instructions.Count} instructions");
            changes++;
        }
        foreach (var id in oldEvents.Keys.Except(newEvents.Keys).OrderBy(k => oldEvents[k].ID))
        {
            Console.WriteLine($"  - event {id,-12} {oldEvents[id].Instructions.Count} instructions");
            changes++;
        }
        foreach (var id in oldEvents.Keys.Intersect(newEvents.Keys).OrderBy(k => oldEvents[k].ID))
        {
            var a = oldEvents[id];
            var b = newEvents[id];
            if (SameInstructions(a, b)) continue;
            Console.WriteLine($"  ~ event {id,-12} {a.Instructions.Count} -> {b.Instructions.Count} instructions");
            changes++;
        }
        if (changes == 0)
            Console.WriteLine("  no differences in event IDs or instruction streams (parameter/layer data may differ)");
        return 0;
    }

    /// <summary>
    /// Index events by ID; a repeated ID gets a "#2", "#3"... suffix so an
    /// oddly authored file is reported instead of crashing ToDictionary.
    /// </summary>
    static Dictionary<string, EMEVD.Event> KeyedById(IEnumerable<EMEVD.Event> events)
    {
        var result = new Dictionary<string, EMEVD.Event>();
        foreach (var ev in events)
        {
            string key = ev.ID.ToString();
            for (int n = 2; result.ContainsKey(key); n++)
                key = $"{ev.ID}#{n}";
            result[key] = ev;
        }
        return result;
    }

    static bool SameInstructions(EMEVD.Event a, EMEVD.Event b)
    {
        if (a.Instructions.Count != b.Instructions.Count) return false;
        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var x = a.Instructions[i];
            var y = b.Instructions[i];
            if (x.Bank != y.Bank || x.ID != y.ID || !x.ArgData.AsSpan().SequenceEqual(y.ArgData))
                return false;
        }
        return true;
    }
}
