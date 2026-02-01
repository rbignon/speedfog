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

    [JsonPropertyName("total_zones")]
    public int TotalZones { get; set; }

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

    /// <summary>
    /// Get all edges with resolved node references.
    /// </summary>
    public IEnumerable<(NodeData Source, NodeData Target, string FogId)> AllEdgesResolved()
    {
        foreach (var edge in Edges)
        {
            var source = GetNode(edge.Source);
            var target = GetNode(edge.Target);
            if (source != null && target != null)
            {
                yield return (source, target, edge.FogId);
            }
        }
    }

    /// <summary>
    /// Get outgoing edges from a node.
    /// </summary>
    public IEnumerable<EdgeData> GetOutgoingEdges(string nodeId)
        => Edges.Where(e => e.Source == nodeId);

    /// <summary>
    /// Get incoming edges to a node.
    /// </summary>
    public IEnumerable<EdgeData> GetIncomingEdges(string nodeId)
        => Edges.Where(e => e.Target == nodeId);

    /// <summary>
    /// Group nodes by layer for iteration.
    /// </summary>
    public Dictionary<int, List<NodeData>> NodesByLayer()
        => Nodes.Values
            .GroupBy(n => n.Layer)
            .ToDictionary(g => g.Key, g => g.ToList());
}
