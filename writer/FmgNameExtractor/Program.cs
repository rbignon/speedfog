using System.Text;
using System.Text.Json;
using SoulsFormats;

/// <summary>
/// Extracts name pairs (EN→target language) from Elden Ring FMG files.
/// Reads PlaceName and NpcName from item.msgbnd.dcx and item_dlc02.msgbnd.dcx.
/// Writes JSON to a file: { "PlaceName": { "en_text": "fr_text", ... }, "NpcName": { ... } }
/// </summary>
class Program
{
    // FMG categories to extract
    private static readonly string[] FmgNames = ["PlaceName", "NpcName"];

    // BND files that contain these FMGs
    private static readonly string[] BndFiles = ["item.msgbnd.dcx", "item_dlc02.msgbnd.dcx"];

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: FmgNameExtractor <eldendata_msg_dir> <output_json> [--target <lang>]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  eldendata_msg_dir  Path to Vanilla/msg/");
            Console.Error.WriteLine("  output_json        Output JSON file path");
            Console.Error.WriteLine("  --target <lang>    Target language directory name (default: frafr)");
            return 1;
        }

        var msgDir = args[0];
        var outputPath = args[1];
        var targetLang = "frafr";

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--target" && i + 1 < args.Length)
                targetLang = args[++i];
        }

        var enDir = Path.Combine(msgDir, "engus");
        var targetDir = Path.Combine(msgDir, targetLang);

        if (!Directory.Exists(enDir))
        {
            Console.Error.WriteLine($"English directory not found: {enDir}");
            return 1;
        }
        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"Target language directory not found: {targetDir}");
            return 1;
        }

        var result = new Dictionary<string, Dictionary<string, string>>();

        foreach (var fmgName in FmgNames)
        {
            var pairs = new Dictionary<string, string>();

            foreach (var bndFile in BndFiles)
            {
                var enPath = Path.Combine(enDir, bndFile);
                var targetPath = Path.Combine(targetDir, bndFile);

                if (!File.Exists(enPath) || !File.Exists(targetPath))
                    continue;

                var enEntries = ExtractFmgEntries(enPath, fmgName);
                var targetEntries = ExtractFmgEntries(targetPath, fmgName);

                if (enEntries == null || targetEntries == null)
                    continue;

                // Build target lookup by ID
                var targetById = new Dictionary<int, string>();
                foreach (var entry in targetEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                        targetById[entry.ID] = entry.Text;
                }

                // Match EN→target by ID
                foreach (var entry in enEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Text))
                        continue;
                    if (targetById.TryGetValue(entry.ID, out var targetText))
                    {
                        pairs.TryAdd(entry.Text, targetText);
                    }
                }
            }

            if (pairs.Count > 0)
                result[fmgName] = pairs;
        }

        // Write JSON to file (UTF-8 without BOM)
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var json = JsonSerializer.Serialize(result, options);
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));

        Console.Error.WriteLine($"Extracted: {string.Join(", ", result.Select(r => $"{r.Key}={r.Value.Count}"))}");
        Console.Error.WriteLine($"Written to: {outputPath}");
        return 0;
    }

    private static List<FMG.Entry>? ExtractFmgEntries(string bndPath, string fmgName)
    {
        try
        {
            var bnd = BND4.Read(bndPath);
            var fmgFile = bnd.Files.Find(f =>
                f.Name.EndsWith($"{fmgName}.fmg", StringComparison.OrdinalIgnoreCase));
            if (fmgFile == null)
                return null;

            var fmg = FMG.Read(fmgFile.Bytes);
            return fmg.Entries;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to read {fmgName} from {bndPath}: {ex.Message}");
            return null;
        }
    }
}
