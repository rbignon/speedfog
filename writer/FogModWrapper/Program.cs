using FogMod;
using FogModWrapper;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;
using YamlDotNet.Serialization;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            var config = ParseArgs(args);
            Run(config);
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

    static void Run(Config config)
    {
        PrintBanner(config);

        var ctx = new Context(config);
        Directory.CreateDirectory(ctx.ModDir);

        LoadInputs(ctx);
        ConstructGraph(ctx);
        InjectConnections(ctx);
        WriteFogMod(ctx);
        PatchEmevd(ctx);
        ApplyCommonInjectors(ctx);
        ApplyRegulation(ctx);
        ApplyModDirInjectors(ctx);

        // Final ME3 packaging is handled by the Python speedfog pipeline.
    }

    static void PrintBanner(Config config)
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
    }

    // ====================================================================
    // Phase 1: Load all input files (graph.json + fog.txt + events + ...)
    // ====================================================================
    static void LoadInputs(Context ctx)
    {
        ctx.GraphData = GraphLoader.Load(ctx.Config.GraphPath);

        var fogPath = Path.Combine(ctx.Config.DataDir, "fog.txt");
        var fogeventsPath = Path.Combine(ctx.Config.DataDir, "fogevents.txt");
        var emedfPath = Path.Combine(ctx.Config.DataDir, "er-common.emedf.json");
        var foglocationsPath = Path.Combine(ctx.Config.DataDir, "foglocations2.txt");
        var phantomCatalogPath = Path.Combine(ctx.Config.DataDir, "phantom_skins.toml");
        var zoneMetadataPath = Path.Combine(ctx.Config.DataDir, "zone_metadata.toml");

        Console.WriteLine($"Loading fog.txt from: {fogPath}");
        ctx.Ann = AnnotationData.LoadLiteConfig(fogPath);

        // LoadLiteConfig only loads Areas/Warps/Entrances/DungeonItems (via internal LiteConfig class).
        // CustomBonfires are needed for newgraces - load them separately.
        LoadCustomBonfires(ctx.Ann, fogPath);

        // Initialize ConfigVars - LoadLiteConfig doesn't load them, but Graph.Construct needs them
        // for condition evaluation. These are FogRando's dungeon crawler mode variables.
        ctx.Ann.ConfigVars = BuildConfigVars();

        // Load foglocations for enemy area info (needed for scaling).
        LoadFoglocations(ctx.Ann, foglocationsPath);

        Console.WriteLine($"Loading events from: {emedfPath}");
        ctx.Events = new Events(emedfPath, darkScriptMode: true, paramAwareMode: true);

        Console.WriteLine($"Loading event config from: {fogeventsPath}");
        using (var input = File.OpenText(fogeventsPath))
        {
            var deserializer = new DeserializerBuilder().Build();
            ctx.EventConfig = deserializer.Deserialize<EventConfig>(input);
            ctx.EventConfig.MakeWarpCommands(ctx.Events);
        }

        // Phantom skins catalog (optional; absent file = no-op).
        ctx.PhantomSkins = PhantomCatalogLoader.Load(phantomCatalogPath);

        // Opensplit overrides (zone_metadata.toml -> entrance tags).
        // Must run before Graph.Construct: FogMod's IsCore + opensplit logic
        // (Graph.cs:1167-1272) consumes these tags during graph construction.
        var openSplitIds = OpenSplitOverrideLoader.Load(zoneMetadataPath);
        OpenSplitInjector.Apply(ctx.Ann, openSplitIds);
    }

    static void LoadCustomBonfires(AnnotationData ann, string fogPath)
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

    static void LoadFoglocations(AnnotationData ann, string path)
    {
        if (!File.Exists(path))
            return;
        Console.WriteLine($"Loading foglocations from: {path}");
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        using var input = File.OpenText(path);
        ann.Locations = deserializer.Deserialize<AnnotationData.FogLocations>(input);
    }

    // ====================================================================
    // Phase 2: Build FogMod RandomizerOptions and construct the Graph.
    // ====================================================================
    static void ConstructGraph(Context ctx)
    {
        ctx.Opt = BuildRandomizerOptions(ctx.GraphData.Seed, ctx.GraphData.Options);

        Console.WriteLine($"Options: seed={ctx.Opt.Seed}, crawl={ctx.Opt["crawl"]}, scale={ctx.Opt["scale"]}, newgraces={ctx.Opt["newgraces"]}");

        Console.WriteLine("Constructing FogMod graph...");
        ctx.Graph = new Graph();
        ctx.Graph.Construct(ctx.Opt, ctx.Ann);
        Console.WriteLine($"Graph constructed: {ctx.Graph.Nodes.Count} nodes");

        DisconnectTrivialEdges(ctx.Graph);
        ExcludeEvergaolZones(ctx.Ann, ctx.Graph);
    }

    static RandomizerOptions BuildRandomizerOptions(int seed, Dictionary<string, bool> graphOptions)
    {
        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.Seed = seed;

        // Apply options from graph.json
        foreach (var (key, value) in graphOptions)
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
        opt["req_rauhruins"] = true;  // Promote rauhruins-tagged sides to core in crawl mode (Graph.cs:1177)
        opt["coupledminor"] = true;  // Keep uniqueminor warps as coupled pairs (not unique)
        // Without this, Graph.Construct converts uniqueminor→unique, then crawl mode marks
        // warps with mixed open/neveropen sides as unused (e.g., Redmane Castle sending gates).
        // Explicitly NOT setting req_evergaol - evergaols lack StakeAsset in fog.txt

        // Configure features that depend on crawl mode (normally done by Randomizer)
        opt[Feature.AllowUnlinked] = true;  // Allow edges without connections
        opt[Feature.ForceUnlinked] = true;  // Force unlinked mode
        opt[Feature.SegmentFortresses] = true;  // Treat fortresses as segments

        return opt;
    }

    static Dictionary<string, string> BuildConfigVars()
    {
        return new Dictionary<string, string>
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
    }

    // In crawl mode, FogMod marks entrances with "trivial" tag as IsFixed and connects them.
    // SpeedFog needs these edges available for our custom graph, so we disconnect them.
    static void DisconnectTrivialEdges(Graph graph)
    {
        var disconnectedCount = 0;
        foreach (var node in graph.Nodes.Values)
        {
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
    }

    // In crawl mode, FogRando replaces evergaols with "fake evergaol" connections,
    // but SpeedFog doesn't use evergaols. Without StakeAsset in fog.txt, they cause errors.
    // Setting BossTrigger=0 prevents FogMod from trying to create Stakes for these zones.
    static void ExcludeEvergaolZones(AnnotationData ann, Graph graph)
    {
        foreach (var area in ann.Areas.Where(a => a.Name.Contains("evergaol")))
        {
            area.IsExcluded = true;
            area.BossTrigger = 0;
        }
        var evergaolCount = 0;
        foreach (var kvp in graph.Areas.Where(a => a.Key.Contains("evergaol")))
        {
            kvp.Value.IsExcluded = true;
            kvp.Value.BossTrigger = 0;
            evergaolCount++;
        }
        Console.WriteLine($"Excluded {evergaolCount} evergaol zones from stake processing");
    }

    // ====================================================================
    // Phase 3: Inject our connections and area tiers into FogMod's Graph.
    // ====================================================================
    static void InjectConnections(Context ctx)
    {
        ctx.InjectionResult = ConnectionInjector.InjectAndExtract(
            ctx.Graph, ctx.GraphData.Connections, ctx.GraphData.FinishEvent, ctx.GraphData.FinalNodeFlag);

        ConnectionInjector.ApplyAreaTiers(ctx.Graph, ctx.GraphData.AreaTiers);
    }

    // ====================================================================
    // Phase 4: Call FogMod's writer to emit mod files, then copy localized
    // FMGs from the merge directory (FogMod only writes msg/engus/).
    // ====================================================================
    static void WriteFogMod(Context ctx)
    {
        Console.WriteLine($"Writing mod files to: {ctx.ModDir}");

        // Create MergedMods to merge Item Randomizer output files.
        // MergedMods.Resolve() looks in these directories for files to merge.
        List<string>? modDirs = null;
        if (!string.IsNullOrEmpty(ctx.Config.MergeDir))
        {
            Console.WriteLine($"Merging with: {ctx.Config.MergeDir}");
            modDirs = new List<string> { ctx.Config.MergeDir };
        }
        var mergedMods = new MergedMods(modDirs, null);

        // Tag vanilla stakes for removal by FogMod.
        // FogMod reads MSBs from BHD archives (not loose files), so we can't
        // post-process them. Instead, inject RetryPoints with "remove" tag so
        // FogMod removes them during Write() and writes the modified MSB.
        // LoadLiteConfig doesn't load RetryPoints, so ann.RetryPoints is null.
        ctx.Ann.RetryPoints = StakeRemover.GetRetryPointsToRemove();

        var writer = new GameDataWriterE();
        writer.Write(ctx.Opt, ctx.Ann, ctx.Graph, mergedMods, ctx.ModDir, ctx.Events, ctx.EventConfig, Console.WriteLine);

        // Side.Warp is now populated by Write() (reads from MSB data).
        ctx.InjectionResult.BuildRegionToFlags(ctx.GraphData.EventMap);

        // Copy non-English FMG files from Item Randomizer output.
        // FogMod only loads and writes msg/engus/ FMGs. When Item Randomizer
        // generates localized content (e.g., class descriptions in French),
        // those files are in the merge-dir but FogMod never writes them out.
        // This must run BEFORE other injectors (e.g., RunCompleteInjector) so
        // they can layer their changes on top of the copied files.
        if (!string.IsNullOrEmpty(ctx.Config.MergeDir))
        {
            CopyLocalizedFmgs(ctx.Config.MergeDir, ctx.ModDir);
        }
    }

    static void CopyLocalizedFmgs(string mergeDir, string modDir)
    {
        var mergeMsgDir = Path.Combine(mergeDir, "msg");
        if (!Directory.Exists(mergeMsgDir))
            return;

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

    // ====================================================================
    // Phase 5: Single-pass EMEVD patches.
    // Each per-map file is loaded once, all applicable patches run, then
    // written once if modified. common.emevd.dcx receives the same warp
    // patches in-memory and is written later (after ApplyCommonInjectors).
    // ====================================================================
    static void PatchEmevd(Context ctx)
    {
        var erdtree = BuildErdtreeWarpData(ctx.Ann);
        var sealingTreeTargets = BuildSealingTreeTargets(ctx.Ann);
        var regionToTrapFlag = BossTriggerInjector.BuildRegionToTrapFlag(ctx.InjectionResult, ctx.Graph.Areas);
        Console.WriteLine($"Boss trigger: {regionToTrapFlag.Count} boss arena warp region(s) mapped");

        bool doZoneTracking = ctx.GraphData.FinishEvent > 0;
        HashSet<int>? expectedFlags = null;

        if (doZoneTracking)
        {
            // Use graph.json's FinishBossDefeatFlag (from fog.txt) with priority,
            // falling back to FogMod's Graph extraction. This fixes leyndell_erdtree
            // where the boss zone (erdtree) is reachable via norandom fogs but not
            // directly in the cluster, so FogMod's Graph doesn't have the DefeatFlag.
            ctx.BossDefeatFlag = ctx.GraphData.FinishBossDefeatFlag > 0
                ? ctx.GraphData.FinishBossDefeatFlag
                : ctx.InjectionResult.BossDefeatFlag;

            if (ctx.GraphData.FinishBossDefeatFlag > 0 && ctx.InjectionResult.BossDefeatFlag > 0
                && ctx.GraphData.FinishBossDefeatFlag != ctx.InjectionResult.BossDefeatFlag)
            {
                Console.WriteLine($"Note: Using graph.json defeat flag {ctx.GraphData.FinishBossDefeatFlag} " +
                    $"(FogMod Graph had {ctx.InjectionResult.BossDefeatFlag})");
            }

            expectedFlags = ctx.GraphData.Connections
                .Where(c => c.FlagId > 0)
                .Select(c => c.FlagId)
                .Distinct()
                .ToHashSet();

            Console.WriteLine($"Zone tracking: region lookup with {ctx.InjectionResult.RegionToFlags.Count} regions, " +
                $"{ctx.InjectionResult.RegionToFlags.Values.Sum(f => f.Count)} flag entries");
        }

        ctx.CommonEmevd = EMEVD.Read(ctx.CommonEmevdPath);

        var injectedFlags = new HashSet<int>();
        int totalZoneTrackingInjected = 0;
        int totalBossTriggerInjected = 0;
        int totalErdtreePatched = 0;
        int totalSealingTreePatched = 0;

        foreach (var file in Directory.GetFiles(ctx.EventDir, "*.emevd.dcx"))
        {
            // Skip common.emevd.dcx: handled in-memory below
            if (Path.GetFullPath(file) == Path.GetFullPath(ctx.CommonEmevdPath))
                continue;

            var emevd = EMEVD.Read(file);
            bool modified = false;

            if (doZoneTracking)
            {
                int n = ZoneTrackingInjector.PatchEmevdFile(
                    emevd, ctx.Events, ctx.InjectionResult.RegionToFlags, injectedFlags);
                if (n > 0)
                {
                    totalZoneTrackingInjected += n;
                    modified = true;
                }
            }

            if (regionToTrapFlag.Count > 0)
            {
                int n = BossTriggerInjector.PatchEmevdFile(emevd, ctx.Events, regionToTrapFlag);
                if (n > 0)
                {
                    totalBossTriggerInjected += n;
                    modified = true;
                }
            }

            if (erdtree != null)
            {
                int n = ErdtreeWarpPatcher.PatchEmevd(
                    emevd, erdtree.PrimaryRegion, erdtree.AltRegion, erdtree.AltMapBytes, erdtree.AltMapPacked);
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

        // Apply the same warp patches to common.emevd (in memory).
        if (doZoneTracking)
        {
            totalZoneTrackingInjected += ZoneTrackingInjector.PatchEmevdFile(
                ctx.CommonEmevd, ctx.Events, ctx.InjectionResult.RegionToFlags, injectedFlags);
        }
        if (regionToTrapFlag.Count > 0)
        {
            totalBossTriggerInjected += BossTriggerInjector.PatchEmevdFile(
                ctx.CommonEmevd, ctx.Events, regionToTrapFlag);
        }
        if (erdtree != null)
        {
            int n = ErdtreeWarpPatcher.PatchEmevd(
                ctx.CommonEmevd, erdtree.PrimaryRegion, erdtree.AltRegion, erdtree.AltMapBytes, erdtree.AltMapPacked);
            if (n > 0)
            {
                Console.WriteLine($"  common.emevd.dcx: patched {n} erdtree warp(s)");
                totalErdtreePatched += n;
            }
        }
        if (sealingTreeTargets.Count > 0)
        {
            int n = SealingTreeWarpPatcher.PatchEmevd(ctx.CommonEmevd, sealingTreeTargets);
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
        if (erdtree != null)
        {
            if (totalErdtreePatched > 0)
                Console.WriteLine($"Erdtree warp fix: patched {totalErdtreePatched} warp(s) " +
                    $"(region {erdtree.PrimaryRegion} -> {erdtree.AltRegion})");
            else
                Console.WriteLine($"Erdtree warp fix: no matching warps found " +
                    $"(region {erdtree.PrimaryRegion} may not be connected)");
        }
        if (sealingTreeTargets.Count > 0)
        {
            if (totalSealingTreePatched > 0)
                Console.WriteLine($"Sealing Tree warp fix: patched {totalSealingTreePatched} warp(s) across {sealingTreeTargets.Count} entrance(s)");
            else
                Console.WriteLine("Sealing Tree warp fix: no matching warps found (entrances may not be connected)");
        }
    }

    // Erdtree warp: patch fogwarps targeting leyndell_erdtree (m11_00) to
    // warp directly to leyndell2_erdtree (m11_05).
    static ErdtreeWarpData? BuildErdtreeWarpData(AnnotationData ann)
    {
        var entrance = ann.Entrances.Concat(ann.Warps).FirstOrDefault(e =>
            e.BSide?.Area == "leyndell_erdtree" &&
            e.BSide?.AlternateSide?.Warp != null &&
            e.BSide.AlternateFlag > 0);

        if (entrance == null ||
            entrance.BSide.Warp.Region == 0 ||
            entrance.BSide.AlternateSide.Warp.Region == 0)
        {
            return null;
        }

        var altMap = entrance.BSide.AlternateSide.Warp.Map;
        return new ErdtreeWarpData
        {
            PrimaryRegion = entrance.BSide.Warp.Region,
            AltRegion = entrance.BSide.AlternateSide.Warp.Region,
            AltMapBytes = ErdtreeWarpPatcher.ParseMapBytes(altMap),
            AltMapPacked = ErdtreeWarpPatcher.PackMapId(altMap),
        };
    }

    // Sealing Tree warp: replace alt destinations (flag 330) with primary destinations.
    static List<(int altRegion, int primaryRegion, byte[] primaryMapBytes, int primaryMapPacked)>
        BuildSealingTreeTargets(AnnotationData ann)
    {
        return ann.Entrances.Concat(ann.Warps)
            .SelectMany(e => e.Sides())
            .Where(s => s.AlternateFlag == 330 && s.AlternateSide?.Warp != null && s.Warp != null)
            .Select(s => (
                altRegion: s.AlternateSide.Warp.Region,
                primaryRegion: s.Warp.Region,
                primaryMap: s.Warp.Map
            ))
            .Where(e => e.altRegion != 0 && e.primaryRegion != 0)
            .Select(e => (
                e.altRegion,
                e.primaryRegion,
                primaryMapBytes: ErdtreeWarpPatcher.ParseMapBytes(e.primaryMap),
                primaryMapPacked: ErdtreeWarpPatcher.PackMapId(e.primaryMap)
            ))
            .ToList();
    }

    // ====================================================================
    // Phase 6: All common.emevd injectors (single Read in PatchEmevd,
    // single Write at the end of this phase).
    // ====================================================================
    static void ApplyCommonInjectors(Context ctx)
    {
        // Starting items
        if (ctx.GraphData.StartingGoods.Count > 0 || ctx.GraphData.CarePackage.Count > 0)
        {
            StartingItemInjector.Inject(ctx.CommonEmevd, ctx.GraphData.StartingGoods, ctx.GraphData.CarePackage, ctx.Events);
        }

        // Starting resources (consumables via EMEVD)
        StartingResourcesInjector.Inject(
            ctx.CommonEmevd,
            ctx.Events,
            ctx.GraphData.StartingGoldenSeeds,
            ctx.GraphData.StartingSacredTears,
            ctx.GraphData.StartingLarvalTears,
            ctx.GraphData.StartingStoneswordKeys
        );

        // Roundtable unlock
        RoundtableUnlockInjector.Inject(ctx.CommonEmevd);

        // Boss death monitor for zone tracking (BossDefeatFlag resolved in PatchEmevd).
        if (ctx.GraphData.FinishEvent > 0 && ctx.BossDefeatFlag > 0)
        {
            ZoneTrackingInjector.InjectBossDeathEvent(
                ctx.CommonEmevd, ctx.Events, ctx.InjectionResult.FinishEvent, ctx.BossDefeatFlag);
        }

        // "RUN COMPLETE" banner event
        if (ctx.GraphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectEmevdEvent(ctx.CommonEmevd, ctx.Events, ctx.GraphData.FinishEvent);
        }

        // Neutralize vanilla events that set AlternateFlag values (flags 300, 330).
        AlternateFlagPatcher.Patch(ctx.CommonEmevd);

        // Write common.emevd.dcx once (was previously read/written 6+ times).
        ctx.CommonEmevd.Write(ctx.CommonEmevdPath);
    }

    // ====================================================================
    // Phase 7: All regulation.bin modifications batched behind a single
    // decrypt/encrypt cycle.
    // ====================================================================
    static void ApplyRegulation(Context ctx)
    {
        var reg = RegulationEditor.Open(ctx.ModDir);
        if (reg == null)
        {
            Console.WriteLine("Warning: regulation.bin unavailable, skipping all regulation injections");
            return;
        }

        ShopInjector.ApplyTo(reg, ctx.GraphData.SentryTorchShop);
        WeaponUpgradeInjector.ApplyTo(reg, ctx.GraphData.WeaponUpgrade);
        StartingRuneInjector.ApplyTo(reg, ctx.GraphData.StartingRunes);

        if (ctx.GraphData.ChapelGrace)
            ChapelGraceInjector.Inject(ctx.ModDir, ctx.Config.GameDir, ctx.Events, reg);

        PhantomCatalogInjector.ApplyTo(reg, ctx.PhantomSkins);

        reg.Save();
    }

    // ====================================================================
    // Phase 8: Remaining mod-directory injectors (FMG, MSB, per-map EMEVD).
    // Vanilla stake removal is handled pre-Write via ann.RetryPoints in
    // WriteFogMod (FogMod reads MSBs from BHD archives and removes tagged
    // stakes during Write()).
    // ====================================================================
    static void ApplyModDirInjectors(Context ctx)
    {
        // "RUN COMPLETE" banner FMG entries (all languages)
        if (ctx.GraphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectFmgEntries(ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.RunCompleteMessage);
        }

        // Death markers at fog gates
        var gateSides = BuildGateSideLookup(ctx.Ann);
        DeathMarkerInjector.Inject(
            ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.Connections, ctx.Events,
            ctx.GraphData.EventMap, ctx.GraphData.DeathFlags, gateSides);

        // Rebirth option at Sites of Grace
        if (ctx.GraphData.StartingLarvalTears > 0)
        {
            RebirthInjector.Inject(ctx.ModDir, ctx.Config.GameDir);
        }

        // Remove the DLC "Shadow Realm Blessing" entry from the grace menu
        // (Scadutree leveling is irrelevant under SpeedFog's tier scaling).
        ShadowRealmBlessingRemover.Inject(ctx.ModDir, ctx.Config.GameDir);

        // Set startup flags (open gates, etc.).
        // See docs/startup-flag-injection.md for the methodology used to find these flags.
        StartupFlagInjector.Inject(ctx.ModDir, new[]
        {
            ("m35_00_00_00", 35000565, true),  // Sewer barred gate 1 (AEG023_330_1000, lever AEG027_002_0503)
            ("m35_00_00_00", 35000566, true),  // Sewer barred gate 2 (AEG023_330_1001, lever AEG027_002_0507)
            ("m10_00_00_00", 10000500, true),  // Stormveil barred gate (AEG219_050_0500, lever AEG219_030_0500)
        });

        // Remove vanilla assets that conflict with fog gates.
        if (ctx.GraphData.RemoveEntities.Count > 0)
        {
            VanillaWarpRemover.Remove(ctx.ModDir, ctx.GraphData.RemoveEntities);
        }
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
    /// Mutable state shared across the pipeline phases.
    /// Each field is populated by exactly one phase and read by later phases.
    /// </summary>
    class Context
    {
        public Config Config { get; }
        public string ModDir { get; }
        public string EventDir { get; }
        public string CommonEmevdPath { get; }

        // Populated by LoadInputs
        public GraphData GraphData = null!;
        public AnnotationData Ann = null!;
        public Events Events = null!;
        public EventConfig EventConfig = null!;
        public List<PhantomSkin> PhantomSkins = new();

        // Populated by ConstructGraph
        public RandomizerOptions Opt = null!;
        public Graph Graph = null!;

        // Populated by InjectConnections
        public InjectionResult InjectionResult = null!;

        // Populated by PatchEmevd, written by ApplyCommonInjectors
        public EMEVD CommonEmevd = null!;

        // Resolved by PatchEmevd, consumed by ApplyCommonInjectors.
        // 0 when zone tracking is disabled or no defeat flag is known.
        public int BossDefeatFlag;

        public Context(Config config)
        {
            Config = config;
            ModDir = Path.Combine(config.OutputDir, "mods", "fogmod");
            EventDir = Path.Combine(ModDir, "event");
            CommonEmevdPath = Path.Combine(EventDir, "common.emevd.dcx");
        }
    }

    class ErdtreeWarpData
    {
        public int PrimaryRegion;
        public int AltRegion;
        public byte[] AltMapBytes = null!;
        public int AltMapPacked;
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
