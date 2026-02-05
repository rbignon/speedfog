using ItemRandomizerWrapper;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_AllRequiredArgs_ReturnsConfig()
    {
        var args = new[] { "config.json", "--game-dir", "/path/to/game", "-o", "/output" };

        var config = ArgParser.Parse(args, TextWriter.Null);

        Assert.NotNull(config);
        Assert.Equal("config.json", config.ConfigPath);
        Assert.Equal("/path/to/game", config.GameDir);
        Assert.Equal("/output", config.OutputDir);
        Assert.Null(config.DataDir);
    }

    [Fact]
    public void Parse_WithDataDir_SetsDataDir()
    {
        var args = new[] { "config.json", "--game-dir", "/game", "-o", "/out", "--data-dir", "/data" };

        var config = ArgParser.Parse(args, TextWriter.Null);

        Assert.NotNull(config);
        Assert.Equal("/data", config.DataDir);
    }

    [Fact]
    public void Parse_LongOutputFlag_Works()
    {
        var args = new[] { "config.json", "--game-dir", "/game", "--output", "/out" };

        var config = ArgParser.Parse(args, TextWriter.Null);

        Assert.NotNull(config);
        Assert.Equal("/out", config.OutputDir);
    }

    [Fact]
    public void Parse_MissingConfigPath_ReturnsNull()
    {
        var args = new[] { "--game-dir", "/game", "-o", "/out" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("config.json path required", errors.ToString());
    }

    [Fact]
    public void Parse_MissingGameDir_ReturnsNull()
    {
        var args = new[] { "config.json", "-o", "/out" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("--game-dir required", errors.ToString());
    }

    [Fact]
    public void Parse_MissingOutput_ReturnsNull()
    {
        var args = new[] { "config.json", "--game-dir", "/game" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("-o/--output required", errors.ToString());
    }

    [Fact]
    public void Parse_UnknownOption_ReturnsNull()
    {
        var args = new[] { "config.json", "--game-dir", "/game", "-o", "/out", "--unknown" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("Unknown option: --unknown", errors.ToString());
    }

    [Fact]
    public void Parse_GameDirMissingValue_ReturnsNull()
    {
        var args = new[] { "config.json", "--game-dir" }; // No value after --game-dir
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("--game-dir requires a value", errors.ToString());
    }

    [Fact]
    public void Parse_OutputMissingValue_ReturnsNull()
    {
        var args = new[] { "config.json", "--game-dir", "/game", "-o" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("-o/--output requires a value", errors.ToString());
    }

    [Fact]
    public void Parse_DataDirMissingValue_ReturnsNull()
    {
        var args = new[] { "config.json", "--game-dir", "/game", "-o", "/out", "--data-dir" };
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
        Assert.Contains("--data-dir requires a value", errors.ToString());
    }

    [Fact]
    public void Parse_EmptyArgs_ReturnsNull()
    {
        var args = Array.Empty<string>();
        var errors = new StringWriter();

        var config = ArgParser.Parse(args, errors);

        Assert.Null(config);
    }

    [Fact]
    public void Parse_ArgsWithSpaces_HandlesCorrectly()
    {
        var args = new[] { "path/to/config.json", "--game-dir", "C:/Program Files/ELDEN RING/Game", "-o", "/my output" };

        var config = ArgParser.Parse(args, TextWriter.Null);

        Assert.NotNull(config);
        Assert.Equal("C:/Program Files/ELDEN RING/Game", config.GameDir);
        Assert.Equal("/my output", config.OutputDir);
    }

    [Fact]
    public void Parse_OrderIndependent_Works()
    {
        // Args in different order
        var args = new[] { "-o", "/out", "config.json", "--game-dir", "/game" };

        var config = ArgParser.Parse(args, TextWriter.Null);

        Assert.NotNull(config);
        Assert.Equal("config.json", config.ConfigPath);
        Assert.Equal("/game", config.GameDir);
        Assert.Equal("/out", config.OutputDir);
    }

    [Fact]
    public void Parse_DefaultErrorWriter_UsesConsoleError()
    {
        // This test just ensures the default parameter works
        var args = new[] { "config.json", "--game-dir", "/game", "-o", "/out" };

        var config = ArgParser.Parse(args); // No error writer specified

        Assert.NotNull(config);
    }
}
