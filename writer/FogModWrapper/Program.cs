using FogMod;
using FogModWrapper;
using FogModWrapper.Models;
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
                    config.GraphPath = arg;
                    break;
            }
        }

        // Validate required args
        if (string.IsNullOrEmpty(config.GraphPath))
            throw new ArgumentException("Missing required argument: graph.json path");
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

Usage: FogModWrapper <graph.json> --game-dir <path> --data-dir <path> -o <output>

Arguments:
  <graph.json>       Path to graph.json v2 from Python core
  --game-dir <path>  Path to Elden Ring Game directory
  --data-dir <path>  Path to data directory (fog.txt, fogevents.txt, er-common.emedf.json)
  -o, --output       Output directory for generated mod files

Example:
  FogModWrapper seeds/123/graph.json --game-dir ""C:/Games/ELDEN RING/Game"" --data-dir data -o output
");
    }

    static void Run(Config config)
    {
        Console.WriteLine("=== FogModWrapper ===");
        Console.WriteLine($"Graph: {config.GraphPath}");
        Console.WriteLine($"Game dir: {config.GameDir}");
        Console.WriteLine($"Data dir: {config.DataDir}");
        Console.WriteLine($"Output: {config.OutputDir}");
        Console.WriteLine();

        // 1. Load our graph.json
        var graphData = GraphLoader.Load(config.GraphPath);

        // 2. Build FogMod options
        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.Seed = graphData.Seed;
        opt.InitFeatures();

        // Apply options from graph.json
        foreach (var (key, value) in graphData.Options)
        {
            opt[key] = value;
        }

        // Required options for SpeedFog
        opt["shuffle"] = true;  // World shuffle mode
        opt["req_backportal"] = true;  // Enable backportals so boss rooms have return warps as edges
        opt["req_all"] = true;  // Make all dungeon tags "core" so backportals aren't marked unused

        Console.WriteLine($"Options: seed={opt.Seed}, shuffle={opt["shuffle"]}, scale={opt["scale"]}");

        // 3. Load FogMod data files
        var fogPath = Path.Combine(config.DataDir, "fog.txt");
        var fogeventsPath = Path.Combine(config.DataDir, "fogevents.txt");
        var emedfPath = Path.Combine(config.DataDir, "er-common.emedf.json");

        Console.WriteLine($"Loading fog.txt from: {fogPath}");
        var ann = AnnotationData.LoadLiteConfig(fogPath);

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
            // DLC kindling/imbued requirements (not used - base game only)
            { "treekindling", "FALSE" },
            { "imbued_base", "FALSE" },
            { "imbued_base_any", "FALSE" },
            { "imbued_dlc", "FALSE" },
            { "imbued_dlc_any", "FALSE" },
            // DLC high seal conditions (areas reached via seals)
            { "rauhruins_high_seal", "FALSE" },
            { "rauhbase_high_seal", "FALSE" },
            { "gravesite_seal", "FALSE" },
            { "scadualtus_high_seal", "FALSE" },
            { "ymir_open", "FALSE" },
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
            { "imbuedswordkey4", "FALSE" },  // DLC key
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
            // DLC keys (FALSE - not supported yet)
            { "omother", "FALSE" },
            { "welldepthskey", "FALSE" },
            { "gaolupperlevelkey", "FALSE" },
            { "gaollowerlevelkey", "FALSE" },
            { "holeladennecklace", "FALSE" },
            { "messmerskindling", "FALSE" },
            { "messmerskindling1", "FALSE" },
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

        // 5. Inject OUR connections (replaces GraphConnector.Connect())
        ConnectionInjector.Inject(graph, graphData.Connections);

        // 6. Apply tiers for scaling
        ConnectionInjector.ApplyAreaTiers(graph, graphData.AreaTiers);

        // 7. Call FogMod writer
        Console.WriteLine($"Writing mod files to: {config.OutputDir}");
        Directory.CreateDirectory(config.OutputDir);

        // Create MergedMods to provide access to game files
        var mergedMods = new MergedMods(config.GameDir);

        var writer = new GameDataWriterE();
        writer.Write(opt, ann, graph, mergedMods, config.OutputDir, events, eventConfig, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine("=== Done! ===");
    }

    class Config
    {
        public string GraphPath { get; set; } = "";
        public string GameDir { get; set; } = "";
        public string DataDir { get; set; } = "";
        public string OutputDir { get; set; } = "";
    }
}
