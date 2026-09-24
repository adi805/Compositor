namespace Compositor.Core.Imaging;

/// <summary>Image containers this build can talk about. Sniffed from content, never from the file name.</summary>
public enum ImageFormat
{
    /// Not any container <see cref="ImageFormatPolicy.Detect"/> knows.
    Unknown,

    Png,
    Jpeg,
    Gif,
    Bmp,
    Ico,
    WebP,
    Tiff,

    /// HEIF/HEIC: upstream reads these through ImageIO; our codec set does not.
    Heif,
}

/// <summary>
/// One row of the format matrix: what a container is called, which extensions it
/// answers to, and whether this build imports and/or exports it. Rows for formats we
/// cannot handle stay in the matrix with a note, so the parity gap is visible instead
/// of quietly absent (the same rule the Filter menu follows for the ML workstream).
/// </summary>
public sealed record ImageFormatSupport(
    ImageFormat Format,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    bool CanImport,
    bool CanExport,
    bool SupportsAlpha,
    string? Note = null);

/// <summary>Content sniffing plus the import/export capability matrix.</summary>
public static class ImageFormatPolicy
{
    /// <summary>
    /// Container sniffing by magic bytes. Upstream is explicit about this
    /// (<c>ImageFileDrop</c>: "the importer validates contents rather than trusting
    /// extensions"), and a dropped file's extension is the least trustworthy thing about it.
    /// </summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ImageFormat.Png;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (bytes.Length >= 6 && bytes[..3].SequenceEqual("GIF"u8) &&
            (bytes[3] == 0x38) && (bytes[4] is 0x37 or 0x39) && bytes[5] == 0x61)
        {
            return ImageFormat.Gif;
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ImageFormat.Bmp;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
        {
            return ImageFormat.Ico;
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageFormat.WebP;
        }

        if (bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
        {
            return ImageFormat.Tiff;
        }

        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = bytes[8..12];
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) ||
                brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) ||
                brand.SequenceEqual("hevc"u8) || brand.SequenceEqual("hevx"u8) ||
                brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8) ||
                brand.SequenceEqual("heif"u8) || brand.SequenceEqual("avif"u8))
            {
                return ImageFormat.Heif;
            }
        }

        return ImageFormat.Unknown;
    }

    /// Every container the matrix knows, upstream order (what it supports first).
    public static IReadOnlyList<ImageFormatSupport> Matrix { get; } =
    [
        new(ImageFormat.Png, "PNG", ["png"], true, true, true,
            "Encode/decode with alpha; pHYs density written from the document resolution."),
        new(ImageFormat.Jpeg, "JPEG", ["jpg", "jpeg"], true, true, false,
            "Transparency is flattened onto a matte, as upstream's JPEG path does."),
        new(ImageFormat.Gif, "GIF", ["gif"], true, false, false,
            "Readable by our codec; not an export target (single frame, 256 colours)."),
        new(ImageFormat.Bmp, "BMP", ["bmp"], true, false, false,
            "Readable by our codec; not an export target."),
        new(ImageFormat.Ico, "ICO", ["ico"], true, false, true,
            "Readable by our codec; first image in the directory is used."),
        new(ImageFormat.WebP, "WebP", ["webp"], true, false, true,
            "Readable by our codec; not an export target."),
        new(ImageFormat.Tiff, "TIFF", ["tif", "tiff"], false, false, true,
            "Upstream imports TIFF. Ours needs a TIFF codec that is not in the shipped Skia build."),
        new(ImageFormat.Heif, "HEIC/HEIF", ["heic", "heif"], false, false, false,
            "Upstream imports HEIC through ImageIO. On Windows this needs a licensed decoder; see the ML/native codec note."),
    ];

    /// Containers we can read, in matrix order.
    public static IEnumerable<ImageFormatSupport> Importable => Matrix.Where(row => row.CanImport);

    /// True when the matrix claims import support for this container.
    public static bool CanImport(ImageFormat format) =>
        Matrix.Any(row => row.Format == format && row.CanImport);

    /// True when the matrix claims export support for this container.
    public static bool CanExport(ImageFormat format) =>
        Matrix.Any(row => row.Format == format && row.CanExport);

    /// Containers we can write, in matrix order.
    public static IEnumerable<ImageFormatSupport> Exportable => Matrix.Where(row => row.CanExport);

    /// Picker patterns for import, e.g. <c>*.png</c>.
    public static IReadOnlyList<string> ImportPatterns { get; } =
        Importable.SelectMany(row => row.Extensions.Select(e => "*." + e)).ToArray();

    /// The user-facing "which formats?" sentence, built from the matrix so it cannot drift from it.
    public static string UnsupportedImportMessage { get; } =
        "Choose a " + JoinAnd(Importable.Select(row => row.DisplayName).ToArray()) + " image.";

    /// Formats upstream handles that this build refuses, for honest disclosure.
    public static IReadOnlyList<string> ImportGaps { get; } =
        Matrix.Where(row => !row.CanImport).Select(row => row.DisplayName + ": " + row.Note).ToArray();

    private static string JoinAnd(string[] items) => items.Length switch
    {
        0 => "supported",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Length - 1)) + ", or " + items[^1],
    };
}
