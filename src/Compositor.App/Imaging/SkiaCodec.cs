using System.Runtime.InteropServices;
using Compositor.Core.Imaging;
using SkiaSharp;

namespace Compositor.App.Imaging;

/// <summary>A decoded image in the document's own convention: straight-alpha RGBA8, upright.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Rgba, ImageFormat Format, int Orientation)
{
    /// Budget units this image consumes (upstream counts layers against 100 MP).
    public long PixelCount => ImageBudget.PixelCount(Width, Height);
}

/// <summary>
/// Raster encode/decode on the Skia build Avalonia already ships (SkiaSharp 2.88.9).
/// Core stays dependency-free, so this is the only place Skia is touched: it converts
/// to and from the straight-alpha RGBA buffer the whole engine works in.
/// PNG export deliberately stays on <see cref="Png"/> (Core) so layer assets inside a
/// .comp file and exported files share one encoder.
/// </summary>
public static class SkiaCodec
{
    /// <summary>
    /// Decodes supported bytes to straight-alpha RGBA8 with EXIF orientation applied.
    /// Upstream does the same work in <c>ImageImporter.decode</c> (type check, budget
    /// check, <c>oriented(forExifOrientation:)</c>), and failures are reported with the
    /// same taxonomy through <see cref="ImageException"/>.
    /// </summary>
    public static DecodedImage Decode(byte[] bytes, long pixelsAlreadyUsed = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var format = ImageFormatPolicy.Detect(bytes);
        if (!ImageFormatPolicy.CanImport(format))
        {
            throw new ImageException(
                format is ImageFormat.Unknown ? ImageFailure.Unreadable : ImageFailure.Unsupported);
        }

        using var data = SKData.CreateCopy(bytes);
        using var image = SKImage.FromEncodedData(data)
            ?? throw new ImageException(ImageFailure.Unreadable);

        var (width, height) = (image.Width, image.Height);
        ImageBudget.ValidateImport(width, height, pixelsAlreadyUsed);

        // Ask for exactly what the engine stores: Rgba8888, straight alpha. Skia converts;
        // if it refuses, the read fails and we report unreadable rather than mis-labelled bytes.
        var requested = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var rowBytes = requested.RowBytes;
        var length = rowBytes * height;
        var buffer = new byte[length];
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            if (!image.ReadPixels(requested, pointer, rowBytes, 0, 0))
            {
                throw new ImageException(ImageFailure.Unreadable);
            }

            Marshal.Copy(pointer, buffer, 0, length);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        var rgba = rowBytes == width * 4 ? buffer : Unpad(buffer, width, height, rowBytes);

        // Skia's encoded-image path already bakes EXIF orientation into both the reported
        // dimensions and the pixels (verified by Decode_JpegWithExifOrientation_*: an 8x4 with
        // orientation 6 comes out 4x8 with the correct layout). Rotating again would flip the
        // picture back, so the tag is only reported here, never applied.
        var orientation = ExifOrientation.Normalize(ExifOrientation.Read(bytes));
        return new DecodedImage(width, height, rgba, format, orientation);
    }

    /// <summary>
    /// Flattens the canvas onto its matte and encodes JPEG, then stamps the document
    /// resolution into JFIF. Quality is the 0-100 form of upstream's 0...1 value.
    /// </summary>
    public static byte[] EncodeJpeg(ReadOnlySpan<byte> rgba, int width, int height, JpegOptions options, double? dpi = null)
    {
        var normalized = options.Normalize();
        ImageBudget.ValidateExport(width, height);

        var opaque = MatteComposite.Apply(rgba, width, height, normalized);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var image = SKImage.FromPixelCopy(info, opaque)
            ?? throw new ImageException(ImageFailure.Encode);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, normalized.QualityPercent);
        if (encoded is null)
        {
            throw new ImageException(ImageFailure.Encode);
        }

        var bytes = encoded.ToArray();
        return dpi is { } value && double.IsFinite(value) && value >= 1
            ? JpegDensity.Set(bytes, value)
            : bytes;
    }

    /// <summary>True when the shipped Skia build produces a codec for these bytes.</summary>
    public static bool CanDecode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            return codec is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Which encoders the shipped Skia build really provides, by trying them.</summary>
    public static IReadOnlyList<SKEncodedImageFormat> EncodableFormats()
    {
        var probe = new byte[2 * 2 * 4];
        for (var i = 0; i < probe.Length; i += 4)
        {
            probe[i] = 0x40;
            probe[i + 1] = 0x80;
            probe[i + 2] = 0xC0;
            probe[i + 3] = 0xFF;
        }

        var info = new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var found = new List<SKEncodedImageFormat>();
        using var image = SKImage.FromPixelCopy(info, probe);
        if (image is null)
        {
            return found;
        }

        foreach (var format in Enum.GetValues<SKEncodedImageFormat>())
        {
            if (format == SKEncodedImageFormat.Astc)
            {
                continue; // a compressed-GPU format, not an image container
            }

            using var data = image.Encode(format, 90);
            if (data is not null && data.ToArray().Length > 0)
            {
                found.Add(format);
            }
        }

        return found;
    }

    /// <summary>Row-padded pixel rows trimmed to a packed RGBA buffer.</summary>
    private static byte[] Unpad(byte[] padded, int width, int height, int rowBytes)
    {
        var packed = width * 4;
        var dst = new byte[packed * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(padded, y * rowBytes, dst, y * packed, packed);
        }

        return dst;
    }
}
