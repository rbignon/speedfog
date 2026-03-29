using Xunit;

namespace FogModWrapper.Tests;

public class WeaponUpgradeTests
{
    [Theory]
    [InlineData(2000000, 8, true, false, 2000008)]   // Longsword +8 (regular)
    [InlineData(2000000, 0, true, false, 2000000)]   // Longsword +0 (no upgrade)
    [InlineData(2000000, 25, true, false, 2000025)]  // Longsword +25 (max regular)
    [InlineData(18000000, 8, false, true, 18000003)] // Somber weapon +8 -> somber +3
    [InlineData(18000000, 25, false, true, 18000010)] // Somber weapon +25 -> somber +10
    [InlineData(18000000, 0, false, true, 18000000)] // Somber weapon +0 (no upgrade)
    public void UpgradeWeaponId_CalculatesCorrectly(
        int weaponId, int level, bool isRegular, bool isSomber, int expected)
    {
        var regularSet = isRegular ? new HashSet<int> { weaponId - weaponId % 100 } : new HashSet<int>();
        var somberSet = isSomber ? new HashSet<int> { weaponId - weaponId % 100 } : new HashSet<int>();
        var result = WeaponUpgradeInjector.UpgradeWeaponId(weaponId, level, regularSet, somberSet);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UpgradeWeaponId_UnknownWeapon_ReturnsOriginal()
    {
        var result = WeaponUpgradeInjector.UpgradeWeaponId(
            9999999, 8, new HashSet<int>(), new HashSet<int>());
        Assert.Equal(9999999, result);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 3)]
    [InlineData(10, 4)]
    [InlineData(15, 6)]
    [InlineData(20, 8)]
    [InlineData(25, 10)]
    public void SomberUpgrade_MatchesCarePackageFormula(int standard, int expectedSomber)
    {
        Assert.Equal(expectedSomber, WeaponUpgradeInjector.SomberUpgrade(standard));
    }

    [Fact]
    public void UpgradeWeaponId_AlreadyUpgraded_StripsAndReapplies()
    {
        var regularSet = new HashSet<int> { 2000000 };
        var result = WeaponUpgradeInjector.UpgradeWeaponId(2000005, 8, regularSet, new HashSet<int>());
        Assert.Equal(2000008, result);
    }

    // Custom weapon (ash of war) upgrade tests

    [Theory]
    [InlineData(2000000, 8, true, false, 8)]    // Regular base weapon -> level 8
    [InlineData(18000000, 8, false, true, 3)]    // Somber base weapon -> somber +3
    [InlineData(2000000, 25, true, false, 25)]   // Regular max -> level 25
    [InlineData(18000000, 25, false, true, 10)]  // Somber max -> somber +10
    [InlineData(2000000, 0, true, false, 0)]     // No upgrade
    public void CustomWeaponUpgradeLevel_CalculatesCorrectly(
        int baseWepId, int level, bool isRegular, bool isSomber, int expected)
    {
        var regularSet = isRegular ? new HashSet<int> { baseWepId } : new HashSet<int>();
        var somberSet = isSomber ? new HashSet<int> { baseWepId } : new HashSet<int>();
        var result = WeaponUpgradeInjector.CustomWeaponUpgradeLevel(baseWepId, level, regularSet, somberSet);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CustomWeaponUpgradeLevel_UnknownBaseWeapon_ReturnsNegativeOne()
    {
        var result = WeaponUpgradeInjector.CustomWeaponUpgradeLevel(
            9999999, 8, new HashSet<int>(), new HashSet<int>());
        Assert.Equal(-1, result);
    }
}
