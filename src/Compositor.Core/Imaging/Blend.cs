namespace Compositor.Core.Imaging;

/// <summary>
/// Per-pixel blend kernels for all 13 modes, ported from the formulas the
/// upstream Mac app relies on (CoreImage / CG per PDF and the W3C Compositing
/// specification). Separable modes act per channel; Hue/Saturation/Color/
/// Luminosity use the spec's non-separable HSL routines.
///
/// All inputs/outputs are straight (non-premultiplied) colors in [0,1].
/// <see cref="Compose"/> additionally applies the source-over alpha math:
///   Cs' = (1-αb)·Cs + αb·B(Cb,Cs);  αo = αs + αb·(1-αs);
///   Co  = (αs·Cs' + αb·(1-αs)·Cb) / αo
/// which for Normal reduces to classic straight-alpha source-over.
/// </summary>
public static class Blend
{
    /// <summary>Blends source over backdrop in the given mode (alpha ignored).</summary>
    public static (float R, float G, float B) BlendColor(
        BlendMode mode,
        (float R, float G, float B) backdrop,
        (float R, float G, float B) source)
    {
        return mode switch
        {
            BlendMode.Normal => source,
            BlendMode.Multiply => Separate(backdrop, source, (b, s) => b * s),
            BlendMode.Screen => Separate(backdrop, source, (b, s) => b + s - b * s),
            BlendMode.Overlay => Separate(backdrop, source, Overlay),
            BlendMode.Darken => Separate(backdrop, source, (b, s) => Math.Min(b, s)),
            BlendMode.Lighten => Separate(backdrop, source, (b, s) => Math.Max(b, s)),
            BlendMode.Difference => Separate(backdrop, source, (b, s) => MathF.Abs(b - s)),
            BlendMode.ColorDodge => Separate(backdrop, source, ColorDodge),
            BlendMode.ColorBurn => Separate(backdrop, source, ColorBurn),
            BlendMode.Hue => NonSeparable(backdrop, source, Hsl.Hue),
            BlendMode.Saturation => NonSeparable(backdrop, source, Hsl.Saturation),
            BlendMode.Color => NonSeparable(backdrop, source, Hsl.Color),
            BlendMode.Luminosity => NonSeparable(backdrop, source, Hsl.Luminosity),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown blend mode."),
        };
    }

    /// <summary>Full source-over compositing of one pixel, including alpha.</summary>
    public static void Compose(
        BlendMode mode, float layerOpacity,
        byte backdropR, byte backdropG, byte backdropB, byte backdropA,
        byte sourceR, byte sourceG, byte sourceB, byte sourceA,
        out byte outR, out byte outG, out byte outB, out byte outA)
    {
        var alphaSource = (sourceA / 255f) * layerOpacity;
        var alphaBackdrop = backdropA / 255f;

        if (alphaSource <= 0f)
        {
            outR = backdropR; outG = backdropG; outB = backdropB; outA = backdropA;
            return;
        }

        var alphaOut = alphaSource + alphaBackdrop * (1f - alphaSource);
        if (alphaOut <= 0f)
        {
            outR = outG = outB = outA = 0;
            return;
        }

        var cb = (backdropR / 255f, backdropG / 255f, backdropB / 255f);
        var cs = (sourceR / 255f, sourceG / 255f, sourceB / 255f);
        var blended = BlendColor(mode, cb, cs);

        // Mix the blended result with the raw source by backdrop transparency.
        var mixedR = (1f - alphaBackdrop) * cs.Item1 + alphaBackdrop * blended.R;
        var mixedG = (1f - alphaBackdrop) * cs.Item2 + alphaBackdrop * blended.G;
        var mixedB = (1f - alphaBackdrop) * cs.Item3 + alphaBackdrop * blended.B;

        var r = (alphaSource * mixedR + alphaBackdrop * (1f - alphaSource) * cb.Item1) / alphaOut;
        var g = (alphaSource * mixedG + alphaBackdrop * (1f - alphaSource) * cb.Item2) / alphaOut;
        var b = (alphaSource * mixedB + alphaBackdrop * (1f - alphaSource) * cb.Item3) / alphaOut;

        outR = ToByte(r);
        outG = ToByte(g);
        outB = ToByte(b);
        outA = ToByte(alphaOut);
    }

    private static (float, float, float) Separate(
        (float R, float G, float B) backdrop,
        (float R, float G, float B) source,
        Func<float, float, float> kernel) =>
    (
        kernel(backdrop.R, source.R),
        kernel(backdrop.G, source.G),
        kernel(backdrop.B, source.B)
    );

    private static (float, float, float) NonSeparable(
        (float R, float G, float B) backdrop,
        (float R, float G, float B) source,
        Func<(float, float, float), (float, float, float), (float, float, float)> kernel) =>
        kernel(backdrop, source);

    // PDF/W3C separable kernels.

    private static float Overlay(float backdrop, float source) =>
        backdrop <= 0.5f
            ? 2f * backdrop * source
            : 1f - 2f * (1f - backdrop) * (1f - source);

    private static float ColorDodge(float backdrop, float source)
    {
        if (backdrop <= 0f) return 0f;
        if (source >= 1f) return 1f;
        return MathF.Min(1f, backdrop / (1f - source));
    }

    private static float ColorBurn(float backdrop, float source)
    {
        if (backdrop >= 1f) return 1f;
        if (source <= 0f) return 0f;
        return 1f - MathF.Min(1f, (1f - backdrop) / source);
    }

    private static byte ToByte(float value) => byte.CreateSaturating((int)MathF.Round(value * 255f));
}

/// <summary>W3C Compositing spec non-separable (HSL) blend routines.</summary>
internal static class Hsl
{
    public static float Luminance((float, float, float) c) =>
        0.3f * c.Item1 + 0.59f * c.Item2 + 0.11f * c.Item3;

    public static float Saturation((float, float, float) c)
    {
        var max = MathF.Max(c.Item1, MathF.Max(c.Item2, c.Item3));
        var min = MathF.Min(c.Item1, MathF.Min(c.Item2, c.Item3));
        return max - min;
    }

    public static (float, float, float) ClipColor((float, float, float) c)
    {
        var luminance = Luminance(c);
        var (r, g, b) = c;
        var min = MathF.Min(r, MathF.Min(g, b));
        var max = MathF.Max(r, MathF.Max(g, b));

        if (min < 0f)
        {
            r = luminance + ((r - luminance) * luminance / (luminance - min));
            g = luminance + ((g - luminance) * luminance / (luminance - min));
            b = luminance + ((b - luminance) * luminance / (luminance - min));
        }

        if (max > 1f)
        {
            r = luminance + ((r - luminance) * (1f - luminance) / (max - luminance));
            g = luminance + ((g - luminance) * (1f - luminance) / (max - luminance));
            b = luminance + ((b - luminance) * (1f - luminance) / (max - luminance));
        }

        return (r, g, b);
    }

    public static (float, float, float) SetLuminance((float, float, float) c, float luminance)
    {
        var offset = luminance - Luminance(c);
        return ClipColor((c.Item1 + offset, c.Item2 + offset, c.Item3 + offset));
    }

    public static (float, float, float) SetSaturation((float, float, float) c, float saturation)
    {
        var comps = new[] { c.Item1, c.Item2, c.Item3 };
        var order = new[] { 0, 1, 2 };
        Array.Sort(order, (a, b) => comps[b].CompareTo(comps[a])); // indices by value, descending

        var cmax = comps[order[0]];
        var cmid = comps[order[1]];
        var cmin = comps[order[2]];
        var newMid = cmax > cmin ? ((cmid - cmin) * saturation) / (cmax - cmin) : 0f;

        var result = new float[3];
        result[order[0]] = saturation;
        result[order[1]] = newMid;
        result[order[2]] = 0f;
        return (result[0], result[1], result[2]);
    }

    public static (float, float, float) Hue((float, float, float) backdrop, (float, float, float) source) =>
        SetLuminance(SetSaturation(source, Saturation(backdrop)), Luminance(backdrop));

    public static (float, float, float) Saturation((float, float, float) backdrop, (float, float, float) source) =>
        SetLuminance(SetSaturation(backdrop, Saturation(source)), Luminance(backdrop));

    public static (float, float, float) Color((float, float, float) backdrop, (float, float, float) source) =>
        SetLuminance(source, Luminance(backdrop));

    public static (float, float, float) Luminosity((float, float, float) backdrop, (float, float, float) source) =>
        SetLuminance(backdrop, Luminance(source));
}
