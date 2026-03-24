using SoulsFormats;
using System.Reflection;

static class CompareAssets
{
    public static void Run(string msbPath, uint eid1, uint eid2)
    {
        var msb = MSBE.Read(msbPath);
        var a1 = msb.Parts.Assets.Find(a => a.EntityID == eid1);
        var a2 = msb.Parts.Assets.Find(a => a.EntityID == eid2);
        if (a1 == null) { Console.WriteLine($"Entity {eid1} not found"); return; }
        if (a2 == null) { Console.WriteLine($"Entity {eid2} not found"); return; }

        Console.WriteLine($"=== {a1.Name} (EID={eid1}) vs {a2.Name} (EID={eid2}) ===");

        // Compare all public properties via reflection
        var type = typeof(MSBE.Part.Asset);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                var v1 = prop.GetValue(a1);
                var v2 = prop.GetValue(a2);
                var s1 = FormatValue(v1);
                var s2 = FormatValue(v2);
                if (s1 != s2)
                    Console.WriteLine($"  DIFF {prop.Name}: {s1} vs {s2}");
            }
            catch { }
        }
    }

    static string FormatValue(object? v)
    {
        if (v == null) return "null";
        if (v is uint[] ua) return $"[{string.Join(",", ua)}]";
        if (v is int[] ia) return $"[{string.Join(",", ia)}]";
        if (v is byte[] ba) return $"[{string.Join(",", ba)}]";
        if (v is string[] sa) return $"[{string.Join(",", sa.Select(x => x ?? "null"))}]";
        if (v is System.Numerics.Vector3 vec) return $"({vec.X:F1},{vec.Y:F1},{vec.Z:F1})";
        return v.ToString() ?? "null";
    }
}
