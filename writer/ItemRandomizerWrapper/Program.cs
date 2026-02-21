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
        },
        ""item_preset_path"": ""item_preset.yaml""
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

        // Build or load enemy preset
        Preset? preset = null;
        if (randoConfig.EnemyOptions != null)
        {
            preset = BuildEnemyPreset(randoConfig.EnemyOptions);
            Console.WriteLine($"Enemy preset: randomize_bosses={randoConfig.EnemyOptions.RandomizeBosses}, "
                + $"lock_final_boss={randoConfig.EnemyOptions.LockFinalBoss}");
        }
        else if (!string.IsNullOrEmpty(randoConfig.Preset))
        {
            // Backward compat: load YAML preset file
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

        // Load item preset if specified
        // ItemPreset.ParsePreset parses YAML to control item placement
        ItemPreset? itemPreset = null;
        if (!string.IsNullOrEmpty(randoConfig.ItemPresetPath))
        {
            var configDir = Path.GetDirectoryName(Path.GetFullPath(config.ConfigPath)) ?? ".";
            var itemPresetFile = Path.Combine(configDir, randoConfig.ItemPresetPath);
            if (File.Exists(itemPresetFile))
            {
                Console.WriteLine($"Loading item preset: {itemPresetFile}");
                var yamlText = await File.ReadAllTextAsync(itemPresetFile);
                itemPreset = ItemPreset.ParsePreset(yamlText);
            }
            else
            {
                Console.WriteLine($"Warning: Item preset file not found: {itemPresetFile}");
            }
        }

        // Run randomizer
        // Inject dummy MeasureText - CharacterWriter requires this for Elden Ring
        // We don't need accurate measurements for headless operation, just estimate based on string length
        CharacterWriter.MeasureText = (string s, Font f) => (int)(s.Length * f.Size * 0.6f);

        // Capture Console.Out during randomization to parse boss placements
        var originalOut = Console.Out;
        var capturedOutput = new StringWriter();
        var teeWriter = new TeeTextWriter(originalOut, capturedOutput);
        Console.SetOut(teeWriter);

        try
        {
            var randomizer = new Randomizer();
            randomizer.Randomize(
                opt,
                GameSpec.FromGame.ER,
                notify: status => Console.Error.WriteLine($"  {status}"),
                outPath: config.OutputDir,
                preset: preset,
                itemPreset: itemPreset,
                messages: null,
                gameExe: Path.Combine(config.GameDir, "eldenring.exe")
            );
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Parse boss placements from captured output
        var capturedLines = capturedOutput.ToString().Split('\n', StringSplitOptions.TrimEntries);
        var placements = BossPlacementParser.Parse(capturedLines);

        if (placements.Count > 0)
        {
            var placementsPath = Path.Combine(config.OutputDir, "boss_placements.json");
            File.WriteAllText(placementsPath, BossPlacementParser.Serialize(placements));
            Console.WriteLine($"Boss placements: {placements.Count} bosses randomized");
            Console.WriteLine($"Written: {placementsPath}");
        }

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

    /// <summary>
    /// Build an enemy Preset programmatically from EnemyOptionsConfig.
    /// </summary>
    static Preset BuildEnemyPreset(EnemyOptionsConfig options)
    {
        var preset = new Preset();

        // Always lock non-boss classes that shouldn't swap
        preset.Classes[EnemyAnnotations.EnemyClass.HostileNPC] =
            new Preset.ClassAssignment { NoRandom = true };
        preset.Classes[EnemyAnnotations.EnemyClass.CaravanTroll] =
            new Preset.ClassAssignment { NoRandom = true };

        if (!options.RandomizeBosses)
        {
            // Lock all boss classes (current default behavior)
            foreach (var cls in new[] {
                EnemyAnnotations.EnemyClass.Boss,
                EnemyAnnotations.EnemyClass.Miniboss,
                EnemyAnnotations.EnemyClass.MinorBoss,
                EnemyAnnotations.EnemyClass.NightMiniboss,
                EnemyAnnotations.EnemyClass.DragonMiniboss,
                EnemyAnnotations.EnemyClass.Evergaol,
            })
            {
                preset.Classes[cls] = new Preset.ClassAssignment { NoRandom = true };
            }
        }
        else if (options.LockFinalBoss && options.FinishBossDefeatFlag > 0)
        {
            // Boss classes randomize by default (absent from Classes dict).
            // Lock only the final boss via DontRandomizeIDs.
            var flag = (uint)options.FinishBossDefeatFlag;
            preset.DontRandomizeIDs.Add(flag);

            // Radahn and Fire Giant (base game): DefeatFlag = entity_id + 200M.
            // DLC bosses (>= 2B): DefeatFlag IS the entity ID directly.
            if (flag >= 1_200_000_000 && flag < 2_000_000_000)
                preset.DontRandomizeIDs.Add(flag - 200_000_000);
        }
        // else: randomize_bosses=true, lock_final_boss=false → all bosses swap freely

        return preset;
    }
}
