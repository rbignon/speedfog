// writer/SpeedFogWriter/GameDataLoader.cs
using SoulsFormats;
using SoulsIds;

namespace SpeedFogWriter;

public class GameDataLoader
{
    private readonly string _gameDir;
    private readonly string? _vanillaDir;
    private readonly GameEditor _editor;

    public Dictionary<string, MSBE> Msbs { get; } = new();
    public Dictionary<string, EMEVD> Emevds { get; } = new();
    public ParamDictionary? Params { get; private set; }
    public Events? EventsHelper { get; private set; }

    public GameDataLoader(string gameDir, string? vanillaDir = null)
    {
        _gameDir = gameDir;
        _vanillaDir = vanillaDir;
        _editor = new GameEditor(GameSpec.FromGame.ER);
        _editor.Spec.GameDir = gameDir;
    }

    public void LoadParams()
    {
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        if (!File.Exists(regulationPath))
            throw new FileNotFoundException($"regulation.bin not found: {regulationPath}");

        var defs = _editor.LoadDefs();
        Params = new ParamDictionary
        {
            Defs = defs,
            Inner = _editor.LoadParams(regulationPath, defs)
        };
        Console.WriteLine($"  Loaded params from regulation.bin");
    }

    public void LoadEmevds()
    {
        // Try vanilla dir first, then game dir
        string? eventDir = null;
        if (_vanillaDir != null)
        {
            eventDir = _vanillaDir; // Vanilla files are flat in the directory
            if (!Directory.Exists(eventDir))
                eventDir = null;
        }
        if (eventDir == null)
        {
            eventDir = Path.Combine(_gameDir, "event");
            if (!Directory.Exists(eventDir))
                throw new DirectoryNotFoundException($"Event directory not found: {eventDir}. Use --vanilla-dir to specify extracted vanilla files location.");
        }

        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var name = Path.GetFileNameWithoutExtension(file).Replace(".emevd", "");
            Emevds[name] = SoulsFile<EMEVD>.Read(file);
        }
        Console.WriteLine($"  Loaded {Emevds.Count} EMEVD files from {eventDir}");
    }

    public void LoadMsbs(IEnumerable<string> requiredMaps)
    {
        // Try vanilla dir first (flat), then game dir (nested)
        string? mapDir = null;
        bool flatStructure = false;

        if (_vanillaDir != null && Directory.Exists(_vanillaDir))
        {
            // Check if vanilla dir has MSB files directly (flat structure)
            if (Directory.GetFiles(_vanillaDir, "*.msb.dcx").Length > 0)
            {
                mapDir = _vanillaDir;
                flatStructure = true;
            }
        }
        if (mapDir == null)
        {
            mapDir = Path.Combine(_gameDir, "map", "mapstudio");
            if (!Directory.Exists(mapDir))
                throw new DirectoryNotFoundException($"Map directory not found: {mapDir}. Use --vanilla-dir to specify extracted vanilla files location.");
        }

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
        Console.WriteLine($"  Loaded {Msbs.Count} MSB files from {mapDir}");
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
