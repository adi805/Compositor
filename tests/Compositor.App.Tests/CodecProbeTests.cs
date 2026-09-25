using System.Runtime.InteropServices;
using Compositor.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// What the shipped Skia build can really decode, measured against real container fixtures instead of
/// against our own policy. <see cref="SkiaCodecTests"/> already proves the format matrix refuses TIFF and
/// HEIF, but it feeds hand-built headers to <c>SkiaCodec.Decode</c>, so it can only report what our code
/// decided. This file asks the codec, which is the question parity actually turns on: the Mac accepts
/// <c>.tiff</c> and <c>.heic</c> through ImageIO for free, and "we refuse" is only honest if "we could not
/// anyway" is a measured fact.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture is 8x8, left half red / right half green, written by a tool other than the one under
/// test: Pillow 12.3.0 for the uncompressed TIFF, LIBTIFF 4.5.1 (via ImageMagick) for the LZW TIFF, and
/// the libheif bundled in pillow_heif 1.8.0 for the HEIC. The picture is known because I drew it, so a
/// decode is checked against the image rather than against whatever the engine happened to return.
/// </para>
/// <para>
/// The assertions pin a measured absence, deliberately. If a SkiaSharp upgrade ever returns a codec for
/// these bytes, these tests fail, and that failure is the prompt to re-open the decision rather than a
/// new capability sitting unused behind a refusal written when nothing could read the file.
/// </para>
/// </remarks>
public class CodecProbeTests
{
    /// <summary>Documented byte length per fixture, asserted below. A base64 literal that loses a line in
    /// transcription decodes to a truncated file, and a truncated file also returns a null codec, for a
    /// reason that has nothing to do with the codec. That exact bug happened while writing this file.</summary>
    private const int PngBytes = 79;

    private const int TiffBaselineBytes = 332;
    private const int TiffLzwBytes = 326;
    private const int HeicBytes = 484;

    // 8x8 PNG, left red / right green (Pillow). The control: if it stops decoding, the probe is broken and
    // every null below it means nothing.
    private const string PngFixture =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAFklEQVR4nGP8z4AAjEgcJgYcYHBKAACHUQIPJi2lpAAAAABJRU5ErkJggg==";

    // 8x8 uncompressed TIFF, little-endian baseline (Pillow 12.3.0). 332 bytes. This base64 is verified to
    // decode to those exact bytes; the first transcription came out 311 and produced a null codec for the
    // wrong reason.
    private const string TiffBaselineFixture =
        "SUkqAAgAAAAKAAABBAABAAAACAAAAAEBBAABAAAACAAAAAIBAwADAAAAhgAAAAMBAwABAAAAAQAAAAYBAwABAAAAAgAAABEBBAAB"
        + "AAAAjAAAABUBAwABAAAAAwAAABYBBAABAAAACAAAABcBBAABAAAAwAAAABwBAwABAAAAAQAAAAAAAAAIAAgACAD/AAD/AAD/AAD/"
        + "AAAA/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA"
        + "/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA"
        + "/wAA/wAA/wD/AAD/AAD/AAD/AAAA/wAA/wAA/wAA/wA=";

    // Same pixels, LZW-compressed by LIBTIFF 4.5.1 (`file` reports compression=LZW). Pillow's own
    // compression='lzw' request had silently produced an UNCOMPRESSED file here, which is another reason
    // the length assertions exist: a fixture is only worth what its verification is.
    private const string TiffLzwFixture =
        "SUkqADoAAACAP8AQOCQUAAGBQaCwmFQSEQ2CQyIQ+IACJQ2KRCLwqMw2NwaOwqPwWQwaRw6TgCAgABAAAAEDAAEAAAAIAAAAAQED"
        + "AAEAAAAIAAAAAgEDAAMAAAAAAQAAAwEDAAEAAAAFAAAABgEDAAEAAAACAAAACgEDAAEAAAABAAAAEQEEAAEAAAAIAAAAEgEDAAEA"
        + "AAABAAAAFQEDAAEAAAADAAAAFgEDAAEAAAAIAAAAFwEEAAEAAAAxAAAAHAEDAAEAAAABAAAAKQEDAAIAAAAAAAEAPQEDAAEAAAAC"
        + "AAAAPgEFAAIAAAA2AQAAPwEFAAYAAAAGAQAAAAAAAAgACAAIAIXrUQAAAIAAw/WoAAAAAALNzEwAAAAAAc3MTAAAAIAAzcxMAAAA"
        + "AAKPwvUAAAAAEDcaoAAAAAACK4cKAAAAIAA=";

    // 8x8 HEIC: HEVC Main Still Profile written by pillow_heif 1.8.0 (bundled libheif). 484 bytes. The
    // coded frame is 64x64 with a `clap` clean-aperture crop to 8x8, which is what real camera files look
    // like: the displayed size is not the coded size, and a decoder that ignored the crop would report
    // 64x64.
    private const string HeicFixture =
        "AAAAHGZ0eXBoZWljAAAAAG1pZjFoZWljbWlhZgAAAXxtZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAA"
        + "ACJpbG9jAAAAAERAAAEAAQAAAAABoAABAAAAAAAAAEQAAAAjaWluZgAAAAAAAQAAABVpbmZlAgAAAAABAABodmMxAAAAAA5waXRt"
        + "AAAAAAABAAAA/GlwcnAAAADcaXBjbwAAAHVodmNDAQNwAAAAAAAAAAAAHvAA/P34+AAADwNgAAEAGEABDAH//wNwAAADAJAAAAMA"
        + "AAMAHroCQGEAAQApQgEBA3AAAAMAkAAAAwAAAwAeoCCBBZbqrprm4CGgwIAAAAyAAAADAIRiAAEABkQBwXPBiQAAABNjb2xybmNs"
        + "eAABAA0ABoAAAAAUaXNwZQAAAAAAAABAAAAAQAAAAChjbGFwAAAACAAAAAEAAAAIAAAAAf///8gAAAAC////yAAAAAIAAAAQcGl4"
        + "aQAAAAADCAgIAAAAGGlwbWEAAAAAAAAAAQABBYECAwWEAAAATG1kYXQAAABAKAGvEyF8o0Dm6EZEUZXqSDWZgpYBdKqbzJMH6LWN"
        + "qmTWFo9WsrU6ApfUV2ofk4WAORXl6PQ0+8ksqvE1FziVgA==";

    /// <summary>Where an operator may drop extra real files to widen the probe (a phone HEIC, a multipage
    /// TIFF). Nothing is committed here: the 718 KB camera HEIC used to corroborate the HEIC result is a
    /// photograph somebody else took, and putting it in this repo is a licensing decision that is not
    /// mine to make. The directory is reported either way, so a run that measured less says so.</summary>
    private static string OptionalFixtureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "probe-fixtures");

    /// <summary>What the shipped codec did with one set of bytes.</summary>
    private readonly record struct Capability(
        bool HasCodec,
        bool HasImage,
        int Width,
        int Height,
        string CodecFormat,
        string LeftPixel,
        string RightPixel)
    {
        public override string ToString() => HasCodec
            ? $"codec=yes format={CodecFormat} size={Width}x{Height} left={LeftPixel} right={RightPixel}"
            : "codec=no image=" + (HasImage ? $"{Width}x{Height}" : "none");
    }

    private static Capability Measure(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var hasCodec = codec is not null;
        var codecFormat = codec?.EncodedFormat.ToString() ?? "none";

        using var image = SKImage.FromEncodedData(data);
        if (image is null)
        {
            return new Capability(hasCodec, false, 0, 0, codecFormat, string.Empty, string.Empty);
        }

        // Read at the image's OWN reported size and sample the middle row's two edges, so a decode that
        // ignored the clean-aperture crop shows up as 64x64 with padding rather than hiding behind "it
        // returned something".
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var buffer = new byte[info.RowBytes * info.Height];
        var pointer = Marshal.AllocHGlobal(buffer.Length);
        bool read;
        try
        {
            read = image.ReadPixels(info, pointer, info.RowBytes, 0, 0);
            if (read)
            {
                Marshal.Copy(pointer, buffer, 0, buffer.Length);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        var middleRow = (info.Height / 2) * info.RowBytes;
        var rightEdge = middleRow + ((info.Width - 1) * 4);
        return new Capability(
            hasCodec,
            true,
            image.Width,
            image.Height,
            codecFormat,
            read ? Pixel(buffer, middleRow) : "unreadable",
            read ? Pixel(buffer, rightEdge) : "unreadable");
    }

    private static string Pixel(byte[] buffer, int offset) =>
        $"({buffer[offset]},{buffer[offset + 1]},{buffer[offset + 2]})";

    /// <summary>The committed fixtures, plus one honest line about whether anything extra was measured.</summary>
    private static List<string> ProbeReport()
    {
        var lines = new List<string>
        {
            $"png control: {Measure(Convert.FromBase64String(PngFixture))}",
            $"tiff uncompressed: {Measure(Convert.FromBase64String(TiffBaselineFixture))}",
            $"tiff lzw: {Measure(Convert.FromBase64String(TiffLzwFixture))}",
            $"heic: {Measure(Convert.FromBase64String(HeicFixture))}",
        };

        var extra = Directory.Exists(OptionalFixtureDirectory)
            ? Directory.GetFiles(OptionalFixtureDirectory)
            : Array.Empty<string>();
        lines.Add(extra.Length == 0
            ? "optional: none present, so this run measured the committed fixtures only"
            : $"optional: {extra.Length} file(s): " + string.Join(" | ", extra.Select(path =>
                Path.GetFileName(path) + " -> " + Measure(File.ReadAllBytes(path)))));

        return lines;
    }

    [Fact]
    public void EveryFixtureDecodesToTheLengthItDocuments()
    {
        Assert.Equal(PngBytes, Convert.FromBase64String(PngFixture).Length);
        Assert.Equal(TiffBaselineBytes, Convert.FromBase64String(TiffBaselineFixture).Length);
        Assert.Equal(TiffLzwBytes, Convert.FromBase64String(TiffLzwFixture).Length);
        Assert.Equal(HeicBytes, Convert.FromBase64String(HeicFixture).Length);
    }

    [Fact]
    public void TheControlProvesTheProbeCanSeeAWorkingDecoder()
    {
        var png = Measure(Convert.FromBase64String(PngFixture));

        Assert.True(png.HasCodec, "the PNG control stopped decoding: every null result in this file is now "
            + "meaningless, and the probe itself is the bug");
        Assert.Equal(8, png.Width);
        Assert.Equal(8, png.Height);
        Assert.Equal("Png", png.CodecFormat);

        // Red where I drew red, green where I drew green. That is the golden that makes the nulls real.
        Assert.Equal("(255,0,0)", png.LeftPixel);
        Assert.Equal("(0,255,0)", png.RightPixel);
    }

    [Theory]
    [InlineData(TiffBaselineFixture, TiffBaselineBytes)]
    [InlineData(TiffLzwFixture, TiffLzwBytes)]
    public void ShippedSkiaBuildHasNoTiffDecoder(string fixture, int expectedBytes)
    {
        var bytes = Convert.FromBase64String(fixture);
        Assert.Equal(expectedBytes, bytes.Length);
        Assert.Equal(ImageFormat.Tiff, ImageFormatPolicy.Detect(bytes));

        var measured = Measure(bytes);

        Assert.False(measured.HasCodec,
            $"Skia decoded TIFF ({measured}). Measured absent on 2026-09-25 with SkiaSharp 2.88.9. Do not "
            + "leave this failing: re-open the decision in ImageFormatPolicy, README Known Limits and "
            + "docs/PARITY.md, and add a round-trip golden against these pixels.");
        Assert.False(measured.HasImage,
            $"SKCodec refused but SKImage.FromEncodedData accepted ({measured}): the two entry points "
            + "disagree, and the probe needs to know which one to trust.");
    }

    [Fact]
    public void ShippedSkiaBuildHasNoHeifDecoder()
    {
        var bytes = Convert.FromBase64String(HeicFixture);
        Assert.Equal(HeicBytes, bytes.Length);
        Assert.Equal(ImageFormat.Heif, ImageFormatPolicy.Detect(bytes));

        var measured = Measure(bytes);

        Assert.False(measured.HasCodec,
            "Skia decoded HEIF. Measured absent on 2026-09-25 with SkiaSharp 2.88.9, against this "
            + "self-generated file AND a 718 KB third-party camera HEIC. Before shipping it, settle the "
            + "decoder question in docs/PARITY.md WS21: HEVC still needs an external decoder.");
        Assert.False(measured.HasImage);
    }

    [Fact]
    public void OurPolicyRefusesExactlyTheContainersTheCodecCannotRead()
    {
        // The matrix and the machine have to agree. A row claiming a format the codec cannot read is a
        // broken import; a row refusing one the codec CAN read is a feature withheld in silence, which is
        // the state this file exists to end.
        Assert.False(ImageFormatPolicy.CanImport(ImageFormat.Tiff));
        Assert.False(ImageFormatPolicy.CanImport(ImageFormat.Heif));

        var gaps = string.Join("\n", ImageFormatPolicy.ImportGaps);
        Assert.Contains("TIFF", gaps, StringComparison.Ordinal);
        Assert.Contains("HEIC", gaps, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtraFixturesAreMeasuredRatherThanSilentlySkipped()
    {
        // The optional directory is not committed, so CI takes the "none present" branch and a developer's
        // machine takes the other. Both branches have to be visible: a probe that quietly measured less is
        // worse than no probe at all.
        var present = Directory.Exists(OptionalFixtureDirectory)
            ? Directory.GetFiles(OptionalFixtureDirectory).Length
            : 0;

        var report = ProbeReport();

        Assert.Equal(5, report.Count);
        Assert.All(report.Take(4), line => Assert.Contains(": codec=", line, StringComparison.Ordinal));
        if (present == 0)
        {
            Assert.Equal(
                "optional: none present, so this run measured the committed fixtures only", report[4]);
        }
        else
        {
            Assert.StartsWith($"optional: {present} file(s):", report[4], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCommittedReportMatchesWhatWasMeasuredOnTheBox()
    {
        // The 2026-09-25 numbers, kept here so an edit that quietly changes a fixture changes the answer.
        // If any of these flip, the format matrix needs revisiting, not this test.
        var report = ProbeReport();

        Assert.Equal("codec=yes format=Png size=8x8 left=(255,0,0) right=(0,255,0)", Body(report[0]));
        foreach (var line in report.Skip(1).Take(3))
        {
            Assert.Equal("codec=no image=none", Body(line));
        }
    }

    /// <summary>The part of a report line after its fixture label.</summary>
    private static string Body(string line)
    {
        var colon = line.IndexOf(':');
        return line[(colon + 1)..].Trim();
    }
}
