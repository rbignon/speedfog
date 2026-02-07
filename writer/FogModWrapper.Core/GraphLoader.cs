using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Loads and parses the graph.json format from Python.
/// </summary>
public static class GraphLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Load graph data from a JSON file.
    /// </summary>
    /// <param name="path">Path to graph.json</param>
    /// <returns>Parsed GraphData</returns>
    /// <exception cref="FileNotFoundException">If the file doesn't exist</exception>
    /// <exception cref="JsonException">If JSON parsing fails</exception>
    public static GraphData Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Graph file not found: {path}");
        }

        var json = File.ReadAllText(path);
        var data = Parse(json);

        Console.WriteLine($"Loaded graph: seed={data.Seed}, {data.Connections.Count} connections, {data.AreaTiers.Count} area tiers");

        return data;
    }

    /// <summary>
    /// Parse graph data from a JSON string.
    /// </summary>
    /// <param name="json">JSON string to parse</param>
    /// <returns>Parsed GraphData</returns>
    /// <exception cref="JsonException">If JSON parsing fails</exception>
    public static GraphData Parse(string json)
    {
        var data = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        if (data == null)
        {
            throw new JsonException("Failed to parse graph JSON: result was null");
        }

        // Validate version
        if (data.Version != "3.0" && data.Version != "4.0")
        {
            Console.WriteLine($"Warning: Expected graph.json version 3.0 or 4.0, got {data.Version}");
        }

        return data;
    }
}
