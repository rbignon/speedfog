using FogModWrapper.Common;
using SoulsFormats;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>
/// Writes the race items' name, info and caption (FMG entry id = goods id)
/// into the base GoodsName / GoodsInfo / GoodsCaption FMGs of
/// item_dlc02.msgbnd.dcx, for engus and frafr (the languages MsgBndEditor
/// authors). The text is English in both. Same add-or-replace pattern as
/// TextTheme.ApplyUiStrings.
/// </summary>
public static class RaceItemsTextInjector
{
    private const string BndName = "item_dlc02.msgbnd.dcx";

    public static void Apply(string modDir, string gameDir, IReadOnlyList<RaceItem> items)
    {
        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            Console.WriteLine("race_items: game msg directory not found, skipping item texts");
            return;
        }

        foreach (var langDir in Directory.GetDirectories(gameMsgDir))
        {
            var lang = Path.GetFileName(langDir);
            if (!MsgBndEditor.TargetLanguages.Contains(lang))
                continue;

            MsgBndEditor.EditBnd(modDir, langDir, lang, BndName, bnd =>
            {
                int n = 0;
                n += SetEntries(bnd, "GoodsName.fmg", items, i => i.Name);
                n += SetEntries(bnd, "GoodsInfo.fmg", items, i => i.Info);
                n += SetEntries(bnd, "GoodsCaption.fmg", items, i => i.Caption);
                return n;
            });
        }
    }

    private static int SetEntries(BND4 bnd, string fmgName, IReadOnlyList<RaceItem> items,
        Func<RaceItem, string> text)
    {
        // Exact file name: GoodsName_dlc01.fmg / _dlc02.fmg also contain "GoodsName".
        var file = bnd.Files.Find(f =>
            Path.GetFileName(f.Name.Replace('\\', '/')) == fmgName);
        if (file == null)
            return 0;

        var fmg = FMG.Read(file.Bytes);
        foreach (var item in items)
        {
            var existing = fmg.Entries.Find(e => e.ID == item.GoodsId);
            if (existing != null)
                existing.Text = text(item);
            else
                fmg.Entries.Add(new FMG.Entry(item.GoodsId, text(item)));
        }
        file.Bytes = fmg.Write();
        return items.Count;
    }
}
