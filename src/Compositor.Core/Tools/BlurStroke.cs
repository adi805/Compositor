namespace Compositor.Core.Tools;

/// <summary>
/// Blur tool, upstream BlurTool.swift: the layer as sampled at stroke start,
/// gaussian-softened by sigma = clamp(diameter / 10, 1.5, 30), painted back
/// under the brush path. Re-stroking the same area in a NEW stroke softens
/// further (each stroke samples the layer fresh); overlapping dabs within one
/// stroke never double-blur because they paint from one sample.
/// Blur runs in premultiplied space via three box passes (sigma approximation).
/// </summary>
public static class BlurStroke
{
    public static float SigmaFor(float diameter) => Math.Clamp(diameter / 10f, 1.5f, 30f);

    public static void Apply(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        BrushSettings settings,
        Selection.SelectionClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            return;
        }

        var before = (byte[])surface.Pixels.Clone();
        var blurred = GaussianBlur(before, surface.Width, surface.Height, SigmaFor(settings.Diameter));

        var coverage = new StrokeCoverage(surface.Width, surface.Height, settings, clip);
        foreach (var point in path)
        {
            coverage.WalkTo(point.X, point.Y);
        }
        coverage.PaintSampleRegion(surface, before, blurred);
    }

    /// <summary>Three-pass box blur (separable), the standard gaussian
    /// approximation; alpha-weighted so transparent pixels stay transparent.</summary>
    public static byte[] GaussianBlur(byte[] rgba, int width, int height, float sigma)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("Buffer size mismatch.", nameof(rgba));
        }

        // Approximate the gaussian with 3 box passes (W3C/cairo convention):
        // box size d = floor(3 * sqrt(2*pi) * sigma / 4 + 0.5), odd.
        var d = (int)MathF.Floor(3f * MathF.Sqrt(2f * MathF.PI) * sigma / 4f + 0.5f);
        if (d < 1)
        {
            return (byte[])rgba.Clone();
        }
        d |= 1; // odd
        var r = d / 2;

        var premul = ToPremultiplied(rgba, width, height);
        var temp = new byte[premul.Length];
        BoxBlurH(premul, temp, width, height, r);
        BoxBlurV(temp, premul, width, height, r);
        BoxBlurH(premul, temp, width, height, r);
        BoxBlurV(temp, premul, width, height, r);
        BoxBlurH(premul, temp, width, height, r);
        BoxBlurV(temp, premul, width, height, r);
        return ToStraight(premul, width, height);
    }

    private static byte[] ToPremultiplied(byte[] rgba, int width, int height)
    {
        var result = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3] / 255f;
            result[i] = (byte)MathF.Round(rgba[i] * a);
            result[i + 1] = (byte)MathF.Round(rgba[i + 1] * a);
            result[i + 2] = (byte)MathF.Round(rgba[i + 2] * a);
            result[i + 3] = rgba[i + 3];
        }
        return result;
    }

    private static byte[] ToStraight(byte[] premul, int width, int height)
    {
        var result = new byte[premul.Length];
        for (var i = 0; i < premul.Length; i += 4)
        {
            var a = premul[i + 3] / 255f;
            if (a <= 0f)
            {
                continue;
            }
            result[i] = (byte)Math.Clamp(MathF.Round(premul[i] / a), 0, 255);
            result[i + 1] = (byte)Math.Clamp(MathF.Round(premul[i + 1] / a), 0, 255);
            result[i + 2] = (byte)Math.Clamp(MathF.Round(premul[i + 2] / a), 0, 255);
            result[i + 3] = premul[i + 3];
        }
        return result;
    }

    private static void BoxBlurH(byte[] src, byte[] dst, int width, int height, int r)
    {
        var inv = 1f / ((r * 2) + 1);
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var c = 0; c < 4; c++)
            {
                var sum = 0f;
                for (var k = -r; k <= r; k++)
                {
                    sum += src[row + (Math.Clamp(k, 0, width - 1) * 4) + c];
                }
                for (var x = 0; x < width; x++)
                {
                    dst[row + (x * 4) + c] = (byte)MathF.Round(sum * inv);
                    var addK = Math.Clamp(x + r + 1, 0, width - 1);
                    var subK = Math.Clamp(x - r, 0, width - 1);
                    sum += src[row + (addK * 4) + c] - src[row + (subK * 4) + c];
                }
            }
        }
    }

    private static void BoxBlurV(byte[] src, byte[] dst, int width, int height, int r)
    {
        var inv = 1f / ((r * 2) + 1);
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < 4; c++)
            {
                var sum = 0f;
                for (var k = -r; k <= r; k++)
                {
                    sum += src[(Math.Clamp(k, 0, height - 1) * width * 4) + (x * 4) + c];
                }
                for (var y = 0; y < height; y++)
                {
                    dst[(y * width * 4) + (x * 4) + c] = (byte)MathF.Round(sum * inv);
                    var addK = Math.Clamp(y + r + 1, 0, height - 1);
                    var subK = Math.Clamp(y - r, 0, height - 1);
                    sum += src[(addK * width * 4) + (x * 4) + c] - src[(subK * width * 4) + (x * 4) + c];
                }
            }
        }
    }
}
