using Compositor.Core.Adjustments;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Adjustment engine tests. Every expectation is hand-computed from the ported
/// upstream formulas, not recorded from a previous run.
/// </summary>
public class AdjustmentTests
{
    private static RasterSurface Surface(params (byte R, byte G, byte B, byte A)[] pixels)
    {
        var surface = new RasterSurface(pixels.Length, 1);
        for (var i = 0; i < pixels.Length; i++)
        {
            surface.SetPixel(i, 0, pixels[i].R, pixels[i].G, pixels[i].B, pixels[i].A);
        }

        return surface;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(RasterSurface surface, int x) =>
        (surface.Pixels[x * 4], surface.Pixels[(x * 4) + 1], surface.Pixels[(x * 4) + 2], surface.Pixels[(x * 4) + 3]);

    // ---------- Levels ----------

    [Fact]
    public void LevelRangeIdentityPassesValuesThrough()
    {
        var range = LevelRange.Identity;
        Assert.Equal(0, range.Apply(0), 6);
        Assert.Equal(0.5, range.Apply(0.5), 6);
        Assert.Equal(1, range.Apply(1), 6);
    }

    [Fact]
    public void LevelRangeBlackPointRemapsInput()
    {
        // (0.4*255 − 51) / (255 − 51) = 51/204 = 0.25
        var range = new LevelRange(51, 1, 255, 0, 255);
        Assert.Equal(0.25, range.Apply(0.4), 6);
        Assert.Equal(0, range.Apply(0.2), 6);
    }

    [Fact]
    public void LevelRangeGammaBendsMidtones()
    {
        // input^ (1/2) = sqrt(0.25) = 0.5
        var range = new LevelRange(0, 2, 255, 0, 255);
        Assert.Equal(0.5, range.Apply(0.25), 6);
    }

    [Fact]
    public void LevelRangeOutputRemapCompressesRange()
    {
        // outputBlack 0 + 0.5 * (128 − 0) = 64 → 64/255
        var range = new LevelRange(0, 1, 255, 0, 128);
        Assert.Equal(64d / 255, range.Apply(0.5), 6);
    }

    [Fact]
    public void LevelsCompositeAppliesChannelThenRgb()
    {
        var settings = new LevelsSettings();
        settings.Ranges[(int)LevelsChannel.Red] = new LevelRange(51, 1, 255, 0, 255);
        settings.Ranges[0] = new LevelRange(0, 2, 255, 0, 255);

        // Red channel first: (0.4*255−51)/204 = 0.25, then RGB gamma 2: sqrt(0.25) = 0.5
        Assert.Equal(0.5, settings.Apply(0.4, LevelsChannel.Red), 6);

        // Green has no channel range, so only the composite runs: sqrt(0.25) = 0.5
        Assert.Equal(0.5, settings.Apply(0.25, LevelsChannel.Green), 6);
    }

    [Fact]
    public void LevelsIdentityIsDetected()
    {
        Assert.True(new LevelsSettings().IsIdentity);
        var settings = new LevelsSettings();
        settings.Ranges[2] = new LevelRange(10, 1, 255, 0, 255);
        Assert.False(settings.IsIdentity);
    }

    [Fact]
    public void LevelsTablesApplyBlackPointToPixels()
    {
        // black 51 / white 204: 100 → (100−51)/153 = 0.32026 → 82; 200 → 0.97386 → 248
        var settings = new LevelsSettings();
        settings.Ranges[0] = new LevelRange(51, 1, 204, 0, 255);
        var source = Surface((100, 100, 100, 255), (200, 200, 200, 255));

        var result = AdjustmentRunner.ApplyTables(source, settings.BuildTables(), null);

        Assert.Equal((byte)82, Pixel(result, 0).R);
        Assert.Equal((byte)248, Pixel(result, 1).R);
        Assert.Equal((byte)255, Pixel(result, 0).A);
    }

    [Fact]
    public void LevelsLeaveTransparentPixelsUntouched()
    {
        var settings = new LevelsSettings();
        settings.Ranges[0] = new LevelRange(100, 1, 200, 0, 255);
        var source = Surface((40, 40, 40, 0));

        var result = AdjustmentRunner.ApplyTables(source, settings.BuildTables(), null);

        Assert.Equal((byte)40, Pixel(result, 0).R);
        Assert.Equal((byte)0, Pixel(result, 0).A);
    }

    [Fact]
    public void LevelsHistogramCountsChannelsAndMeansRgb()
    {
        // Red 255 opaque plus gray 100 opaque: bins[1][255] = 1, bins[0][255] = 1/3,
        // bins[c][100] = 1 for each channel, bins[0][100] = 1 (three × 1/3).
        var source = Surface((255, 0, 0, 255), (100, 100, 100, 255));

        var bins = LevelsHistogram.Compute(source, null);

        Assert.Equal(1, bins[1][255], 6);
        Assert.Equal(1d / 3, bins[0][255], 6);
        Assert.Equal(1, bins[1][100], 6);
        Assert.Equal(1, bins[2][100], 6);
        Assert.Equal(1, bins[3][100], 6);
        Assert.Equal(1, bins[0][100], 6);
        // The red pixel's G and B sit at 0, each contributing weight/3 to bin 0.
        Assert.Equal(2d / 3, bins[0][0], 6);
    }

    [Fact]
    public void LevelsHistogramWeightsBySelectionCoverage()
    {
        var source = Surface((100, 100, 100, 255), (100, 100, 100, 255));
        var selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(0, 0, 1, 1, SelectionMode.Replace)).Clip(2, 1);

        var bins = LevelsHistogram.Compute(source, selection);

        Assert.Equal(1, bins[1][100], 6);
    }

    [Fact]
    public void HistogramDisplayScaleCapsIsolatedSpikes()
    {
        var bins = new double[256];
        bins[4] = 1;
        bins[200] = 10;

        // interior peak 95th percentile = 1 → min(10, 1*4) = 4
        Assert.Equal(4, LevelsHistogram.DisplayScale(bins), 6);
        Assert.Equal(0, LevelsHistogram.DisplayScale(new double[256]), 6);
    }

    [Fact]
    public void AutoContrastSharesOneIntervalAcrossChannels()
    {
        var histogram = new double[4][];
        for (var c = 0; c < 4; c++)
        {
            histogram[c] = new double[256];
        }

        // Red runs 60..200, green 51..190, blue 70..204: contrast takes the widest envelope.
        histogram[1][60] = 1; histogram[1][200] = 1;
        histogram[2][51] = 1; histogram[2][190] = 1;
        histogram[3][70] = 1; histogram[3][204] = 1;

        var settings = LevelsAuto.Contrast.Settings(histogram);

        Assert.Equal(51, settings.Ranges[0].Black, 6);
        Assert.Equal(204, settings.Ranges[0].White, 6);
        Assert.True(settings.Ranges[1].Equals(LevelRange.Identity));
    }

    [Fact]
    public void AutoColorFindsPerChannelEndpoints()
    {
        var histogram = new double[4][];
        for (var c = 0; c < 4; c++)
        {
            histogram[c] = new double[256];
        }

        histogram[1][60] = 1; histogram[1][200] = 1;
        histogram[2][51] = 1; histogram[2][190] = 1;
        histogram[3][70] = 1; histogram[3][204] = 1;

        var settings = LevelsAuto.Color.Settings(histogram);

        Assert.Equal(60, settings.Ranges[1].Black, 6);
        Assert.Equal(200, settings.Ranges[1].White, 6);
        Assert.Equal(51, settings.Ranges[2].Black, 6);
        Assert.Equal(204, settings.Ranges[3].White, 6);
    }

    [Fact]
    public void AutoNeutralPullsWeightedMeanToMidGray()
    {
        var histogram = new double[4][];
        for (var c = 0; c < 4; c++)
        {
            histogram[c] = new double[256];
        }

        // Weights 4 at black 51 and 1 at white 255: applied mean = (0*4 + 1*1)/5 = 0.2
        // → gamma = log(0.2)/log(0.5) ≈ 2.3219
        histogram[1][51] = 4; histogram[2][51] = 4; histogram[3][51] = 4;
        histogram[1][255] = 1; histogram[2][255] = 1; histogram[3][255] = 1;

        var settings = LevelsAuto.Neutral.Settings(histogram);
        var expected = Math.Log(0.2) / Math.Log(0.5);

        Assert.Equal(expected, settings.Ranges[1].Gamma, 4);
    }

    [Fact]
    public void LevelsSamplingSetsBlackWhiteAndGrayPoints()
    {
        var settings = new LevelsSettings();
        double[] rgb = [0.2, 0.2, 0.2];

        var black = settings.Sampling(rgb, LevelsSampleMode.Black);
        Assert.Equal(51, black.Ranges[1].Black, 6);
        Assert.True(black.Ranges[0].Equals(LevelRange.Identity));

        var white = settings.Sampling(rgb, LevelsSampleMode.White);
        Assert.Equal(51, white.Ranges[1].White, 6);

        // Gray at half the range: gamma = log(0.5)/log(0.5) = 1
        double[] mid = [0.5, 0.5, 0.5];
        var gray = settings.Sampling(mid, LevelsSampleMode.Gray);
        Assert.Equal(1, gray.Ranges[1].Gamma, 6);
    }

    // ---------- Curves ----------

    [Fact]
    public void CurvesIdentityIsLinear()
    {
        var curves = new CurvesSettings();
        Assert.True(curves.IsValid);
        Assert.Equal(128, curves.Value(128, 0), 6);
        Assert.Equal(0, curves.Value(0, 0), 6);
        Assert.Equal(255, curves.Value(255, 0), 6);
    }

    [Fact]
    public void CurvesHermiteHitsHandlesWithoutOvershoot()
    {
        var curves = new CurvesSettings();
        curves.Channels[0] = [new CurvePoint(0, 0), new CurvePoint(128, 64), new CurvePoint(255, 255)];

        Assert.Equal(64, curves.Value(128, 0), 6);

        // Between the handles the shape-preserving spline stays monotone.
        var mid = curves.Value(64, 0);
        Assert.InRange(mid, 0, 64);
        var late = curves.Value(192, 0);
        Assert.InRange(late, 64, 255);
        Assert.True(curves.Value(64, 0) < curves.Value(192, 0));
    }

    [Fact]
    public void CurvesRejectOutOfOrderOrTooFewPoints()
    {
        var curves = new CurvesSettings();
        curves.Channels[1] = [new CurvePoint(0, 0), new CurvePoint(200, 100), new CurvePoint(150, 200), new CurvePoint(255, 255)];
        Assert.False(curves.IsValid);

        curves.Channels[1] = [new CurvePoint(10, 0), new CurvePoint(255, 255)];
        Assert.False(curves.IsValid);
    }

    [Fact]
    public void CurvesTablesComposeChannelWithRgb()
    {
        var curves = new CurvesSettings();
        curves.Channels[1] = [new CurvePoint(0, 0), new CurvePoint(128, 0), new CurvePoint(255, 255)];
        curves.Channels[0] = [new CurvePoint(0, 0), new CurvePoint(128, 255), new CurvePoint(255, 255)];

        var tables = curves.BuildTables();

        // Red channel maps 128 → 0, then the RGB curve maps 0 → 0.
        Assert.Equal(0, tables[(0 * 256) + 128], 6);
        // Green has no channel curve (identity), so only the RGB curve applies: 128 → 255.
        Assert.Equal(1, tables[(1 * 256) + 128], 6);
        // Blue likewise.
        Assert.Equal(1, tables[(2 * 256) + 128], 6);
    }

    // ---------- Hue / Saturation ----------

    [Fact]
    public void HslRoundTripKeepsPrimaryColors()
    {
        var (h, s, l) = HueSaturationSettings.ToHsl(1, 0, 0);
        Assert.Equal(0, h, 6);
        Assert.Equal(1, s, 6);
        Assert.Equal(0.5, l, 6);

        var (r, g, b) = HueSaturationSettings.ToRgb(0, 1, 0.5);
        Assert.Equal(1, r, 6);
        Assert.Equal(0, g, 6);
        Assert.Equal(0, b, 6);
    }

    [Fact]
    public void HslReportsGrayAsNeutral()
    {
        var (h, s, l) = HueSaturationSettings.ToHsl(0.5, 0.5, 0.5);
        Assert.Equal(0, h, 6);
        Assert.Equal(0, s, 6);
        Assert.Equal(0.5, l, 6);
    }

    [Fact]
    public void HueShiftTurnsRedIntoCyan()
    {
        var settings = new HueSaturationSettings(hue: 180);

        var (r, g, b) = settings.AdjustPixel(1, 0, 0);

        Assert.Equal(0, r, 6);
        Assert.Equal(1, g, 6);
        Assert.Equal(1, b, 6);
    }

    [Fact]
    public void SaturationMultipliesAndKeepsGraysNeutral()
    {
        // Red (1, 0.5, 0.5) is fully saturated, so −50% halves it: s = 1 * (1 − 0.5) = 0.5
        var settings = new HueSaturationSettings(saturation: -50);
        var (r, g, b) = settings.AdjustPixel(1, 0.5, 0.5);
        var expected = HueSaturationSettings.ToRgb(0, 0.5, 0.75);
        Assert.Equal(expected.R, r, 6);
        Assert.Equal(expected.G, g, 6);
        Assert.Equal(expected.B, b, 6);

        // Gray has no saturation to multiply, so it stays gray.
        var (gr, gg, gb) = settings.AdjustPixel(0.4, 0.4, 0.4);
        Assert.Equal(0.4, gr, 6);
        Assert.Equal(0.4, gg, 6);
        Assert.Equal(0.4, gb, 6);
    }

    [Fact]
    public void LightnessPullsTowardWhiteAndBlack()
    {
        var brighter = new HueSaturationSettings(lightness: 50);
        var (r, _, _) = brighter.AdjustPixel(0.4, 0.4, 0.4);
        Assert.Equal(0.7, r, 6); // 0.4 + (1 − 0.4) * 0.5

        var darker = new HueSaturationSettings(lightness: -50);
        var (dr, _, _) = darker.AdjustPixel(0.4, 0.4, 0.4);
        Assert.Equal(0.2, dr, 6); // 0.4 * (1 − 0.5)
    }

    [Fact]
    public void ColorizeMapsGrayToTheChosenHue()
    {
        var settings = new HueSaturationSettings(hue: 120, saturation: 50, lightness: 0, colorize: true);

        var (r, g, b) = settings.AdjustPixel(0.5, 0.5, 0.5);

        // Sector 2 gives (0, chroma, second) + base 0.25 → (0.25, 0.75, 0.25)
        Assert.Equal(0.25, r, 6);
        Assert.Equal(0.75, g, 6);
        Assert.Equal(0.25, b, 6);
    }

    [Fact]
    public void BandWeightsRampThroughShoulders()
    {
        var reds = HueBand.DefaultFor(ColorRange.Reds);

        Assert.Equal(1, reds.WeightOf(0), 6); // inside 345..15
        Assert.Equal(1, reds.WeightOf(350), 6);
        Assert.Equal(0.5, reds.WeightOf(30), 6); // half way out the 15..45 shoulder
        Assert.Equal(0, reds.WeightOf(90), 6); // outside 315..45
        Assert.Equal(1, HueBand.DefaultFor(ColorRange.Master).WeightOf(200), 6);
    }

    [Fact]
    public void BandHandleMovesRejectInversions()
    {
        var reds = HueBand.DefaultFor(ColorRange.Reds);
        var moved = reds.WithHandle(1, 350);
        Assert.Equal(350, moved.RangeStart, 6);

        // Moving the range start past the range end is refused.
        Assert.Equal(reds, reds.WithHandle(1, 40));
    }

    [Fact]
    public void RangedAdjustmentOnlyTouchesItsBand()
    {
        var settings = new HueSaturationSettings(range: ColorRange.Blues);
        settings.Hue = 180;

        // A blue pixel (hue 240) is inside the band and shifts.
        var blue = settings.AdjustPixel(0, 0, 1);
        Assert.NotEqual(0, blue.R);
        Assert.Equal(1, blue.G, 6);

        // A red pixel (hue 0) is far outside it and stays put.
        var red = settings.AdjustPixel(1, 0, 0);
        Assert.Equal(1, red.R, 6);
        Assert.Equal(0, red.G, 6);
    }

    [Fact]
    public void InvertedRangeAppliesOutsideTheBand()
    {
        var settings = new HueSaturationSettings(range: ColorRange.Blues);
        settings.Hue = 180;
        settings.InvertRange = true;

        var red = settings.AdjustPixel(1, 0, 0);
        var (expectedR, expectedG, expectedB) = HueSaturationSettings.ToRgb(180, 1, 0.5);
        Assert.Equal(expectedR, red.R, 6);
        Assert.Equal(expectedG, red.G, 6);
        Assert.Equal(expectedB, red.B, 6);
    }

    [Fact]
    public void HueSaturationRunnerAppliesToPixelsAndKeepsAlpha()
    {
        var source = Surface((255, 0, 0, 128));
        var settings = new HueSaturationSettings(hue: 180);

        var result = AdjustmentRunner.ApplyHueSaturation(source, settings, null);

        Assert.Equal((byte)0, Pixel(result, 0).R);
        Assert.Equal((byte)255, Pixel(result, 0).G);
        Assert.Equal((byte)255, Pixel(result, 0).B);
        Assert.Equal((byte)128, Pixel(result, 0).A);
    }

    // ---------- Exposure ----------

    [Fact]
    public void ExposureTableEndsAreExactAndMidtonesBrighten()
    {
        var settings = new ExposureSettings { Exposure = 1 };
        var table = settings.BuildTable();

        Assert.Equal(0, table[0], 6);
        Assert.Equal(1, table[255], 6);

        // 128 → sRGB decode 0.2159, ×2 = 0.4318, encode ≈ 0.688
        Assert.Equal(0.688, table[128], 2);
        Assert.True(table[64] > 64 / 255f);
    }

    [Fact]
    public void ExposureRunnerUsesTheTable()
    {
        var settings = new ExposureSettings { Exposure = 1 };
        var source = Surface((128, 128, 128, 255));

        var result = AdjustmentRunner.ApplyExposure(source, settings, null);

        // 128 → sRGB decode 0.2159, ×2 = 0.4317, encode ≈ 0.6884 → 176
        Assert.InRange(Pixel(result, 0).R, (byte)175, (byte)177);
        Assert.Equal((byte)255, Pixel(result, 0).A);
    }

    [Fact]
    public void ExposureRejectsOutOfRangeSettings()
    {
        var settings = new ExposureSettings { Exposure = 30 };
        Assert.False(settings.IsValid);
        Assert.Throws<ArgumentException>(() => AdjustmentRunner.ApplyExposure(Surface((1, 1, 1, 255)), settings, null));
    }

    // ---------- Gradient Map ----------

    [Fact]
    public void GradientMapMapsLumaBetweenEnds()
    {
        var settings = new GradientMapSettings { Shadows = AdjustmentColor.Black, Highlights = new AdjustmentColor(1, 0, 0) };
        var source = Surface((255, 255, 255, 255), (0, 0, 0, 255), (128, 128, 128, 255));

        var result = AdjustmentRunner.ApplyGradientMap(source, settings, null);

        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(result, 0)); // white → highlights
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), Pixel(result, 1)); // black → shadows
        Assert.Equal(((byte)128, (byte)0, (byte)0, (byte)255), Pixel(result, 2)); // mid gray → half way
    }

    [Fact]
    public void GradientMapReverseSwapsTheEnds()
    {
        var settings = new GradientMapSettings
        {
            Shadows = AdjustmentColor.Black,
            Highlights = new AdjustmentColor(1, 0, 0),
            Reversed = true,
        };
        var source = Surface((255, 255, 255, 255));

        var result = AdjustmentRunner.ApplyGradientMap(source, settings, null);

        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), Pixel(result, 0));
    }

    // ---------- Invert ----------

    [Fact]
    public void InvertComplementsColorsAndKeepsAlpha()
    {
        var source = Surface((255, 0, 0, 255), (100, 50, 25, 128));

        var result = AdjustmentRunner.ApplyInvert(source, null);

        Assert.Equal(((byte)0, (byte)255, (byte)255, (byte)255), Pixel(result, 0));
        Assert.Equal(((byte)155, (byte)205, (byte)230, (byte)128), Pixel(result, 1));
    }

    [Fact]
    public void InvertLimitedToSelectionLeavesOutsidePixelsAlone()
    {
        var source = Surface((255, 0, 0, 255), (255, 0, 0, 255));
        var selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(0, 0, 1, 1, SelectionMode.Replace)).Clip(2, 1);

        var result = AdjustmentRunner.ApplyInvert(source, selection);

        Assert.Equal(((byte)0, (byte)255, (byte)255, (byte)255), Pixel(result, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(result, 1));
    }

    [Fact]
    public void SelectionBlendKeepsUnselectedPixelsForTableAdjustments()
    {
        var settings = new LevelsSettings();
        settings.Ranges[0] = new LevelRange(100, 1, 200, 0, 255);
        var source = Surface((40, 40, 40, 255), (40, 40, 40, 255));
        var selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(0, 0, 1, 1, SelectionMode.Replace)).Clip(2, 1);

        var result = AdjustmentRunner.ApplyTables(source, settings.BuildTables(), selection);

        Assert.Equal((byte)0, Pixel(result, 0).R); // selected: remapped by the black point
        Assert.Equal((byte)40, Pixel(result, 1).R); // unselected: untouched
    }

    // ---------- Undo ----------

    [Fact]
    public void AdjustmentCommandRestoresExactBuffers()
    {
        var surface = new RasterSurface(2, 1);
        surface.SetPixel(0, 0, 10, 20, 30, 255);
        surface.SetPixel(1, 0, 40, 50, 60, 255);
        var before = (byte[])surface.Pixels.Clone();

        var settings = new LevelsSettings();
        settings.Ranges[0] = new LevelRange(0, 2, 255, 0, 255);
        var adjusted = AdjustmentRunner.ApplyTables(surface, settings.BuildTables(), null);
        var after = (byte[])adjusted.Pixels.Clone();
        Array.Copy(after, surface.Pixels, after.Length);
        surface.MarkDirty();
        var versionAfterEdit = surface.Version;

        var command = new AdjustmentCommand(surface, before, after, "Levels");
        command.Undo();
        Assert.Equal(before, surface.Pixels);
        Assert.True(surface.Version > versionAfterEdit);

        command.Redo();
        Assert.Equal(after, surface.Pixels);
    }
}
