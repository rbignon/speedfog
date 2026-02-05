namespace ItemRandomizerWrapper;

/// <summary>
/// Parses command-line arguments for ItemRandomizerWrapper.
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parse command-line arguments into a CliConfig.
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <param name="errorWriter">Optional writer for error messages (defaults to Console.Error)</param>
    /// <returns>Parsed config, or null if parsing failed</returns>
    public static CliConfig? Parse(string[] args, TextWriter? errorWriter = null)
    {
        errorWriter ??= Console.Error;
        var config = new CliConfig();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-dir":
                    if (i + 1 >= args.Length)
                    {
                        errorWriter.WriteLine("Error: --game-dir requires a value");
                        return null;
                    }
                    config.GameDir = args[++i];
                    break;
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        errorWriter.WriteLine("Error: -o/--output requires a value");
                        return null;
                    }
                    config.OutputDir = args[++i];
                    break;
                case "--data-dir":
                    if (i + 1 >= args.Length)
                    {
                        errorWriter.WriteLine("Error: --data-dir requires a value");
                        return null;
                    }
                    config.DataDir = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("-"))
                    {
                        errorWriter.WriteLine($"Unknown option: {args[i]}");
                        return null;
                    }
                    if (string.IsNullOrEmpty(config.ConfigPath))
                    {
                        config.ConfigPath = args[i];
                    }
                    break;
            }
        }

        // Validate required arguments
        if (string.IsNullOrEmpty(config.ConfigPath))
        {
            errorWriter.WriteLine("Error: config.json path required");
            return null;
        }
        if (string.IsNullOrEmpty(config.GameDir))
        {
            errorWriter.WriteLine("Error: --game-dir required");
            return null;
        }
        if (string.IsNullOrEmpty(config.OutputDir))
        {
            errorWriter.WriteLine("Error: -o/--output required");
            return null;
        }

        return config;
    }
}
