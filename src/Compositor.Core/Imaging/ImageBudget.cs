namespace Compositor.Core.Imaging;

/// <summary>
/// Pixel ceilings shared by image IO and geometry. Mirrors upstream
/// <c>Document/DocumentLimits.swift</c>, which centralises them because two
/// separate ideas had collapsed onto one number:
/// <list type="bullet">
/// <item>how large a <b>single surface</b> may be (canvas, export, filter target,
/// adjustment or mask render), and</item>
/// <item>how much raster a <b>whole document</b> may hold across every layer and mask.</item>
/// </list>
/// Upstream: <c>maxSide = 30_000</c>, <c>maxSurfacePixels = 200_000_000</c>,
/// <c>documentPixelBudget = min(800_000_000, max(maxSurfacePixels, physicalMemory / 16))</c>.
/// </summary>
public static class ImageBudget
{
    /// Longest side, in pixels, of any canvas, layer, mask or generated surface.
    public const int MaxSide = 30_000;

    /// Largest single surface: a canvas, an export, a filter target, an adjustment or mask render.
    /// At RGBA8 one allocation is at most 800 MB, and a filter holds a few of them at once.
    public const long MaxSurfacePixels = 200_000_000;

    /// Total imported raster one document may hold, summed across every layer and mask.
    /// Upstream scales this to the machine's physical memory; we read what the runtime
    /// reports as available to this process, the closest portable equivalent, which also
    /// honours container limits where the Mac reads the host's RAM. Clamped so it is never
    /// below one surface and never above 800 MP.
    public static long DocumentPixelBudget { get; } = Math.Min(
        800_000_000L,
        Math.Max(MaxSurfacePixels, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 16));

    /// The single-surface ceiling in megapixels, for the messages that quote it back to the reader.
    public static int MaxSurfaceMegapixels => (int)(MaxSurfacePixels / 1_000_000);

    /// The document ceiling in megapixels, for the messages that quote it back to the reader.
    public static int DocumentBudgetMegapixels => (int)(DocumentPixelBudget / 1_000_000);

    /// Upstream <c>snapshot.manifest.resolution ?? 72</c>, written as PNG pHYs / JPEG density.
    public const double DefaultResolution = 72;

    /// Upstream import thumbnail: <c>scale = min(1, 96 / max(w, h))</c>.
    public const int ImportThumbnailLongSide = 96;

    /// Upstream JPEG sheet preview: <c>kCGImageSourceThumbnailMaxPixelSize: 1000</c>.
    public const int PreviewLongSide = 1_000;

    /// True when a buffer of this size fits the side limits and the remaining document budget.
    public static bool Fits(int width, int height, long pixelsAlreadyUsed = 0) =>
        width >= 1 &&
        height >= 1 &&
        width <= MaxSide &&
        height <= MaxSide &&
        (width * (long)height) + pixelsAlreadyUsed <= DocumentPixelBudget;

    /// Throws <see cref="ImageFailure.ExportTooLarge"/> outside the single-surface ceiling.
    public static void ValidateExport(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide
            || width * (long)height > MaxSurfacePixels)
        {
            throw new ImageException(ImageFailure.ExportTooLarge);
        }
    }

    /// Throws <see cref="ImageFailure.ImportTooLarge"/> when the image will not fit what is left.
    public static void ValidateImport(int width, int height, long pixelsAlreadyUsed)
    {
        if (!Fits(width, height, pixelsAlreadyUsed))
        {
            throw new ImageException(ImageFailure.ImportTooLarge);
        }
    }

    /// Long-side scale factor for a thumbnail that must fit <paramref name="longSide"/>; 1 keeps size.
    public static double ThumbnailScale(int width, int height, int longSide)
    {
        var extent = Math.Max(width, height);
        return extent <= 0 ? 1 : Math.Min(1, (double)longSide / extent);
    }

    /// Total canvas pixels used by a buffer, the unit the budget is counted in.
    public static long PixelCount(int width, int height) => width * (long)height;
}
