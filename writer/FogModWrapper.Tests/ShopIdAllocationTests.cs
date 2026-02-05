using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class ShopIdAllocationTests
{
    [Fact]
    public void FindContiguousFreeRange_EmptySet_ReturnsMinId()
    {
        var existing = new HashSet<int>();

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(100, result);
    }

    [Fact]
    public void FindContiguousFreeRange_AllOccupied_ReturnsMinusOne()
    {
        var existing = new HashSet<int>(Enumerable.Range(100, 101)); // 100-200 all taken

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindContiguousFreeRange_GapAtStart_ReturnsMinId()
    {
        var existing = new HashSet<int> { 105, 106, 107, 108, 109 };

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(100, result);
    }

    [Fact]
    public void FindContiguousFreeRange_GapInMiddle_ReturnsGapStart()
    {
        var existing = new HashSet<int> { 100, 101, 102, 108, 109 }; // Gap at 103-107

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(103, result);
    }

    [Fact]
    public void FindContiguousFreeRange_GapAtEnd_ReturnsGapStart()
    {
        var existing = new HashSet<int>(Enumerable.Range(100, 90)); // 100-189 taken, 190-199 free

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(190, result);
    }

    [Fact]
    public void FindContiguousFreeRange_ExactFit_ReturnsStart()
    {
        var existing = new HashSet<int> { 100, 101, 107, 108 }; // Gap 102-106 = exactly 5 slots

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(102, result);
    }

    [Fact]
    public void FindContiguousFreeRange_NotEnoughContiguous_ReturnsMinusOne()
    {
        // Gaps of size 4, but we need 5
        var existing = new HashSet<int> { 100, 105, 110, 115, 120 };

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 125, 5);

        // Available gaps: 101-104 (4), 106-109 (4), 111-114 (4), 116-119 (4), 121-124 (4)
        // None is 5 contiguous
        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindContiguousFreeRange_SingleSlotNeeded_FindsFirst()
    {
        var existing = new HashSet<int> { 100, 102, 103, 104 };

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 1);

        Assert.Equal(101, result);
    }

    [Fact]
    public void FindContiguousFreeRange_BoundaryCondition_RespectsBounds()
    {
        var existing = new HashSet<int>();

        // Need 10 slots, range is 100-105 (only 6 slots total)
        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 105, 10);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void FindContiguousFreeRange_LargeGap_ReturnsFirstAvailable()
    {
        var existing = new HashSet<int> { 100, 150 }; // Huge gap 101-149

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 20);

        Assert.Equal(101, result);
    }

    [Fact]
    public void FindContiguousFreeRange_MultipleSuitableGaps_ReturnsFirst()
    {
        // Two gaps that both fit: 105-109 (5) and 120-124 (5)
        var existing = new HashSet<int>(Enumerable.Range(100, 5).Concat(Enumerable.Range(110, 10)));

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 5);

        Assert.Equal(105, result);
    }

    [Fact]
    public void FindContiguousFreeRange_ZeroCount_ReturnsMinId()
    {
        var existing = new HashSet<int> { 100, 101, 102 };

        // Edge case: asking for 0 items should return minId
        var result = ShopIdAllocator.FindContiguousFreeRange(existing, 100, 200, 0);

        Assert.Equal(100, result);
    }

    [Theory]
    [InlineData(101800, 101900, 14)] // Real use case: smithing stones (8 normal + 6 somber)
    public void FindContiguousFreeRange_RealWorldScenario(int minId, int maxId, int count)
    {
        // Simulate Twin Maiden Husks shop with some existing entries
        var existing = new HashSet<int> { 101810, 101811, 101812, 101850 };

        var result = ShopIdAllocator.FindContiguousFreeRange(existing, minId, maxId, count);

        // Should find a gap of 14+ starting at 101800 (before 101810)
        Assert.True(result >= minId);
        Assert.True(result + count <= maxId);

        // Verify no conflicts
        for (int i = 0; i < count; i++)
        {
            Assert.DoesNotContain(result + i, existing);
        }
    }
}
