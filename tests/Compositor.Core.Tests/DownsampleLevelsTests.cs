using Compositor.Core.Imaging;
using Compositor.Core.Rendering;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Level arithmetic for the downsample cache. Every expected number here is derived by hand from the
/// upstream rule (min(6, floor(log2(1/factor)))) and the 2x halving rule, never recorded from the engine.
/// </summary>
public class DownsampleLevelsTests
{
    [Theory]
    [InlineData(0.5, 0)]      // half size: the scaler can do that in one step
    [InlineData(0.75, 0)]
    [InlineData(1.0, 0)]
    [InlineData(2.0, 0)]
    [InlineData(0.49, 1)]     // log2(1/0.49) = 1.029 -> 1
    [InlineData(0.25, 2)]
    [InlineData(0.2, 2)]      // log2(5) = 2.32 -> 2
    [InlineData(0.125, 3)]
    [InlineData(0.1, 3)]      // log2(10) = 3.32 -> 3
    [InlineData(0.01, 6)]     // log2(100) = 6.64 -> clamped to maxLevel
    [InlineData(0.001, 6)]
    [InlineData(0.0, 0)]
    [InlineData(-0.25, 0)]
    public void For_MatchesTheHandDerivedLevel(double factor, int expected)
        => Assert.Equal(expected, DownsampleLevels.For(factor));

    [Fact]
    public void For_RejectsNonFiniteFactors()
    {
        Assert.Equal(0, DownsampleLevels.For(double.NaN));
        Assert.Equal(0, DownsampleLevels.For(double.PositiveInfinity));
        Assert.Equal(0, DownsampleLevels.For(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(8, 8, 4, 4)]
    [InlineData(7, 7, 4, 4)]      // rounds up
    [InlineData(1, 1, 1, 1)]      // a 1px image cannot shrink
    [InlineData(9, 1, 5, 1)]
    [InlineData(2, 3, 1, 2)]
    public void Halve_RoundsUp(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), DownsampleLevels.Halve(width, height));

    [Fact]
    public void AtLevel_MatchesRepeatedHalving()
    {
        // 1000 -> 500 -> 250 -> 125 and 500 -> 250 -> 125 -> 63
        Assert.Equal((125, 63), DownsampleLevels.AtLevel(1000, 500, 3));
        Assert.Equal((1000, 500), DownsampleLevels.AtLevel(1000, 500, 0));
        Assert.Equal((500, 250), DownsampleLevels.AtLevel(1000, 500, 1));
    }

    [Theory]
    [InlineData(0, 5, 5, 6)]
    [InlineData(1, 5, 10, 12)]
    [InlineData(3, 2, 16, 24)]
    [InlineData(6, 1, 64, 128)]
    public void Covers_IsIndexTimesTwoToTheLevel(int level, int index, int start, int end)
        => Assert.Equal((start, end), DownsampleLevels.Covers(level, index));

    [Fact]
    public void Covers_TilesTheSourceWithoutGaps()
    {
        for (var level = 0; level <= DownsampleLevels.MaxLevel; level++)
        {
            var previousEnd = 0;
            for (var i = 0; i < 8; i++)
            {
                var (start, end) = DownsampleLevels.Covers(level, i);
                Assert.Equal(previousEnd, start);
                Assert.Equal(1 << level, end - start);
                previousEnd = end;
            }
        }
    }

    [Fact]
    public void LevelTwoRamp_ReducesToTheHandComputedBlocks()
    {
        // Hand-derived fixture: source pixel x holds x. A level-2 reduction (span 4) must give pixel i the
        // mean over [4i, 4i+4), which for a ramp is (4i + (4i+1) + (4i+2) + (4i+3)) / 4 = 4i + 1.5.
        const int width = 64;
        var ramp = new byte[width];
        for (var x = 0; x < width; x++) ramp[x] = (byte)x;

        Assert.Equal(16, DownsampleLevels.AtLevel(width, 1, 2).Width);

        var reduced = new double[width / 4];
        for (var i = 0; i < reduced.Length; i++)
        {
            var (start, end) = DownsampleLevels.Covers(2, i);
            Assert.Equal(4, end - start);
            var sum = 0;
            for (var x = start; x < end; x++) sum += ramp[x];
            reduced[i] = sum / 4d;
        }

        Assert.Equal(1.5, reduced[0]);
        Assert.Equal(5.5, reduced[1]);
        Assert.Equal(9.5, reduced[2]);
        Assert.Equal(13.5, reduced[3]);
        Assert.Equal(61.5, reduced[15]);   // 4*15 + 1.5
    }

    [Fact]
    public void PixelBudget_MatchesTheSurfaceCeiling()
        => Assert.Equal(ImageBudget.MaxSurfacePixels, DownsampleLevels.PixelBudget);
}
