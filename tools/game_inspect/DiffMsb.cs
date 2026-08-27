using System.Numerics;
using SoulsFormats;

/// <summary>
/// Compare two MSBs (typically the same map from two game versions). Lists
/// parts, regions and events added or removed by name, and common entries whose
/// model, entity ID, position or rotation changed. Starts by comparing the
/// decompressed bytes, so a re-compressed but otherwise identical file is
/// reported as such without walking the entries.
/// </summary>
static class DiffMsb
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: game_inspect diff-msb <old.msb.dcx> <new.msb.dcx>");
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
            Console.Error.WriteLine($"Failed to read MSB: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"=== {Path.GetFileName(args[1])} -> {Path.GetFileName(args[2])} ===");
        if (oldBytes.AsSpan().SequenceEqual(newBytes))
        {
            Console.WriteLine("  decompressed content identical (container-only change)");
            return 0;
        }
        Console.WriteLine($"  decompressed size {oldBytes.Length} -> {newBytes.Length}");

        var oldMsb = MSBE.Read(oldBytes);
        var newMsb = MSBE.Read(newBytes);
        int changes = 0;
        changes += DiffParts(oldMsb, newMsb);
        changes += DiffRegions(oldMsb, newMsb);
        changes += DiffEvents(oldMsb, newMsb);
        if (changes == 0)
            Console.WriteLine("  no differences in part/region/event names, models, entity IDs or transforms");
        return 0;
    }

    static int DiffParts(MSBE oldMsb, MSBE newMsb)
    {
        var oldParts = KeyedByName(oldMsb.Parts.GetEntries(), p => p.Name);
        var newParts = KeyedByName(newMsb.Parts.GetEntries(), p => p.Name);
        int changes = 0;
        foreach (var name in newParts.Keys.Except(oldParts.Keys).OrderBy(n => n))
        {
            var p = newParts[name];
            Console.WriteLine($"  + part   {p.GetType().Name,-10} {name,-24} model={p.ModelName,-10} entity={p.EntityID,-12} pos={Fmt(p.Position)}");
            changes++;
        }
        foreach (var name in oldParts.Keys.Except(newParts.Keys).OrderBy(n => n))
        {
            var p = oldParts[name];
            Console.WriteLine($"  - part   {p.GetType().Name,-10} {name,-24} model={p.ModelName,-10} entity={p.EntityID,-12} pos={Fmt(p.Position)}");
            changes++;
        }
        foreach (var name in oldParts.Keys.Intersect(newParts.Keys).OrderBy(n => n))
        {
            var a = oldParts[name];
            var b = newParts[name];
            var notes = new List<string>();
            if (a.ModelName != b.ModelName) notes.Add($"model {a.ModelName} -> {b.ModelName}");
            if (a.EntityID != b.EntityID) notes.Add($"entity {a.EntityID} -> {b.EntityID}");
            if (Vector3.Distance(a.Position, b.Position) > 0.001f) notes.Add($"pos {Fmt(a.Position)} -> {Fmt(b.Position)}");
            if (Vector3.Distance(a.Rotation, b.Rotation) > 0.001f) notes.Add($"rot {Fmt(a.Rotation)} -> {Fmt(b.Rotation)}");
            if (notes.Count == 0) continue;
            Console.WriteLine($"  ~ part   {a.GetType().Name,-10} {name,-24} {string.Join(", ", notes)}");
            changes++;
        }
        return changes;
    }

    static int DiffRegions(MSBE oldMsb, MSBE newMsb)
    {
        var oldRegions = KeyedByName(oldMsb.Regions.GetEntries(), r => r.Name);
        var newRegions = KeyedByName(newMsb.Regions.GetEntries(), r => r.Name);
        int changes = 0;
        foreach (var name in newRegions.Keys.Except(oldRegions.Keys).OrderBy(n => n))
        {
            var r = newRegions[name];
            Console.WriteLine($"  + region {r.GetType().Name,-10} {name,-24} entity={r.EntityID,-12} pos={Fmt(r.Position)}");
            changes++;
        }
        foreach (var name in oldRegions.Keys.Except(newRegions.Keys).OrderBy(n => n))
        {
            var r = oldRegions[name];
            Console.WriteLine($"  - region {r.GetType().Name,-10} {name,-24} entity={r.EntityID,-12} pos={Fmt(r.Position)}");
            changes++;
        }
        foreach (var name in oldRegions.Keys.Intersect(newRegions.Keys).OrderBy(n => n))
        {
            var a = oldRegions[name];
            var b = newRegions[name];
            var notes = new List<string>();
            if (a.EntityID != b.EntityID) notes.Add($"entity {a.EntityID} -> {b.EntityID}");
            if (Vector3.Distance(a.Position, b.Position) > 0.001f) notes.Add($"pos {Fmt(a.Position)} -> {Fmt(b.Position)}");
            if (notes.Count == 0) continue;
            Console.WriteLine($"  ~ region {a.GetType().Name,-10} {name,-24} {string.Join(", ", notes)}");
            changes++;
        }
        return changes;
    }

    static int DiffEvents(MSBE oldMsb, MSBE newMsb)
    {
        var oldEvents = KeyedByName(oldMsb.Events.GetEntries(), e => e.Name);
        var newEvents = KeyedByName(newMsb.Events.GetEntries(), e => e.Name);
        int changes = 0;
        foreach (var name in newEvents.Keys.Except(oldEvents.Keys).OrderBy(n => n))
        {
            var e = newEvents[name];
            Console.WriteLine($"  + event  {e.GetType().Name,-10} {name,-24} entity={e.EntityID}");
            changes++;
        }
        foreach (var name in oldEvents.Keys.Except(newEvents.Keys).OrderBy(n => n))
        {
            var e = oldEvents[name];
            Console.WriteLine($"  - event  {e.GetType().Name,-10} {name,-24} entity={e.EntityID}");
            changes++;
        }
        return changes;
    }

    /// <summary>
    /// Index entries by name; a repeated name gets a "#2", "#3"... suffix so
    /// an oddly authored map is reported instead of crashing ToDictionary.
    /// </summary>
    static Dictionary<string, T> KeyedByName<T>(IEnumerable<T> entries, Func<T, string> name)
    {
        var result = new Dictionary<string, T>();
        foreach (var entry in entries)
        {
            string key = name(entry);
            for (int n = 2; result.ContainsKey(key); n++)
                key = $"{name(entry)}#{n}";
            result[key] = entry;
        }
        return result;
    }

    static string Fmt(Vector3 v) => $"({v.X:F1},{v.Y:F1},{v.Z:F1})";
}
