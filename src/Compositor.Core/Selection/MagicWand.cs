namespace Compositor.Core.Selection;

/// <summary>
/// Magic wand: 4-connected flood fill over a straight-alpha RGBA surface,
/// matching pixels within a per-channel tolerance of the seed. Ported from
/// upstream Document/MagicWand.swift semantics (wand selects contiguous
/// similarly-colored pixels); produces a raster coverage selection.
/// </summary>
public static class MagicWand
{
    public static DocumentSelection Select(RasterSurface surface, int seedX, int seedY, int tolerance, SelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (seedX < 0 || seedY < 0 || seedX >= surface.Width || seedY >= surface.Height)
        {
            return DocumentSelection.Empty();
        }
        tolerance = Math.Clamp(tolerance, 0, 255);

        var w = surface.Width;
        var h = surface.Height;
        var px = surface.Pixels;
        var seedIndex = (seedY * w) + seedX;
        var (sr, sg, sb, sa) = (px[seedIndex * 4], px[(seedIndex * 4) + 1], px[(seedIndex * 4) + 2], px[(seedIndex * 4) + 3]);

        var mask = new byte[w * h];
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        queue.Enqueue(seedIndex);
        visited[seedIndex] = true;

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % w;
            var y = index / w;

            var baseIndex = index * 4;
            if (Math.Abs(px[baseIndex] - sr) > tolerance ||
                Math.Abs(px[baseIndex + 1] - sg) > tolerance ||
                Math.Abs(px[baseIndex + 2] - sb) > tolerance ||
                Math.Abs(px[baseIndex + 3] - sa) > tolerance)
            {
                continue;
            }

            mask[index] = 255;

            if (x > 0 && !visited[index - 1]) { visited[index - 1] = true; queue.Enqueue(index - 1); }
            if (x < w - 1 && !visited[index + 1]) { visited[index + 1] = true; queue.Enqueue(index + 1); }
            if (y > 0 && !visited[index - w]) { visited[index - w] = true; queue.Enqueue(index - w); }
            if (y < h - 1 && !visited[index + w]) { visited[index + w] = true; queue.Enqueue(index + w); }
        }

        return DocumentSelection.FromCoverage(mask, w, h);
    }
}
