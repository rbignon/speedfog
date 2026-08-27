using SoulsFormats;
using SoulsIds;

/// <summary>
/// Compare the params of two regulation.bin files (typically two game
/// versions): rows added or removed by ID, and for common rows the cells whose
/// value changed, field by field. Defs are applied the way SoulsIds does for
/// FogMod (see CheckParams). A param that no def applies to is compared by
/// row ID and row name only (the old SoulsFormats does not expose raw row
/// bytes).
/// </summary>
static class DiffParam
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: game_inspect diff-param <old-regulation.bin> <new-regulation.bin> [--defs <dir>] [--param <name>]... [--rows-only]");
            Console.Error.WriteLine("  --defs       directory holding paramdef XMLs (default: ./eldendata/Defs)");
            Console.Error.WriteLine("  --param      restrict to the named param (repeatable, e.g. EquipParamGoods)");
            Console.Error.WriteLine("  --rows-only  list added/removed rows only, skip cell comparison");
            return 1;
        }

        string oldPath = args[1];
        string newPath = args[2];
        string? defsDir = null;
        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool rowsOnly = false;
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--defs" when i + 1 < args.Length: defsDir = args[++i]; break;
                case "--param" when i + 1 < args.Length: only.Add(args[++i]); break;
                case "--rows-only": rowsOnly = true; break;
                default:
                    Console.Error.WriteLine($"Unknown or malformed argument: {args[i]}");
                    return 1;
            }
        }

        defsDir ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eldendata", "Defs");
        if (!Directory.Exists(defsDir))
        {
            Console.Error.WriteLine($"Paramdef directory not found: {defsDir} (pass --defs <dir>)");
            return 1;
        }
        var defs = Directory.GetFiles(defsDir, "*.xml").Select(p => PARAMDEF.XmlDeserialize(p)).ToList();

        Dictionary<string, PARAM> oldParams, newParams;
        try
        {
            oldParams = LoadParams(oldPath, defs);
            newParams = LoadParams(newPath, defs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load regulation.bin: {ex.Message}");
            return 1;
        }

        foreach (var missing in only.Where(n => !oldParams.ContainsKey(n) && !newParams.ContainsKey(n)))
            Console.WriteLine($"=== {missing}: no such param in either file ===");
        foreach (var name in oldParams.Keys.Union(newParams.Keys).OrderBy(n => n))
        {
            if (only.Count > 0 && !only.Contains(name)) continue;
            if (!oldParams.TryGetValue(name, out var oldParam)) { Console.WriteLine($"=== {name}: only in new ==="); continue; }
            if (!newParams.TryGetValue(name, out var newParam)) { Console.WriteLine($"=== {name}: only in old ==="); continue; }

            var oldRows = oldParam.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
            var newRows = newParam.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
            if (oldRows.Count != oldParam.Rows.Count || newRows.Count != newParam.Rows.Count)
                Console.WriteLine($"=== {name}: duplicate row IDs ({oldParam.Rows.Count - oldRows.Count} old, {newParam.Rows.Count - newRows.Count} new), only the first copy is compared ===");
            var added = newRows.Keys.Except(oldRows.Keys).OrderBy(i => i).ToList();
            var removed = oldRows.Keys.Except(newRows.Keys).OrderBy(i => i).ToList();
            var changed = new List<(int id, string name, List<string> cells)>();
            bool defsApplied = oldParam.AppliedParamdef != null && newParam.AppliedParamdef != null;
            if (!rowsOnly)
            {
                foreach (var id in oldRows.Keys.Intersect(newRows.Keys).OrderBy(i => i))
                {
                    var a = oldRows[id];
                    var b = newRows[id];
                    var cells = defsApplied ? ChangedCells(a, b) : a.Name != b.Name ? new List<string> { $"row name: {a.Name} -> {b.Name} (no def applied, bytes not compared)" } : new List<string>();
                    if (cells.Count > 0) changed.Add((id, b.Name ?? "", cells));
                }
            }
            if (added.Count == 0 && removed.Count == 0 && changed.Count == 0) continue;

            Console.WriteLine($"=== {name}: +{added.Count} rows, -{removed.Count} rows, {changed.Count} rows changed{(defsApplied ? "" : " (no def applied)")} ===");
            foreach (var id in added) Console.WriteLine($"  + {id,-10} {newRows[id].Name}");
            foreach (var id in removed) Console.WriteLine($"  - {id,-10} {oldRows[id].Name}");
            foreach (var (id, rowName, cells) in changed)
            {
                Console.WriteLine($"  ~ {id,-10} {rowName}");
                foreach (var c in cells) Console.WriteLine($"      {c}");
            }
        }
        return 0;
    }

    static Dictionary<string, PARAM> LoadParams(string regPath, List<PARAMDEF> defs)
    {
        var bnd = SFUtil.DecryptERRegulation(regPath);
        var result = new Dictionary<string, PARAM>();
        foreach (var file in bnd.Files)
        {
            if (!file.Name.EndsWith(".param", StringComparison.OrdinalIgnoreCase)) continue;
            var param = PARAM.Read(file.Bytes);
            ParamDictionary.ApplyParamdefCarefully(param, defs);
            result[Path.GetFileNameWithoutExtension(file.Name)] = param;
        }
        return result;
    }

    static List<string> ChangedCells(PARAM.Row a, PARAM.Row b)
    {
        var result = new List<string>();
        int n = Math.Min(a.Cells.Count, b.Cells.Count);
        for (int i = 0; i < n; i++)
        {
            var x = a.Cells[i];
            var y = b.Cells[i];
            if (x.Def.InternalName != y.Def.InternalName)
            {
                result.Add($"field layout differs at cell {i}: {x.Def.InternalName} vs {y.Def.InternalName}");
                break;
            }
            if (Equals(x.Value, y.Value)) continue;
            if (x.Value is byte[] xa && y.Value is byte[] ya && xa.AsSpan().SequenceEqual(ya)) continue;
            result.Add($"{x.Def.InternalName}: {Fmt(x.Value)} -> {Fmt(y.Value)}");
        }
        return result;
    }

    static string Fmt(object? v) => v is byte[] bytes ? Convert.ToHexString(bytes) : v?.ToString() ?? "null";
}
