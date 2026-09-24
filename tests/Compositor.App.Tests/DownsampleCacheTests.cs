using Compositor.App.Rendering;
using Compositor.Core;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Behaviour of the halved-copy cache behind canvas rendering. Expectations are derived by hand from the
/// upstream rule (exact 2x halvings, copies keyed by identity, LRU beyond a pixel budget), never recorded
/// from a run: a constant surface must survive any halving unchanged, and a level-k copy must be exactly the
/// size repeated halving predicts.
/// </summary>
public class DownsampleCacheTests
{
    private static RasterSurface Filled(int width, int height, byte r, byte g, byte b, byte a)
    {
        var surface = new RasterSurface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                surface.SetPixel(x, y, r, g, b, a);
            }
        }

        return surface;
    }

    [Fact]
    public void LevelZero_ReturnsTheSourceItself()
    {
        var cache = new DownsampleCache();
        var source = Filled(32, 32, 10, 20, 30, 255);

        var (surface, level) = cache.AtLevel(source, 0);

        Assert.Same(source, surface);
        Assert.Equal(0, level);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void AtLevel_ProducesTheHandDerivedDimensions()
    {
        var cache = new DownsampleCache();
        var source = Filled(64, 32, 1, 2, 3, 255);

        Assert.Equal((32, 16), Dimensions(cache.AtLevel(source, 1).Surface));
        Assert.Equal((16, 8), Dimensions(cache.AtLevel(source, 2).Surface));
        Assert.Equal((8, 4), Dimensions(cache.AtLevel(source, 3).Surface));
        Assert.Equal((1, 1), Dimensions(cache.AtLevel(source, 6).Surface));
    }

    [Fact]
    public void Halving_StopsAtOnePixelRatherThanFailing()
    {
        var cache = new DownsampleCache();
        var source = Filled(1, 1, 9, 9, 9, 255);

        var (surface, level) = cache.AtLevel(source, 4);

        Assert.Same(source, surface);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ConstantSurface_SurvivesEveryLevelUnchanged()
    {
        // The strongest cheap check: any correct resampler preserves a constant image exactly. A cache that
        // premultiplied, or that mixed in transparent padding, would shift these values.
        var cache = new DownsampleCache();
        var source = Filled(16, 16, 200, 100, 50, 128);

        for (var level = 1; level <= 4; level++)
        {
            var reduced = cache.AtLevel(source, level).Surface;
            var (r, g, b, a) = reduced.GetPixel(reduced.Width / 2, reduced.Height / 2);
            Assert.InRange(r, 198, 202);
            Assert.InRange(g, 98, 102);
            Assert.InRange(b, 48, 52);
            Assert.InRange(a, 126, 130);
        }
    }

    [Fact]
    public void UnchangedSurface_ReusesTheSameChain()
    {
        var cache = new DownsampleCache();
        var source = Filled(64, 64, 5, 6, 7, 255);

        var first = cache.AtLevel(source, 3).Surface;
        var second = cache.AtLevel(source, 3).Surface;

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void PaintInvalidatesTheChain()
    {
        // A surface keeps its identity while painting mutates it in place, so a copy made before the last
        // MarkDirty must not be handed back.
        var cache = new DownsampleCache();
        var source = Filled(64, 64, 0, 0, 0, 255);
        var before = cache.AtLevel(source, 2).Surface;

        source.SetPixel(0, 0, 255, 255, 255, 255);
        source.MarkDirty();
        var after = cache.AtLevel(source, 2).Surface;

        Assert.NotSame(before, after);
    }

    [Fact]
    public void Factor_SelectsTheLevelTheRulePredicts()
    {
        var cache = new DownsampleCache();
        var source = Filled(64, 64, 3, 3, 3, 255);

        Assert.Equal(0, cache.For(source, 0.9).Level);
        Assert.Equal(0, cache.For(source, 0.5).Level);
        Assert.Equal(1, cache.For(source, 0.4).Level);
        Assert.Equal(2, cache.For(source, 0.25).Level);
        Assert.Equal(3, cache.For(source, 0.12).Level);
    }

    [Fact]
    public void Eviction_DropsTheLeastRecentlyUsedBeyondTheBudget()
    {
        // Each 64x64 chain at level 1 is 32x32 = 1024 px, so a 2500 px budget holds exactly two chains.
        var cache = new DownsampleCache(pixelBudget: 2500);
        var first = Filled(64, 64, 1, 1, 1, 255);
        var second = Filled(64, 64, 2, 2, 2, 255);
        var third = Filled(64, 64, 3, 3, 3, 255);

        var firstChain = cache.AtLevel(first, 1).Surface;
        cache.AtLevel(second, 1);
        Assert.Equal(2, cache.Count);

        // Touching the first makes it the most recent, so the third chain has to evict the second instead.
        var firstAgain = cache.AtLevel(first, 1).Surface;
        Assert.Same(firstChain, firstAgain);

        cache.AtLevel(third, 1);

        Assert.Equal(2, cache.Count);
        Assert.Same(firstAgain, cache.AtLevel(first, 1).Surface);
    }

    [Fact]
    public void ReducedCopy_ActuallyReducesRatherThanCopying()
    {
        // A left-to-right ramp: after one halving the left half must be darker than the right half. This
        // fails if the cache silently hands back the full-size surface.
        var cache = new DownsampleCache();
        var source = new RasterSurface(16, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                source.SetPixel(x, y, (byte)(x * 16), 0, 0, 255);
            }
        }

        var reduced = cache.AtLevel(source, 1).Surface;
        Assert.Equal((8, 8), Dimensions(reduced));
        var middleRow = reduced.Height / 2;
        var (left, _, _, _) = reduced.GetPixel(0, middleRow);
        var (right, _, _, _) = reduced.GetPixel(reduced.Width - 1, middleRow);

        // Hand-derived: left covers source x 0..1 (mean 8), right covers 14..15 (mean 232).
        Assert.InRange(left, 6, 10);
        Assert.InRange(right, 230, 234);
    }

    [Fact]
    public void LevelFor_SmallSurface_StillReturnsTheSource()
    {
        var cache = new DownsampleCache();
        var source = Filled(2, 2, 7, 7, 7, 255);

        var (surface, level) = cache.AtLevel(source, 1);

        Assert.Equal((1, 1), Dimensions(surface));
        Assert.Equal(1, level);
    }

    private static (int Width, int Height) Dimensions(RasterSurface surface) => (surface.Width, surface.Height);
}
