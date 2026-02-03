using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects starting resources (runes, golden seeds, sacred tears) into the game.
/// - Runes: Added to CharaInitParam for each starting class
/// - Golden Seeds/Sacred Tears: Custom ItemLots created and awarded via EMEVD
/// </summary>
public static class StartingResourcesInjector
{
    // Good IDs for consumables
    private const int GOLDEN_SEED_GOOD_ID = 10010;
    private const int SACRED_TEAR_GOOD_ID = 10020;

    // ItemLot category for Goods
    private const int ITEMLOT_CATEGORY_GOODS = 1;

    // Base ItemLot ID for our custom lots (using a safe range)
    private const int BASE_ITEMLOT_ID = 75586000;

    // Base event ID for resource events (after StartingItemInjector's range)
    private const int BASE_EVENT_ID = 755861000;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    // CharaInitParam row IDs for each starting class
    // These are the standard Elden Ring classes (3000-3009)
    private static readonly int[] STARTING_CLASS_IDS = {
        3000, // Vagabond
        3001, // Warrior
        3002, // Hero
        3003, // Bandit
        3004, // Astrologer
        3005, // Prophet
        3006, // Samurai
        3007, // Prisoner
        3008, // Confessor
        3009, // Wretch
    };

    /// <summary>
    /// Inject starting resources into the mod files.
    /// </summary>
    /// <param name="modDir">Directory containing mod files</param>
    /// <param name="runes">Amount of runes to add to starting classes (max 10,000,000)</param>
    /// <param name="goldenSeeds">Number of golden seeds to give (max 99)</param>
    /// <param name="sacredTears">Number of sacred tears to give (max 12)</param>
    public static void Inject(string modDir, int runes, int goldenSeeds, int sacredTears)
    {
        if (runes <= 0 && goldenSeeds <= 0 && sacredTears <= 0)
        {
            return;
        }

        // Clamp values to safe ranges
        if (runes > 10_000_000)
        {
            Console.WriteLine($"Warning: starting_runes capped at 10,000,000 (was {runes})");
            runes = 10_000_000;
        }
        if (goldenSeeds > 99)
        {
            Console.WriteLine($"Warning: golden_seeds capped at 99 (was {goldenSeeds})");
            goldenSeeds = 99;
        }
        if (sacredTears > 12)
        {
            Console.WriteLine($"Warning: sacred_tears capped at 12 (was {sacredTears})");
            sacredTears = 12;
        }

        Console.WriteLine("Injecting starting resources...");

        // Inject runes via CharaInitParam
        if (runes > 0)
        {
            InjectStartingRunes(modDir, runes);
        }

        // Inject golden seeds and sacred tears via ItemLots + EMEVD
        if (goldenSeeds > 0 || sacredTears > 0)
        {
            InjectConsumables(modDir, goldenSeeds, sacredTears);
        }

        Console.WriteLine("Starting resources injected successfully");
    }

    /// <summary>
    /// Add runes to each starting class in CharaInitParam.
    /// </summary>
    private static void InjectStartingRunes(string modDir, int runes)
    {
        var regulationPath = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(regulationPath))
        {
            Console.WriteLine($"Warning: regulation.bin not found at {regulationPath}, skipping rune injection");
            return;
        }

        Console.WriteLine($"  Adding {runes:N0} runes to starting classes...");

        // Load regulation.bin
        var regulation = BND4.Read(regulationPath);

        // Find CharaInitParam
        var charaInitFile = regulation.Files.Find(f => f.Name.EndsWith("CharaInitParam.param"));
        if (charaInitFile == null)
        {
            Console.WriteLine("Warning: CharaInitParam not found in regulation.bin");
            return;
        }

        // Parse the param
        var charaInit = PARAM.Read(charaInitFile.Bytes);

        // Add runes to each starting class
        int modifiedCount = 0;
        foreach (var rowId in STARTING_CLASS_IDS)
        {
            var row = charaInit.Rows.Find(r => r.ID == rowId);
            if (row == null)
            {
                continue;
            }

            // Get current soul value and add our runes (with overflow protection)
            var soulCell = row.Cells.FirstOrDefault(c => c.Def.InternalName == "soul");
            if (soulCell != null)
            {
                var currentSoul = Convert.ToInt32(soulCell.Value);
                long newSoul = (long)currentSoul + runes;
                if (newSoul > int.MaxValue)
                {
                    newSoul = int.MaxValue;
                }
                soulCell.Value = (int)newSoul;
                modifiedCount++;
            }
        }

        if (modifiedCount > 0)
        {
            // Write back to regulation
            charaInitFile.Bytes = charaInit.Write();
            regulation.Write(regulationPath);
            Console.WriteLine($"  Modified {modifiedCount} starting classes (+{runes:N0} runes each)");
        }
    }

    /// <summary>
    /// Create ItemLots for golden seeds and sacred tears, and EMEVD events to award them.
    /// </summary>
    private static void InjectConsumables(string modDir, int goldenSeeds, int sacredTears)
    {
        var regulationPath = Path.Combine(modDir, "regulation.bin");
        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");

        if (!File.Exists(regulationPath))
        {
            Console.WriteLine($"Warning: regulation.bin not found, skipping consumable injection");
            return;
        }

        if (!File.Exists(emevdPath))
        {
            Console.WriteLine($"Warning: common.emevd.dcx not found, skipping consumable injection");
            return;
        }

        // Load regulation.bin
        var regulation = BND4.Read(regulationPath);

        // Find ItemLotParam_map
        var itemLotFile = regulation.Files.Find(f => f.Name.EndsWith("ItemLotParam_map.param"));
        if (itemLotFile == null)
        {
            Console.WriteLine("Warning: ItemLotParam_map not found in regulation.bin");
            return;
        }

        var itemLotParam = PARAM.Read(itemLotFile.Bytes);

        // Load EMEVD
        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd");
            return;
        }

        int lotIndex = 0;
        int eventIndex = 0;

        // Create ItemLot and event for golden seeds
        if (goldenSeeds > 0)
        {
            var lotId = BASE_ITEMLOT_ID + lotIndex;
            var eventId = BASE_EVENT_ID + eventIndex;

            CreateItemLot(itemLotParam, lotId, GOLDEN_SEED_GOOD_ID, goldenSeeds);
            CreateAwardEvent(emevd, initEvent, eventId, lotId);

            Console.WriteLine($"  Added {goldenSeeds} Golden Seeds (ItemLot {lotId}, Event {eventId})");
            lotIndex++;
            eventIndex++;
        }

        // Create ItemLot and event for sacred tears
        if (sacredTears > 0)
        {
            var lotId = BASE_ITEMLOT_ID + lotIndex;
            var eventId = BASE_EVENT_ID + eventIndex;

            CreateItemLot(itemLotParam, lotId, SACRED_TEAR_GOOD_ID, sacredTears);
            CreateAwardEvent(emevd, initEvent, eventId, lotId);

            Console.WriteLine($"  Added {sacredTears} Sacred Tears (ItemLot {lotId}, Event {eventId})");
            lotIndex++;
            eventIndex++;
        }

        // Write back to files
        itemLotFile.Bytes = itemLotParam.Write();
        regulation.Write(regulationPath);
        emevd.Write(emevdPath);
    }

    /// <summary>
    /// Create an ItemLot row for a given good.
    /// </summary>
    private static void CreateItemLot(PARAM param, int lotId, int goodId, int quantity)
    {
        // Find an existing row to use as template (for the layout)
        var templateRow = param.Rows.FirstOrDefault();
        if (templateRow == null)
        {
            Console.WriteLine("Warning: No template row found in ItemLotParam_map");
            return;
        }

        // Create new row
        var newRow = new PARAM.Row(lotId, "", param.AppliedParamdef);

        // Set the item in slot 1
        SetCellValue(newRow, "lotItemId01", goodId);
        SetCellValue(newRow, "lotItemCategory01", ITEMLOT_CATEGORY_GOODS);
        SetCellValue(newRow, "lotItemBasePoint01", (ushort)1000); // 100% chance
        SetCellValue(newRow, "lotItemNum01", (byte)quantity);

        // Clear other slots
        for (int i = 2; i <= 8; i++)
        {
            SetCellValue(newRow, $"lotItemId0{i}", 0);
            SetCellValue(newRow, $"lotItemCategory0{i}", 0);
            SetCellValue(newRow, $"lotItemBasePoint0{i}", (ushort)0);
        }

        // No flag restriction (can always be obtained)
        SetCellValue(newRow, "getItemFlagId", 0u);

        param.Rows.Add(newRow);
    }

    /// <summary>
    /// Helper to set a cell value by name.
    /// </summary>
    private static void SetCellValue(PARAM.Row row, string cellName, object value)
    {
        var cell = row.Cells.FirstOrDefault(c => c.Def.InternalName == cellName);
        if (cell != null)
        {
            cell.Value = value;
        }
    }

    /// <summary>
    /// Create an EMEVD event that awards an ItemLot when the finger is picked up.
    /// </summary>
    private static void CreateAwardEvent(EMEVD emevd, EMEVD.Event initEvent, int eventId, int itemLotId)
    {
        var evt = new EMEVD.Event(eventId);

        // IfEventFlag(MAIN, ON, EventFlag, FINGER_PICKUP_FLAG)
        var ifFlagArgs = new byte[8];
        ifFlagArgs[0] = 0;  // MAIN condition group
        ifFlagArgs[1] = 1;  // ON state
        ifFlagArgs[2] = 0;  // EventFlag type
        ifFlagArgs[3] = 0;  // padding
        BitConverter.GetBytes(FINGER_PICKUP_FLAG).CopyTo(ifFlagArgs, 4);
        evt.Instructions.Add(new EMEVD.Instruction(3, 3, ifFlagArgs));

        // AwardItemLot(itemLotId)
        var awardArgs = new byte[4];
        BitConverter.GetBytes(itemLotId).CopyTo(awardArgs, 0);
        evt.Instructions.Add(new EMEVD.Instruction(2003, 4, awardArgs));

        emevd.Events.Add(evt);

        // Add initialization call to event 0
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);        // slot = 0
        BitConverter.GetBytes(eventId).CopyTo(initArgs, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);        // arg0 = 0
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));
    }
}
