using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Shared editor for regulation.bin that decrypts once, caches parsed PARAM
/// objects across injectors, and re-encrypts once on Save().
///
/// Cached PARAMs are shared by reference across GetParam calls with the same
/// name. If two injectors both modify the same PARAM (e.g. CharaInitParam),
/// their changes coexist in the same in-memory object. This is safe as long
/// as they write disjoint fields, which is a documented invariant of the
/// current consumer set (WeaponUpgradeInjector writes weapon fields;
/// StartingRuneInjector writes the soul field).
/// </summary>
public sealed class RegulationEditor
{
    private readonly string? _regulationPath;
    private readonly BND4 _bnd;
    private readonly string _defsDir;
    private readonly Dictionary<string, PARAM> _params = new();
    private readonly Dictionary<string, PARAMDEF> _defs = new();

    /// <summary>
    /// Test-visible constructor. When <paramref name="path"/> is null, Save()
    /// serializes accessed PARAMs back into the BND4 but skips the encryption
    /// step (the file is never written).
    /// </summary>
    internal RegulationEditor(BND4 bnd, string defsDir, string? path = null)
    {
        _bnd = bnd;
        _defsDir = defsDir;
        _regulationPath = path;
    }

    /// <summary>
    /// Opens and decrypts regulation.bin from <paramref name="modDir"/>.
    /// Returns null (and logs a warning) if the file is missing or decryption
    /// throws.
    /// </summary>
    public static RegulationEditor? Open(string modDir)
    {
        var path = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(path))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping regulation injections");
            return null;
        }

        BND4 bnd;
        try
        {
            bnd = SFUtil.DecryptERRegulation(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decrypt regulation.bin: {ex.Message}");
            return null;
        }

        var defsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eldendata", "Defs");
        return new RegulationEditor(bnd, defsDir, path);
    }

    /// <summary>
    /// Returns the parsed PARAM identified by its short name (e.g.
    /// "CharaInitParam"). Cached: subsequent calls return the same instance.
    /// Returns null (and logs a warning) if the matching binder file or
    /// paramdef XML cannot be found.
    /// </summary>
    public PARAM? GetParam(string name)
    {
        if (_params.TryGetValue(name, out var cached))
            return cached;

        var file = _bnd.Files.Find(f => f.Name.EndsWith($"{name}.param"));
        if (file == null)
        {
            Console.WriteLine($"Warning: {name}.param not found in regulation.bin");
            return null;
        }

        var def = LoadParamdef(name);
        if (def == null)
            return null;

        var param = PARAM.Read(file.Bytes);
        param.ApplyParamdef(def);
        _params[name] = param;
        return param;
    }

    /// <summary>
    /// Re-serializes every accessed PARAM back into the BND4, then re-encrypts
    /// regulation.bin at the path supplied to Open(). No-op when no PARAM has
    /// been accessed. Skips the encryption step entirely when the editor was
    /// constructed without a path (test fixtures only).
    /// </summary>
    public void Save()
    {
        if (_params.Count == 0)
            return;

        foreach (var kvp in _params)
        {
            var file = _bnd.Files.Find(f => f.Name.EndsWith($"{kvp.Key}.param"));
            if (file == null)
                continue;
            file.Bytes = kvp.Value.Write();
        }

        if (_regulationPath == null)
            return;

        SFUtil.EncryptERRegulation(_regulationPath, _bnd);
        Console.WriteLine($"Regulation saved: {_params.Count} param(s) modified");
    }

    private PARAMDEF? LoadParamdef(string name)
    {
        if (_defs.TryGetValue(name, out var cached))
            return cached;

        var path = Path.Combine(_defsDir, $"{name}.xml");
        if (!File.Exists(path))
        {
            Console.WriteLine($"Warning: paramdef {name}.xml not found at {path}");
            return null;
        }

        var def = PARAMDEF.XmlDeserialize(path);
        _defs[name] = def;
        return def;
    }
}
