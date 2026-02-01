// writer/SpeedFogWriter/Models/EdgeData.cs
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class EdgeData
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("fog_id")]
    public string FogId { get; set; } = "";
}
