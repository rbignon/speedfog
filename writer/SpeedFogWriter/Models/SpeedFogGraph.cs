// writer/SpeedFogWriter/Models/SpeedFogGraph.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class SpeedFogGraph
{
    [JsonPropertyName("seed")]
    public int Seed { get; set; }

    [JsonPropertyName("total_layers")]
    public int TotalLayers { get; set; }

    [JsonPropertyName("total_nodes")]
    public int TotalNodes { get; set; }

    [JsonPropertyName("total_paths")]
    public int TotalPaths { get; set; }

    [JsonPropertyName("path_weights")]
    public List<int> PathWeights { get; set; } = new();

    [JsonPropertyName("nodes")]
    public Dictionary<string, NodeData> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<EdgeData> Edges { get; set; } = new();

    [JsonPropertyName("start_id")]
    public string StartId { get; set; } = "";

    [JsonPropertyName("end_id")]
    public string EndId { get; set; } = "";

    public static SpeedFogGraph Load(string path)
    {
        var json = File.ReadAllText(path);
        var graph = JsonSerializer.Deserialize<SpeedFogGraph>(json)
            ?? throw new InvalidOperationException("Failed to parse graph.json");

        foreach (var (id, node) in graph.Nodes)
        {
            node.Id = id;
        }

        return graph;
    }

    public IEnumerable<NodeData> AllNodes() => Nodes.Values;
    public NodeData? GetNode(string id) => Nodes.GetValueOrDefault(id);
    public NodeData? StartNode => GetNode(StartId);
    public NodeData? EndNode => GetNode(EndId);
}
