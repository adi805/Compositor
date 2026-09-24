using Compositor.Core.Filters;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Filter tests for the WS9 port of upstream Filters.swift. Golden values come from the upstream
/// contract, not from recording this implementation's output: the noise and lens goldens were
/// derived with an independent Python re-implementation of NoisePixels.c and LensPixels.c, and the
/// blur assertions test the properties a normalized separable kernel must have. Where float32
/// accumulation order can move a value by one LSB the test allows exactly that and says why.
/// </summary>
public class FilterTests
{
    private static RasterSurface Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var s = new RasterSurface(w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                s.SetPixel(x, y, r, g, b, a);
            }
        }

        return s;
    }

    private static int Alpha(RasterSurface s, int x, int y) => s.GetPixel(x, y).A;

    private static int Diff(byte a, byte b) => Math.Abs(a - b);

    // ------------------------------------------------------------ settings parity

    [Theory]
    [InlineData(300, 250)]              // upstream: clamp(radius, 0.1...250, 1)
    [InlineData(0.05, 0.1)]
    [InlineData(double.NaN, 1)]         // non-finite falls back instead of poisoning pixels
    [InlineData(double.PositiveInfinity, 1)]
    public void RadiusClampsToUpstreamRange(double input, double expected)
    {
        Assert.Equal(expected, new FilterSettings { Radius = input }.Normalize().Radius, 6);
    }

    [Theory]
    [InlineData(-180, -90)]
    [InlineData(180, 90)]
    [InlineData(double.NaN, 0)]
    public void AngleClampsToUpstreamRange(double input, double expected)
    {
        Assert.Equal(expected, new FilterSettings { Angle = input }.Normalize().Angle, 6);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(5000, 2000)]
    [InlineData(double.NaN, 10)]
    public void DistanceClampsToUpstreamRange(double input, double expected)
    {
        Assert.Equal(expected, new FilterSettings { Distance = input }.Normalize().Distance, 6);
    }

    [Theory]
    [InlineData(0, 0.1)]
    [InlineData(1000, 400)]
    [InlineData(double.NaN, 10)]
    public void AmountClampsToUpstreamRange(double input, double expected)
    {
        Assert.Equal(expected, new FilterSettings { Amount = input }.Normalize().Amount, 6);
    }

    [Theory]
    [InlineData(-500, -100)]
    [InlineData(500, 100)]
    [InlineData(double.NaN, 0)]
    public void DistortionClampsToUpstreamRange(double input, double expected)
    {
        Assert.Equal(expected, new FilterSettings { Distortion = input }.Normalize().Distortion, 6);
    }

    [Fact]
    public void DefaultsMatchUpstreamFilterSettings()
    {
        var s = new FilterSettings();
        Assert.Equal(1, s.Radius, 6);
        Assert.Equal(0, s.Angle, 6);
        Assert.Equal(10, s.Distance, 6);
        Assert.Equal(10, s.Amount, 6);
        Assert.Equal(0, s.Distortion, 6);
        Assert.False(s.Gaussian);
        Assert.False(s.Monochromatic);
    }

    [Fact]
    public void GrainDefaultsAndClampsMatchUpstream()
    {
        var g = new GrainSettings();
        Assert.Equal(25, g.Amount, 6);
        Assert.Equal(1.5, g.Size, 6);
        Assert.Equal(50, g.Roughness, 6);

        var clamped = new GrainSettings { Amount = 500, Size = 0.1, Roughness = double.NaN }.Normalize();
        Assert.Equal(100, clamped.Amount, 6);
        Assert.Equal(0.5, clamped.Size, 6);
        Assert.Equal(50, clamped.Roughness, 6);
        Assert.False(new GrainSettings { Amount = 500 }.IsValid);
    }

    [Fact]
    public void FilterSetCoversUpstreamMenuEntries()
    {
        var kinds = Enum.GetValues<FilterKind>();
        Assert.Contains(FilterKind.GaussianBlur, kinds);
        Assert.Contains(FilterKind.MotionBlur, kinds);
        Assert.Contains(FilterKind.AddNoise, kinds);
        Assert.Contains(FilterKind.LensCorrection, kinds);
        Assert.Contains(FilterKind.RemoveBackground, kinds);
        Assert.Contains(FilterKind.ContentAwareFill, kinds);
    }

    [Theory]
    [InlineData(FilterKind.RemoveBackground)]
    public void AutomaticFiltersFailLoudlyUntilTheirGapsClose(FilterKind kind)
    {
        // Upstream runs these through Vision. A stub must refuse, not quietly return the input.
        Assert.Throws<NotSupportedException>(() =>
            FilterRunner.Apply(kind, Solid(4, 4, 10, 20, 30, 255), new FilterSettings(), null));
    }

    // ------------------------------------------------------------------- noise

    [Fact]
    public void AddNoiseMatchesIndependentKernelPort()
    {
        // Golden: a Python re-implementation of noise_add() from NoisePixels.c. Amount 10, uniform,
        // non-monochromatic, over opaque mid-gray, seed = FilterRunner.DefaultSeed (0x436F6D70).
        var source = Solid(8, 1, 128, 128, 128, 255);
        var result = FilterRunner.Apply(FilterKind.AddNoise, source, new FilterSettings { Amount = 10 }, null);

        Assert.Equal((byte)119, result.GetPixel(0, 0).R);
        Assert.Equal((byte)121, result.GetPixel(0, 0).G);
        Assert.Equal((byte)123, result.GetPixel(0, 0).B);
        Assert.Equal((byte)118, result.GetPixel(1, 0).R);
        Assert.Equal((byte)121, result.GetPixel(1, 0).G);
        Assert.Equal((byte)117, result.GetPixel(1, 0).B);
        Assert.Equal((byte)125, result.GetPixel(5, 0).R);
        Assert.Equal((byte)125, result.GetPixel(5, 0).G);
        Assert.Equal((byte)131, result.GetPixel(5, 0).B);
    }

    [Fact]
    public void AddNoiseLeavesAlphaAndEmptyPixelsAlone()
    {
        var source = new RasterSurface(4, 1);
        source.SetPixel(0, 0, 200, 100, 50, 128);
        source.SetPixel(1, 0, 200, 100, 50, 255);

        var result = FilterRunner.Apply(FilterKind.AddNoise, source, new FilterSettings { Amount = 400 }, null);

        Assert.Equal((byte)128, result.GetPixel(0, 0).A);
        Assert.Equal((byte)255, result.GetPixel(1, 0).A);
        Assert.Equal((byte)0, result.GetPixel(2, 0).A);
        Assert.Equal((byte)0, result.GetPixel(2, 0).R);
        Assert.Equal((byte)0, result.GetPixel(3, 0).B);
    }

    [Fact]
    public void AddNoiseIsReproduciblePerSeedAndVariesBetweenSeeds()
    {
        var source = Solid(16, 16, 128, 128, 128, 255);
        var settings = new FilterSettings { Amount = 40 };

        var a = FilterRunner.Apply(FilterKind.AddNoise, source, settings, null, seed: 12345);
        var b = FilterRunner.Apply(FilterKind.AddNoise, source, settings, null, seed: 12345);
        var c = FilterRunner.Apply(FilterKind.AddNoise, source, settings, null, seed: 54321);

        Assert.Equal(a.Pixels, b.Pixels);
        Assert.False(a.Pixels.AsSpan().SequenceEqual(c.Pixels));
    }

    [Fact]
    public void MonochromaticNoiseShiftsAllChannelsByTheSameAmount()
    {
        // One shared offset per pixel, so each channel's difference against R survives untouched.
        // Amount 10 over mid-gray keeps every channel inside 0..255, so no clamp can mask this.
        var source = Solid(8, 8, 128, 150, 106, 255);
        var result = FilterRunner.Apply(
            FilterKind.AddNoise, source, new FilterSettings { Amount = 10, Monochromatic = true }, null);

        var checkedPixels = 0;
        for (var i = 0; i < source.Pixels.Length; i += 4)
        {
            Assert.Equal(
                source.Pixels[i + 1] - source.Pixels[i],
                result.Pixels[i + 1] - result.Pixels[i]);
            Assert.Equal(
                source.Pixels[i + 2] - source.Pixels[i],
                result.Pixels[i + 2] - result.Pixels[i]);
            checkedPixels++;
        }

        Assert.Equal(64, checkedPixels);
    }

    [Fact]
    public void ColourNoiseDoesNotShiftChannelsTogether()
    {
        // The counter-case for the test above: three independent keys, so the channel gaps move.
        var source = Solid(8, 8, 128, 150, 106, 255);
        var result = FilterRunner.Apply(
            FilterKind.AddNoise, source, new FilterSettings { Amount = 10, Monochromatic = false }, null);

        var moved = 0;
        for (var i = 0; i < source.Pixels.Length; i += 4)
        {
            if (result.Pixels[i + 1] - result.Pixels[i] != source.Pixels[i + 1] - source.Pixels[i])
            {
                moved++;
            }
        }

        Assert.True(moved > 32, $"expected most pixels to change their channel balance, saw {moved}");
    }

    [Fact]
    public void GaussianNoiseCentresOnTheSourceMean()
    {
        var source = Solid(32, 32, 128, 128, 128, 255);
        var result = FilterRunner.Apply(
            FilterKind.AddNoise, source, new FilterSettings { Amount = 40, Gaussian = true }, null);

        var total = 0.0;
        for (var i = 0; i < result.Pixels.Length; i += 4)
        {
            total += result.Pixels[i];
        }

        Assert.InRange(total / (32 * 32), 118, 138); // zero-mean noise over 1024 samples
    }

    // -------------------------------------------------------------------- lens

    [Fact]
    public void LensCorrectionMatchesIndependentKernelPort()
    {
        // Golden: Python re-implementation of lens_distort() from LensPixels.c. A 4x4 gradient
        // (value = 40 + 10x + 20y), fully opaque, k = 0.35 at Remove Distortion +/-100.
        var source = new RasterSurface(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var v = (byte)(40 + (10 * x) + (20 * y));
                source.SetPixel(x, y, v, v, v, 255);
            }
        }

        var barrel = FilterRunner.Apply(
            FilterKind.LensCorrection, source, new FilterSettings { Distortion = 100 }, null);
        Assert.Equal((byte)49, barrel.GetPixel(0, 0).R);
        Assert.Equal((byte)70, barrel.GetPixel(1, 1).R);
        Assert.Equal((byte)100, barrel.GetPixel(2, 2).R);
        Assert.Equal((byte)121, barrel.GetPixel(3, 3).R);
        Assert.Equal((byte)107, barrel.GetPixel(3, 2).R);
        foreach (var (x, y) in new[] { (0, 0), (3, 0), (0, 3), (3, 3) })
        {
            Assert.Equal((byte)255, Alpha(barrel, x, y)); // barrel crops, it never empties a corner
        }

        var pincushion = FilterRunner.Apply(
            FilterKind.LensCorrection, source, new FilterSettings { Distortion = -100 }, null);
        Assert.Equal((byte)127, Alpha(pincushion, 0, 0)); // corners sample outside the source
        Assert.Equal((byte)213, Alpha(pincushion, 1, 0));
        Assert.Equal((byte)255, Alpha(pincushion, 1, 1)); // the middle keeps full coverage
        Assert.Equal((byte)70, pincushion.GetPixel(1, 1).R);
        Assert.Equal((byte)127, Alpha(pincushion, 3, 3)); // every corner loses the same way
        Assert.Equal((byte)65, pincushion.GetPixel(3, 3).R);
    }

    [Fact]
    public void LensCorrectionAtZeroIsAnExactCopy()
    {
        var source = Solid(6, 6, 11, 22, 33, 200);
        var result = FilterRunner.Apply(
            FilterKind.LensCorrection, source, new FilterSettings { Distortion = 0 }, null);
        Assert.Equal(source.Pixels, result.Pixels);
    }

    // ------------------------------------------------------------------ blurs

    [Fact]
    public void GaussianBlurOfAConstantRegionIsUnchanged()
    {
        // A normalized kernel over a constant field is that field. This is the first thing a broken
        // normalization or a mis-clipped margin breaks.
        var source = Solid(32, 32, 40, 80, 120, 255);
        var result = FilterRunner.Apply(FilterKind.GaussianBlur, source, new FilterSettings { Radius = 4 }, null);

        Assert.Equal((byte)40, result.GetPixel(16, 16).R);
        Assert.Equal((byte)80, result.GetPixel(16, 16).G);
        Assert.Equal((byte)120, result.GetPixel(16, 16).B);
        Assert.Equal((byte)255, result.GetPixel(16, 16).A);
    }

    [Fact]
    public void GaussianBlurSoftensAnEdgeIntoTheRoomMadeForIt()
    {
        // Upstream's contract, verbatim from Filters.swift: "a blur softens the layer's edges and
        // spreads into the room made for it, rather than smearing the border outwards and stopping
        // at it". A 32x32 white left half at sigma 2, so there is real room on every side of the
        // sampled pixels and a genuine interior that must stay fully opaque.
        //
        // Goldens from the normalized kernel computed independently: weights for offsets 0..6 are
        // [0.19968, 0.17621, 0.12111, 0.06483, 0.02702, 0.00877, 0.00222].
        var source = new RasterSurface(32, 32);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                source.SetPixel(x, y, 255, 255, 255, 255);
            }
        }

        var result = FilterRunner.Apply(FilterKind.GaussianBlur, source, new FilterSettings { Radius = 2 }, null);

        void ExpectAlpha(int x, int expected)
        {
            // float32 accumulation can cost one LSB against the double-precision reference.
            Assert.InRange(Alpha(result, x, 16), expected - 1, expected + 1);
        }

        ExpectAlpha(8, 255);    // interior: clear of both the seam and the canvas edge
        ExpectAlpha(10, 254);   // one kernel tap starts to see through
        ExpectAlpha(15, 153);   // last opaque column: half the kernel hangs over the edge
        ExpectAlpha(16, 102);   // first empty column: it received the other half
        ExpectAlpha(18, 26);    // and it falls off with distance
        ExpectAlpha(20, 3);
        ExpectAlpha(22, 0);     // nothing left by three sigma
        ExpectAlpha(31, 0);

        // Energy conservation across a step edge: the two sides trade coverage exactly.
        Assert.Equal(255, Alpha(result, 15, 16) + Alpha(result, 16, 16));

        // The canvas border fades too, because outside the layer is transparent. That is the
        // unclamped behaviour the upstream comment asks for.
        ExpectAlpha(0, 153);

        // Purely horizontal spread: rows that are themselves clear of the top and bottom falloff
        // (within kernel reach of those edges the alpha drops, which is correct behaviour) all
        // carry the same value as the sampled row.
        foreach (var y in new[] { 8, 12, 16, 20, 24 })
        {
            Assert.InRange(Alpha(result, 16, y), 101, 103);
        }
    }

    [Fact]
    public void GaussianBlurIsSymmetricAboutASingleImpulse()
    {
        var source = new RasterSurface(16, 16);
        source.SetPixel(8, 8, 255, 255, 255, 255);
        var result = FilterRunner.Apply(FilterKind.GaussianBlur, source, new FilterSettings { Radius = 2 }, null);

        // Radially symmetric separable kernel: mirrored taps agree to within the one LSB that
        // float32 accumulation order can cost, and coverage falls off monotonically.
        Assert.True(Diff((byte)Alpha(result, 6, 8), (byte)Alpha(result, 10, 8)) <= 1);
        Assert.True(Diff((byte)Alpha(result, 8, 6), (byte)Alpha(result, 8, 10)) <= 1);
        Assert.True(Diff((byte)Alpha(result, 6, 8), (byte)Alpha(result, 8, 6)) <= 1);
        Assert.True(Alpha(result, 8, 8) > Alpha(result, 7, 8));
        Assert.True(Alpha(result, 7, 8) > Alpha(result, 6, 8));
        Assert.True(Alpha(result, 6, 8) > 0);
    }

    [Fact]
    public void MotionBlurStreaksAlongItsAngleAndNowhereElse()
    {
        var source = new RasterSurface(41, 41);
        source.SetPixel(20, 20, 255, 255, 255, 255);

        var horizontal = FilterRunner.Apply(
            FilterKind.MotionBlur, source, new FilterSettings { Angle = 0, Distance = 16 }, null);

        // Angle 0 smears along x only: the rows above and below stay empty and the streak is
        // symmetric about the source pixel.
        Assert.Equal((byte)0, Alpha(horizontal, 20, 24));
        Assert.Equal((byte)0, Alpha(horizontal, 20, 16));
        Assert.True(Alpha(horizontal, 24, 20) > 0);
        Assert.True(Alpha(horizontal, 16, 20) > 0);
        Assert.True(Diff((byte)Alpha(horizontal, 24, 20), (byte)Alpha(horizontal, 16, 20)) <= 1);

        var vertical = FilterRunner.Apply(
            FilterKind.MotionBlur, source, new FilterSettings { Angle = 90, Distance = 16 }, null);
        Assert.True(Alpha(vertical, 20, 16) > 0);
        Assert.True(Alpha(vertical, 20, 24) > 0);
        Assert.Equal((byte)0, Alpha(vertical, 24, 20));
        Assert.Equal((byte)0, Alpha(vertical, 16, 20));

        var diagonal = FilterRunner.Apply(
            FilterKind.MotionBlur, source, new FilterSettings { Angle = 45, Distance = 16 }, null);

        // Angles are counterclockwise from horizontal with y up (as in Core Image), so 45 degrees
        // reaches up-and-right plus down-and-left on screen and never the perpendicular pair.
        Assert.True(Alpha(diagonal, 24, 16) > 0);
        Assert.True(Alpha(diagonal, 16, 24) > 0);
        Assert.Equal((byte)0, Alpha(diagonal, 16, 16));
        Assert.Equal((byte)0, Alpha(diagonal, 24, 24));
    }

    [Fact]
    public void MotionRadiusMatchesUpstreamStreakConversion()
    {
        Assert.Equal(1.0 / Math.Sqrt(12.0), FilterRunner.MotionRadiusPerPixel, 12);
    }

    [Fact]
    public void BlurMarginGrowsWithTheFilter()
    {
        var big = FilterRunner.BlurMargin(FilterKind.GaussianBlur, new FilterSettings { Radius = 10 });
        var small = FilterRunner.BlurMargin(FilterKind.GaussianBlur, new FilterSettings { Radius = 1 });
        Assert.True(big > small);
        Assert.Equal((3 * 1) + 2, small);
        Assert.Equal(0, FilterRunner.BlurMargin(FilterKind.AddNoise, new FilterSettings { Amount = 10 }));
    }

    // ------------------------------------------------------------------- grain

    [Fact]
    public void GrainLeavesAlphaAloneAndIsReproducible()
    {
        var source = Solid(16, 16, 128, 128, 128, 255);
        var settings = new GrainSettings { Amount = 60, Size = 2, Roughness = 50 };

        var a = FilterRunner.ApplyGrain(source, settings, null, seed: 7);
        var b = FilterRunner.ApplyGrain(source, settings, null, seed: 7);
        var c = FilterRunner.ApplyGrain(source, settings, null, seed: 8);

        Assert.Equal(a.Pixels, b.Pixels);
        Assert.False(a.Pixels.AsSpan().SequenceEqual(c.Pixels));
        Assert.False(a.Pixels.AsSpan().SequenceEqual(source.Pixels));
        for (var i = 3; i < a.Pixels.Length; i += 4)
        {
            Assert.Equal((byte)255, a.Pixels[i]);
        }
    }

    [Fact]
    public void GrainZeroAmountIsANoOp()
    {
        var source = Solid(8, 8, 90, 90, 90, 255);
        var result = FilterRunner.ApplyGrain(source, new GrainSettings { Amount = 0 }, null, seed: 3);
        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void GrainIsStrongestInTheMidtones()
    {
        // Upstream weights the delta by 0.4 + 2.4*L*(1-L): it peaks at L=0.5 and is thinnest at the
        // ends. Same seed, dark patch versus mid patch, must move the mid one more.
        var dark = Solid(24, 24, 12, 12, 12, 255);
        var mid = Solid(24, 24, 128, 128, 128, 255);
        var settings = new GrainSettings { Amount = 100, Size = 4, Roughness = 0 };

        var darkOut = FilterRunner.ApplyGrain(dark, settings, null, seed: 11);
        var midOut = FilterRunner.ApplyGrain(mid, settings, null, seed: 11);

        Assert.True(MeanAbsDelta(dark, darkOut) < MeanAbsDelta(mid, midOut));
    }

    private static double MeanAbsDelta(RasterSurface before, RasterSurface after)
    {
        var total = 0.0;
        for (var i = 0; i < before.Pixels.Length; i += 4)
        {
            total += Math.Abs(after.Pixels[i] - before.Pixels[i]);
        }

        return total / (before.Pixels.Length / 4);
    }

    // -------------------------------------------------------------- selection

    [Fact]
    public void FiltersRespectAHardSelection()
    {
        // Coverage 1 inside, 0 outside: outside must be restored to the original exactly, which is
        // the factor-0 bug this blend had in the first draft.
        var source = Solid(8, 4, 30, 30, 30, 255);
        var coverage = new byte[8 * 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                coverage[(y * 8) + x] = x < 4 ? (byte)255 : (byte)0;
            }
        }

        var selection = new SelectionClip(0, 0, 8, 4, coverage);
        var result = FilterRunner.Apply(FilterKind.AddNoise, source, new FilterSettings { Amount = 100 }, selection);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 4; x < 8; x++)
            {
                var untouched = result.GetPixel(x, y);
                Assert.Equal((byte)30, untouched.R);
                Assert.Equal((byte)30, untouched.G);
                Assert.Equal((byte)30, untouched.B);
            }
        }

        var changedInside = 0;
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var touched = result.GetPixel(x, y);
                if (touched.R != 30 || touched.G != 30 || touched.B != 30)
                {
                    changedInside++;
                }
            }
        }

        Assert.True(changedInside >= 14, $"expected nearly all 16 selected pixels to change, saw {changedInside}");
    }

    [Fact]
    public void EmptySelectionRestoresTheOriginal()
    {
        var source = Solid(6, 6, 77, 88, 99, 255);
        var result = FilterRunner.Apply(FilterKind.AddNoise, source, new FilterSettings { Amount = 400 }, SelectionClip.Empty);
        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void FiltersDoNotModifyTheirInput()
    {
        var source = Solid(8, 8, 100, 100, 100, 255);
        var before = (byte[])source.Pixels.Clone();
        FilterRunner.Apply(FilterKind.GaussianBlur, source, new FilterSettings { Radius = 3 }, null);
        Assert.Equal(before, source.Pixels);
    }

    [Fact]
    public void ContentAwareFillRunsThroughTheFilterRunnerAndInventsNothing()
    {
        // The runner path is the one a settings sheet would use, so it has to work too. On a
        // flat image the only thing a copy can produce is that same flat colour: a result that
        // differs anywhere would mean the kernel invented a pixel.
        const int size = 15;
        var surface = new RasterSurface(size, size);
        for (var i = 0; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = 200;
            surface.Pixels[i + 1] = 20;
            surface.Pixels[i + 2] = 20;
            surface.Pixels[i + 3] = 255;
        }

        var clip = DocumentSelection
            .FromShape(SelectionShape.Rectangle(7, 7, 1, 1, SelectionMode.Replace)).Clip(size, size);
        var result = FilterRunner.Apply(FilterKind.ContentAwareFill, surface, new FilterSettings(), clip);
        Assert.Equal(surface.Pixels, result.Pixels);
    }
}
