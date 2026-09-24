namespace Compositor.Core.Imaging;

/// <summary>
/// Reads the EXIF orientation tag out of a file's bytes. Upstream asks Core Image to apply
/// it (<c>CIImage.oriented(forExifOrientation:)</c>); on this side the codec's encoded-image
/// path already returns upright pixels, so the tag is read for reporting and for the
/// assertion that we are not rotating twice. Values 1..8 follow JEITA CP-3451 (EXIF 2.3).
/// </summary>
public static class ExifOrientation
{
    /// "No transformation" - the value returned for absent or unreadable tags.
    public const int Identity = 1;

    private const ushort OrientationTag = 0x0112;

    /// Maps anything outside the 1..8 table back to identity.
    public static int Normalize(int orientation) => orientation is >= 1 and <= 8 ? orientation : Identity;

    /// <summary>
    /// Orientation from a JPEG APP1 segment or a bare TIFF header; 1 when the bytes hold no
    /// readable tag. Never throws: an undecodable header is simply "unoriented".
    /// </summary>
    public static int Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8)
        {
            if (IsTiffHeader(bytes, 0))
            {
                return ReadTiff(bytes, 0);
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return ReadJpeg(bytes);
            }

            // HEIF/AVIF: "ftyp" at offset 4; its Exif data lives in a box chain we do not chase here.
        }

        return Identity;
    }

    private static bool IsTiffHeader(ReadOnlySpan<byte> b, int start) =>
        b.Length >= start + 8 &&
        ((b[start] == 0x49 && b[start + 1] == 0x49 && b[start + 2] == 0x2A && b[start + 3] == 0x00) ||
         (b[start] == 0x4D && b[start + 1] == 0x4D && b[start + 2] == 0x00 && b[start + 3] == 0x2A));

    private static int ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 4 <= bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return Identity; // outside the marker region; stop looking
            }

            var m = at + 1;
            while (m < bytes.Length && bytes[m] == 0xFF)
            {
                m++; // 0xFF fill bytes may pad a marker
            }

            if (m >= bytes.Length)
            {
                return Identity;
            }

            var marker = bytes[m];
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                if (marker == 0xDA || marker == 0xD9)
                {
                    return Identity; // start of scan or EOI: no metadata past here
                }

                at = m + 1; // standalone marker, no length field
                continue;
            }

            var length = (bytes[m + 1] << 8) | bytes[m + 2];
            var segmentEnd = m + 1 + length; // length counts its own two bytes; SOF/APP payload starts at m+3
            if (length < 2 || segmentEnd > bytes.Length)
            {
                return Identity;
            }

            var tiffStart = m + 9; // m+3 payload start, +6 for the "Exif\0\0" identifier
            if (marker == 0xE1 && (segmentEnd - (m + 3)) >= 14
                && bytes.Slice(m + 3, 6).SequenceEqual("Exif\0\0"u8)
                && IsTiffHeader(bytes, tiffStart))
            {
                return ReadTiff(bytes, tiffStart);
            }

            at = segmentEnd;
        }

        return Identity;
    }

    private static int ReadTiff(ReadOnlySpan<byte> bytes, int start)
    {
        var little = bytes[start] == 0x49;
        var ifdOffset = ReadU32(bytes, start + 4, little);
        var ifd = start + (long)ifdOffset;
        if (ifdOffset == 0 || ifd + 2 > bytes.Length)
        {
            return Identity;
        }

        var count = ReadU16(bytes, (int)ifd, little);
        for (var entry = 0; entry < count; entry++)
        {
            var at = (int)ifd + 2 + (entry * 12);
            if (at + 12 > bytes.Length)
            {
                return Identity;
            }

            if (ReadU16(bytes, at, little) != OrientationTag)
            {
                continue;
            }

            var type = ReadU16(bytes, at + 2, little);
            return type switch
            {
                3 => Normalize(ReadU16(bytes, at + 8, little)),
                4 => Normalize((int)ReadU32(bytes, at + 8, little)),
                _ => Identity,
            };
        }

        return Identity;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> b, int at, bool little) =>
        little ? (ushort)(b[at] | (b[at + 1] << 8)) : (ushort)((b[at] << 8) | b[at + 1]);

    private static uint ReadU32(ReadOnlySpan<byte> b, int at, bool little) =>
        little
            ? (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24))
            : (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

    // No pixel transform lives here, deliberately. The codec's encoded-image path hands back
    // upright pixels already, which Decode_RealExifJpegFromAnotherWriter_LandsUpright pins
    // down; applying the tag here again is the double-rotation bug that test exists to catch.
}
