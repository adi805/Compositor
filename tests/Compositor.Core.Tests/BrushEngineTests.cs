using Compositor.Core;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Brush v2 parity tests against upstream BrushStroke.swift semantics:
/// Gaussian falloff (k=2.5), spacing fraction (1.5% hard / 2.5% soft of
/// diameter, min 0.25px), uniform dab spacing with remainder carry, and the
/// stroke opacity cap (overlapping dabs never exceed it).
/// </summary>
public class BrushEngineTests
{
    [Theory]
    [InlineData(0.0, 1.0)]           // falloff(0) = 1 (rim of the hardness edge)
    [InlineData(1.0, 0.0)]           // falloff(1) = 0 (at the rim)
    public void Falloff_PortsUpstreamGaussian(double t, double expected)
    {
        Assert.Equal(expected, StrokeCoverage.Falloff(t), 12);
    }

    [Fact]
    public void Falloff_Midpoint_MatchesHandComputed()
    {
        // exp(-2.5*0.25)=0.53526, exp(-2.5)=0.08208 -> (0.53526-0.08208)/(1-0.08208)
        Assert.Equal(0.4937, StrokeCoverage.Falloff(0.5), 3);
    }

    [Fact]
    public void Falloff_MonotonicDecreasing()
    {
        for (var t = 0.0; t < 1.0; t += 0.05)
        {
            Assert.True(StrokeCoverage.Falloff(t) >= StrokeCoverage.Falloff(t + 0.025));
        }
    }

    [Theory]
    [InlineData(1.0, 0.015)]   // hard: denser dabs
    [InlineData(0.5, 0.025)]   // soft
    [InlineData(0.0, 0.025)]   // fully soft
    public void SpacingFraction_MatchesUpstream(double hardness, double expected)
    {
        Assert.Equal(expected, StrokeCoverage.SpacingFraction(hardness), 12);
    }

    [Fact]
    public void Spacing_HasFloorOfQuarterPixel()
    {
        // diameter 4 hard: 4 * 0.015 = 0.06 -> clamps to 0.25 (checked via behavior below).
        var surface = new RasterSurface(64, 64);
        var stroke = new StrokeCoverage(64, 64, new BrushSettings { Diameter = 4, Hardness = 1 });
        stroke.WalkTo(32, 32);
        stroke.WalkTo(33, 32);
        // 1px segment with 0.25px spacing must lay dabs along the whole run.
        var covered = 0;
        for (var x = 30; x <= 35; x++)
        {
            if (stroke.CoverageAt(x, 32) > 0)
            {
                covered++;
            }
        }
        Assert.True(covered >= 4, $"expected dabs across the segment, covered={covered}");
    }

    [Fact]
    public void SoftTip_CenterFull_EdgeFalloff()
    {
        var surface = new RasterSurface(80, 80);
        var stroke = new StrokeCoverage(80, 80, new BrushSettings { Diameter = 40, Hardness = 0.5f });
        stroke.WalkTo(40, 40);

        // Center: inside hardness radius (radius 20, hardness edge at 10).
        Assert.Equal(255, stroke.CoverageAt(40, 40));
        // Just outside the rim (dist >= 20): no coverage.
        Assert.Equal(0, stroke.CoverageAt(40, 61));
    }

    [Fact]
    public void HardTip_BinaryDisc()
    {
        var surface = new RasterSurface(80, 80);
        var stroke = new StrokeCoverage(80, 80, new BrushSettings { Diameter = 20, Hardness = 1 });
        stroke.WalkTo(40, 40);

        Assert.Equal(255, stroke.CoverageAt(40, 40));   // center
        Assert.Equal(255, stroke.CoverageAt(45, 40));   // inside radius 10
        Assert.Equal(0, stroke.CoverageAt(40, 52));     // outside radius
    }

    [Fact]
    public void OpacityCap_OverlappingDabs_NeverExceedStrokeOpacity()
    {
        var surface = new RasterSurface(60, 60);
        var settings = new BrushSettings
        {
            Diameter = 16,
            Hardness = 1,
            R = 255,
            Opacity = 0.25f,
        };

        // A long path over the same area: dozens of overlapping dabs.
        var path = new List<(float X, float Y)>();
        for (var i = 0; i <= 40; i++)
        {
            path.Add((10 + i * 0.1f, 30));
        }
        BrushStroke.Apply(surface, path, settings);

        // Center of the run: coverage saturated, applied alpha capped at 0.25.
        var (_, _, _, a) = surface.GetPixel(14, 30);
        Assert.InRange(a, 62, 66); // 255 * 0.25 = 63.75
    }

    [Fact]
    public void LiveSegmentPainting_MatchesSingleApply()
    {
        var live = new RasterSurface(100, 100);
        var batch = new RasterSurface(100, 100);
        var settings = new BrushSettings { Diameter = 12, Hardness = 0.7f, G = 200, Opacity = 0.8f };
        var path = new List<(float X, float Y)>
        {
            (10, 50), (30, 52), (55, 48), (80, 50),
        };

        // Live: incremental WalkTo + paint from the same before-snapshot.
        var beforeLive = (byte[])live.Pixels.Clone();
        var stroke = new StrokeCoverage(100, 100, settings);
        foreach (var p in path)
        {
            stroke.WalkTo(p.X, p.Y);
            stroke.PaintRegion(live, beforeLive);
        }

        BrushStroke.Apply(batch, path, settings);

        Assert.Equal(batch.Pixels, live.Pixels);
    }

    [Fact]
    public void Eraser_ReducesAlpha_PreservesColor()
    {
        var surface = new RasterSurface(40, 40);
        // Opaque red layer.
        for (var i = 0; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = 200;
            surface.Pixels[i + 3] = 255;
        }

        BrushStroke.Apply(
            surface, [(20f, 20f)],
            new BrushSettings { Diameter = 12, Hardness = 1, Erasing = true, Opacity = 0.5f });

        var (r, g, b, a) = surface.GetPixel(20, 20);
        Assert.InRange(a, 125, 130);   // alpha halved by the eraser cap
        Assert.Equal(200, r);          // color untouched
        Assert.Equal(0, g);
    }

    [Fact]
    public void UndoRestores_BrushV2Stroke()
    {
        var surface = new RasterSurface(60, 60);
        var before = (byte[])surface.Pixels.Clone();
        var path = new List<(float X, float Y)> { (10, 10), (40, 40) };
        var settings = new BrushSettings { Diameter = 10, Hardness = 0.8f, B = 90, Opacity = 0.9f };

        BrushStroke.Apply(surface, path, settings);
        var after = (byte[])surface.Pixels.Clone();
        var cmd = new StrokeCommand(surface, path, settings, before, alreadyApplied: true);

        cmd.Undo();
        Assert.Equal(before, surface.Pixels);
        cmd.Redo();
        Assert.Equal(after, surface.Pixels);
    }

    [Fact]
    public void InvalidSettings_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StrokeCoverage(10, 10, new BrushSettings { Hardness = 1.5f }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StrokeCoverage(10, 10, new BrushSettings { Opacity = -0.1f }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StrokeCoverage(10, 10, new BrushSettings { Diameter = 0 }));
    }
}
