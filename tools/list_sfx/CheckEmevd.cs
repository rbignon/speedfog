static class CheckEmevd
{
    public static void Run(string emevdPath, uint targetEntity)
    {
        var emevd = SoulsFormats.EMEVD.Read(emevdPath);
        var evt0 = emevd.Events.Find(e => e.ID == 0);
        if (evt0 == null) { System.Console.WriteLine("Event 0 not found"); return; }

        System.Console.WriteLine($"Event 0: {evt0.Instructions.Count} instructions");
        int found = 0;
        for (int i = 0; i < evt0.Instructions.Count; i++)
        {
            var instr = evt0.Instructions[i];
            if (instr.ArgData.Length >= 4)
            {
                uint eid = System.BitConverter.ToUInt32(instr.ArgData, 0);
                if (eid == targetEntity || (targetEntity == 0 && eid >= 755895000 && eid < 755896000))
                {
                    System.Console.WriteLine($"  [{i}] Bank={instr.Bank} ID={instr.ID} entity={eid}");
                    found++;
                }
            }
        }
        if (found == 0)
            System.Console.WriteLine($"  No instructions found for entity {targetEntity} (or range 755895xxx)");
    }
}
