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

        // --- Prepare warp patcher data ---

        // Erdtree warp: patch fogwarps targeting leyndell_erdtree (m11_00) to
        // warp directly to leyndell2_erdtree (m11_05).
        var erdtreeEntrance = ann.Entrances.Concat(ann.Warps).FirstOrDefault(e =>
            e.BSide?.Area == "leyndell_erdtree" &&
            e.BSide?.AlternateSide?.Warp != null &&
            e.BSide.AlternateFlag > 0);

        int erdtreePrimaryRegion = 0, erdtreeAltRegion = 0;
        byte[]? erdtreeAltMapBytes = null;
        int erdtreeAltMapPacked = 0;
        if (erdtreeEntrance != null &&
            erdtreeEntrance.BSide.Warp.Region != 0 &&
            erdtreeEntrance.BSide.AlternateSide.Warp.Region != 0)
        {
            erdtreePrimaryRegion = erdtreeEntrance.BSide.Warp.Region;
            erdtreeAltRegion = erdtreeEntrance.BSide.AlternateSide.Warp.Region;
            var altMap = erdtreeEntrance.BSide.AlternateSide.Warp.Map;
            erdtreeAltMapBytes = ErdtreeWarpPatcher.ParseMapBytes(altMap);
            erdtreeAltMapPacked = ErdtreeWarpPatcher.PackMapId(altMap);
        }

        // Sealing Tree warp: replace alt destinations (flag 330) with primary destinations.
        var sealingTreeEntrances = ann.Entrances.Concat(ann.Warps)
            .SelectMany(e => e.Sides())
            .Where(s => s.AlternateFlag == 330 && s.AlternateSide?.Warp != null && s.Warp != null)
            .Select(s => (
                altRegion: s.AlternateSide.Warp.Region,
                primaryRegion: s.Warp.Region,
                primaryMap: s.Warp.Map
            ))
            .ToList();

        var sealingTreeTargets = sealingTreeEntrances
            .Where(e => e.altRegion != 0 && e.primaryRegion != 0)
            .Select(e => (
                e.altRegion,
                e.primaryRegion,
                primaryMapBytes: ErdtreeWarpPatcher.ParseMapBytes(e.primaryMap),
                primaryMapPacked: ErdtreeWarpPatcher.PackMapId(e.primaryMap)
            ))
            .ToList();

        // Boss trigger: build region-to-TrapFlag mapping for warp patching.
        var regionToTrapFlag = BossTriggerInjector.BuildRegionToTrapFlag(injectionResult, graph.Areas);
        Console.WriteLine($"Boss trigger: {regionToTrapFlag.Count} boss arena warp region(s) mapped");

        // Zone tracking: prepare region-to-flags mapping and expected flags.
        bool doZoneTracking = graphData.FinishEvent > 0;
        int bossDefeatFlag = 0;
        HashSet<int>? expectedFlags = null;

        if (doZoneTracking)
        {
            // Use graph.json's FinishBossDefeatFlag (from fog.txt) with priority,
            // falling back to FogMod's Graph extraction. This fixes leyndell_erdtree
            // where the boss zone (erdtree) is reachable via norandom fogs but not
            // directly in the cluster, so FogMod's Graph doesn't have the DefeatFlag.
            bossDefeatFlag = graphData.FinishBossDefeatFlag > 0
                ? graphData.FinishBossDefeatFlag
                : injectionResult.BossDefeatFlag;

            if (graphData.FinishBossDefeatFlag > 0 && injectionResult.BossDefeatFlag > 0
                && graphData.FinishBossDefeatFlag != injectionResult.BossDefeatFlag)
            {
                Console.WriteLine($"Note: Using graph.json defeat flag {graphData.FinishBossDefeatFlag} " +
                    $"(FogMod Graph had {injectionResult.BossDefeatFlag})");
            }

            expectedFlags = graphData.Connections
                .Where(c => c.FlagId > 0)
                .Select(c => c.FlagId)
                .Distinct()
                .ToHashSet();

            Console.WriteLine($"Zone tracking: region lookup with {injectionResult.RegionToFlags.Count} regions, " +
                $"{injectionResult.RegionToFlags.Values.Sum(f => f.Count)} flag entries");
        }

        // --- Single pass over all EMEVD files (zone tracking + warp patches) ---
        // Loads each file once, applies all applicable patches, writes once if modified.
        // common.emevd.dcx is handled separately below (also receives 6 injectors).

        var eventDir = Path.Combine(modDir, "event");
        var commonEmevdPath = Path.Combine(eventDir, "common.emevd.dcx");
        var commonEmevd = EMEVD.Read(commonEmevdPath);

        var injectedFlags = new HashSet<int>();
        int totalZoneTrackingInjected = 0;
        int totalBossTriggerInjected = 0;
        int totalErdtreePatched = 0;
        int totalSealingTreePatched = 0;

        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            // Skip common.emevd.dcx: handled in-memory below
            if (Path.GetFullPath(file) == Path.GetFullPath(commonEmevdPath))
                continue;

            var emevd = EMEVD.Read(file);
            bool modified = false;

            if (doZoneTracking)
            {
                int n = ZoneTrackingInjector.PatchEmevdFile(
                    emevd, events, injectionResult.RegionToFlags, injectedFlags);
                if (n > 0)
                {
                    totalZoneTrackingInjected += n;
                    modified = true;
                }
            }

            if (regionToTrapFlag.Count > 0)
            {
                int n = BossTriggerInjector.PatchEmevdFile(emevd, events, regionToTrapFlag);
                if (n > 0)
                {
                    totalBossTriggerInjected += n;
                    modified = true;
                }
            }

            if (erdtreeAltMapBytes != null)
            {
                int n = ErdtreeWarpPatcher.PatchEmevd(
                    emevd, erdtreePrimaryRegion, erdtreeAltRegion, erdtreeAltMapBytes, erdtreeAltMapPacked);
                if (n > 0)
                {
                    Console.WriteLine($"  {Path.GetFileName(file)}: patched {n} erdtree warp(s)");
                    totalErdtreePatched += n;
                    modified = true;
                }
            }

            if (sealingTreeTargets.Count > 0)
            {
                int n = SealingTreeWarpPatcher.PatchEmevd(emevd, sealingTreeTargets);
                if (n > 0)
                {
                    Console.WriteLine($"  {Path.GetFileName(file)}: patched {n} sealing tree warp(s)");
                    totalSealingTreePatched += n;
                    modified = true;
                }
            }

            // Suppress "Somewhere, a heavy door has opened" popup in common_func
            if (Path.GetFileName(file).Equals("common_func.emevd.dcx", StringComparison.OrdinalIgnoreCase))
            {
                if (HeavyDoorMessagePatcher.Patch(emevd) > 0)
                    modified = true;
            }

            if (modified)
                emevd.Write(file);
        }

        // Also apply warp patches to common.emevd (already in memory)
        if (doZoneTracking)
        {
            totalZoneTrackingInjected += ZoneTrackingInjector.PatchEmevdFile(
                commonEmevd, events, injectionResult.RegionToFlags, injectedFlags);
        }
        if (regionToTrapFlag.Count > 0)
        {
            totalBossTriggerInjected += BossTriggerInjector.PatchEmevdFile(
                commonEmevd, events, regionToTrapFlag);
        }
        if (erdtreeAltMapBytes != null)
        {
            int n = ErdtreeWarpPatcher.PatchEmevd(
                commonEmevd, erdtreePrimaryRegion, erdtreeAltRegion, erdtreeAltMapBytes, erdtreeAltMapPacked);
            if (n > 0)
            {
                Console.WriteLine($"  common.emevd.dcx: patched {n} erdtree warp(s)");
                totalErdtreePatched += n;
            }
        }
        if (sealingTreeTargets.Count > 0)
        {
            int n = SealingTreeWarpPatcher.PatchEmevd(commonEmevd, sealingTreeTargets);
            if (n > 0)
            {
                Console.WriteLine($"  common.emevd.dcx: patched {n} sealing tree warp(s)");
                totalSealingTreePatched += n;
            }
        }

        // Log scan results
        if (regionToTrapFlag.Count > 0)
        {
            Console.WriteLine($"Boss trigger: injected {totalBossTriggerInjected} SetEventFlag(TrapFlag) " +
                $"before warp instructions ({regionToTrapFlag.Count} boss arena regions)");
        }
        if (doZoneTracking)
        {
            ZoneTrackingInjector.ValidateInjectedFlags(injectedFlags, expectedFlags!, totalZoneTrackingInjected);
        }
        if (erdtreeAltMapBytes != null)
        {
            if (totalErdtreePatched > 0)
                Console.WriteLine($"Erdtree warp fix: patched {totalErdtreePatched} warp(s) " +
                    $"(region {erdtreePrimaryRegion} -> {erdtreeAltRegion})");
            else
                Console.WriteLine($"Erdtree warp fix: no matching warps found " +
                    $"(region {erdtreePrimaryRegion} may not be connected)");
        }
        if (sealingTreeTargets.Count > 0)
        {
            if (totalSealingTreePatched > 0)
                Console.WriteLine($"Sealing Tree warp fix: patched {totalSealingTreePatched} warp(s) across {sealingTreeTargets.Count} entrance(s)");
            else
                Console.WriteLine("Sealing Tree warp fix: no matching warps found (entrances may not be connected)");
        }

        // --- Apply all common.emevd injectors (single Read, single Write) ---

        // 7b. Starting items
        if (graphData.StartingGoods.Count > 0 || graphData.CarePackage.Count > 0)
        {
            StartingItemInjector.Inject(commonEmevd, graphData.StartingGoods, graphData.CarePackage, events);
        }

        // 7c. Starting resources (consumables via EMEVD)
        StartingResourcesInjector.Inject(
            commonEmevd,
            events,
            graphData.StartingGoldenSeeds,
            graphData.StartingSacredTears,
            graphData.StartingLarvalTears,
            graphData.StartingStoneswordKeys
        );

        // 7d. Roundtable unlock
        RoundtableUnlockInjector.Inject(commonEmevd);

        // 7f. Boss death monitor for zone tracking
        if (doZoneTracking && bossDefeatFlag > 0)
        {
            ZoneTrackingInjector.InjectBossDeathEvent(
                commonEmevd, events, injectionResult.FinishEvent, bossDefeatFlag);
        }

        // 7g. "RUN COMPLETE" banner event
        if (graphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectEmevdEvent(commonEmevd, events, graphData.FinishEvent);
        }

        // 7j2. Neutralize vanilla events that set AlternateFlag values (flags 300, 330).
        AlternateFlagPatcher.Patch(commonEmevd);

        // Write common.emevd.dcx once (was previously read/written 6+ times)
        commonEmevd.Write(commonEmevdPath);

        // --- Non-EMEVD injectors and per-map injectors ---

        // 7e. Consolidated regulation.bin modifications begin here.
        // Additional injectors will migrate into this block in subsequent commits.
        var reg = RegulationEditor.Open(modDir);
        if (reg != null)
        {
            ShopInjector.ApplyTo(reg, graphData.SentryTorchShop);
            WeaponUpgradeInjector.ApplyTo(reg, graphData.WeaponUpgrade);
            StartingRuneInjector.ApplyTo(reg, graphData.StartingRunes);
            reg.Save();
        }

        // 7g-fmg. "RUN COMPLETE" banner FMG entries (all languages)
        if (graphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectFmgEntries(modDir, config.GameDir, graphData.RunCompleteMessage);
        }

        // 7h. Site of Grace at Chapel of Anticipation
        if (graphData.ChapelGrace)
        {
            ChapelGraceInjector.Inject(modDir, config.GameDir, events);
        }

        // 7h2. Death markers at fog gates
        var gateSides = BuildGateSideLookup(ann);
        DeathMarkerInjector.Inject(
            modDir, config.GameDir, graphData.Connections, events,
            graphData.EventMap, graphData.DeathFlags, gateSides);

        // 7i. Rebirth option at Sites of Grace
        if (graphData.StartingLarvalTears > 0)
        {
            RebirthInjector.Inject(modDir, config.GameDir);
        }

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
        // (step 6b). FogMod reads MSBs from BHD archives and removes tagged stakes.

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
    /// Build a lookup mapping gate FullName to (ASideArea, BSideArea) from fog.txt entrances.
    /// FullName = "{entrance.Area}_{entrance.Name}" (e.g., "m10_00_00_00_AEG099_002_9000").
    /// ASide = the zone in the gate's facing direction; BSide = the opposite zone.
    /// Used by DeathMarkerInjector to place bloodstains on the correct side of fog gates.
    /// </summary>
    static Dictionary<string, (string ASideArea, string BSideArea)> BuildGateSideLookup(
        AnnotationData ann)
    {
        var lookup = new Dictionary<string, (string, string)>();

        foreach (var e in ann.Entrances.Concat(ann.Warps))
        {
            if (e.ASide?.Area == null || e.BSide?.Area == null)
                continue;

            // LoadLiteConfig does not set FullName (it's [YamlIgnore]),
            // so we construct it: "{map_area}_{gate_name}"
            var fullName = $"{e.Area}_{e.Name}";
            lookup[fullName] = (e.ASide.Area, e.BSide.Area);
        }

        Console.WriteLine($"Gate side lookup: {lookup.Count} entries from fog.txt");
        return lookup;
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
