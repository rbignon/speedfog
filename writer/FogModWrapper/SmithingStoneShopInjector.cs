using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects smithing stones (normal and somber) into Kalé's shop at game start.
/// Modifies regulation.bin after FogMod writes it.
/// </summary>
public static class SmithingStoneShopInjector
{
    // Smithing Stone item IDs (from EquipParamGoods)
    private static readonly (int id, string name, int price)[] NormalStones = new[]
    {
        (10100, "Smithing Stone [1]", 200),
        (10101, "Smithing Stone [2]", 400),
        (10102, "Smithing Stone [3]", 600),
        (10103, "Smithing Stone [4]", 800),
        (10104, "Smithing Stone [5]", 1200),
        (10105, "Smithing Stone [6]", 1600),
        (10106, "Smithing Stone [7]", 2400),
        (10107, "Smithing Stone [8]", 3600),
    };

    private static readonly (int id, string name, int price)[] SomberStones = new[]
    {
        (10160, "Somber Smithing Stone [1]", 2000),
        (10161, "Somber Smithing Stone [2]", 3000),
        (10162, "Somber Smithing Stone [3]", 4000),
        (10163, "Somber Smithing Stone [4]", 5000),
        (10164, "Somber Smithing Stone [5]", 8000),
        (10165, "Somber Smithing Stone [6]", 12000),
        // Somber [7-9] omitted - not needed for short runs, and [9] ID seems incorrect
    };

    // Base shop ID for our entries
    // Kalé's shop is 101800-101899, we use the upper range (101880-101899) for SpeedFog
    // This allows 20 items which is enough for normal (8) + somber (9) stones
    private const int BASE_SHOP_ID = 101880;

    /// <summary>
    /// Inject smithing stones into the shop.
    /// </summary>
    public static void Inject(string modDir)
    {
        var regulationPath = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(regulationPath))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping smithing stone injection");
            return;
        }

        // Paramdefs are in eldendata/Defs/ next to the executable
        var defPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eldendata", "Defs", "ShopLineupParam.xml");
        if (!File.Exists(defPath))
        {
            Console.WriteLine($"Warning: ShopLineupParam.xml not found at {defPath}, skipping smithing stone injection");
            return;
        }

        Console.WriteLine("Injecting smithing stones into merchant shop...");

        // Load the paramdef
        var paramdef = PARAMDEF.XmlDeserialize(defPath);

        // Load regulation.bin - Elden Ring uses AES encryption
        BND4 regulation;
        try
        {
            // SFUtil.DecryptERRegulation returns BND4 directly
            regulation = SFUtil.DecryptERRegulation(regulationPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decrypt regulation.bin: {ex.Message}");
            return;
        }

        // Find ShopLineupParam
        var shopFile = regulation.Files.Find(f => f.Name.EndsWith("ShopLineupParam.param"));
        if (shopFile == null)
        {
            Console.WriteLine("Warning: ShopLineupParam.param not found in regulation.bin");
            return;
        }

        // Parse the param
        var shopParam = PARAM.Read(shopFile.Bytes);
        shopParam.ApplyParamdef(paramdef);

        // Find existing IDs to avoid conflicts
        var existingIds = new HashSet<int>(shopParam.Rows.Select(r => r.ID));
        Console.WriteLine($"  ShopLineupParam has {existingIds.Count} existing entries");

        // Log existing entries in the Twin Maiden Husks range (101800-101999)
        var twinMaidenIds = shopParam.Rows
            .Where(r => r.ID >= 101800 && r.ID < 102000)
            .Select(r => r.ID)
            .OrderBy(id => id)
            .ToList();
        Console.WriteLine($"  Existing IDs in 101800-101999: {string.Join(", ", twinMaidenIds)}");

        // Use a fixed range that we CLEAR first - we'll take over 101800-101819 for our stones
        // This should be part of Kalé's shop or Twin Maiden Husks
        int baseId = 101800;
        int itemCount = NormalStones.Length + SomberStones.Length;

        // Remove any existing entries in our target range
        shopParam.Rows.RemoveAll(r => r.ID >= baseId && r.ID < baseId + itemCount);
        Console.WriteLine($"  Cleared range {baseId}-{baseId + itemCount - 1} for smithing stones");

        // Add smithing stones
        int shopId = baseId;
        foreach (var (itemId, name, price) in NormalStones)
        {
            AddShopEntry(shopParam, shopId, itemId, price);
            Console.WriteLine($"  Added {name} for {price} runes (shop ID {shopId})");
            shopId++;
        }

        foreach (var (itemId, name, price) in SomberStones)
        {
            AddShopEntry(shopParam, shopId, itemId, price);
            Console.WriteLine($"  Added {name} for {price} runes (shop ID {shopId})");
            shopId++;
        }

        // Sort rows by ID (required for game to read correctly)
        shopParam.Rows = shopParam.Rows.OrderBy(r => r.ID).ToList();

        // Write back to regulation
        shopFile.Bytes = shopParam.Write();

        // Write back - encrypt for Elden Ring
        SFUtil.EncryptERRegulation(regulationPath, regulation);

        Console.WriteLine($"Smithing stones injected successfully ({NormalStones.Length + SomberStones.Length} items)");
    }

    // FindContiguousFreeRange is now in ShopIdAllocator (FogModWrapper.Core)

    private static void AddShopEntry(PARAM param, int shopId, int itemId, int price)
    {
        // Create new row
        var row = new PARAM.Row(shopId, "", param.AppliedParamdef);

        row["equipId"].Value = itemId;
        row["value"].Value = price;
        row["mtrlId"].Value = -1;  // No material required
        row["eventFlag_forStock"].Value = 0U;  // No stock flag (always available)
        row["eventFlag_forRelease"].Value = 0U;  // No release flag (always unlocked)
        row["sellQuantity"].Value = (short)-1;  // Unlimited quantity
        row["equipType"].Value = (byte)3;  // Goods
        row["costType"].Value = (byte)0;  // Runes
        row["setNum"].Value = (ushort)1;  // 1 per purchase
        row["value_Add"].Value = 0;
        row["value_Magnification"].Value = 1f;
        row["iconId"].Value = -1;  // Use default icon
        row["nameMsgId"].Value = -1;  // Use default name
        row["menuTitleMsgId"].Value = -1;
        row["menuIconId"].Value = (short)-1;

        param.Rows.Add(row);
    }
}
