using System.Numerics;

namespace Compositor.Core.Imaging;

/// <summary>
/// Flattens a document stack into a single straight-alpha RGBA buffer
/// (bottom-first compositing). Used by PNG export. Mirrors upstream
/// ImageExporter: each visible layer contributes with its own opacity and
/// blend mode; blending follows the PDF/W3C formulas upstream gets from
/// CoreGraphics/CoreImage (see Imaging.Blend).
/// </summary>
public static class Flatten
{
    public static (int Width, int Height, byte[] Rgba) ToRgba(Document doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var output = new byte[doc.Width * doc.Height * 4];

        // Hierarchy-aware: hidden groups hide their subtree; groups carry no pixels.
        foreach (var layer in LayerHierarchy.VisibleLayers(doc.Layers)) // bottom-first
        {
            if (!layer.IsVisible || layer.Pixels is not { } src)
            {
                continue; // hidden or blank layers contribute nothing
            }

            if (src.Width <= 0 || src.Height <= 0)
            {
                continue; // nothing to draw
            }

            var opacity = Math.Clamp(layer.Opacity, 0.0, 1.0);
            if (opacity <= 0.0)
            {
                continue;
            }

            // Position, size and rotation are a display transform (upstream's CTM), so the
            // surface is resampled onto its placement rect. An untransformed layer gets its
            // own buffer back with no copy, so the common path stays byte-identical.
            // A surface of any size is placed, not skipped: an imported image or a resized
            // layer is drawn on the canvas, and the export has to agree with the canvas.
            var srcPx = LayerPlacement.Place(
                layer.Transform, src.Pixels, src.Width, src.Height, doc.Width, doc.Height);
            var mode = layer.Blend;
            for (var i = 0; i < output.Length; i += 4)
            {
                if (srcPx[i + 3] == 0)
                {
                    continue;
                }

                Blend.Compose(
                    mode, (float)opacity,
                    output[i], output[i + 1], output[i + 2], output[i + 3],
                    srcPx[i], srcPx[i + 1], srcPx[i + 2], srcPx[i + 3],
                    out var r, out var g, out var b, out var a);

                output[i] = r;
                output[i + 1] = g;
                output[i + 2] = b;
                output[i + 3] = a;
            }
        }

        return (doc.Width, doc.Height, output);
    }
}
