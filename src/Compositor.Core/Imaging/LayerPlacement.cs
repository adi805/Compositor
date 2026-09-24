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
    /// The mapping between document pixels and one layer's own pixel space. Built once per
    /// draw: the trigonometry must not be repeated per pixel, and both directions have to
    /// come from the same numbers, or the brush would paint somewhere the renderer says the
    /// ink landed.
    /// </summary>
    public readonly struct Mapper
    {
        private readonly double _rectCenterX;
        private readonly double _rectCenterY;
        private readonly double _sourceCenterX;
        private readonly double _sourceCenterY;
        private readonly double _scaleX;
        private readonly double _scaleY;
        private readonly double _cos;
        private readonly double _sin;

        internal Mapper(
            LayerTransform transform, int sourceWidth, int sourceHeight,
            double rectX, double rectY, double rectWidth, double rectHeight)
        {
            var radians = transform.RotationDegrees * Math.PI / 180.0;
            _cos = Math.Cos(radians);
            _sin = Math.Sin(radians);
            _rectCenterX = rectX + rectWidth / 2.0;
            _rectCenterY = rectY + rectHeight / 2.0;
            _sourceCenterX = sourceWidth / 2.0;
            _sourceCenterY = sourceHeight / 2.0;
            _scaleX = sourceWidth > 0 ? rectWidth / sourceWidth : 0;
            _scaleY = sourceHeight > 0 ? rectHeight / sourceHeight : 0;
        }

        /// <summary>False when either side has no size, so nothing can be mapped.</summary>
        public bool IsValid => _scaleX > 0 && _scaleY > 0;

        /// <summary>
        /// Which point inside the layer's own surface a document point is drawn from.
        /// Continuous units: layer pixel i covers [i, i+1). This is the inverse of
        /// <see cref="ToDocument"/>.
        /// </summary>
        public (double X, double Y) ToLayer(double documentX, double documentY)
        {
            var dx = documentX - _rectCenterX;
            var dy = documentY - _rectCenterY;

            // Inverse of a clockwise turn: rx = dx*cos + dy*sin, ry = -dx*sin + dy*cos.
            var rotatedX = dx * _cos + dy * _sin;
            var rotatedY = -dx * _sin + dy * _cos;
            return (rotatedX / _scaleX + _sourceCenterX, rotatedY / _scaleY + _sourceCenterY);
        }

        /// <summary>
        /// Where a point in the layer's own pixel space ends up on the canvas. This is the
        /// direction a stroke travels to check its own work, and the same maths the canvas
        /// control performs with a matrix.
        /// </summary>
        public (double X, double Y) ToDocument(double layerX, double layerY)
        {
            var sx = (layerX - _sourceCenterX) * _scaleX;
            var sy = (layerY - _sourceCenterY) * _scaleY;
            return (
                sx * _cos - sy * _sin + _rectCenterX,
                sx * _sin + sy * _cos + _rectCenterY);
        }
    }

    /// <summary>The document &lt;-&gt; layer mapping for a layer of the given pixel size.</summary>
    public static Mapper MapFor(
        LayerTransform transform, int sourceWidth, int sourceHeight, int canvasWidth, int canvasHeight)
    {
        var (x, y, w, h) = Resolve(transform, canvasWidth, canvasHeight);
        return new Mapper(transform, sourceWidth, sourceHeight, x, y, w, h);
    }

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

        if (IsIdentity(transform, srcWidth, srcHeight) && srcWidth == canvasWidth && srcHeight == canvasHeight)
        {
            return source;
        }

        var output = new byte[canvasWidth * canvasHeight * 4];
        var mapper = MapFor(transform, srcWidth, srcHeight, canvasWidth, canvasHeight);
        if (!mapper.IsValid)
        {
            return output; // nothing to place, or a degenerate rect: fully transparent
        }

        for (var dy = 0; dy < canvasHeight; dy++)
        {
            for (var dx = 0; dx < canvasWidth; dx++)
            {
                // Continuous layer space, then drop the half pixel to reach texel space.
                var (layerX, layerY) = mapper.ToLayer(dx + 0.5, dy + 0.5);
                var u = layerX - 0.5;
                var v = layerY - 0.5;

                var x0 = (int)Math.Floor(u);
                var y0 = (int)Math.Floor(v);
                if (x0 < -1 || y0 < -1 || x0 > srcWidth || y0 > srcHeight)
                {
                    continue; // a full texel away from the source: stays transparent
                }

                SampleBilinear(
                    source, srcWidth, srcHeight, x0, y0, u - x0, v - y0,
                    output, canvasWidth, dx, dy);
            }
        }

        return output;
    }

    /// <summary>
    /// A document-space selection mask expressed in one layer's own pixel space, so a clip
    /// drawn on the canvas still bounds the brush after the layer has been moved or scaled.
    /// Nearest neighbour on purpose: a mask is yes/no per canvas pixel, and interpolating it
    /// would quietly erode or fatten the selection at its edge.
    /// </summary>
    public static byte[] PlaceMask(
        LayerTransform transform,
        byte[] documentMask,
        int canvasWidth,
        int canvasHeight,
        int layerWidth,
        int layerHeight)
    {
        ArgumentNullException.ThrowIfNull(documentMask);
        if (documentMask.Length != canvasWidth * canvasHeight)
        {
            throw new ArgumentException(
                $"Expected {canvasWidth * canvasHeight} bytes for a {canvasWidth}x{canvasHeight} mask.",
                nameof(documentMask));
        }

        var output = new byte[layerWidth * layerHeight];
        if (layerWidth <= 0 || layerHeight <= 0)
        {
            return output;
        }

        var mapper = MapFor(transform, layerWidth, layerHeight, canvasWidth, canvasHeight);
        if (!mapper.IsValid)
        {
            return output;
        }

        for (var y = 0; y < layerHeight; y++)
        {
            for (var x = 0; x < layerWidth; x++)
            {
                var (docX, docY) = mapper.ToDocument(x + 0.5, y + 0.5);
                var sourceX = (int)Math.Floor(docX);
                var sourceY = (int)Math.Floor(docY);
                if (sourceX < 0 || sourceY < 0 || sourceX >= canvasWidth || sourceY >= canvasHeight)
                {
                    continue; // outside the canvas: nothing was ever selected there
                }

                output[y * layerWidth + x] = documentMask[sourceY * canvasWidth + sourceX];
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
