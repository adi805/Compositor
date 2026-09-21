using System.Numerics;

namespace Compositor.Core.Imaging;

/// <summary>
/// Flattens a document stack into a single straight-alpha RGBA buffer
/// (bottom-first over-compositing). Used by PNG export.
/// v1 scope: Normal blend only; layer opacity and visibility are applied,
/// other BlendMode values are treated as Normal until the raster engine
/// grows per-mode kernels.
/// </summary>
public static class Flatten
{
    public static (int Width, int Height, byte[] Rgba) ToRgba(Document doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var output = new byte[doc.Width * doc.Height * 4];

        foreach (var layer in doc.Layers) // bottom-first
        {
            if (!layer.IsVisible || layer.Pixels is not { } src)
            {
                continue; // hidden or blank layers contribute nothing
            }

            if (src.Width != doc.Width || src.Height != doc.Height)
            {
                continue; // v1: full-canvas surfaces only
            }

            var opacity = Math.Clamp(layer.Opacity, 0.0, 1.0);
            if (opacity <= 0.0)
            {
                continue;
            }

            var srcPx = src.Pixels;
            for (var i = 0; i < output.Length; i += 4)
            {
                var sa = (srcPx[i + 3] / 255f) * (float)opacity;
                if (sa <= 0f)
                {
                    continue;
                }

                var da = output[i + 3] / 255f;
                var oa = sa + (da * (1f - sa));
                if (oa <= 0f)
                {
                    continue;
                }

                output[i] = ToByte((srcPx[i] * sa + output[i] * da * (1f - sa)) / oa);
                output[i + 1] = ToByte((srcPx[i + 1] * sa + output[i + 1] * da * (1f - sa)) / oa);
                output[i + 2] = ToByte((srcPx[i + 2] * sa + output[i + 2] * da * (1f - sa)) / oa);
                output[i + 3] = ToByte(oa * 255f);
            }
        }

        return (doc.Width, doc.Height, output);
    }

    private static byte ToByte(float value) => byte.CreateSaturating(Math.Round(value));
}
