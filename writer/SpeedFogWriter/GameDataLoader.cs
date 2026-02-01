// writer/SpeedFogWriter/GameDataLoader.cs
using SoulsFormats;
using SoulsIds;

namespace SpeedFogWriter;

public class GameDataLoader
{
    private readonly string _gameDir;
    private readonly GameEditor _editor;

    public Dictionary<string, MSBE> Msbs { get; } = new();
    public Dictionary<string, EMEVD> Emevds { get; } = new();
    public ParamDictionary? Params { get; private set; }
    public Events? EventsHelper { get; private set; }

    public GameDataLoader(string gameDir)
    {
        _gameDir = gameDir;
        _editor = new GameEditor(GameSpec.FromGame.ER);
        _editor.Spec.GameDir = gameDir;
    }

    public void LoadParams()
    {
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        if (!File.Exists(regulationPath))
            throw new FileNotFoundException($"regulation.bin not found: {regulationPath}");

        Params = new ParamDictionary
        {
            Defs = _editor.LoadDefs(),
            Inner = _editor.LoadParams(regulationPath, null)
        };
        Console.WriteLine($"  Loaded params from regulation.bin");
    }

    public void LoadEmevds()
    {
        var eventDir = Path.Combine(_gameDir, "event");
        if (!Directory.Exists(eventDir))
            throw new DirectoryNotFoundException($"Event directory not found: {eventDir}");

        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var name = Path.GetFileNameWithoutExtension(file).Replace(".emevd", "");
            Emevds[name] = SoulsFile<EMEVD>.Read(file);
        }
        Console.WriteLine($"  Loaded {Emevds.Count} EMEVD files");
    }

    public void LoadMsbs(IEnumerable<string> requiredMaps)
    {
        var mapDir = Path.Combine(_gameDir, "map", "mapstudio");
        if (!Directory.Exists(mapDir))
            throw new DirectoryNotFoundException($"Map directory not found: {mapDir}");

        foreach (var mapName in requiredMaps)
        {
            var file = Path.Combine(mapDir, $"{mapName}.msb.dcx");
            if (File.Exists(file))
            {
                Msbs[mapName] = SoulsFile<MSBE>.Read(file);
            }
            else
            {
                Console.WriteLine($"  Warning: MSB not found for {mapName}");
            }
        }
        Console.WriteLine($"  Loaded {Msbs.Count} MSB files");
    }

    public void InitializeEvents(string emedfPath)
    {
        if (!File.Exists(emedfPath))
            throw new FileNotFoundException($"er-common.emedf.json not found: {emedfPath}");

        EventsHelper = new Events(emedfPath, darkScriptMode: true, paramAwareMode: true);
        Console.WriteLine($"  Initialized Events helper from {emedfPath}");
    }

    public GameEditor Editor => _editor;
}
