namespace FogModWrapper.Tests;

/// <summary>
/// Disposable temp directory helper shared by injector tests that need a
/// real filesystem tree (mod dir, merge dir, ...) without a full seed
/// output. Promoted here from six near-identical private copies
/// (UntouchableBossInjectorTests, VanillaWarpRemoverTests,
/// VanillaMapPinnerTests, SpiritspringRemoverTests, StartupFlagInjectorTests,
/// EventDisablerTests).
/// </summary>
internal sealed class TempDir : IDisposable
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
