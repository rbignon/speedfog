using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RandomizerCommon;
using SoulsIds;

[assembly: InternalsVisibleTo("ItemRandomizerWrapper.Tests")]

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
            if (randoConfig.EnemyOptions.IgnoreArenaSize)
            {
                opt["ignoresize"] = true;
            }
            if (randoConfig.EnemyOptions.SwapBoss)
            {
                opt["swapboss"] = true;
            }
            Console.WriteLine($"Enemy preset: randomize_bosses={randoConfig.EnemyOptions.RandomizeBosses}, "
                + $"ignore_arena_size={randoConfig.EnemyOptions.IgnoreArenaSize}, "
                + $"swap_boss={randoConfig.EnemyOptions.SwapBoss}");
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

    // Extra enemy IDs promoted from Basic class into the MinorBoss pool,
    // sourced from community preset ABND_UWYG_Pirl_BossModifiésBETA.randomizeopt.
    // These are beefy field enemies (trolls, elite knights, crucible knights,
    // etc.) that play well as non-major boss encounters.
    internal static readonly uint[] ExtraMinorBossPoolIds =
    {
        2053480290, // Colossal Fingercreeper
        1051400299, // Guardian Golem
        1051570310, // Elder Lion
        1050540300, // Fire Prelate
        1052550250, // Fire Prelate
        2810395, // Giant Death Crab
        20010451, // Divine Bird Warrior (Lightning)
        20010450, // Hornsent
        20010453, // Divine Bird Warrior (Frost)
        20010455, // Divine Bird Warrior (Wind)
        1035430230, // Lobster
        35000366, // Omen
        35000361, // Omen
        1044530450, // Omen
        2820478, // Runebear
        21010459, // Fire Knight
        21020450, // Fire Knight
        21010464, // Fire Knight
        11000495, // Crucible Knight
        13000295, // Crucible Knight
        42000200, // Smith Golem
        42030302, // Smith Golem
        42030304, // Smith Golem
        42030300, // Smith Golem
        2045470200, // Crucible Knight Devonia
        1039510800, // Death Rite Bird
        1043370340, // Deathbird
        1047400800, // Night's cavalry
    };

    // Subset of ExtraMinorBossPoolIds that are classified Basic in enemy.txt
    // and therefore must be removed from the Basic pool (otherwise they would
    // also appear as random basic mobs, on top of being boss candidates).
    // The last 3 IDs from ExtraMinorBossPoolIds (1039510800, 1043370340,
    // 1047400800) are not Basic-class so they are intentionally absent here.
    internal static readonly uint[] BasicRemoveSourceIds =
    {
        2053480290, 1051400299, 1051570310, 1050540300, 1052550250, 2810395,
        20010451, 20010450, 20010453, 20010455, 1035430230, 35000366, 35000361,
        1044530450, 2820478, 21010459, 21020450, 21010464, 11000495, 13000295,
        42000200, 42030302, 42030304, 42030300, 2045470200,
    };

    // Category names (resolved through Preset.getIds → enemiesForName) that
    // should not be candidates in the MinorBoss pool. These are field-boss
    // archetypes whose gameplay doesn't translate well into randomized minor-
    // boss arenas (roaming-on-mount encounters, mini-dungeon bespoke fights,
    // etc.). Sourced from the same community preset.
    internal static readonly string[] MinorBossRemoveSourceNames =
    {
        "Night's Cavalry",
        "Cemetery Shade Boss",
        "Guardian Golem Boss",
        "Tibia Mariner Boss",
        "Erdtree Avatar Boss",
        "Ulcerated Tree Spirit Boss",
        "Putrid Avatar Boss",
        "Fire Erdtree Burial Watchdog Boss",
        "Lightning Erdtree Burial Watchdog Boss",
        "Scepter Erdtree Burial Watchdog Boss",
        "2046460800", // Divine Beast Dancing Lion and Basilisks
    };

    /// <summary>
    /// Build an enemy Preset programmatically from EnemyOptionsConfig.
    /// Pool / RemoveSource values are semicolon-separated strings, parsed by
    /// Preset.PhraseRe (see Preset.cs:107, getPoolMultiIds / getMultiIds).
    /// </summary>
    internal static Preset BuildEnemyPreset(EnemyOptionsConfig options)
    {
        var valid = new[] { "none", "minor", "all" };
        if (!valid.Contains(options.RandomizeBosses))
            throw new ArgumentException(
                $"Invalid randomize_bosses value: '{options.RandomizeBosses}' (expected: {string.Join(", ", valid)})");

        var preset = new Preset();

        // HostileNPC: NOT listed in preset → default behavior = randomize
        // among themselves only (each class without a preset entry swaps within itself).
        // CaravanTroll: never randomize (special scripted entity)
        preset.Classes[EnemyAnnotations.EnemyClass.CaravanTroll] =
            new Preset.ClassAssignment { NoRandom = true };

        if (options.RandomizeBosses == "none")
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
        else
        {
            // "minor" or "all": merge non-major boss classes into the MinorBoss
            // pool so Miniboss, NightMiniboss, DragonMiniboss, Evergaol can all
            // swap with each other.
            //
            // MinorBoss needs an explicit entry to prevent inheriting NoRandom
            // from Boss via DefaultInherit (enemy.txt: MinorBoss.Parent = Boss).
            // Without this, ProcessEnemyPreset copies Boss.NoRandom → MinorBoss,
            // which disables the entire MinorBoss silo.
            //
            // The pool string "default; <ids>" keeps the vanilla MinorBoss pool
            // and adds extra promoted enemies on top.
            preset.Classes[EnemyAnnotations.EnemyClass.MinorBoss] = new Preset.ClassAssignment
            {
                Pools = new List<Preset.PoolAssignment>
                {
                    new Preset.PoolAssignment
                    {
                        Weight = 1000,
                        Pool = "default; " + string.Join("; ", ExtraMinorBossPoolIds),
                    }
                },
                RemoveSource = string.Join("; ", MinorBossRemoveSourceNames),
            };
            foreach (var cls in new[] {
                EnemyAnnotations.EnemyClass.NightMiniboss,
                EnemyAnnotations.EnemyClass.DragonMiniboss,
                EnemyAnnotations.EnemyClass.Evergaol,
            })
            {
                preset.Classes[cls] = new Preset.ClassAssignment
                {
                    MergeParent = true,
                    Pools = new List<Preset.PoolAssignment>
                    {
                        new Preset.PoolAssignment { Weight = 1000, Pool = "default" }
                    }
                };
            }
            // Miniboss.Parent is Boss, but AltParent includes MinorBoss.
            // Use ManualParent to redirect the merge into MinorBoss instead of Boss.
            preset.Classes[EnemyAnnotations.EnemyClass.Miniboss] = new Preset.ClassAssignment
            {
                MergeParent = true,
                ManualParent = EnemyAnnotations.EnemyClass.MinorBoss,
                Pools = new List<Preset.PoolAssignment>
                {
                    new Preset.PoolAssignment { Weight = 1000, Pool = "default" }
                }
            };

            // Remove the promoted Basic enemies from the Basic pool so they
            // don't keep appearing as random basic mobs alongside their new
            // role as MinorBoss candidates.
            preset.Classes[EnemyAnnotations.EnemyClass.Basic] = new Preset.ClassAssignment
            {
                RemoveSource = string.Join("; ", BasicRemoveSourceIds),
            };

            // Disable bosshp: when a Basic enemy is placed in an important-target
            // slot (Boss/MinorBoss/Miniboss/...), default behavior is to inflate
            // its HP via geom-mean(basic_hp, boss_hp). Combined with speedfog's
            // scale=true (tier-based scaling on top), this double-boost makes
            // promoted basics overly tanky in high tiers. regularhp is left at
            // its default (true); it only fires boss→basic placements, which
            // never happen in speedfog's configuration.
            preset["bosshp"] = false;

            if (options.RandomizeBosses == "minor")
            {
                // Lock major bosses (Boss class) in place
                preset.Classes[EnemyAnnotations.EnemyClass.Boss] =
                    new Preset.ClassAssignment { NoRandom = true };
            }

        }

        return preset;
    }
}
