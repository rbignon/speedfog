using SoulsFormats;

namespace StaticModBuilder.Winter;

public sealed record TpfResult(int Frosted, int Reduced, int Untouched, int Warnings);

/// <summary>
/// Builds the half-resolution winter texture packs: albedos are frosted (BC1), the other
/// textures lose their top mip level, small textures are left alone.
/// </summary>
public static class WinterTextureBuilder
{
    /// <summary>TPF texture format byte for BC1 (DXT1).</summary>
    public const byte TpfFormatBc1 = 1;

    public static bool IsAlbedo(string textureName) =>
        textureName.EndsWith("_a", StringComparison.OrdinalIgnoreCase)
        || textureName.EndsWith("_a_l", StringComparison.OrdinalIgnoreCase);

    public static TpfResult ProcessTpf(TPF tpf, string tpfName, WinterReport report)
    {
        int frosted = 0, reduced = 0, untouched = 0, warnings = 0;
        foreach (var texture in tpf.Textures)
        {
            DdsHeaderInfo info;
            try
            {
                info = DdsImage.Parse(texture.Bytes);
            }
            catch (InvalidDataException e)
            {
                throw new WinterBuildException($"{tpfName}/{texture.Name}: {e.Message}");
            }

            int native = Math.Max(info.Width, info.Height);
            int drop = DdsImage.LevelsToDrop(native);
            if (IsAlbedo(texture.Name))
            {
                texture.Bytes = FrostEncoder.FrostAlbedo(texture.Bytes, DdsImage.TargetLongSide(native));
                texture.Format = TpfFormatBc1;
                frosted++;
            }
            else if (drop == 1 && info.MipCount >= 2)
            {
                texture.Bytes = DdsImage.DropTopMips(texture.Bytes, 1);
                reduced++;
            }
            else if (drop == 1)
            {
                report.Line($"warning: {tpfName}/{texture.Name}: single mip level, left at full resolution");
                untouched++;
                warnings++;
            }
            else
            {
                untouched++;
            }
        }
        return new TpfResult(frosted, reduced, untouched, warnings);
    }

    public static int Build(string gameDir, string winterDir, IReadOnlyList<string> tpfStems, WinterReport report)
    {
        int written = 0;
        int totalFrosted = 0, totalReduced = 0, totalUntouched = 0;
        foreach (var stem in tpfStems)
        {
            string family = stem[..6];
            string sourceDir = Path.Combine(gameDir, "asset", "aet", family);
            string mainName = $"{stem}.tpf.dcx";
            if (!File.Exists(Path.Combine(sourceDir, mainName)))
            {
                throw new WinterBuildException($"{stem}: missing {Path.Combine(sourceDir, mainName)}");
            }

            int frosted = 0, reduced = 0, untouched = 0;
            foreach (var fileName in new[] { mainName, $"{stem}_l.tpf.dcx" })
            {
                string source = Path.Combine(sourceDir, fileName);
                if (!File.Exists(source))
                {
                    report.Line($"warning: {fileName}: low-resolution twin not found, skipped");
                    continue;
                }

                var tpf = TPF.Read(source);
                var result = ProcessTpf(tpf, fileName, report);
                string destDir = Path.Combine(winterDir, "asset", "aet", family);
                Directory.CreateDirectory(destDir);
                tpf.Write(Path.Combine(destDir, fileName));
                written++;
                frosted += result.Frosted;
                reduced += result.Reduced;
                untouched += result.Untouched;
            }

            report.Line($"{stem}: {frosted} frosted, {reduced} reduced, {untouched} untouched");
            totalFrosted += frosted;
            totalReduced += reduced;
            totalUntouched += untouched;
        }
        report.Line($"total: {written} TPF(s), {totalFrosted} frosted, {totalReduced} reduced, {totalUntouched} untouched");
        return written;
    }
}
