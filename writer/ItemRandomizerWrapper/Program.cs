using System.Drawing;
using System.Text.Json;
using RandomizerCommon;
using SoulsIds;

namespace ItemRandomizerWrapper;

/// <summary>
/// Thin wrapper around RandomizerCommon.dll for SpeedFog item randomization.
///
/// This wrapper:
/// 1. Accepts a configuration JSON file specifying randomization options
/// 2. Calls RandomizerCommon's Randomizer.Randomize() method
/// 3. Outputs randomized game files to a specified directory
///
/// The output can then be merged with FogModWrapper's output using MergedMods.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var config = ArgParser.Parse(args);
            if (config == null)
            {
                PrintUsage();
                return 1;
            }

            await RunRandomizer(config);
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

    static void PrintUsage()
    {
        Console.WriteLine(@"ItemRandomizerWrapper - SpeedFog Item Randomization

Usage:
    ItemRandomizerWrapper <config.json> --game-dir <path> -o <output>

Arguments:
    <config.json>       Path to item randomization config JSON
    --game-dir <path>   Path to ELDEN RING/Game folder
    -o <output>         Output directory for randomized files

Config JSON format:
    {
        ""seed"": 12345,
        ""difficulty"": 50,
        ""options"": {
            ""item"": true,
            ""enemy"": false
        }
    }

Example:
    ItemRandomizerWrapper config.json --game-dir ""C:\ELDEN RING\Game"" -o output/
");
    }

    static async Task RunRandomizer(CliConfig config)
    {
        // Load configuration from JSON
        var configJson = await File.ReadAllTextAsync(config.ConfigPath);
        var randoConfig = JsonSerializer.Deserialize<RandomizerConfig>(configJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("Failed to parse config JSON");

        Console.WriteLine($"Item Randomizer Wrapper");
        Console.WriteLine($"=======================");
        Console.WriteLine($"Seed: {randoConfig.Seed}");
        Console.WriteLine($"Difficulty: {randoConfig.Difficulty}");
        Console.WriteLine($"Game dir: {config.GameDir}");
        Console.WriteLine($"Output dir: {config.OutputDir}");
        Console.WriteLine();

        // Build RandomizerOptions
        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.Seed = (uint)randoConfig.Seed;
        opt.Difficulty = randoConfig.Difficulty;

        // Apply options from config
        if (randoConfig.Options != null)
        {
            foreach (var kv in randoConfig.Options)
            {
                opt[kv.Key] = kv.Value;
            }
        }

        // Default to item randomization only (no enemies)
        if (!randoConfig.Options?.ContainsKey("item") ?? true)
        {
            opt["item"] = true;
        }
        if (!randoConfig.Options?.ContainsKey("enemy") ?? true)
        {
            opt["enemy"] = false;
        }

        // Create output directory
        Directory.CreateDirectory(config.OutputDir);

        // Determine data directory (diste/)
        string dataDir = config.DataDir ?? Path.Combine(AppContext.BaseDirectory, "diste");
        if (!Directory.Exists(dataDir))
        {
            // Try relative to executable
            dataDir = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".", "diste");
        }
        if (!Directory.Exists(dataDir))
        {
            throw new Exception($"Data directory not found: {dataDir}");
        }

        Console.WriteLine($"Data dir: {dataDir}");
        Console.WriteLine();

        // Load enemy preset if specified
        // Preset.LoadPreset expects a preset name and looks in presets/{name}.txt
        Preset? preset = null;
        if (!string.IsNullOrEmpty(randoConfig.Preset))
        {
            var presetFile = Path.Combine("presets", $"{randoConfig.Preset}.txt");
            if (File.Exists(presetFile))
            {
                Console.WriteLine($"Loading preset: {randoConfig.Preset}");
                preset = Preset.LoadPreset(randoConfig.Preset);
            }
            else
            {
                Console.WriteLine($"Warning: Preset file not found: {presetFile}");
            }
        }

        // Run randomizer
        // Inject dummy MeasureText - CharacterWriter requires this for Elden Ring
        // We don't need accurate measurements for headless operation, just estimate based on string length
        CharacterWriter.MeasureText = (string s, Font f) => (int)(s.Length * f.Size * 0.6f);

        var randomizer = new Randomizer();
        randomizer.Randomize(
            opt,
            GameSpec.FromGame.ER,
            notify: status => Console.WriteLine($"  {status}"),
            outPath: config.OutputDir,
            preset: preset,
            itemPreset: null,
            messages: null,
            gameExe: Path.Combine(config.GameDir, "eldenring.exe")
        );

        // Write helper options if specified (for RandomizerHelper.dll)
        if (randoConfig.HelperOptions != null && randoConfig.HelperOptions.Count > 0)
        {
            var helperIniPath = Path.Combine(config.OutputDir, "RandomizerHelper_config.ini");
            using var writer = new StreamWriter(helperIniPath);
            writer.WriteLine("[settings]");
            foreach (var kv in randoConfig.HelperOptions)
            {
                writer.WriteLine($"{kv.Key} = {kv.Value.ToString().ToLowerInvariant()}");
            }
            Console.WriteLine($"Written: {helperIniPath}");
        }

        Console.WriteLine();
        Console.WriteLine($"Item randomization complete!");
        Console.WriteLine($"Output written to: {config.OutputDir}");
    }
}
