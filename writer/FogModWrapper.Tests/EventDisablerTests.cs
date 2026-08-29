using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class EventDisablerTests
{
    private const long INVASION_EVENT = 1051360740;
    private const long OTHER_EVENT = 1051360741;

    private static EMEVD.Instruction MakeFiller() =>
        new(1003, 14, new byte[] { 0, 1, 0, 0 });

    private static EMEVD.Instruction MakeInitializeEvent(long eventId)
    {
        var args = new byte[12];
        BitConverter.GetBytes((int)eventId).CopyTo(args, 4);
        return new EMEVD.Instruction(2000, 0, args);
    }

    /// <summary>
    /// Event 0 initializing both events; the target event has three
    /// instructions and a parameter binding, like a vanilla NPC event.
    /// </summary>
    private static string WriteTestEmevd(string dir, string mapId)
    {
        var emevd = new EMEVD();
        var evt0 = new EMEVD.Event(0);
        evt0.Instructions.Add(MakeInitializeEvent(INVASION_EVENT));
        evt0.Instructions.Add(MakeInitializeEvent(OTHER_EVENT));
        // A parameter binding on Event 0 (FogMod adds some): must survive untouched.
        evt0.Parameters.Add(new EMEVD.Parameter(1, 4, 0, 4));
        emevd.Events.Add(evt0);

        var target = new EMEVD.Event(INVASION_EVENT) { RestBehavior = EMEVD.Event.RestBehaviorType.Restart };
        for (int i = 0; i < 3; i++)
            target.Instructions.Add(MakeFiller());
        target.Parameters.Add(new EMEVD.Parameter(1, 0, 0, 4));
        emevd.Events.Add(target);

        var other = new EMEVD.Event(OTHER_EVENT);
        other.Instructions.Add(MakeFiller());
        other.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        emevd.Events.Add(other);

        var eventDir = Path.Combine(dir, "event");
        Directory.CreateDirectory(eventDir);
        var path = Path.Combine(eventDir, $"{mapId}.emevd.dcx");
        emevd.Write(path);
        return path;
    }

    [Fact]
    public void Inject_ReplacesEventBodyWithEndAndClearsParameters()
    {
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "m60_51_36_00");

        EventDisabler.Inject(tmp.Path, new[] { new DisableEvent("m60_51_36_00", INVASION_EVENT) });

        var emevd = EMEVD.Read(path);
        var target = emevd.Events.First(e => e.ID == INVASION_EVENT);
        var end = Assert.Single(target.Instructions);
        Assert.Equal(1000, end.Bank);
        Assert.Equal(4, end.ID);
        Assert.Equal(0, BitConverter.ToInt32(end.ArgData, 0)); // End, not Restart
        Assert.Empty(target.Parameters);
    }

    [Fact]
    public void Inject_LeavesEvent0AndOtherEventsUntouched()
    {
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "m60_51_36_00");

        EventDisabler.Inject(tmp.Path, new[] { new DisableEvent("m60_51_36_00", INVASION_EVENT) });

        var emevd = EMEVD.Read(path);
        var evt0 = emevd.Events.First(e => e.ID == 0);
        Assert.Equal(2, evt0.Instructions.Count);
        Assert.Equal(INVASION_EVENT, BitConverter.ToInt32(evt0.Instructions[0].ArgData, 4));
        var p = Assert.Single(evt0.Parameters);
        Assert.Equal((1L, 4L, 0L, 4L), (p.InstructionIndex, p.TargetStartByte, p.SourceStartByte, p.ByteCount));
        var other = emevd.Events.First(e => e.ID == OTHER_EVENT);
        Assert.Single(other.Instructions);
        Assert.Single(other.Parameters);
    }

    [Fact]
    public void Inject_OneOfTwoEntriesAbsent_DisablesThePresentOne()
    {
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "m60_51_36_00");

        EventDisabler.Inject(tmp.Path, new[]
        {
            new DisableEvent("m60_51_36_00", 999),
            new DisableEvent("m60_51_36_00", INVASION_EVENT),
        });

        var emevd = EMEVD.Read(path);
        Assert.Single(emevd.Events.First(e => e.ID == INVASION_EVENT).Instructions);
        Assert.Single(emevd.Events.First(e => e.ID == OTHER_EVENT).Parameters);
    }

    [Fact]
    public void Inject_EventAbsent_DoesNotRewriteFile()
    {
        // A 1.16 map file does not carry the 1.17 event: nothing to disable.
        using var tmp = new TempDir();
        var path = WriteTestEmevd(tmp.Path, "m60_51_36_00");
        var before = File.ReadAllBytes(path);

        EventDisabler.Inject(tmp.Path, new[] { new DisableEvent("m60_51_36_00", 999) });

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "speedfog-test-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void Inject_MissingFile_SkipsGracefully()
    {
        using var tmp = new TempDir();

        EventDisabler.Inject(tmp.Path, new[] { new DisableEvent("m99_00_00_00", INVASION_EVENT) });

        Assert.False(File.Exists(Path.Combine(tmp.Path, "event", "m99_00_00_00.emevd.dcx")));
    }
}
