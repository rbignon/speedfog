namespace FogModWrapper;

/// <summary>
/// Utility functions for allocating shop IDs.
/// Currently not used by SmithingStoneShopInjector (which uses fixed ranges),
/// but available for dynamic ID allocation if needed in the future.
/// </summary>
public static class ShopIdAllocator
{
    /// <summary>
    /// Find a contiguous range of free IDs within the given bounds.
    /// </summary>
    /// <param name="existingIds">Set of IDs already in use</param>
    /// <param name="minId">Minimum ID to consider</param>
    /// <param name="maxId">Maximum ID to consider</param>
    /// <param name="count">Number of contiguous IDs needed</param>
    /// <returns>Starting ID of free range, or -1 if not found</returns>
    public static int FindContiguousFreeRange(HashSet<int> existingIds, int minId, int maxId, int count)
    {
        int start = minId;
        while (start + count <= maxId)
        {
            bool found = true;
            for (int i = 0; i < count; i++)
            {
                if (existingIds.Contains(start + i))
                {
                    // Skip to after this conflict
                    start = start + i + 1;
                    found = false;
                    break;
                }
            }
            if (found)
            {
                return start;
            }
        }
        return -1;
    }
}
