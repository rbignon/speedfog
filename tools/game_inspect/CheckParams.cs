using SoulsFormats;
using SoulsIds;

/// <summary>
/// Check every param in a regulation.bin against a paramdef directory with
/// the predicate SoulsIds uses for FogMod: ParamType equality and
/// DetectedSize == GetRowSize(ulong.MaxValue) (ParamDictionary
/// .ApplyParamdefCarefully, same test as the ApplyParamdefAggressively that
/// FogMod's lazy ParamDictionary indexer runs, GameDataWriterE.cs:67-68). A
/// param that no def applies to keeps its vanilla bytes on write, but FogMod
/// throws as soon as it accesses one it edits, which is what breaks
/// generation after a game patch. SoulsFormats' own PARAM
/// .ApplyParamdefCarefully uses a different row size and reports false
/// mismatches; do not switch to it.
///
/// With --reference, also reports params whose row count differs between the
/// two regulation files (new content) or paramdef data version.
/// </summary>
static class CheckParams
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: game_inspect check-params <regulation.bin> [--defs <dir>] [--reference <regulation.bin>] [--all]");
            Console.Error.WriteLine("  --defs       directory holding paramdef XMLs (default: ./eldendata/Defs)");
            Console.Error.WriteLine("  --reference  second regulation.bin to compare row counts against");
            Console.Error.WriteLine("  --all        list every param, not only problems and differences");
            return 1;
        }

        string regPath = args[1];
        string? defsDir = null;
        string? referencePath = null;
        bool listAll = false;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--defs" when i + 1 < args.Length: defsDir = args[++i]; break;
                case "--reference" when i + 1 < args.Length: referencePath = args[++i]; break;
                case "--all": listAll = true; break;
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
        Console.WriteLine($"Loaded {defs.Count} paramdefs from {defsDir}");

        Dictionary<string, PARAM> target;
        Dictionary<string, PARAM>? reference;
        try
        {
            target = LoadParams(regPath);
            reference = referencePath != null ? LoadParams(referencePath) : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load regulation.bin: {ex.Message}");
            return 1;
        }

        int problems = 0, differences = 0;
        foreach (var (name, param) in target.OrderBy(kv => kv.Key))
        {
            string status;
            if (ParamDictionary.ApplyParamdefCarefully(param, defs))
            {
                status = "ok";
            }
            else
            {
                var candidates = defs.Where(d => d.ParamType == param.ParamType).ToList();
                if (candidates.Count == 0)
                    status = $"NO DEF for ParamType {param.ParamType}";
                else
                    status = $"ROW SIZE MISMATCH (file {param.DetectedSize}, def {string.Join("/", candidates.Select(d => d.GetRowSize(ulong.MaxValue)))})";
                problems++;
            }

            string diff = "";
            if (reference != null)
            {
                if (!reference.TryGetValue(name, out var refParam))
                {
                    diff = "  [absent from reference]";
                    differences++;
                }
                else
                {
                    var notes = new List<string>();
                    if (refParam.Rows.Count != param.Rows.Count)
                        notes.Add($"rows {refParam.Rows.Count} -> {param.Rows.Count}");
                    if (refParam.ParamdefDataVersion != param.ParamdefDataVersion)
                        notes.Add($"data version {refParam.ParamdefDataVersion} -> {param.ParamdefDataVersion}");
                    if (notes.Count > 0)
                    {
                        diff = "  [" + string.Join(", ", notes) + "]";
                        differences++;
                    }
                }
            }

            if (listAll || status != "ok" || diff.Length > 0)
                Console.WriteLine($"  {name,-40} {param.Rows.Count,6} rows  {status}{diff}");
        }

        if (reference != null)
        {
            foreach (var name in reference.Keys.Except(target.Keys).OrderBy(n => n))
            {
                Console.WriteLine($"  {name,-40} [only in reference]");
                differences++;
            }
        }

        Console.WriteLine($"Params: {target.Count}, paramdef problems: {problems}" +
                          (reference != null ? $", differences vs reference: {differences}" : ""));
        return problems > 0 ? 2 : 0;
    }

    static Dictionary<string, PARAM> LoadParams(string regPath)
    {
        var bnd = SFUtil.DecryptERRegulation(regPath);
        var result = new Dictionary<string, PARAM>();
        foreach (var file in bnd.Files)
        {
            if (!file.Name.EndsWith(".param", StringComparison.OrdinalIgnoreCase)) continue;
            result[Path.GetFileNameWithoutExtension(file.Name)] = PARAM.Read(file.Bytes);
        }
        return result;
    }
}
