using FogMod;
using FogModWrapper;
using FogModWrapper.Models;
using FogModWrapper.Packaging;
using SoulsFormats;
using SoulsIds;
using YamlDotNet.Serialization;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var config = ParseArgs(args);
            await RunAsync(config);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("DEBUG") != null)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }

    static Config ParseArgs(string[] args)
    {
        var config = new Config();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--game-dir":
                    config.GameDir = args[++i];
                    break;
                case "--data-dir":
                    config.DataDir = args[++i];
                    break;
                case "-o":
                case "--output":
                    config.OutputDir = args[++i];
                    break;
                case "--merge-dir":
                    config.MergeDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    if (arg.StartsWith("-"))
                    {
                        throw new ArgumentException($"Unknown option: {arg}");
                    }
                    config.SeedDir = arg;
                    break;
            }
        }

        // Validate required args
        if (string.IsNullOrEmpty(config.SeedDir))
            throw new ArgumentException("Missing required argument: seed directory");
        if (!File.Exists(config.GraphPath))
            throw new ArgumentException($"graph.json not found in seed directory: {config.GraphPath}");
        if (string.IsNullOrEmpty(config.GameDir))
            throw new ArgumentException("Missing required argument: --game-dir");
        if (string.IsNullOrEmpty(config.DataDir))
            throw new ArgumentException("Missing required argument: --data-dir");
        if (string.IsNullOrEmpty(config.OutputDir))
            throw new ArgumentException("Missing required argument: -o/--output");

        return config;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"FogModWrapper - SpeedFog mod generator using FogMod.dll

Usage: FogModWrapper <seed_dir> --game-dir <path> --data-dir <path> -o <output> [--merge-dir <path>]

Arguments:
  <seed_dir>         Path to seed directory (contains graph.json, spoiler.txt)
  --game-dir <path>  Path to Elden Ring Game directory
  --data-dir <path>  Path to data directory (fog.txt, fogevents.txt, er-common.emedf.json)
  -o, --output       Output directory for generated mod files
  --merge-dir <path> Optional: Path to Item Randomizer output to merge

Example:
  FogModWrapper seeds/123456 --game-dir ""C:/Games/ELDEN RING/Game"" --data-dir data -o output
  FogModWrapper seeds/123456 --game-dir ""C:/Games/ELDEN RING/Game"" --data-dir data -o output --merge-dir temp/item-randomizer
");
    }

    static async Task RunAsync(Config config)
    {
        Console.WriteLine("=== FogModWrapper ===");
        Console.WriteLine($"Seed dir: {config.SeedDir}");
        Console.WriteLine($"Game dir: {config.GameDir}");
        Console.WriteLine($"Data dir: {config.DataDir}");
        Console.WriteLine($"Output: {config.OutputDir}");
        if (!string.IsNullOrEmpty(config.MergeDir))
        {
            Console.WriteLine($"Merge dir: {config.MergeDir}");
        }
        Console.WriteLine();

        // Mod files go in mods/fogmod/ subdirectory for ModEngine
        var modDir = Path.Combine(config.OutputDir, "mods", "fogmod");
        Directory.CreateDirectory(modDir);

        // 1. Load our graph.json
        var graphData = GraphLoader.Load(config.GraphPath);

        // 2. Build FogMod options
        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.Seed = graphData.Seed;

        // Apply options from graph.json
        foreach (var (key, value) in graphData.Options)
        {
            opt[key] = value;
        }

        // Initialize features first (creates internal structures)
        opt.InitFeatures();

        // Required options for SpeedFog
        opt["crawl"] = true;  // Dungeon crawler mode - enables AllowUnlinked, tier progression
        opt["unconnected"] = true;  // Allow unconnected edges in the graph
        opt["req_backportal"] = true;  // Enable backportals so boss rooms have return warps as edges
        opt["roundtable"] = true;  // Make Roundtable Hold available from the start

        opt["newgraces"] = true;  // Enable additional Sites of Grace
        opt["dlc"] = true;  // Include DLC areas in fog graph

        // Mark dungeon types as "core" so their edges are processed
        // Note: Do NOT use req_all as it includes evergaols which lack StakeAsset definitions
        opt["req_graveyard"] = true;  // Required when req_dungeon + shuffle are both enabled
        opt["req_dungeon"] = true;  // Caves, tunnels, catacombs, graves
        opt["req_cave"] = true;
        opt["req_tunnel"] = true;
        opt["req_catacomb"] = true;
        opt["req_grave"] = true;
        opt["req_forge"] = true;   // DLC forges (Taylew's, Starfall Past, Lava Intake)
        opt["req_gaol"] = true;    // DLC gaols (Belurat, Bonny, Lamenter's)
        opt["req_legacy"] = true;  // Legacy dungeons
        opt["req_major"] = true;   // Major bosses
        opt["req_underground"] = true;  // Underground areas (Siofra, Ainsel, Nokron, etc.)
        opt["req_minorwarp"] = true;  // Minor warps (transporter chests)
        opt["coupledminor"] = true;  // Keep uniqueminor warps as coupled pairs (not unique)
        // Without this, Graph.Construct converts uniqueminor→unique, then crawl mode marks
        // warps with mixed open/neveropen sides as unused (e.g., Redmane Castle sending gates).
        // Explicitly NOT setting req_evergaol - evergaols lack StakeAsset in fog.txt

        // Configure features that depend on crawl mode (normally done by Randomizer)
        opt[Feature.AllowUnlinked] = true;  // Allow edges without connections
        opt[Feature.ForceUnlinked] = true;  // Force unlinked mode
        opt[Feature.SegmentFortresses] = true;  // Treat fortresses as segments

        Console.WriteLine($"Options: seed={opt.Seed}, crawl={opt["crawl"]}, scale={opt["scale"]}, newgraces={opt["newgraces"]}");

        // 3. Load FogMod data files
        var fogPath = Path.Combine(config.DataDir, "fog.txt");
        var fogeventsPath = Path.Combine(config.DataDir, "fogevents.txt");
        var emedfPath = Path.Combine(config.DataDir, "er-common.emedf.json");

        Console.WriteLine($"Loading fog.txt from: {fogPath}");
        var ann = AnnotationData.LoadLiteConfig(fogPath);

        // LoadLiteConfig only loads Areas/Warps/Entrances/DungeonItems (via internal LiteConfig class).
        // CustomBonfires are needed for newgraces - load them separately.
        {
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            using var input = File.OpenText(fogPath);
            var bonfireConfig = deserializer.Deserialize<BonfireConfig>(input);
            ann.CustomBonfires = bonfireConfig?.CustomBonfires;
            if (ann.CustomBonfires != null)
            {
                Console.WriteLine($"Loaded {ann.CustomBonfires.Count} custom bonfires for newgraces");
            }
            else
            {
                Console.WriteLine("Warning: No CustomBonfires found in fog.txt - newgraces will have no effect");
            }
        }

        // Initialize ConfigVars - LoadLiteConfig doesn't load them, but Graph.Construct needs them
        // for condition evaluation. These are FogRando's dungeon crawler mode variables.
        ann.ConfigVars = new Dictionary<string, string>
        {
            // Scaling/logic pass control (not used in SpeedFog)
            { "scalepass", "FALSE" },
            { "logicpass", "TRUE" },
            // Great rune requirements (set to always true for SpeedFog - we give all items)
            { "runes_leyndell", "TRUE" },
            { "runes_rold", "TRUE" },
            { "runes_end", "TRUE" },
            // Dungeon crawler tier variables - all FALSE since we don't use crawl mode
            { "tier1", "FALSE" },
            { "tier2", "FALSE" },
            { "tier3", "FALSE" },
            { "tier4", "FALSE" },
            { "tier5", "FALSE" },
            { "tier6", "FALSE" },
            { "tier7", "FALSE" },
            { "tier8", "FALSE" },
            { "tier9", "FALSE" },
            // DLC kindling/imbued requirements (all TRUE - SpeedFog gives all items)
            { "treekindling", "TRUE" },
            { "imbued_base", "TRUE" },
            { "imbued_base_any", "TRUE" },
            { "imbued_dlc", "TRUE" },
            { "imbued_dlc_any", "TRUE" },
            // DLC high seal conditions (areas reached via seals - all TRUE)
            { "rauhruins_high_seal", "TRUE" },
            { "rauhbase_high_seal", "TRUE" },
            { "gravesite_seal", "TRUE" },
            { "scadualtus_high_seal", "TRUE" },
            { "ymir_open", "TRUE" },
            // Key items - all TRUE since SpeedFog gives all items at start
            // Base game keys
            { "academyglintstonekey", "TRUE" },
            { "carianinvertedstatue", "TRUE" },
            { "cursemarkofdeath", "TRUE" },
            { "darkmoonring", "TRUE" },
            { "dectusmedallionleft", "TRUE" },
            { "dectusmedallionright", "TRUE" },
            { "discardedpalacekey", "TRUE" },
            { "drawingroomkey", "TRUE" },
            { "haligtreesecretmedallionleft", "TRUE" },
            { "haligtreesecretmedallionright", "TRUE" },
            { "imbuedswordkey", "TRUE" },
            { "imbuedswordkey1", "TRUE" },
            { "imbuedswordkey2", "TRUE" },
            { "imbuedswordkey3", "TRUE" },
            { "imbuedswordkey4", "TRUE" },  // DLC key
            { "purebloodknightsmedal", "TRUE" },
            { "roldmedallion", "TRUE" },
            { "runegodrick", "TRUE" },
            { "runemalenia", "TRUE" },
            { "runemohg", "TRUE" },
            { "runemorgott", "TRUE" },
            { "runeradahn", "TRUE" },
            { "runerennala", "TRUE" },
            { "runerykard", "TRUE" },
            { "rustykey", "TRUE" },
            // DLC keys (all TRUE - SpeedFog gives all items at start)
            { "omother", "TRUE" },
            { "welldepthskey", "TRUE" },
            { "gaolupperlevelkey", "TRUE" },
            { "gaollowerlevelkey", "TRUE" },
            { "holeladennecklace", "TRUE" },
            { "messmerskindling", "TRUE" },
            { "messmerskindling1", "TRUE" },
            // Boss defeat conditions used in world edges
            { "farumazula_maliketh", "TRUE" },
        };

        // Load foglocations for enemy area info (needed for scaling)
        var foglocationsPath = Path.Combine(config.DataDir, "foglocations2.txt");
        if (File.Exists(foglocationsPath))
        {
            Console.WriteLine($"Loading foglocations from: {foglocationsPath}");
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            using var input = File.OpenText(foglocationsPath);
            ann.Locations = deserializer.Deserialize<AnnotationData.FogLocations>(input);
        }

        Console.WriteLine($"Loading events from: {emedfPath}");
        var events = new Events(emedfPath, darkScriptMode: true, paramAwareMode: true);

        Console.WriteLine($"Loading event config from: {fogeventsPath}");
        EventConfig eventConfig;
        using (var input = File.OpenText(fogeventsPath))
        {
            var deserializer = new DeserializerBuilder().Build();
            eventConfig = deserializer.Deserialize<EventConfig>(input);
            eventConfig.MakeWarpCommands(events);
        }

        // 4. Build FogMod Graph (unconnected nodes/edges)
        Console.WriteLine("Constructing FogMod graph...");
        var graph = new Graph();
        graph.Construct(opt, ann);

        Console.WriteLine($"Graph constructed: {graph.Nodes.Count} nodes");

        // 4b. Disconnect trivial edges that were pre-connected by Graph.Construct()
        // In crawl mode, FogMod marks entrances with "trivial" tag as IsFixed and connects them.
        // SpeedFog needs these edges available for our custom graph, so we disconnect them.
        var disconnectedCount = 0;
        foreach (var node in graph.Nodes.Values)
        {
            // Find exit edges that are fixed, connected, and from trivial entrances
            var edgesToDisconnect = node.To
                .Where(e => e.IsFixed && e.Link != null && !e.IsWorld && e.Name != null)
                .Where(e => graph.EntranceIds.TryGetValue(e.Name, out var entrance) && entrance.HasTag("trivial"))
                .ToList();

            foreach (var edge in edgesToDisconnect)
            {
                graph.Disconnect(edge);
                disconnectedCount++;
            }
        }
        if (disconnectedCount > 0)
        {
            Console.WriteLine($"Disconnected {disconnectedCount} trivial edges for SpeedFog graph");
        }

        // 4c. Exclude evergaol zones from stake processing
        // In crawl mode, FogRando replaces evergaols with "fake evergaol" connections,
        // but SpeedFog doesn't use evergaols. Without StakeAsset in fog.txt, they cause errors.
        // Setting BossTrigger=0 prevents FogMod from trying to create Stakes for these zones.
        var evergaolCount = 0;
        foreach (var area in ann.Areas.Where(a => a.Name.Contains("evergaol")))
        {
            area.IsExcluded = true;
            area.BossTrigger = 0;
        }
        foreach (var kvp in graph.Areas.Where(a => a.Key.Contains("evergaol")))
        {
            kvp.Value.IsExcluded = true;
            kvp.Value.BossTrigger = 0;
            evergaolCount++;
        }
        Console.WriteLine($"Excluded {evergaolCount} evergaol zones from stake processing");

        // 5. Inject OUR connections (replaces GraphConnector.Connect())
        var injectionResult = ConnectionInjector.InjectAndExtract(
            graph, graphData.Connections, graphData.FinishEvent, graphData.FinalNodeFlag);

        // 6. Apply tiers for scaling
        ConnectionInjector.ApplyAreaTiers(graph, graphData.AreaTiers);

        // 7. Call FogMod writer
        Console.WriteLine($"Writing mod files to: {modDir}");

        // Create MergedMods to merge Item Randomizer output files
        // MergedMods.Resolve() looks in these directories for files to merge
        List<string>? modDirs = null;
        if (!string.IsNullOrEmpty(config.MergeDir))
        {
            Console.WriteLine($"Merging with: {config.MergeDir}");
            modDirs = new List<string> { config.MergeDir };
        }
        var mergedMods = new MergedMods(modDirs, null);

        // 6b. Tag vanilla stakes for removal by FogMod.
        // FogMod reads MSBs from BHD archives (not loose files), so we can't
        // post-process them. Instead, inject RetryPoints with "remove" tag so
        // FogMod removes them during Write() and writes the modified MSB.
        // LoadLiteConfig doesn't load RetryPoints, so ann.RetryPoints is null.
        ann.RetryPoints = StakeRemover.GetRetryPointsToRemove();

        var writer = new GameDataWriterE();
        writer.Write(opt, ann, graph, mergedMods, modDir, events, eventConfig, Console.WriteLine);

        // 7a2. Build region-to-flags mapping for zone tracking.
        // Side.Warp is now populated by Write() (reads from MSB data).
        injectionResult.BuildRegionToFlags(graphData.EventMap);

        // 7a3. Copy non-English FMG files from Item Randomizer output.
        // FogMod only loads and writes msg/engus/ FMGs. When Item Randomizer
        // generates localized content (e.g., class descriptions in French),
        // those files are in the merge-dir but FogMod never writes them out.
        // This must run BEFORE other injectors (e.g., RunCompleteInjector) so
        // they can layer their changes on top of the copied files.
        if (!string.IsNullOrEmpty(config.MergeDir))
        {
            var mergeMsgDir = Path.Combine(config.MergeDir, "msg");
            if (Directory.Exists(mergeMsgDir))
            {
                foreach (var langDir in Directory.GetDirectories(mergeMsgDir))
                {
                    var langName = Path.GetFileName(langDir);
                    if (langName == "engus")
                        continue; // FogMod already merges English FMGs

                    var destDir = Path.Combine(modDir, "msg", langName);
                    Directory.CreateDirectory(destDir);

                    var files = Directory.GetFiles(langDir);
                    foreach (var srcFile in files)
                    {
                        var destFile = Path.Combine(destDir, Path.GetFileName(srcFile));
                        File.Copy(srcFile, destFile, overwrite: true);
                    }
                    Console.WriteLine($"Copied {files.Length} localized FMGs: msg/{langName}/");
                }
            }
        }

        // 7b. Inject starting items (post-process EMEVD)
        // Use StartingGoods (Good IDs) instead of StartingItemLots (ItemLot IDs)
        // because Item Randomizer modifies ItemLotParam but not the items themselves
        if (graphData.StartingGoods.Count > 0 || graphData.CarePackage.Count > 0)
        {
            StartingItemInjector.Inject(modDir, graphData.StartingGoods, graphData.CarePackage, events);
        }

        // 7c. Inject starting resources (runes, golden seeds, sacred tears, larval tears)
        StartingResourcesInjector.Inject(
            modDir,
            events,
            graphData.StartingRunes,
            graphData.StartingGoldenSeeds,
            graphData.StartingSacredTears,
            graphData.StartingLarvalTears,
            graphData.StartingStoneswordKeys
        );

        // 7d. Inject Roundtable unlock (bypasses DLC finger pickup detection)
        RoundtableUnlockInjector.Inject(modDir);

        // 7e. Inject smithing stones into merchant shop
        ShopInjector.Inject(modDir, graphData.SentryTorchShop);

        // 7f. Inject zone tracking events for racing support
        if (graphData.FinishEvent > 0)
        {
            // Use graph.json's FinishBossDefeatFlag (from fog.txt) with priority,
            // falling back to FogMod's Graph extraction. This fixes leyndell_erdtree
            // where the boss zone (erdtree) is reachable via norandom fogs but not
            // directly in the cluster, so FogMod's Graph doesn't have the DefeatFlag.
            int bossDefeatFlag = graphData.FinishBossDefeatFlag > 0
                ? graphData.FinishBossDefeatFlag
                : injectionResult.BossDefeatFlag;

            if (graphData.FinishBossDefeatFlag > 0 && injectionResult.BossDefeatFlag > 0
                && graphData.FinishBossDefeatFlag != injectionResult.BossDefeatFlag)
            {
                Console.WriteLine($"Note: Using graph.json defeat flag {graphData.FinishBossDefeatFlag} " +
                    $"(FogMod Graph had {injectionResult.BossDefeatFlag})");
            }

            // Build expected flags set from connections for Phase 3 validation
            var expectedFlags = graphData.Connections
                .Where(c => c.FlagId > 0)
                .Select(c => c.FlagId)
                .Distinct()
                .ToHashSet();

            ZoneTrackingInjector.Inject(
                modDir,
                events,
                injectionResult.RegionToFlags,
                expectedFlags,
                injectionResult.FinishEvent,
                bossDefeatFlag);
        }

        // 7f2. Patch Erdtree warp to target Ashen Leyndell (m11_05) directly.
        // FogMod's fogwarp template compiles an alt-warp: primary → m11_00,
        // alt (flag 300) → m11_05. We replace the primary with m11_05 so
        // the Erdtree is reachable without Maliketh defeat / flag 300.
        var erdtreeEntrance = ann.Entrances.Concat(ann.Warps).FirstOrDefault(e =>
            e.BSide?.Area == "leyndell_erdtree" &&
            e.BSide?.AlternateSide?.Warp != null &&
            e.BSide.AlternateFlag > 0);

        if (erdtreeEntrance != null)
        {
            ErdtreeWarpPatcher.Patch(
                modDir,
                erdtreeEntrance.BSide.Warp.Region,
                erdtreeEntrance.BSide.AlternateSide.Warp.Region,
                erdtreeEntrance.BSide.AlternateSide.Warp.Map);
        }

        // 7f3. Patch Sealing Tree fogwarps to eliminate flag 330 dependency.
        // FogMod's fogwarp template compiles an alt-warp: primary → m61_44_45_00,
        // alt (flag 330) → m61_44_45_10. Something outside EMEVD sets flag 330 on
        // saves with prior DLC progress, warping to the wrong map variant (no Romina).
        // We replace alt destinations with primary destinations in all compiled events.
        var sealingTreeEntrances = ann.Entrances.Concat(ann.Warps)
            .SelectMany(e => e.Sides())
            .Where(s => s.AlternateFlag == 330 && s.AlternateSide?.Warp != null && s.Warp != null)
            .Select(s => (
                altRegion: s.AlternateSide.Warp.Region,
                primaryRegion: s.Warp.Region,
                primaryMap: s.Warp.Map
            ))
            .ToList();

        if (sealingTreeEntrances.Count > 0)
        {
            SealingTreeWarpPatcher.Patch(modDir, sealingTreeEntrances);
        }

        // 7g. Inject "RUN COMPLETE" banner on final boss defeat
        RunCompleteInjector.Inject(modDir, config.GameDir, events, graphData.FinishEvent, graphData.RunCompleteMessage);

        // 7h. Inject Site of Grace at Chapel of Anticipation and relocate player spawn
        if (graphData.ChapelGrace)
        {
            ChapelGraceInjector.Inject(modDir, config.GameDir, events);
        }

        // 7h2. Death markers at fog gates
        DeathMarkerInjector.Inject(
            modDir, config.GameDir, graphData.Connections, events,
            graphData.EventMap, graphData.DeathFlags);

        // 7i. Inject rebirth option at Sites of Grace
        if (graphData.StartingLarvalTears > 0)
        {
            RebirthInjector.Inject(modDir, config.GameDir);
        }

        // 7j2. Neutralize vanilla Sealing Tree events to prevent flag 330 contamination.
        // Event 915 sets flag 330 (Sealing Tree burned) when flag 9140 (Dancing Lion
        // defeated) is ON. On saves with prior DLC progress, this fires immediately and
        // causes fogwarps to Romina's area to use the wrong map variant (m61_44_45_10).
        SealingTreePatcher.Patch(modDir);

        // 7j3. Set startup flags (open gates, etc.)
        StartupFlagInjector.Inject(modDir, new[]
        {
            ("m35_00_00_00", 35000565, true),  // Sewer barred gate 1 (AEG023_330_1000, lever AEG027_002_0503)
            ("m35_00_00_00", 35000566, true),  // Sewer barred gate 2 (AEG023_330_1001, lever AEG027_002_0507)
        });

        // 7k. Remove vanilla assets that conflict with fog gates.
        if (graphData.RemoveEntities.Count > 0)
        {
            VanillaWarpRemover.Remove(modDir, graphData.RemoveEntities);
        }

        // 7l. Vanilla stake removal is handled pre-Write via ann.RetryPoints
        // (step 6b) — FogMod reads MSBs from BHD archives and removes tagged stakes.

        // 8. Package with ModEngine 2
        var packager = new PackagingWriter(config.OutputDir);
        await packager.WritePackageAsync(config.MergeDir);
    }

    class Config
    {
        public string SeedDir { get; set; } = "";
        public string GraphPath => Path.Combine(SeedDir, "graph.json");
        public string GameDir { get; set; } = "";
        public string DataDir { get; set; } = "";
        public string OutputDir { get; set; } = "";
        public string? MergeDir { get; set; }
    }

    /// <summary>
    /// Minimal YAML wrapper to load CustomBonfires from fog.txt.
    /// LoadLiteConfig uses an internal LiteConfig class that omits CustomBonfires,
    /// so we load them separately with this focused class.
    /// </summary>
    class BonfireConfig
    {
        public List<AnnotationData.CustomBonfire>? CustomBonfires { get; set; }
    }
}
