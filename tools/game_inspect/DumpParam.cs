using SoulsFormats;

public static class DumpParam
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: game_inspect dump-param <regulation.bin> <ParamName> [--row <id>] [--prefix <prefix>] [--defs <dir>] [--def-name <name>] [--field <name>]");
            Console.Error.WriteLine("  --row       dump every field of the named row");
            Console.Error.WriteLine("  --prefix    list IDs starting with the given digits");
            Console.Error.WriteLine("  --field     restrict --row output to fields whose name contains this substring (repeatable)");
            Console.Error.WriteLine("  --defs      directory holding paramdef XMLs (default: ./eldendata/Defs)");
            Console.Error.WriteLine("  --def-name  paramdef XML basename when it differs from ParamName (e.g. SpEffect for SpEffectParam)");
            return 1;
        }

        string regPath = args[1];
        string paramName = args[2];
        int? rowId = null;
        string? prefix = null;
        string? defsDir = null;
        string? defName = null;
        var fieldFilters = new List<string>();

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--row" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out var parsedRow))
                    {
                        Console.Error.WriteLine($"Invalid --row value (expected integer): {args[i]}");
                        return 1;
                    }
                    rowId = parsedRow;
                    break;
                case "--prefix" when i + 1 < args.Length:
                    prefix = args[++i];
                    break;
                case "--defs" when i + 1 < args.Length:
                    defsDir = args[++i];
                    break;
                case "--def-name" when i + 1 < args.Length:
                    defName = args[++i];
                    break;
                case "--field" when i + 1 < args.Length:
                    fieldFilters.Add(args[++i]);
                    break;
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

        BND4 bnd;
        try { bnd = SFUtil.DecryptERRegulation(regPath); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to decrypt regulation.bin: {ex.Message}");
            return 1;
        }

        var file = bnd.Files.Find(f => f.Name.EndsWith($"{paramName}.param"));
        if (file == null)
        {
            Console.Error.WriteLine($"{paramName}.param not found in regulation.bin");
            return 1;
        }

        var defPath = Path.Combine(defsDir, $"{defName ?? paramName}.xml");
        if (!File.Exists(defPath))
        {
            Console.Error.WriteLine($"Paramdef XML not found: {defPath} (pass --def-name <basename> if it differs from the param)");
            return 1;
        }

        PARAMDEF def;
        PARAM param;
        try
        {
            def = PARAMDEF.XmlDeserialize(defPath);
            param = PARAM.Read(file.Bytes);
            param.ApplyParamdef(def);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse {paramName} with paramdef {Path.GetFileName(defPath)}: {ex.Message}");
            Console.Error.WriteLine("If the paramdef basename differs from the param name, pass --def-name <basename>.");
            return 1;
        }

        if (rowId.HasValue)
        {
            var row = param.Rows.Find(r => r.ID == rowId.Value);
            if (row == null)
            {
                Console.Error.WriteLine($"Row {rowId} not found in {paramName}");
                return 1;
            }
            DumpRow(paramName, row, fieldFilters);
        }
        else if (prefix != null)
        {
            var matches = param.Rows.Where(r => r.ID.ToString().StartsWith(prefix)).OrderBy(r => r.ID).ToList();
            Console.WriteLine($"{paramName}: {matches.Count} rows matching prefix '{prefix}'");
            foreach (var row in matches)
                Console.WriteLine($"  {row.ID}  {row.Name}");
        }
        else
        {
            Console.WriteLine($"{paramName}: {param.Rows.Count} rows");
        }
        return 0;
    }

    static void DumpRow(string paramName, PARAM.Row row, List<string> fieldFilters)
    {
        Console.WriteLine($"=== {paramName}[{row.ID}] {row.Name} ===");
        foreach (var cell in row.Cells)
        {
            var name = cell.Def.InternalName;
            if (fieldFilters.Count > 0 && !fieldFilters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                continue;
            Console.WriteLine($"  {name,-40} = {cell.Value}");
        }
    }
}
