using System;
using Compositor.Core.Filters;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Goldens for <see cref="GuidedFilter"/>, every expected number derived by hand from the
/// algorithm (upstream <c>GuidedMatte.swift</c>), not recorded from this implementation.
/// The arithmetic is deliberately small: a two-pixel plane and a 3x3 impulse can be summed on
/// paper, which is the point - a wrong running sum cannot hide.
/// </summary>
public class GuidedFilterTests
{
    [Fact]
    public void Box_RadiusZero_IsIdentity()
    {
        // span = 1, and the window holds only the pixel itself.
        var source = new[] { 0.1f, 0.9f, 0.4f, 0.7f };
        var result = GuidedFilter.Box(source, 2, 2, 0);
        Assert.Equal(source.Length, result.Length);
        for (var i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i], result[i], 6);
        }
    }

    [Fact]
    public void Box_TwoByOne_ReplicatesTheEdgeAndKeepsTheFullDivisor()
    {
        // Radius 1 over [2, 5]:
        //   x=0 window covers (clamped) 0,0,1 -> (2 + 2 + 5) / 3 = 3
        //   x=1 window covers 0,1,1 (clamped) -> (2 + 5 + 5) / 3 = 4
        // The vertical pass sees a constant column, so it returns the same values.
        var result = GuidedFilter.Box(new[] { 2f, 5f }, 2, 1, 1);
        Assert.Equal(3f, result[0], 6);
        Assert.Equal(4f, result[1], 6);
    }

    [Fact]
    public void Box_ImpulseInThreeByThree_SpreadsOneNinthAcrossEverything()
    {
        var source = new float[9];
        source[(1 * 3) + 1] = 1f;
        var result = GuidedFilter.Box(source, 3, 3, 1);

        // Horizontal: row 1 becomes [1/3, 1/3, 1/3] because the leading window clamps onto the
        // impulse and the trailing one replicates past the edge. Vertical does the same again,
        // so every cell ends at 1/9 and the total energy is preserved.
        foreach (var value in result)
        {
            Assert.Equal(1f / 9f, value, 6);
        }

        var total = 0f;
        foreach (var value in result)
        {
            total += value;
        }

        Assert.Equal(1f, total, 5);
    }

    [Fact]
    public void Apply_PlainGreyOnPlainGreyReturnsGreyExactly()
    {
        // Zero variance and zero covariance, so slope is 0 and offset is meanMask = 0.5. Every
        // value here is a dyadic rational, which means float has nothing to round.
        var plane = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var result = GuidedFilter.Apply(plane, plane, 2, 2, 1, GuidedFilter.DefaultEpsilon);
        Assert.Equal(4, result.Length);
        foreach (var value in result)
        {
            Assert.Equal(0.5f, value, 6);
        }
    }

    [Fact]
    public void Apply_GuideThatIsTheInverseOfTheMask_CutsTheMaskClean()
    {
        // mask [1, 0] over guide [0, 1], radius 1.
        //   meanGuide = [1/3, 2/3], meanMask = [2/3, 1/3]
        //   squares = [0, 1] -> meanSquares = [1/3, 2/3]; products = [0, 0] -> meanProducts = [0, 0]
        //   variance = 1/3 - 1/9 = 2/9 and covariance = -2/9 at both pixels, so
        //   slope = -(2/9) / (2/9 + 1e-4) = -0.99955 and offset = [0.99985, 0.99970].
        //   Box of a constant slope is itself; box of the offsets gives [0.99980, 0.99975].
        //   result = slope * guide + offset -> [0.99980, 0.00020].
        // A guided filter is linear in the guide, so with the guide pointing away from the mask
        // the answer has to snap to the guide's own step. That is the whole reason hair masks work.
        var result = GuidedFilter.Apply(
            new[] { 1f, 0f }, new[] { 0f, 1f }, 2, 1, 1, GuidedFilter.DefaultEpsilon);

        Assert.Equal(1f, result[0], 3);
        Assert.Equal(0f, result[1], 3);
        Assert.True(result[0] > 0.99f, $"left pixel {result[0]}");
        Assert.True(result[1] < 0.01f, $"right pixel {result[1]}");
    }

    [Fact]
    public void Apply_ConstantGuideReducesToAPureBlur()
    {
        // A flat guide has zero variance and zero covariance, so slope is exactly 0 and the
        // result is the box-blurred mask, whatever epsilon says. That is the degenerate case
        // where a broken variance term shows up loudest.
        // box([0, 1, 0], radius 1) on a 3x1 plane: the leading window clamps onto the impulse,
        // giving 1/3 at every position, and the vertical pass sees constant columns.
        var result = GuidedFilter.Apply(
            new[] { 0f, 1f, 0f }, new[] { 1f, 1f, 1f }, 3, 1, 1, GuidedFilter.DefaultEpsilon);
        Assert.Equal(1f / 3f, result[0], 6);
        Assert.Equal(1f / 3f, result[1], 6);
        Assert.Equal(1f / 3f, result[2], 6);
    }

    [Fact]
    public void WorkingSize_ShrinksToTheLimitAndKeepsAspectRatio()
    {
        // Upstream: factor = min(1, limit / longest side), sides rounded away from zero.
        Assert.Equal((1400, 1050), GuidedFilter.WorkingSize(4000, 3000, 1400));
        Assert.Equal((4000, 3000), GuidedFilter.WorkingSize(4000, 3000, 9000));
        Assert.Equal((800, 800), GuidedFilter.WorkingSize(800, 800, 800));
        Assert.Equal(1d, GuidedFilter.ScaleFactor(10, 20, 40), 9);
    }

    [Fact]
    public void WorkingSize_NeverCollapsesBelowOnePixel()
    {
        var (w, h) = GuidedFilter.WorkingSize(3, 2, 0.0001);
        Assert.Equal(1, w);
        Assert.Equal(1, h);
    }

    [Fact]
    public void EffectiveRadius_ScalesWithTheCopyButFloorsAtOne()
    {
        // radius 20 at a 0.35 factor -> 7; a zero slider still reaches one pixel upstream.
        Assert.Equal(7, GuidedFilter.EffectiveRadius(20, 1400, 4000, 3000));
        Assert.Equal(20, GuidedFilter.EffectiveRadius(20, 9000, 4000, 3000));
        Assert.Equal(1, GuidedFilter.EffectiveRadius(0, 1400, 4000, 3000));
        Assert.Equal(1, GuidedFilter.EffectiveRadius(0.4, 1400, 4000, 3000));
    }

    [Fact]
    public void Guards_RejectImpossiblePlanes()
    {
        Assert.Throws<ArgumentException>(() => GuidedFilter.Box(new[] { 1f }, 2, 2, 0));
        Assert.Throws<ArgumentException>(() => GuidedFilter.Box(new[] { 1f }, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => GuidedFilter.Box(new[] { 1f, 2f, 3f, 4f }, 2, 2, -1));
        Assert.Throws<ArgumentException>(
            () => GuidedFilter.Apply(new[] { 1f, 0f }, new[] { 1f }, 2, 1, 1, GuidedFilter.DefaultEpsilon));
        Assert.Throws<ArgumentException>(() => GuidedFilter.ScaleFactor(0, 0, 10));
    }
}
