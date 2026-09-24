namespace Compositor.Core.Imaging;

/// <summary>
/// Composites a straight-alpha buffer over an opaque matte. This is the
/// equivalent of upstream's JPEG path in <c>ImageExporter.jpeg</c>: a context
/// without an alpha channel filled with the matte colour, then the rendered
/// canvas drawn into it with source-over. JPEG has no alpha, so the result is
/// always opaque and every transparent pixel becomes the chosen background.
/// </summary>
public static class MatteComposite
{
    /// <summary>Returns a new opaque buffer; the input is never modified.</summary>
    public static byte[] Apply(ReadOnlySpan<byte> rgba, int width, int height, byte red, byte green, byte blue)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Dimensions must be positive.", nameof(width));
        }

        var expected = width * height * 4;
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Pixel buffer is {rgba.Length} bytes; expected {expected}.", nameof(rgba));
        }

        var output = new byte[expected];
        for (var i = 0; i < expected; i += 4)
        {
            var coverage = rgba[i + 3];
            if (coverage == 255)
            {
                output[i] = rgba[i];
                output[i + 1] = rgba[i + 1];
                output[i + 2] = rgba[i + 2];
            }
            else if (coverage == 0)
            {
                output[i] = red;
                output[i + 1] = green;
                output[i + 2] = blue;
            }
            else
            {
                var t = coverage / 255f;
                output[i] = Blend(rgba[i], red, t);
                output[i + 1] = Blend(rgba[i + 1], green, t);
                output[i + 2] = Blend(rgba[i + 2], blue, t);
            }

            output[i + 3] = 255;
        }

        return output;
    }

    /// <summary>Convenience overload taking the options record.</summary>
    public static byte[] Apply(ReadOnlySpan<byte> rgba, int width, int height, JpegOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (r, g, b) = options.Normalize().MatteBytes;
        return Apply(rgba, width, height, r, g, b);
    }

    /// <summary>
    /// Straight-alpha blend onto the matte in 8-bit units. Rounded away from zero so a
    /// .5 tie never decays toward the matte on mid-tone edges.
    /// </summary>
    private static byte Blend(byte source, byte matte, float t) =>
        byte.CreateSaturating((int)MathF.Round((source * t) + (matte * (1f - t)), MidpointRounding.AwayFromZero));
}
