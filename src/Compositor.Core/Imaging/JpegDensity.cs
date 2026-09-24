namespace Compositor.Core.Imaging;

/// <summary>
/// Reads and writes the print density stored in a JPEG's JFIF APP0 segment. Upstream
/// asks ImageIO for <c>kCGImagePropertyDPIWidth/Height</c>; on this side the encoder we
/// have writes a JFIF header with aspect units, so the density is patched into that
/// header afterwards. Same metadata, one extra step.
/// </summary>
public static class JpegDensity
{
    private const int MaxDensity = 65_533; // JFIF stores 16-bit densities; 0 and >65533 are reserved.

    /// <summary>
    /// Returns JPEG bytes carrying <paramref name="dpi"/> as JFIF dots-per-inch. An existing
    /// APP0 is patched in place; when there is none, a minimal thumbnail-free APP0 is inserted
    /// directly after SOI, which is where JFIF requires it.
    /// </summary>
    public static byte[] Set(ReadOnlySpan<byte> jpeg, double dpi)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new ArgumentException("Not a JPEG stream (missing SOI).", nameof(jpeg));
        }

        if (!double.IsFinite(dpi) || dpi < 1 || dpi > MaxDensity)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), $"Density must be 1..{MaxDensity} dpi.");
        }

        var density = (ushort)Math.Round(dpi, MidpointRounding.AwayFromZero);
        var output = jpeg.ToArray();

        var app0 = FindApp0(output);
        if (app0 >= 0)
        {
            output[app0 + 11] = 1; // units: dots per inch
            output[app0 + 12] = (byte)(density >> 8);
            output[app0 + 13] = (byte)density;
            output[app0 + 14] = (byte)(density >> 8);
            output[app0 + 15] = (byte)density;
            return output;
        }

        var segment = new byte[18];
        segment[0] = 0xFF;
        segment[1] = 0xE0;
        segment[2] = 0x00;
        segment[3] = 0x10; // length 16, counting itself
        "JFIF\0"u8.CopyTo(segment.AsSpan(4));
        segment[9] = 1; // version 1.1
        segment[10] = 1;
        segment[11] = 1; // units: dots per inch
        segment[12] = (byte)(density >> 8);
        segment[13] = (byte)density;
        segment[14] = (byte)(density >> 8);
        segment[15] = (byte)density;
        // segment[16], segment[17]: thumbnail 0x0

        var rebuilt = new byte[output.Length + segment.Length];
        output.AsSpan(0, 2).CopyTo(rebuilt);
        segment.CopyTo(rebuilt.AsSpan(2));
        output.AsSpan(2).CopyTo(rebuilt.AsSpan(2 + segment.Length));
        return rebuilt;
    }

    /// <summary>
    /// Density in dpi, or null when the stream has no JFIF APP0 or declares aspect units.
    /// Never throws: an unreadable header is "unknown density", not a failed export.
    /// </summary>
    public static double? TryGet(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 20 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        var app0 = FindApp0(jpeg);
        if (app0 < 0 || jpeg[app0 + 11] != 1)
        {
            return null;
        }

        var x = (jpeg[app0 + 12] << 8) | jpeg[app0 + 13];
        return x == 0 ? null : x;
    }

    /// Offset of a JFIF APP0 segment (at its 0xFF) or -1. Only a leading APP0 counts, per JFIF.
    private static int FindApp0(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length >= 12 && jpeg[2] == 0xFF && jpeg[3] == 0xE0 && jpeg[6..11].SequenceEqual("JFIF\0"u8))
        {
            return 2;
        }

        return -1;
    }
}
