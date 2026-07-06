using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for GraceTalkEsd: locating the grace talk BND, anchoring the grace menu
/// machine via the "Memorize spell" talk data (bank 1, command 19, message in
/// argument slot 1, the shape ESDEdits.FindMachinesWithTalkData matches), and
/// writing edits back to the mod directory.
///
/// Uses real BND4/ESD files written to temp directories, since Load/Save are
/// file-system entry points.
/// </summary>
public class GraceTalkEsdTests : IDisposable
{
    private const string BND_NAME = "m00_00_00_00.talkesdbnd.dcx";
    private const string ESD_ENTRY_NAME =
        @"N:\GR\data\INTERROOT_win64\script\talk\m00_00_00_00\t000001000.esd";

    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gracetalk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>A state carrying the "Memorize spell" AddTalkListData anchor.</summary>
    private static ESD.State MakeAnchorState()
    {
        var state = new ESD.State();
        state.EntryCommands.Add(AST.MakeCommand(1, 19,
            new object[] { 4, GraceTalkEsd.MemorizeSpellMsg, -1 }));
        return state;
    }

    private static ESD MakeGraceEsd(long machineId = 1000, long stateId = 17)
    {
        var esd = new ESD();
        esd.StateGroups[machineId] = new Dictionary<long, ESD.State> { [stateId] = MakeAnchorState() };
        return esd;
    }

    private static void WriteBnd(string baseDir, ESD esd, string dirName = "talk",
                                 string entryName = ESD_ENTRY_NAME)
    {
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, entryName, esd.Write()));
        var talkDir = Path.Combine(baseDir, "script", dirName);
        Directory.CreateDirectory(talkDir);
        bnd.Write(Path.Combine(talkDir, BND_NAME));
    }

    [Fact]
    public void Load_MissingBnd_ReturnsNull()
    {
        var result = GraceTalkEsd.Load(TempDir(), TempDir());
        Assert.Null(result);
    }

    [Fact]
    public void Load_MissingEsdEntry_ReturnsNull()
    {
        var gameDir = TempDir();
        WriteBnd(gameDir, MakeGraceEsd(), entryName: @"N:\GR\script\talk\t999999000.esd");

        var result = GraceTalkEsd.Load(TempDir(), gameDir);

        Assert.Null(result);
    }

    [Fact]
    public void Load_NoGraceMachine_ReturnsNull()
    {
        // ESD with a machine, but no "Memorize spell" anchor anywhere
        var esd = new ESD();
        var state = new ESD.State();
        state.EntryCommands.Add(AST.MakeCommand(1, 19, new object[] { 4, 99999, -1 }));
        esd.StateGroups[1000] = new Dictionary<long, ESD.State> { [0] = state };
        var gameDir = TempDir();
        WriteBnd(gameDir, esd);

        var result = GraceTalkEsd.Load(TempDir(), gameDir);

        Assert.Null(result);
    }

    [Fact]
    public void Load_MultipleGraceMachines_ReturnsNull()
    {
        var esd = new ESD();
        esd.StateGroups[1000] = new Dictionary<long, ESD.State> { [0] = MakeAnchorState() };
        esd.StateGroups[2000] = new Dictionary<long, ESD.State> { [0] = MakeAnchorState() };
        var gameDir = TempDir();
        WriteBnd(gameDir, esd);

        var result = GraceTalkEsd.Load(TempDir(), gameDir);

        Assert.Null(result);
    }

    [Fact]
    public void Load_SingleGraceMachine_ReturnsAnchoredMachine()
    {
        var gameDir = TempDir();
        WriteBnd(gameDir, MakeGraceEsd(machineId: 1000, stateId: 17));

        var result = GraceTalkEsd.Load(TempDir(), gameDir);

        Assert.NotNull(result);
        Assert.True(result.GraceMachine.ContainsKey(17));
        Assert.Same(result.Esd.StateGroups[1000], result.GraceMachine);
    }

    [Fact]
    public void Load_PrefersModDirOverGameDir()
    {
        var modDir = TempDir();
        var gameDir = TempDir();
        WriteBnd(modDir, MakeGraceEsd(stateId: 42));
        WriteBnd(gameDir, MakeGraceEsd(stateId: 7));

        var result = GraceTalkEsd.Load(modDir, gameDir);

        Assert.NotNull(result);
        Assert.True(result.GraceMachine.ContainsKey(42));
        Assert.False(result.GraceMachine.ContainsKey(7));
    }

    [Fact]
    public void Load_FindsBndInPascalCaseTalkDir()
    {
        // Vanilla game layout uses "Talk"; FogMod under Wine writes "talk"
        var gameDir = TempDir();
        WriteBnd(gameDir, MakeGraceEsd(), dirName: "Talk");

        var result = GraceTalkEsd.Load(TempDir(), gameDir);

        Assert.NotNull(result);
    }

    [Fact]
    public void Save_WritesEditedEsdToModDir()
    {
        var modDir = TempDir();
        var gameDir = TempDir();
        WriteBnd(gameDir, MakeGraceEsd(stateId: 17));

        var loaded = GraceTalkEsd.Load(modDir, gameDir);
        Assert.NotNull(loaded);
        // Edit the machine: add a new state, as injectors do
        loaded.GraceMachine[99] = new ESD.State();
        loaded.Save();

        // Written to the mod dir, and the edit survives a reload
        Assert.True(File.Exists(Path.Combine(modDir, "script", "talk", BND_NAME)));
        var reloaded = GraceTalkEsd.Load(modDir, TempDir());
        Assert.NotNull(reloaded);
        Assert.True(reloaded.GraceMachine.ContainsKey(99));
        Assert.True(reloaded.GraceMachine.ContainsKey(17));
    }

    [Fact]
    public void Save_OverwritesExistingModBnd()
    {
        var modDir = TempDir();
        WriteBnd(modDir, MakeGraceEsd(stateId: 17));

        var loaded = GraceTalkEsd.Load(modDir, TempDir());
        Assert.NotNull(loaded);
        loaded.GraceMachine[123] = new ESD.State();
        loaded.Save();

        var reloaded = GraceTalkEsd.Load(modDir, TempDir());
        Assert.NotNull(reloaded);
        Assert.True(reloaded.GraceMachine.ContainsKey(123));
    }
}
