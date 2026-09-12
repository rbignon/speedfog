using System.Text.RegularExpressions;
using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Boss healthbar names of the enemies the randomizer relocates (graph.json
/// boss_names, see docs/boss-healthbar-names.md). The healthbar name comes
/// from the nameId argument of DisplayBossHealthBar (2003[11]); the enemy
/// randomizer rewrites it only for sources whose own healthbar events it
/// copies into the arena, so a source without such events (a regular mob, a
/// hostile NPC) leaves the arena's vanilla name in place ("Ancient Hero of
/// Zamor" over a placed Hornsent). Per arena: the display name is resolved
/// to a NpcName id (an exact vanilla engus match first, so "Crucible Knight"
/// stays localized everywhere, else a SpeedFog id whose entry is written to
/// engus + frafr, English text in both), then every 2003[11] of the arena
/// entity is repointed at it, in every EMEVD of the mod dir that holds one
/// (an arena's healthbar can be driven from a neighbouring tile or from a
/// common event).
///
/// An instruction already displaying the placed enemy's name is left
/// untouched, including when it uses another of the several vanilla ids
/// carrying that text, so the pass is a no-op wherever the randomizer did
/// the job and the invariant holds for the rest. Instructions whose nameId
/// is an event parameter are left alone (none of the tagged arenas uses
/// one).
///
/// New entries go to the base NpcName.fmg of item_dlc02.msgbnd.dcx, the
/// bundle the game resolves text from (it carries a full copy of the base
/// FMGs; FogMod and the Item Randomizer write only the _dlc02 bundles, and
/// the base item.msgbnd.dcx is not consulted when the DLC one is present).
/// </summary>
public static class BossNameInjector
{
    private static readonly string[] NpcNameBnds = { "item.msgbnd.dcx", "item_dlc02.msgbnd.dcx" };
    private const string NewEntriesBnd = "item_dlc02.msgbnd.dcx";
    private const string NpcNameFmg = "NpcName.fmg";

    private const int HealthbarBank = 2003;
    private const int HealthbarId = 11;
    private const int EntityOffset = 4;
    private const int NameIdOffset = 12;

    /// <summary>Trailing " (...)" of a boss_arena_tags disambiguation name
    /// ("Hornsent (Leda Fight)"). Vanilla variant names of the same shape
    /// ("Mad Pumpkin Head (Hammer)") are kept: the full text is looked up
    /// first.</summary>
    private static readonly Regex ParentheticalSuffix = new(@"\s*\([^()]*\)$", RegexOptions.Compiled);

    /// <summary>One arena's resolved healthbar name.</summary>
    public sealed record Resolution(uint ArenaId, string Name, string Map, int NameId, bool IsNew);

    public static void Inject(string modDir, string gameDir,
        IReadOnlyDictionary<string, BossNameEntry> bossNames, Action<string> log)
    {
        if (bossNames.Count == 0)
            return;

        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            log("Boss names: game msg directory not found, skipping");
            return;
        }

        var (idByText, textById) = LoadVanillaNpcNames(gameMsgDir);
        var resolved = ResolveNameIds(bossNames, idByText);

        // One pass over every EMEVD of the mod dir: an arena's healthbar can
        // be driven from its own map, from a neighbouring tile (a large-tile
        // boss) or from a common event, and several files can hold a copy.
        var held = new HashSet<uint>();
        var patched = new HashSet<uint>();
        var eventDir = Path.Combine(modDir, "event");
        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx").OrderBy(f => f, StringComparer.Ordinal))
        {
            var mapId = Path.GetFileName(emevdPath).Replace(".emevd.dcx", "", StringComparison.OrdinalIgnoreCase);
            var emevd = EMEVD.Read(emevdPath);
            int total = 0;
            foreach (var r in resolved)
            {
                var (rewritten, holds) = PatchEmevd(emevd, r.ArenaId, r.NameId, r.Name, textById);
                if (!holds)
                    continue;
                held.Add(r.ArenaId);
                if (rewritten == 0)
                    continue;
                log($"  {mapId}: arena {r.ArenaId} -> \"{r.Name}\" (NpcName {r.NameId}, {(r.IsNew ? "new" : "vanilla")}, {rewritten} instruction(s))");
                patched.Add(r.ArenaId);
                total += rewritten;
            }
            if (total > 0)
                emevd.Write(emevdPath);
        }

        foreach (var r in resolved.Where(r => !patched.Contains(r.ArenaId)))
        {
            if (held.Contains(r.ArenaId))
                log($"  arena {r.ArenaId} already names \"{r.Name}\"");
            else
                log($"  Warning: arena {r.ArenaId} (\"{r.Name}\") has no DisplayBossHealthBar in any EMEVD "
                    + $"of the mod dir (declared map {r.Map}), healthbar name kept");
        }

        // FMG entries only for the names a patch actually used: an arena the
        // randomizer already named, or one we could not reach, ships none.
        var newEntries = resolved.Where(r => r.IsNew && patched.Contains(r.ArenaId))
            .GroupBy(r => r.NameId)
            .Select(g => (id: g.Key, text: g.First().Name))
            .OrderBy(e => e.id)
            .ToList();
        int languages = WriteNpcNameEntries(modDir, gameMsgDir, newEntries);

        int alreadyCorrect = held.Count - patched.Count;
        int unmatched = resolved.Count - held.Count;
        log($"Boss names: {patched.Count} arena(s) patched, {alreadyCorrect} already correct, "
            + $"{unmatched} unmatched, {newEntries.Count} new NpcName entr{(newEntries.Count == 1 ? "y" : "ies")} "
            + $"in {languages} language(s)");
    }

    /// <summary>Resolves each arena's name to a NpcName id: the lowest vanilla
    /// entry with exactly that English text when one exists, else the same
    /// lookup without a trailing parenthetical (the boss_arena_tags
    /// disambiguation suffix, "Hornsent (Leda Fight)"; a vanilla variant name
    /// such as "Mad Pumpkin Head (Hammer)" matched on the first try and is
    /// kept), else a SpeedFogIds.BossNameFmgIds id shared by every arena with
    /// the same name, allocated in ascending arena id order (deterministic
    /// across runs). The returned Name is the text that ends up displayed.
    /// Throws when the distinct new names exceed the range.</summary>
    public static IReadOnlyList<Resolution> ResolveNameIds(
        IReadOnlyDictionary<string, BossNameEntry> bossNames,
        IReadOnlyDictionary<string, int> vanillaNameIds)
    {
        var range = SpeedFogIds.BossNameFmgIds;
        var allocated = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<Resolution>();
        foreach (var (key, entry) in bossNames.Select(kv => (ParseArenaId(kv.Key), kv.Value)).OrderBy(kv => kv.Item1))
        {
            var full = entry.Name.Trim();
            if (vanillaNameIds.TryGetValue(full, out int vanillaId))
            {
                result.Add(new Resolution(key, full, entry.Map, vanillaId, IsNew: false));
                continue;
            }
            var stripped = ParentheticalSuffix.Replace(full, "").Trim();
            var name = stripped.Length > 0 ? stripped : full;
            if (name != full && vanillaNameIds.TryGetValue(name, out int strippedId))
            {
                result.Add(new Resolution(key, name, entry.Map, strippedId, IsNew: false));
                continue;
            }
            if (!allocated.TryGetValue(name, out int id))
            {
                if (allocated.Count >= range.Capacity)
                {
                    throw new InvalidOperationException(
                        $"Boss name NpcName budget exceeded: {allocated.Count + 1} distinct names " +
                        $"(max {range.Capacity}, next range starts at {range.End})");
                }
                id = range.Base + allocated.Count;
                allocated[name] = id;
            }
            result.Add(new Resolution(key, name, entry.Map, id, IsNew: true));
        }
        return result;
    }

    private static uint ParseArenaId(string key)
        => uint.TryParse(key, out uint id)
            ? id
            : throw new InvalidOperationException($"graph.json boss_names: arena key \"{key}\" is not an entity id");

    /// <summary>Both directions of the engus NpcName index, over every NpcName
    /// FMG of the item bnds (base game + DLC): each text to its lowest id
    /// (what a new healthbar should point at) and each id to its text (what
    /// an existing healthbar displays). Several ids share a text in vanilla
    /// ("Ancient Hero of Zamor" has three), so the second map cannot be
    /// derived from the first.</summary>
    public static (Dictionary<string, int> IdByText, Dictionary<int, string> TextById)
        LoadVanillaNpcNames(string gameMsgDir)
    {
        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        var byId = new Dictionary<int, string>();
        var engusDir = Path.Combine(gameMsgDir, "engus");
        foreach (var bndName in NpcNameBnds)
        {
            var path = Path.Combine(engusDir, bndName);
            if (!File.Exists(path))
                continue;
            var bnd = BND4.Read(path);
            foreach (var file in bnd.Files.Where(f => f.Name.Contains("NpcName")))
            {
                FMG fmg;
                try
                { fmg = FMG.Read(file.Bytes); }
                catch { continue; }

                foreach (var entry in fmg.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Text))
                        continue;
                    var text = entry.Text.Trim();
                    if (!byText.TryGetValue(text, out int existing) || entry.ID < existing)
                        byText[text] = entry.ID;
                    byId[entry.ID] = text;
                }
            }
        }
        return (byText, byId);
    }

    /// <summary>Repoints the nameId of every DisplayBossHealthBar (2003[11])
    /// of <paramref name="arenaId"/>, enabling and disabling calls alike. An
    /// instruction whose current nameId already displays
    /// <paramref name="name"/> is left as it is, so a healthbar the
    /// randomizer already named keeps its own (localized) id. Instructions
    /// whose nameId is bound to an event parameter are skipped (the literal
    /// bytes are dead). Returns the number of instructions rewritten and
    /// whether this EMEVD holds the arena's healthbar at all.</summary>
    public static (int Rewritten, bool Held) PatchEmevd(EMEVD emevd, uint arenaId, int nameId,
        string name, IReadOnlyDictionary<int, string> textByNameId)
    {
        int n = 0;
        bool held = false;
        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                if (instr.Bank != HealthbarBank || instr.ID != HealthbarId || instr.ArgData.Length < NameIdOffset + 4)
                    continue;
                if (BitConverter.ToUInt32(instr.ArgData, EntityOffset) != arenaId)
                    continue;
                if (evt.Parameters.Any(p => p.InstructionIndex == i && p.TargetStartByte == NameIdOffset))
                    continue;
                held = true;
                int current = BitConverter.ToInt32(instr.ArgData, NameIdOffset);
                if (current == nameId
                    || (textByNameId.TryGetValue(current, out var text) && string.Equals(text, name, StringComparison.Ordinal)))
                    continue;
                BitConverter.GetBytes(nameId).CopyTo(instr.ArgData, NameIdOffset);
                n++;
            }
        }
        return (n, held);
    }

    /// <summary>Adds (or overwrites) the entries in the base NpcName.fmg of
    /// item_dlc02.msgbnd.dcx for engus and frafr, English text in both.
    /// Returns the number of languages written.</summary>
    private static int WriteNpcNameEntries(string modDir, string gameMsgDir,
        IReadOnlyList<(int id, string text)> entries)
    {
        if (entries.Count == 0)
            return 0;

        int languages = 0;
        foreach (var lang in MsgBndEditor.TargetLanguages.OrderBy(l => l, StringComparer.Ordinal))
        {
            var langDir = Path.Combine(gameMsgDir, lang);
            if (!Directory.Exists(langDir))
                continue;
            int n = MsgBndEditor.EditBnd(modDir, langDir, lang, NewEntriesBnd, bnd =>
            {
                var file = bnd.Files.Find(f => f.Name.EndsWith(NpcNameFmg, StringComparison.OrdinalIgnoreCase));
                if (file == null)
                    return 0;
                var fmg = FMG.Read(file.Bytes);
                foreach (var (id, text) in entries)
                {
                    var existing = fmg.Entries.Find(e => e.ID == id);
                    if (existing != null)
                        existing.Text = text;
                    else
                        fmg.Entries.Add(new FMG.Entry(id, text));
                }
                file.Bytes = fmg.Write();
                return entries.Count;
            });
            if (n > 0)
                languages++;
        }
        return languages;
    }
}
