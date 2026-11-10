namespace StaticModBuilder.Winter;

/// <summary>Collects the winter build's report lines; echoes them to the console when Verbose.</summary>
public sealed class WinterReport
{
    private readonly List<string> _lines = new();

    public bool Verbose { get; init; }

    public IReadOnlyList<string> Lines => _lines;

    public void Line(string text)
    {
        _lines.Add(text);
        if (Verbose)
        {
            Console.WriteLine(text);
        }
    }

    public void Write(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllLines(path, _lines);
    }
}
