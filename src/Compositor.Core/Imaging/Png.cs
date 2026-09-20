using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Compositor.Core.Imaging;

/// <summary>
/// Minimal PNG encode/decode for 8-bit RGBA (color type 6, non-interlaced)
/// project layer assets. Enough for .comp round-trips; not a general codec.
/// </summary>
public static class Png
{
    private const int ColorTypeRgba8 = 6;
    private const int BitsPerChannel = 8;
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Encode(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Invalid dimensions.");
        }

        var expected = width * height * 4;
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Pixel buffer is {rgba.Length} bytes; expected {expected}.");
        }

        Span<byte> chunkType = stackalloc byte[4];

        output.Write(Signature);

        // IHDR: width, height, depth, color type, compression 0, filter 0, interlace 0.
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = BitsPerChannel;
        ihdr[9] = ColorTypeRgba8;
        WriteChunk(output, "IHDR"u8, ihdr);

        // Raw scanlines with filter byte 0 (None) per row, then deflate.
        var stride = (width * 4) + 1;
        var raw = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            rgba.Slice(y * width * 4, width * 4)
                .CopyTo(raw.AsSpan((y * stride) + 1));
        }

        using (var deflated = new MemoryStream())
        {
            var zlib = new ZLibStream(deflated, CompressionLevel.Optimal);
            zlib.Write(raw);
            zlib.Dispose();
            WriteChunk(output, "IDAT"u8, deflated.ToArray());
        }

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    public static (int Width, int Height, byte[] Rgba) Decode(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var signature = new byte[8];
        ReadExactly(input, signature);
        if (!signature.AsSpan().SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file.");
        }

        int width = 0, height = 0;
        var idat = new List<byte[]>();
        var chunkHeader = new byte[8];

        while (true)
        {
            if (!TryReadExactly(input, chunkHeader))
            {
                throw new InvalidDataException("PNG truncated before IEND.");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(chunkHeader);
            var type = (ReadOnlySpan<byte>)chunkHeader.AsSpan(4);
            var data = new byte[length];
            ReadExactly(input, data);

            var crc = new byte[4];
            ReadExactly(input, crc);
            var expectedCrc = Crc32(chunkHeader.AsSpan(4), data);
            if (BinaryPrimitives.ReadInt32BigEndian(crc) != (int)expectedCrc)
            {
                throw new InvalidDataException("PNG chunk CRC mismatch.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                if (width <= 0 || height <= 0 || data[8] != BitsPerChannel || data[9] != ColorTypeRgba8 || data[12] != 0)
                {
                    throw new InvalidDataException("Unsupported PNG: only 8-bit RGBA non-interlaced.");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Add(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || idat.Count == 0)
        {
            throw new InvalidDataException("PNG missing IHDR or IDAT.");
        }

        using var concatenated = new MemoryStream(idat.SelectMany(b => b).ToArray());
        using var inflate = new ZLibStream(concatenated, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        var rawBytes = raw.ToArray();

        var stride = (width * 4) + 1;
        if (rawBytes.Length < stride * height)
        {
            throw new InvalidDataException("PNG pixel data truncated.");
        }

        Defilter(rawBytes, width, height);
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            rawBytes.AsSpan((y * stride) + 1, width * 4).CopyTo(rgba.AsSpan(y * width * 4));
        }

        return (width, height, rgba);
    }

    private static void Defilter(byte[] raw, int width, int height)
    {
        var stride = (width * 4) + 1;
        var bpp = 4;
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * stride];
            var row = y * stride;
            for (var x = 1; x < stride; x++)
            {
                var a = x > bpp ? raw[row + x - bpp] : (byte)0;
                var b = y > 0 ? raw[row - stride + x] : (byte)0;
                var c = y > 0 && x > bpp ? raw[row - stride + x - bpp] : (byte)0;
                raw[row + x] = filter switch
                {
                    0 => raw[row + x],
                    1 => (byte)(raw[row + x] + a),
                    2 => (byte)(raw[row + x] + b),
                    3 => (byte)(raw[row + x] + ((a + b) >> 1)),
                    4 => (byte)(raw[row + x] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = (a + b) - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        type.CopyTo(header[4..]);
        output.Write(header);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var t in a)
        {
            crc = Update(crc, t);
        }

        foreach (var t in b)
        {
            crc = Update(crc, t);
        }

        return crc ^ 0xFFFFFFFF;

        static uint Update(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (crc & 1) != 0 ? 0xEDB88320u : 0;
                crc = (crc >> 1) ^ mask;
            }

            return crc;
        }
    }

    private static void ReadExactly(Stream input, Span<byte> buffer)
    {
        if (!TryReadExactly(input, buffer))
        {
            throw new EndOfStreamException();
        }
    }

    private static bool TryReadExactly(Stream input, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = input.Read(buffer[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }
}
