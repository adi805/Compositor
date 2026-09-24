namespace Compositor.Core.Imaging;

/// <summary>
/// Turns a layer's <see cref="LayerTransform"/> into actual pixels: the layer's own
/// surface is mapped onto its placement rect (position, size, clockwise rotation) and
/// sampled back out at document resolution. This is what the Mac app gets for free from
/// CoreGraphics' CTM; here it is explicit so export, compositing and the canvas agree.
/// </summary>
/// <remarks>
/// Sampling is inverse-mapped and bilinear, and the interpolation happens on
/// <em>premultiplied</em> colour. Averaging straight-alpha channels pulls a dark or
/// coloured fringe out of fully transparent texels, which is visible the moment a layer
/// is scaled or rotated, so the weights are applied after multiplying by coverage.
/// <para>
/// <see cref="LayerTransform.FlipH"/> and <see cref="LayerTransform.FlipV"/> are
/// deliberately NOT applied here. v0.2 bakes flips into the pixels at edit time
/// (see EditorViewModel), so honouring the flag as well would mirror every saved layer a
/// second time. Upstream is flag-only; unifying the two is the open .comp compatibility
/// question recorded in docs/PARITY.md, not something to change quietly.
/// </para>
/// </remarks>
public static class LayerPlacement
{
    /// <summary>
    /// The placement rect in document pixels, with "cover the canvas" resolved.
    /// Mirrors upstream, where a zero-sized transform means the layer fills its canvas.
    /// </summary>
    public static (double X, double Y, double W, double H) Resolve(
        LayerTransform transform, int canvasWidth, int canvasHeight)
    {
        var w = transform.CoversCanvas ? canvasWidth : transform.Width;
        var h = transform.CoversCanvas ? canvasHeight : transform.Height;
        return (transform.OriginX, transform.OriginY, w, h);
    }

    /// <summary>
    /// True when the transform leaves a canvas-sized surface exactly where it already is,
    /// so callers can skip the resample and keep byte-identical output.
    /// </summary>
    public static bool IsIdentity(LayerTransform transform, int canvasWidth, int canvasHeight) =>
        transform.RotationDegrees == 0
        && transform.OriginX == 0
        && transform.OriginY == 0
        && (transform.CoversCanvas
            || (transform.Width == canvasWidth && transform.Height == canvasHeight));

    /// <summary>
    /// Resample <paramref name="source"/> (straight-alpha RGBA, <c>srcWidth</c> x
    /// <c>srcHeight</c>) onto a document-sized canvas through its transform.
    /// Returns a new buffer unless the transform is an exact identity, in which case the
    /// caller's own buffer is handed straight back.
    /// </summary>
    public static byte[] Place(
        LayerTransform transform,
        byte[] source,
        int srcWidth,
        int srcHeight,
        int canvasWidth,
        int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length != srcWidth * srcHeight * 4)
        {
            throw new ArgumentException(
                $"Expected {srcWidth * srcHeight * 4} bytes for a {srcWidth}x{srcHeight} surface.",
                nameof(source));
        }

        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), "Canvas must have a size.");
        }

        var (x, y, w, h) = Resolve(transform, canvasWidth, canvasHeight);
        if (IsIdentity(transform, srcWidth, srcHeight) && srcWidth == canvasWidth && srcHeight == canvasHeight)
        {
            return source;
        }

        var output = new byte[canvasWidth * canvasHeight * 4];
        if (srcWidth <= 0 || srcHeight <= 0 || w <= 0 || h <= 0)
        {
            return output; // nothing to place, or a degenerate rect: fully transparent
        }

        var radians = transform.RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // The source's own centre is the pivot, and it lands on the placement rect's
        // centre, exactly like upstream rotating a layer around its middle.
        var rectCenterX = x + w / 2.0;
        var rectCenterY = y + h / 2.0;
        var scaleX = w / srcWidth;
        var scaleY = h / srcHeight;
        var srcCenterX = srcWidth / 2.0;
        var srcCenterY = srcHeight / 2.0;

        for (var dy = 0; dy < canvasHeight; dy++)
        {
            var destY = dy + 0.5 - rectCenterY;
            for (var dx = 0; dx < canvasWidth; dx++)
            {
                var destX = dx + 0.5 - rectCenterX;

                // Inverse of dest = centre + R(clockwise) * (src - srcCentre) * scale.
                var rotatedX = destX * cos + destY * sin;
                var rotatedY = -destX * sin + destY * cos;
                var u = rotatedX / scaleX + srcCenterX - 0.5;
                var v = rotatedY / scaleY + srcCenterY - 0.5;

                var x0 = (int)Math.Floor(u);
                var y0 = (int)Math.Floor(v);
                if (x0 < -1 || y0 < -1 || x0 > srcWidth || y0 > srcHeight)
                {
                    continue; // a full texel away from the source: stays transparent
                }

                var fx = u - x0;
                var fy = v - y0;
                SampleBilinear(
                    source, srcWidth, srcHeight, x0, y0, fx, fy,
                    output, canvasWidth, dx, dy);
            }
        }

        return output;
    }

    private static void SampleBilinear(
        byte[] source, int srcWidth, int srcHeight,
        int x0, int y0, double fx, double fy,
        byte[] output, int outStridePixels, int destX, int destY)
    {
        double r = 0, g = 0, b = 0, a = 0;
        for (var oy = 0; oy < 2; oy++)
        {
            var sy = y0 + oy;
            if (sy < 0 || sy >= srcHeight)
            {
                continue; // outside the source reads as transparent black
            }

            for (var ox = 0; ox < 2; ox++)
            {
                var sx = x0 + ox;
                if (sx < 0 || sx >= srcWidth)
                {
                    continue;
                }

                var weight = (ox == 0 ? 1 - fx : fx) * (oy == 0 ? 1 - fy : fy);
                if (weight <= 0)
                {
                    continue;
                }

                var i = (sy * srcWidth + sx) * 4;
                var coverage = source[i + 3] / 255.0;
                r += source[i] * coverage * weight;
                g += source[i + 1] * coverage * weight;
                b += source[i + 2] * coverage * weight;
                a += coverage * weight;
            }
        }

        if (a <= 0)
        {
            return;
        }

        var straight = a > 1 ? 1 : a;
        WritePixel(output, destX, destY, outStridePixels, r, g, b, straight);
    }

    private static void WritePixel(
        byte[] output, int destX, int destY, int stridePixels,
        double premulR, double premulG, double premulB, double alpha)
    {
        var i = (destY * stridePixels + destX) * 4;
        output[i] = ByteFrom(premulR / alpha);
        output[i + 1] = ByteFrom(premulG / alpha);
        output[i + 2] = ByteFrom(premulB / alpha);
        output[i + 3] = ByteFrom(alpha * 255.0);
    }

    private static byte ByteFrom(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= 255)
        {
            return 255;
        }

        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
