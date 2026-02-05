using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class ResourceCalculationTests
{
    public class ConvertRunesToLordsRunesTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(-100, 0)]
        [InlineData(1, 1)]          // 1 rune -> 1 Lord's Rune (ceiling)
        [InlineData(49999, 1)]      // Just under 50k -> 1
        [InlineData(50000, 1)]      // Exactly 50k -> 1
        [InlineData(50001, 2)]      // Just over 50k -> 2
        [InlineData(100000, 2)]     // 100k -> 2
        [InlineData(150000, 3)]     // 150k -> 3
        [InlineData(1000000, 20)]   // 1M -> 20
        [InlineData(10000000, 200)] // 10M -> 200
        public void ConvertsCorrectly(int runes, int expectedLordsRunes)
        {
            var result = ResourceCalculations.ConvertRunesToLordsRunes(runes);
            Assert.Equal(expectedLordsRunes, result);
        }

        [Fact]
        public void CeilingDivision_WorksCorrectly()
        {
            // Verify ceiling division formula: (n + d - 1) / d
            // For any value 1-50000, should return 1
            for (int i = 1; i <= 50000; i += 10000)
            {
                Assert.Equal(1, ResourceCalculations.ConvertRunesToLordsRunes(i));
            }

            // For 50001-100000, should return 2
            for (int i = 50001; i <= 100000; i += 10000)
            {
                Assert.Equal(2, ResourceCalculations.ConvertRunesToLordsRunes(i));
            }
        }
    }

    public class ClampGoldenSeedsTests
    {
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(50, 50, false)]
        [InlineData(99, 99, false)]
        [InlineData(100, 99, true)]
        [InlineData(1000, 99, true)]
        [InlineData(-5, 0, false)]
        public void ClampsCorrectly(int input, int expected, bool expectedClamped)
        {
            var result = ResourceCalculations.ClampGoldenSeeds(input, out var wasClamped);

            Assert.Equal(expected, result);
            Assert.Equal(expectedClamped, wasClamped);
        }

        [Fact]
        public void MaxValue_MatchesConstant()
        {
            Assert.Equal(99, ResourceCalculations.MaxGoldenSeeds);
        }
    }

    public class ClampSacredTearsTests
    {
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(6, 6, false)]
        [InlineData(12, 12, false)]
        [InlineData(13, 12, true)]
        [InlineData(100, 12, true)]
        [InlineData(-1, 0, false)]
        public void ClampsCorrectly(int input, int expected, bool expectedClamped)
        {
            var result = ResourceCalculations.ClampSacredTears(input, out var wasClamped);

            Assert.Equal(expected, result);
            Assert.Equal(expectedClamped, wasClamped);
        }

        [Fact]
        public void MaxValue_MatchesConstant()
        {
            Assert.Equal(12, ResourceCalculations.MaxSacredTears);
        }
    }

    public class ClampLordsRunesTests
    {
        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(100, 100, false)]
        [InlineData(200, 200, false)]
        [InlineData(201, 200, true)]
        [InlineData(1000, 200, true)]
        [InlineData(-10, 0, false)]
        public void ClampsCorrectly(int input, int expected, bool expectedClamped)
        {
            var result = ResourceCalculations.ClampLordsRunes(input, out var wasClamped);

            Assert.Equal(expected, result);
            Assert.Equal(expectedClamped, wasClamped);
        }

        [Fact]
        public void MaxValue_MatchesConstant()
        {
            Assert.Equal(200, ResourceCalculations.MaxLordsRunes);
        }

        [Fact]
        public void MaxValue_Equals10MillionRunes()
        {
            var maxRunes = ResourceCalculations.LordsRunesToRunes(ResourceCalculations.MaxLordsRunes);
            Assert.Equal(10_000_000, maxRunes);
        }
    }

    public class LordsRunesToRunesTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 50000)]
        [InlineData(2, 100000)]
        [InlineData(10, 500000)]
        [InlineData(200, 10000000)]
        public void ConvertsCorrectly(int lordsRunes, int expectedRunes)
        {
            var result = ResourceCalculations.LordsRunesToRunes(lordsRunes);
            Assert.Equal(expectedRunes, result);
        }
    }

    public class ConstantsTests
    {
        [Fact]
        public void LordsRuneValue_Is50000()
        {
            Assert.Equal(50_000, ResourceCalculations.LordsRuneValue);
        }

        [Fact]
        public void Constants_AreConsistent()
        {
            // Max runes = MaxLordsRunes * LordsRuneValue = 10M
            var maxRunes = ResourceCalculations.MaxLordsRunes * ResourceCalculations.LordsRuneValue;
            Assert.Equal(10_000_000, maxRunes);
        }
    }
}
