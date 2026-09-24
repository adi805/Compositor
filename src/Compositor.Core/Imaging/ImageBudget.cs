namespace Compositor.Core.Imaging;

/// <summary>
/// Pixel budgets shared by image IO. Limits mirror upstream
/// <c>IO/ImageExporter.swift</c> and <c>IO/ImageImporter.swift</c>: 30,000 px per
/// side plus a 100 megapixel document budget that imports are drained against
/// (upstream <c>EditorSession.drainImports</c> passes
/// <c>remainingPixels: 100_000_000 - usedPixels</c>).
/// </summary>
public static class ImageBudget
{
    /// Upstream guard <c>(1...30_000).contains(width)</c> on export and import.
    public const int MaxSide = 30_000;

    /// Upstream document budget: <c>width * height &lt;= 100_000_000</c>.
    public const long MaxPixels = 100_000_000;

    /// Upstream <c>snapshot.manifest.resolution ?? 72</c>, written as PNG pHYs / JPEG density.
    public const double DefaultResolution = 72;

    /// Upstream import thumbnail: <c>scale = min(1, 96 / max(w, h))</c>.
    public const int ImportThumbnailLongSide = 96;

    /// Upstream JPEG sheet preview: <c>kCGImageSourceThumbnailMaxPixelSize: 1000</c>.
    public const int PreviewLongSide = 1_000;

    /// True when a buffer of this size fits the side limits and the remaining budget.
    public static bool Fits(int width, int height, long pixelsAlreadyUsed = 0) =>
        width >= 1 &&
        height >= 1 &&
        width <= MaxSide &&
        height <= MaxSide &&
        (width * (long)height) + pixelsAlreadyUsed <= MaxPixels;

    /// Throws <see cref="ImageFailure.ExportTooLarge"/> outside the export budget.
    public static void ValidateExport(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide
            || width * (long)height > MaxPixels)
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
