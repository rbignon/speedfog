using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class SkinnedMaterialScannerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "winter-scan-" + Guid.NewGuid().ToString("N"));

    public SkinnedMaterialScannerTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, true);

    private static BND4 MakeBinder(params (string Name, byte[] Bytes)[] files)
    {
        var binder = new BND4();
        for (int i = 0; i < files.Length; i++)
        {
            binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, i, files[i].Name, files[i].Bytes));
        }
        return binder;
    }

    /// <summary>Writes an uncompressed binder under the fake game folder (BND4.Read accepts it).</summary>
    private void WriteBinder(string relativePath, BND4 binder) => WriteFile(relativePath, binder.Write());

    private void WriteFile(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(_temp, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Collect_AddsOnlySkinnedStems(bool useFastReader)
    {
        var binder = MakeBinder((@"N:\x\AEG001_001.flver", FlverMaterialTableTests.MakeSyntheticFlver()));
        var skinned = new HashSet<string>();

        SkinnedMaterialScanner.Collect(binder, skinned, useFastReader, new WinterReport());

        Assert.Equal(new[] { "m10_00_519" }, skinned);
    }

    [Fact]
    public void Collect_IgnoresNonFlverEntries()
    {
        var binder = MakeBinder(
            (@"N:\x\AEG001_001.hkx", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            (@"N:\x\AEG001_001.flver", FlverMaterialTableTests.MakeSyntheticFlver()));
        var skinned = new HashSet<string>();

        SkinnedMaterialScanner.Collect(binder, skinned, useFastReader: true, new WinterReport());

        Assert.Equal(new[] { "m10_00_519" }, skinned);
    }

    [Fact]
    public void Collect_ThrowsOnAnUnreadableFlver()
    {
        var binder = MakeBinder((@"N:\x\AEG001_002.flver", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

        var e = Assert.Throws<WinterBuildException>(() =>
            SkinnedMaterialScanner.Collect(binder, new HashSet<string>(), useFastReader: true, new WinterReport()));

        Assert.Contains("AEG001_002.flver", e.Message);
    }

    [Fact]
    public void EnumerateBinders_ListsGeombndsThenMapbndsSorted()
    {
        var empty = new BND4();
        WriteBinder("map/m10/m10_00_00_00/m10_00_00_00_000100.mapbnd.dcx", empty);
        WriteBinder("asset/aeg/aeg002/aeg002_000.geombnd.dcx", empty);
        WriteBinder("asset/aeg/aeg001/aeg001_001.geombnd.dcx", empty);
        WriteBinder("asset/aeg/aeg001/aeg001_001.geomhkxbnd.dcx", empty);

        var binders = SkinnedMaterialScanner.EnumerateBinders(_temp)
            .Select(p => Path.GetRelativePath(_temp, p).Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[]
        {
            "asset/aeg/aeg001/aeg001_001.geombnd.dcx",
            "asset/aeg/aeg002/aeg002_000.geombnd.dcx",
            "map/m10/m10_00_00_00/m10_00_00_00_000100.mapbnd.dcx",
        }, binders);
    }

    [Fact]
    public void EnumerateBinders_ThrowsWhenARootHasNoBinder()
    {
        WriteBinder("asset/aeg/aeg001/aeg001_001.geombnd.dcx", new BND4());

        var e = Assert.Throws<WinterBuildException>(() => SkinnedMaterialScanner.EnumerateBinders(_temp).ToList());

        Assert.Contains("mapbnd", e.Message);
    }

    [Fact]
    public void InSelfCheckSample_SpreadsOverGeombndsAndMapbnds()
    {
        // The real game's list size; EnumerateBinders puts the geombnds first.
        const int count = 24987;
        const int geombnds = 4000;
        var sample = Enumerable.Range(0, count).Where(i => SkinnedMaterialScanner.InSelfCheckSample(i, count)).ToList();

        Assert.InRange(sample.Count, SkinnedMaterialScanner.SelfCheckBinders, SkinnedMaterialScanner.SelfCheckBinders + 10);
        Assert.Equal(0, sample[0]);
        Assert.Contains(sample, i => i < geombnds);
        Assert.Contains(sample, i => i >= geombnds);
        Assert.True(sample[^1] >= count - count / SkinnedMaterialScanner.SelfCheckBinders);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(399)]
    public void InSelfCheckSample_ChecksEveryBinderOfAShortList(int count)
    {
        Assert.All(Enumerable.Range(0, count), i => Assert.True(SkinnedMaterialScanner.InSelfCheckSample(i, count)));
    }

    [Fact]
    public void Scan_CollectsSkinnedStemsOfEveryBinder()
    {
        WriteBinder("asset/aeg/aeg001/aeg001_001.geombnd.dcx",
            MakeBinder((@"N:\x\AEG001_001.flver", FlverMaterialTableTests.MakeSyntheticFlver())));
        WriteBinder("map/m21/m21_00_00_00/m21_00_00_00_000200.mapbnd.dcx",
            MakeBinder((@"N:\x\m21_00_00_00_000200.flver",
                FlverMaterialTableTests.MakeSyntheticFlver(@"N:\y\m21_00_020.matbin", @"N:\y\m21_00_021.matbin"))));
        var report = new WinterReport();
        var calls = new List<(int Done, int Total)>();

        var skinned = SkinnedMaterialScanner.Scan(_temp, report, (done, total) => calls.Add((done, total)));

        Assert.Equal(new[] { "m10_00_519", "m21_00_020" }, skinned.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(new[] { (2, 2) }, calls);
        Assert.Contains(report.Lines, l => l.Contains("2 binders") && l.Contains("2 skinned matbins"));
    }

    [Fact]
    public void Scan_StartsOverWithFlver2ReadWhenALateSelfCheckFails()
    {
        // 400 binders: every other one is sampled. The fake table reader loses the skinned
        // stems of "m10_00_bad" FLVERs: binder 1 (not sampled) is misread silently, binder 398
        // (sampled) reveals the mismatch. Only a rescan from the first binder recovers binder 1.
        const int count = 400;
        for (int i = 0; i < count - 1; i++)
        {
            var binder = i is 1 or 398
                ? MakeBinder(($@"N:\x\AEG001_{i:000}.flver", FlverMaterialTableTests.MakeSyntheticFlver($@"N:\y\m10_00_bad{i}.matbin")))
                : new BND4();
            WriteBinder($"asset/aeg/aeg001/aeg001_{i:000}.geombnd.dcx", binder);
        }
        WriteBinder("map/m10/m10_00_00_00/m10_00_00_00_000100.mapbnd.dcx", new BND4());
        Assert.False(SkinnedMaterialScanner.InSelfCheckSample(1, count));
        Assert.True(SkinnedMaterialScanner.InSelfCheckSample(398, count));
        var report = new WinterReport();

        var skinned = SkinnedMaterialScanner.Scan(_temp, report, null, bytes =>
        {
            var entries = FlverMaterialTable.Read(bytes);
            return entries.Any(e => e.MatbinStem.StartsWith("m10_00_bad", StringComparison.Ordinal))
                ? entries.Select(e => e with { Skinned = false }).ToList()
                : entries;
        });

        Assert.Equal(new[] { "m10_00_bad1", "m10_00_bad398" }, skinned.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Contains(report.Lines, l => l.Contains("disagrees") && l.Contains("aeg001_398"));
        Assert.Contains(report.Lines, l => l.Contains($"{count} binders, 2 FLVERs") && l.Contains("FLVER2.Read for every binder"));
    }

    [Fact]
    public void Scan_ThrowsOnAnUnreadableBinder()
    {
        WriteBinder("asset/aeg/aeg001/aeg001_001.geombnd.dcx", new BND4());
        WriteFile("map/m10/m10_00_00_00/m10_00_00_00_000100.mapbnd.dcx", new byte[] { 1, 2, 3, 4 });

        var e = Assert.Throws<WinterBuildException>(() => SkinnedMaterialScanner.Scan(_temp, new WinterReport()));

        Assert.Contains("m10_00_00_00_000100.mapbnd.dcx", e.Message);
    }
}
