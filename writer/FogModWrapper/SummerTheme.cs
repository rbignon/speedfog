using System.Linq;
using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// "summer" plugin: reskins boss healthbar names (NpcName) and recurring UI
/// banners to a summer theme by editing FMG entries, mirroring
/// RunCompleteInjector. Runs only when [plugin.summer] enabled = true. Bosses
/// or UI ids absent from the game's FMGs are skipped (tolerant).
///
/// Only the English (engus) and French (frafr) message archives are edited;
/// the catalogue carries content for those two languages only, and touching
/// all ~15 game languages tripled the per-seed cost for no benefit. Other
/// languages keep their vanilla names.
/// </summary>
public static class SummerTheme
{
    private static readonly string[] BossBnds = { "item.msgbnd.dcx", "item_dlc02.msgbnd.dcx" };

    // Only languages we actually author for. engus -> en, frafr -> fr (else en).
    private static readonly HashSet<string> TargetLanguages = new() { "engus", "frafr" };

    /// <summary>frafr gets fr when present; every other language (incl. engus) gets en.</summary>
    public static string LocalizedText(string langName, string en, string? fr)
        => langName == "frafr" && !string.IsNullOrEmpty(fr) ? fr! : en;

    public static void Apply(string modDir, string gameDir, string dataDir)
    {
        var catalog = SummerCatalogLoader.Load(Path.Combine(dataDir, "plugins", "summer.toml"));
        if (catalog.IsEmpty)
            return;

        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            Console.WriteLine("Summer theme: game msg directory not found, skipping");
            return;
        }

        var bossById = catalog.Bosses.ToDictionary(b => b.NpcNameId);
        int touchedLangs = 0;
        foreach (var langDir in Directory.GetDirectories(gameMsgDir))
        {
            var lang = Path.GetFileName(langDir);
            if (!TargetLanguages.Contains(lang))
                continue;
            int n = ApplyBossEpithets(modDir, langDir, lang, bossById)
                  + ApplyUiStrings(modDir, langDir, lang, catalog.Ui);
            if (n > 0)
                touchedLangs++;
        }

        Console.WriteLine(
            $"Summer theme: applied {catalog.Bosses.Count} boss + {catalog.Ui.Count} UI overrides across {touchedLangs} languages");
    }

    private static int ApplyBossEpithets(string modDir, string langDir, string lang,
        IReadOnlyDictionary<int, SummerBossEntry> bossById)
    {
        int total = 0;
        foreach (var bndName in BossBnds)
            total += EditBnd(modDir, langDir, lang, bndName, bnd =>
            {
                int n = 0;
                foreach (var file in bnd.Files.Where(f => f.Name.Contains("NpcName")))
                {
                    FMG fmg;
                    try
                    { fmg = FMG.Read(file.Bytes); }
                    catch { continue; }

                    bool changed = false;
                    foreach (var entry in fmg.Entries)
                    {
                        if (entry.Text == null)
                            continue;
                        if (bossById.TryGetValue(entry.ID, out var b))
                        {
                            entry.Text = LocalizedText(lang, b.En, b.Fr);
                            changed = true;
                            n++;
                        }
                    }
                    if (changed)
                        file.Bytes = fmg.Write();
                }
                return n;
            });
        return total;
    }

    private static int ApplyUiStrings(string modDir, string langDir, string lang,
        IReadOnlyList<SummerUiEntry> ui)
    {
        int total = 0;
        foreach (var group in ui.GroupBy(u => u.Bnd))
            total += EditBnd(modDir, langDir, lang, group.Key, bnd =>
            {
                int n = 0;
                foreach (var u in group)
                {
                    var file = bnd.Files.Find(f => f.Name.Contains(u.Fmg));
                    if (file == null)
                        continue;

                    FMG fmg;
                    try
                    { fmg = FMG.Read(file.Bytes); }
                    catch { continue; }

                    var text = LocalizedText(lang, u.En, u.Fr);
                    var existing = fmg.Entries.Find(e => e.ID == u.Id);
                    if (existing != null)
                        existing.Text = text;
                    else
                        fmg.Entries.Add(new FMG.Entry(u.Id, text));

                    file.Bytes = fmg.Write();
                    n++;
                }
                return n;
            });
        return total;
    }

    // Reads source (mod copy if present, else vanilla), runs edit, writes back
    // to mod dir only when edit reports changes. Returns number of edits.
    private static int EditBnd(string modDir, string langDir, string lang, string bndName,
        Func<BND4, int> edit)
    {
        var vanillaPath = Path.Combine(langDir, bndName);
        if (!File.Exists(vanillaPath))
            return 0;

        var modPath = Path.Combine(modDir, "msg", lang, bndName);
        var sourcePath = File.Exists(modPath) ? modPath : vanillaPath;

        var bnd = BND4.Read(sourcePath);
        int n = edit(bnd);
        if (n == 0)
            return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
        bnd.Write(modPath);
        return n;
    }
}
