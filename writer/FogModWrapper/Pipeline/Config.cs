namespace FogModWrapper.Pipeline;

/// <summary>Command-line arguments of FogModWrapper.</summary>
internal sealed class Config
{
    public string SeedDir { get; set; } = "";
    public string GraphPath => Path.Combine(SeedDir, "graph.json");
    public string GameDir { get; set; } = "";
    public string DataDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string? MergeDir { get; set; }
}
