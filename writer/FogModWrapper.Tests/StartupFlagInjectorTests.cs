using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class StartupFlagInjectorTests
{
    private static EMEVD.Instruction MakeFiller()
    {
        return new EMEVD.Instruction(1003, 14, new byte[] { 0, 1, 0, 0 });
    }

    /// <summary>
    /// Write a minimal EMEVD with Event 0 containing filler instructions and optional parameters.
    /// Returns the path to the written file.
    /// </summary>
    private static string WriteTestEmevd(string dir, string mapId, int fillerCount, List<EMEVD.Parameter>? parameters = null)
    {
        var emevd = new EMEVD();
        var evt0 = new EMEVD.Event(0);
        for (int i = 0; i < fillerCount; i++)
            evt0.Instructions.Add(MakeFiller());
        if (parameters != null)
        {
            foreach (var p in parameters)
                evt0.Parameters.Add(p);
        }
        emevd.Events.Add(evt0);

        var eventDir = Path.Combine(dir, "event");
        Directory.CreateDirectory(eventDir);
        var path = Path.Combine(eventDir, $"{mapId}.emevd.dcx");
        emevd.Write(path);
        return path;
    }

    private static EMEVD.Event ReadEvent0(string path)
    {
        var emevd = EMEVD.Read(path);
        return emevd.Events.First(e => e.ID == 0);
    }

    [Fact]
    public void Inject_SetsFlagsOnInEvent0()
    {
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "m35_00_00_00", fillerCount: 1);

        StartupFlagInjector.Inject(tmp.Path, new[]
        {
            ("m35_00_00_00", 35008542, true),
            ("m35_00_00_00", 35008544, true),
        });

        var evt0 = ReadEvent0(path);
        Assert.Equal(3, evt0.Instructions.Count); // 2 inserted + 1 filler

        // First two instructions should be SetEventFlag ON
        for (int i = 0; i < 2; i++)
        {
            var instr = evt0.Instructions[i];
            Assert.Equal(2003, instr.Bank);
            Assert.Equal(66, instr.ID);
            Assert.Equal((byte)1, instr.ArgData[8]); // ON
        }

        Assert.Equal(35008542, BitConverter.ToInt32(evt0.Instructions[0].ArgData, 4));
        Assert.Equal(35008544, BitConverter.ToInt32(evt0.Instructions[1].ArgData, 4));

        // Original filler is now at index 2
        Assert.Equal(1003, evt0.Instructions[2].Bank);
    }

    [Fact]
    public void Inject_SetsFlag_Off()
    {
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "common", fillerCount: 0);

        StartupFlagInjector.Inject(tmp.Path, new[]
        {
            ("common", 330, false),
        });

        var evt0 = ReadEvent0(path);
        Assert.Single(evt0.Instructions);
        Assert.Equal((byte)0, evt0.Instructions[0].ArgData[8]); // OFF
        Assert.Equal(330, BitConverter.ToInt32(evt0.Instructions[0].ArgData, 4));
    }

    [Fact]
    public void Inject_ShiftsParameterIndices()
    {
        using var tmp = new TempDir();
        var parameters = new List<EMEVD.Parameter>
        {
            new EMEVD.Parameter(0, 0, 0, 4),
            new EMEVD.Parameter(2, 0, 0, 4),
        };
        WriteTestEmevd(tmp.Path, "m35_00_00_00", fillerCount: 3, parameters: parameters);

        StartupFlagInjector.Inject(tmp.Path, new[]
        {
            ("m35_00_00_00", 35008542, true),
            ("m35_00_00_00", 35008544, true),
        });

        var path = Path.Combine(tmp.Path, "event", "m35_00_00_00.emevd.dcx");
        var evt0 = ReadEvent0(path);

        // Both parameters should be shifted by +2
        Assert.Equal(2, evt0.Parameters[0].InstructionIndex);
        Assert.Equal(4, evt0.Parameters[1].InstructionIndex);
    }

    [Fact]
    public void Inject_GroupsByEmevdFile()
    {
        using var tmp = new TempDir();
        WriteTestEmevd(tmp.Path, "m35_00_00_00", fillerCount: 1);
        WriteTestEmevd(tmp.Path, "common", fillerCount: 1);

        StartupFlagInjector.Inject(tmp.Path, new[]
        {
            ("m35_00_00_00", 35008542, true),
            ("common", 999, true),
            ("m35_00_00_00", 35008544, true),
        });

        var m35Path = Path.Combine(tmp.Path, "event", "m35_00_00_00.emevd.dcx");
        var commonPath = Path.Combine(tmp.Path, "event", "common.emevd.dcx");

        // m35 gets 2 flags + 1 filler = 3
        Assert.Equal(3, ReadEvent0(m35Path).Instructions.Count);
        // common gets 1 flag + 1 filler = 2
        Assert.Equal(2, ReadEvent0(commonPath).Instructions.Count);
    }

    [Fact]
    public void Inject_MissingFile_SkipsGracefully()
    {
        using var tmp = new TempDir();

        // Should not throw — just warns
        StartupFlagInjector.Inject(tmp.Path, new[]
        {
            ("m99_00_00_00", 12345, true),
        });
    }

    /// <summary>
    /// Disposable temp directory helper.
    /// </summary>
    private class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sftest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
