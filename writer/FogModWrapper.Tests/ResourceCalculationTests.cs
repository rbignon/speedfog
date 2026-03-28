using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class ResourceCalculationTests
{
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
}
