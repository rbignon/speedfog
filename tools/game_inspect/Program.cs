using SoulsFormats;

// Dispatch subcommand
if (args.Length >= 1 && args[0] == "dump-entity")
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: game_inspect dump-entity <msb-dir> <entity-id>"); return 1; }
    DumpEntity(args[1], int.Parse(args[2]));
    return 0;
}
if (args.Length >= 1 && args[0] == "find-model")
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: game_inspect find-model <msb> <model>"); return 1; }
    FindModel.Run(args[1], args[2]);
    return 0;
}
if (args.Length >= 1 && args[0] == "compare")
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: game_inspect compare <msb> <eid1> <eid2>"); return 1; }
    CompareAssets.Run(args[1], uint.Parse(args[2]), uint.Parse(args[3]));
    return 0;
}
if (args.Length >= 1 && args[0] == "check-emevd")
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: game_inspect check-emevd <emevd> [entity_id]"); return 1; }
    uint target = args.Length > 2 ? uint.Parse(args[2]) : 0;
    CheckEmevd.Run(args[1], target);
    return 0;
}

// Default: list SFX
return ListSfx(args);

// ── dump-entity ─────────────────────────────────────────────────────

static void DumpEntity(string msbDir, int entityId)
{
    foreach (var msbPath in Directory.GetFiles(msbDir, "*.msb.dcx").OrderBy(f => f))
    {
        var msb = MSBE.Read(msbPath);
        var mapName = Path.GetFileName(msbPath).Replace(".msb.dcx", "");

        foreach (var part in msb.Parts.GetEntries())
        {
            if (part.EntityID != entityId) continue;

            Console.WriteLine($"=== FOUND in {mapName} ===");
            Console.WriteLine($"  Name: {part.Name}");
            Console.WriteLine($"  Type: {part.GetType().Name}");
            Console.WriteLine($"  EntityID: {part.EntityID}");
            Console.WriteLine($"  ModelName: {part.ModelName}");
            Console.WriteLine($"  Position: ({part.Position.X:F2}, {part.Position.Y:F2}, {part.Position.Z:F2})");
            Console.WriteLine($"  Rotation: ({part.Rotation.X:F2}, {part.Rotation.Y:F2}, {part.Rotation.Z:F2})");
            Console.WriteLine($"  Scale: ({part.Scale.X:F2}, {part.Scale.Y:F2}, {part.Scale.Z:F2})");

            if (part is MSBE.Part.Asset asset)
            {
                Console.WriteLine($"  AssetSfxParamRelativeID: {asset.AssetSfxParamRelativeID}");
            }
            else if (part is MSBE.Part.Enemy enemy)
            {
                Console.WriteLine($"  NPCParamID: {enemy.NPCParamID}");
                Console.WriteLine($"  ThinkParamID: {enemy.ThinkParamID}");
                Console.WriteLine($"  TalkID: {enemy.TalkID}");
                Console.WriteLine($"  CharaInitID: {enemy.CharaInitID}");
            }
            else if (part is MSBE.Part.DummyEnemy dummyEnemy)
            {
                Console.WriteLine($"  NPCParamID: {dummyEnemy.NPCParamID}");
                Console.WriteLine($"  ThinkParamID: {dummyEnemy.ThinkParamID}");
            }

            var groups = part.EntityGroupIDs?.Where(g => g > 0).ToArray();
            if (groups?.Length > 0)
                Console.WriteLine($"  EntityGroupIDs: [{string.Join(", ", groups)}]");

            Console.WriteLine();
        }
    }
}

// ── list-sfx ────────────────────────────────────────────────────────

static int ListSfx(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: game_inspect list-sfx <path> [--search <id>] [--range <min>-<max>] [--bundle <pattern>]");
        return 1;
    }

    string path = args[0];
    int? searchId = null;
    int? rangeMin = null;
    int? rangeMax = null;
    string? bundleFilter = null;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--search" when i + 1 < args.Length:
                searchId = int.Parse(args[++i]);
                break;
            case "--range" when i + 1 < args.Length:
                var parts = args[++i].Split('-');
                rangeMin = int.Parse(parts[0]);
                rangeMax = int.Parse(parts[1]);
                break;
            case "--bundle" when i + 1 < args.Length:
                bundleFilter = args[++i];
                break;
        }
    }

    var files = new List<string>();
    if (Directory.Exists(path))
        files.AddRange(Directory.GetFiles(path, "*.ffxbnd.dcx").OrderBy(f => f));
    else if (File.Exists(path))
        files.Add(path);
    else
    {
        Console.Error.WriteLine($"Path not found: {path}");
        return 1;
    }

    if (bundleFilter != null)
        files = files.Where(f => Path.GetFileName(f).Contains(bundleFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    foreach (var file in files)
    {
        var bundleName = Path.GetFileName(file);
        try
        {
            var bnd = BND4.Read(file);
            var sfxIds = new List<(int id, string name)>();

            foreach (var entry in bnd.Files)
            {
                var fileName = Path.GetFileNameWithoutExtension(entry.Name ?? "");
                if (fileName.StartsWith("f") && fileName.Length > 1)
                {
                    if (int.TryParse(fileName.Substring(1), out int sfxId))
                        sfxIds.Add((sfxId, entry.Name ?? ""));
                }
            }

            sfxIds.Sort((a, b) => a.id.CompareTo(b.id));

            if (searchId.HasValue)
            {
                var match = sfxIds.FirstOrDefault(s => s.id == searchId.Value);
                if (match.name != null)
                    Console.WriteLine($"FOUND: SFX {searchId} in {bundleName} ({match.name})");
                continue;
            }

            if (rangeMin.HasValue && rangeMax.HasValue)
            {
                var filtered = sfxIds.Where(s => s.id >= rangeMin.Value && s.id <= rangeMax.Value).ToList();
                if (filtered.Count > 0)
                {
                    Console.WriteLine($"--- {bundleName} ({filtered.Count} matches in range) ---");
                    foreach (var (id, name) in filtered)
                        Console.WriteLine($"  {id,10}  {name}");
                }
                continue;
            }

            Console.WriteLine($"--- {bundleName} ({sfxIds.Count} SFX) ---");
            foreach (var (id, name) in sfxIds)
                Console.WriteLine($"  {id,10}  {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading {bundleName}: {ex.Message}");
        }
    }

    return 0;
}
