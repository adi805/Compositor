using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Per-mode blend kernels with hand-computed expected values from the
/// PDF/W3C Compositing formulas (the ones upstream gets from
/// CoreGraphics/CoreImage). Inputs chosen for clean arithmetic:
/// 204/255 = 0.8, 128/255 = 0.501961, 64/255 = 0.250980.
/// </summary>
public class BlendTests
{
    private static byte Blend1(BlendMode mode, byte backdrop, byte source)
    {
        Blend.Compose(mode, 1f,
            backdrop, backdrop, backdrop, 255,
            source, source, source, 255,
            out var r, out _, out _, out _);
        return r;
    }

    [Theory]
    [InlineData(204, 128, 102)]  // 0.8 * 0.501961 = 0.401569 -> 102.4 -> 102
    [InlineData(128, 128, 64)]   // 0.501961^2 = 0.251965 -> 64.25 -> 64
    public void Multiply_MatchesPdfFormula(byte backdrop, byte source, byte expected)
        => Assert.Equal(expected, Blend1(BlendMode.Multiply, backdrop, source));

    [Theory]
    [InlineData(204, 128, 230)]  // b+s-bs = 0.900392 -> 229.6 -> 230
    [InlineData(64, 128, 160)]   // 0.250980+0.501961-0.125982 = 0.626959 -> 159.87 -> 160
    public void Screen_MatchesPdfFormula(byte backdrop, byte source, byte expected)
        => Assert.Equal(expected, Blend1(BlendMode.Screen, backdrop, source));

    [Theory]
    [InlineData(204, 128, 204)]  // light half: 1-2(0.2)(0.498039) = 0.800784 -> 204
    [InlineData(64, 128, 64)]    // dark half: 2(0.250980)(0.501961) = 0.251964 -> 64
    public void Overlay_MatchesPdfFormula(byte backdrop, byte source, byte expected)
        => Assert.Equal(expected, Blend1(BlendMode.Overlay, backdrop, source));

    [Fact]
    public void Darken_TakesMinimum() => Assert.Equal(128, Blend1(BlendMode.Darken, 204, 128));

    [Fact]
    public void Lighten_TakesMaximum() => Assert.Equal(204, Blend1(BlendMode.Lighten, 204, 128));

    [Fact]
    public void Difference_TakesAbsoluteDelta() => Assert.Equal(76, Blend1(BlendMode.Difference, 204, 128));

    [Theory]
    [InlineData(128, 128, 255)]  // b/(1-s) = 0.501961/0.498039 = 1.007874 -> clamped 1
    [InlineData(64, 128, 129)]   // 0.250980/0.498039 = 0.503937 -> 128.5 -> 129
    [InlineData(0, 128, 0)]      // spec: Cb=0 -> 0
    public void ColorDodge_MatchesPdfFormula(byte backdrop, byte source, byte expected)
        => Assert.Equal(expected, Blend1(BlendMode.ColorDodge, backdrop, source));

    [Theory]
    [InlineData(204, 128, 153)]  // 1-(0.2/0.501961) = 0.601563 -> 153.4 -> 153
    [InlineData(255, 128, 255)]  // spec: Cb=1 -> 1
    [InlineData(128, 0, 0)]      // spec: Cs=0 -> 0
    public void ColorBurn_MatchesPdfFormula(byte backdrop, byte source, byte expected)
        => Assert.Equal(expected, Blend1(BlendMode.ColorBurn, backdrop, source));

    private static (byte R, byte G, byte B) BlendRgb(BlendMode mode, (byte R, byte G, byte B) backdrop, (byte R, byte G, byte B) source)
    {
        Blend.Compose(mode, 1f,
            backdrop.R, backdrop.G, backdrop.B, 255,
            source.R, source.G, source.B, 255,
            out var r, out var g, out var b, out _);
        return (r, g, b);
    }

    // Non-separable (HSL) modes. Definitions from the W3C Compositing spec;
    // expected values computed by hand, asserted with +/-2 tolerance for
    // float rounding at the byte boundary. Backdrop: neutral gray (sat 0,
    // lum 0.501961). Source: pure red (hue 0deg, sat 1, lum 0.3).

    [Fact]
    public void Hue_KeepsBackdropLuminanceAndSaturation()
    {
        // Source hue with backdrop saturation (0) and luminance -> gray stays gray.
        var result = BlendRgb(BlendMode.Hue, (128, 128, 128), (255, 0, 0));
        Assert.Equal((byte)128, result.R);
        Assert.Equal((byte)128, result.G);
        Assert.Equal((byte)128, result.B);
    }

    [Fact]
    public void Saturation_TakesSourceSaturationWithBackdropHue()
    {
        // Backdrop channels spread to source's full saturation, keeping its
        // own luminance. Hand-computed incl. the spec's ClipColor rescale:
        // SetSat -> (1,0,0); SetLum offset +0.201961 then max>1 rescale
        // factor 0.711484 -> (1.0, 0.288516, 0.288516) -> (255, 74, 74).
        var (r, g, b) = BlendRgb(BlendMode.Saturation, (128, 128, 128), (255, 0, 0));
        Assert.InRange(r, 253, 255);
        Assert.InRange(g, 72, 76);
        Assert.InRange(b, 72, 76);
    }

    [Fact]
    public void Color_TakesSourceColorWithBackdropLuminance()
    {
        // Red hue/sat at gray's luminance. Same clip path as Saturation:
        // SetLum(red, 0.501961) -> (255, 74, 74) computed by hand.
        var (r, g, b) = BlendRgb(BlendMode.Color, (128, 128, 128), (255, 0, 0));
        Assert.InRange(r, 253, 255);
        Assert.InRange(g, 72, 76);
        Assert.InRange(b, 72, 76);
    }

    [Fact]
    public void Luminosity_TakesBackdropColorWithSourceLuminance()
    {
        // Neutral gray at red's luminance (0.3) -> (77,77,77).
        var result = BlendRgb(BlendMode.Luminosity, (128, 128, 128), (255, 0, 0));
        Assert.InRange(result.R, 75, 79);
        Assert.Equal(result.R, result.G);
        Assert.Equal(result.R, result.B);
    }

    [Fact]
    public void Luminosity_WhiteSourceLiftsBackdropToWhite()
    {
        // Source luminance 1.0 pulls the gray backdrop all the way to white.
        var result = BlendRgb(BlendMode.Luminosity, (128, 128, 128), (255, 255, 255));
        Assert.Equal((byte)255, result.R);
        Assert.Equal((byte)255, result.G);
        Assert.Equal((byte)255, result.B);
    }

    [Fact]
    public void Compose_NormalTransparentSourceKeepsBackdrop()
    {
        Blend.Compose(BlendMode.Normal, 1f,
            10, 20, 30, 255,
            0, 0, 0, 0,
            out var r, out var g, out var b, out var a);
        Assert.Equal((10, 20, 30, 255), (r, g, b, a));
    }

    [Fact]
    public void Compose_OpaqueSourceOverTransparentBackdropIsSource()
    {
        Blend.Compose(BlendMode.Normal, 1f,
            0, 0, 0, 0,
            200, 100, 50, 255,
            out var r, out var g, out var b, out var a);
        Assert.Equal((200, 100, 50, 255), (r, g, b, a));
    }

    [Fact]
    public void Compose_HalfOpacitySourceOverBlackGivesQuarterWhite()
    {
        // sa = 1.0 * 0.5, da = 1: out = (0.5 * 1.0) / 1.0 = 0.5 -> 128.
        Blend.Compose(BlendMode.Normal, 0.5f,
            0, 0, 0, 255,
            255, 255, 255, 255,
            out var r, out _, out _, out var a);
        Assert.Equal(128, r);
        Assert.Equal(255, a);
    }

    [Fact]
    public void IsValid_AcceptsAllThirteenModes()
    {
        Assert.Equal(13, Enum.GetValues<BlendMode>().Length);
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            Assert.True(mode.IsValid());
        }
        Assert.False(((BlendMode)13).IsValid());
    }
}
