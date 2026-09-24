using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// The IO primitives added for WS10: budgets, content sniffing, the format matrix, the JPEG
/// matte and density, EXIF tag reading and PNG density round-trips. Numbers here are computed
/// from the specs cited in each member's doc comment, not recorded from the implementation.
/// </summary>
public class ImageIoTests
{
    // ----- budgets (upstream DocumentLimits: 30,000 per side, 200 MP per surface, RAM-scaled document budget) -----

    [Theory]
    [InlineData(1, 1, 0, true)]
    [InlineData(30_000, 1, 0, true)]
    [InlineData(1, 30_000, 0, true)]
    [InlineData(30_001, 1, 0, false)]
    [InlineData(0, 10, 0, false)]
    [InlineData(14_142, 14_142, 0, true)] // 199,996,164 px: inside the smallest document budget (200 MP)
    [InlineData(29_000, 29_000, 0, false)] // 841 MP: past every document budget
    [InlineData(1_000, 1_000, 800_000_000, false)] // spent budget already at the 800 MP ceiling
    public void Budget_Fits_MatchesUpstreamLimits(int w, int h, long used, bool expected) =>
        Assert.Equal(expected, ImageBudget.Fits(w, h, used));

    [Fact]
    public void Budget_ExportLimits_ThrowWithUpstreamMessage()
    {
        var error = Assert.Throws<ImageException>(() => ImageBudget.ValidateExport(40_000, 1));
        Assert.Equal(ImageFailure.ExportTooLarge, error.Failure);
        Assert.Equal("Image export supports canvases up to 200 megapixels and 30,000 pixels per side.", error.Message);
    }

    [Fact]
    public void Budget_ImportFailureNamesTheBudgetAndTheSideLimit()
    {
        var error = Assert.Throws<ImageException>(() => ImageBudget.ValidateImport(1, 40_000, 0));
        Assert.Equal(ImageFailure.ImportTooLarge, error.Failure);
        Assert.Contains($"{ImageBudget.DocumentBudgetMegapixels}-megapixel", error.Message, StringComparison.Ordinal);
        Assert.Contains("30,000-pixel", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(800, 600, ImageBudget.ImportThumbnailLongSide, 0.12)]
    [InlineData(96, 96, ImageBudget.ImportThumbnailLongSide, 1.0)]
    [InlineData(48, 48, ImageBudget.ImportThumbnailLongSide, 1.0)] // never upscale a thumbnail
    [InlineData(2000, 4000, ImageBudget.PreviewLongSide, 0.25)]
    public void Budget_ThumbnailScale_FollowsUpstreamMinFormula(int w, int h, int longSide, double expected) =>
        Assert.Equal(expected, ImageBudget.ThumbnailScale(w, h, longSide), 10);

    [Fact]
    public void Budget_Defaults_MatchUpstreamConstants()
    {
        Assert.Equal(30_000, ImageBudget.MaxSide);
        Assert.Equal(200_000_000L, ImageBudget.MaxSurfacePixels);
        Assert.Equal(72d, ImageBudget.DefaultResolution);
        Assert.Equal(96, ImageBudget.ImportThumbnailLongSide);
        Assert.Equal(1_000, ImageBudget.PreviewLongSide);
    }

    [Fact]
    public void Budget_SurfaceCeiling_IsTwoHundredMegapixels()
    {
        Assert.Equal(200, ImageBudget.MaxSurfaceMegapixels);

        // 14,142^2 = 199,996,164 px: the largest square that still fits one surface.
        ImageBudget.ValidateExport(14_142, 14_142);

        // 14,143^2 = 200,024,449 px: one row past it.
        var error = Assert.Throws<ImageException>(() => ImageBudget.ValidateExport(14_143, 14_143));
        Assert.Equal(ImageFailure.ExportTooLarge, error.Failure);
    }

    [Fact]
    public void Budget_DocumentCeiling_CountsWhatEveryLayerAlreadySpent()
    {
        var budget = ImageBudget.DocumentPixelBudget;
        const long million = 1_000_000L;

        // A 1,000 x 1,000 surface is 1 MP: it fits while the spent budget leaves room for it.
        Assert.True(ImageBudget.Fits(1_000, 1_000, budget - million));
        Assert.False(ImageBudget.Fits(1_000, 1_000, (budget - million) + 1));
    }

    [Fact]
    public void Budget_DocumentCeiling_FollowsTheUpstreamRamFormula()
    {
        var expected = Math.Min(
            800_000_000L,
            Math.Max(ImageBudget.MaxSurfacePixels, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 16));

        Assert.Equal(expected, ImageBudget.DocumentPixelBudget);
        Assert.InRange(ImageBudget.DocumentPixelBudget, ImageBudget.MaxSurfacePixels, 800_000_000L);
    }

    // ----- content sniffing -----

    [Fact]
    public void Detect_ReadsEverySignatureFromContentNotName()
    {
        Assert.Equal(ImageFormat.Png, ImageFormatPolicy.Detect([
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]));
        Assert.Equal(ImageFormat.Jpeg, ImageFormatPolicy.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0]));
        Assert.Equal(ImageFormat.Gif, ImageFormatPolicy.Detect("GIF89a"u8.ToArray()));
        Assert.Equal(ImageFormat.Gif, ImageFormatPolicy.Detect("GIF87a"u8.ToArray()));
        Assert.Equal(ImageFormat.Bmp, ImageFormatPolicy.Detect([0x42, 0x4D, 0, 0]));
        Assert.Equal(ImageFormat.Ico, ImageFormatPolicy.Detect([0x00, 0x00, 0x01, 0x00, 0x01, 0x00]));
        Assert.Equal(ImageFormat.Tiff, ImageFormatPolicy.Detect([0x49, 0x49, 0x2A, 0x00]));
        Assert.Equal(ImageFormat.Tiff, ImageFormatPolicy.Detect([0x4D, 0x4D, 0x00, 0x2A]));
        Assert.Equal(ImageFormat.WebP, ImageFormatPolicy.Detect(WebpHeader()));
        Assert.Equal(ImageFormat.Heif, ImageFormatPolicy.Detect(HeifHeader()));
        Assert.Equal(ImageFormat.Unknown, ImageFormatPolicy.Detect([1, 2, 3, 4]));
        Assert.Equal(ImageFormat.Unknown, ImageFormatPolicy.Detect([]));
    }

    private static byte[] WebpHeader()
    {
        var bytes = new byte[12];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static byte[] HeifHeader()
    {
        var bytes = new byte[12];
        bytes[3] = 0x18;
        "ftypheic"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    [Fact]
    public void Matrix_ClaimsAreInternallyConsistent()
    {
        Assert.NotEmpty(ImageFormatPolicy.Matrix);
        foreach (var row in ImageFormatPolicy.Matrix)
        {
            Assert.NotEmpty(row.DisplayName);
            Assert.NotEmpty(row.Extensions);
            Assert.True(row.CanImport || row.CanExport || row.Note is not null,
                $"{row.DisplayName}: an unsupported row must say why");
            foreach (var extension in row.Extensions)
            {
                Assert.False(extension.StartsWith('.'));
                Assert.Equal(extension.ToLowerInvariant(), extension);
            }
        }

        // Png and Jpeg are the two rows upstream exports; both must be claimed here.
        Assert.True(ImageFormatPolicy.CanExport(ImageFormat.Png));
        Assert.True(ImageFormatPolicy.CanExport(ImageFormat.Jpeg));
        Assert.False(ImageFormatPolicy.CanExport(ImageFormat.Heif));
    }

    [Fact]
    public void Matrix_PickerPatternsAreDerivedFromImportClaims()
    {
        foreach (var row in ImageFormatPolicy.Importable)
        {
            foreach (var extension in row.Extensions)
            {
                Assert.Contains("*." + extension, ImageFormatPolicy.ImportPatterns);
            }
        }

        Assert.DoesNotContain("*.tiff", ImageFormatPolicy.ImportPatterns);
        Assert.DoesNotContain("*.heic", ImageFormatPolicy.ImportPatterns);
    }

    [Fact]
    public void UnsupportedMessage_ListsExactlyWhatWeCanImport()
    {
        var message = ImageFormatPolicy.UnsupportedImportMessage;

        Assert.StartsWith("Choose a ", message, StringComparison.Ordinal);
        Assert.EndsWith(" image.", message, StringComparison.Ordinal);
        foreach (var row in ImageFormatPolicy.Importable)
        {
            Assert.Contains(row.DisplayName, message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("TIFF", message, StringComparison.Ordinal);
        Assert.DoesNotContain("HEIC", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportGaps_KeepTheUpstreamOnlyFormatsVisible()
    {
        var gaps = string.Join("\n", ImageFormatPolicy.ImportGaps);

        Assert.Contains("TIFF", gaps, StringComparison.Ordinal);
        Assert.Contains("HEIC", gaps, StringComparison.Ordinal);
    }

    // ----- JPEG options -----

    [Fact]
    public void JpegOptions_DefaultsMatchUpstreamStruct()
    {
        var options = new JpegOptions();

        Assert.Equal(0.85, options.Quality);
        Assert.Equal(1d, options.Red);
        Assert.Equal(1d, options.Green);
        Assert.Equal(1d, options.Blue);
        Assert.Equal(85, options.QualityPercent);
        Assert.Equal("85%", options.QualityReadout);
        Assert.Equal((byte.MaxValue, byte.MaxValue, byte.MaxValue), options.MatteBytes);
    }

    [Theory]
    [InlineData(2.0, 100)]
    [InlineData(-3.0, 0)]
    [InlineData(0.625, 63)] // Swift's rounded(): half away from zero
    [InlineData(0.624, 62)]
    [InlineData(double.NaN, 85)] // not finite -> upstream's default, not garbage
    [InlineData(double.PositiveInfinity, 85)]
    public void JpegOptions_Normalize_ClampsLikeUpstreamEncode(double quality, int expectedPercent)
    {
        var normalized = new JpegOptions { Quality = quality }.Normalize();

        Assert.Equal(expectedPercent, normalized.QualityPercent);
        Assert.InRange(normalized.Quality, 0d, 1d);
    }

    [Fact]
    public void JpegOptions_MatteBytes_ClampAndRound()
    {
        var matte = new JpegOptions { Red = 1.5, Green = -0.2, Blue = 0.5 }.Normalize().MatteBytes;

        Assert.Equal(255, matte.R);
        Assert.Equal(0, matte.G);
        Assert.Equal(128, matte.B); // 0.5 * 255 = 127.5, away from zero
    }

    // ----- matte composite -----

    [Fact]
    public void Matte_CompositesOverMatteAndKeepsOpaqueSource()
    {
        var rgba = new byte[]
        {
            255, 0, 0, 255, // opaque: untouched
            0, 0, 0, 0, // empty: pure matte
            200, 100, 50, 128, // half-covered: blends
        };

        var output = MatteComposite.Apply(rgba, 3, 1, red: 10, green: 20, blue: 30);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, output[..4]);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, output[4..8]);

        // t = 128/255 = 0.501961: 200t + 10(1-t) = 105.37 -> 105; 100t + 20(1-t) = 60.16 -> 60;
        // 50t + 30(1-t) = 40.04 -> 40. Alpha is always forced opaque for a format with no alpha.
        Assert.Equal(105, output[8]);
        Assert.Equal(60, output[9]);
        Assert.Equal(40, output[10]);
        Assert.Equal(255, output[11]);
    }

    [Fact]
    public void Matte_DoesNotModifyTheInputBuffer()
    {
        var rgba = new byte[] { 0, 0, 0, 0 };

        MatteComposite.Apply(rgba, 1, 1, new JpegOptions { Red = 1, Green = 1, Blue = 1 });

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, rgba);
    }

    [Fact]
    public void Matte_RejectsMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() => MatteComposite.Apply(new byte[7], 2, 2, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => MatteComposite.Apply(new byte[16], 0, 1, 0, 0, 0));
    }

    // ----- JFIF density -----

    private static byte[] JpegWithoutApp0() =>
        [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x02, 0x00, 0xFF, 0xD9];

    private static byte[] JpegWithAspectApp0()
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[] { 1, 1, 0, 0, 0x01, 0, 0x01, 0, 0 }); // units 0 = aspect, 1x1
        bytes.AddRange(new byte[32]);
        return bytes.ToArray();
    }

    [Fact]
    public void JpegDensity_InsertsApp0WhenTheEncoderOmittedIt()
    {
        var patched = JpegDensity.Set(JpegWithoutApp0(), 300);

        Assert.Equal(0xFF, patched[2]);
        Assert.Equal(0xE0, patched[3]);
        Assert.Equal(0x00, patched[4]); // length 16, big-endian: high byte then low
        Assert.Equal(0x10, patched[5]);
        Assert.Equal("JFIF\0"u8.ToArray(), patched[6..11]);
        Assert.Equal(1, patched[13]); // units: dots per inch
        Assert.Equal(300, patched[14] << 8 | patched[15]);
        Assert.Equal(300d, JpegDensity.TryGet(patched));
    }

    [Fact]
    public void JpegDensity_PatchesAnExistingApp0InPlaceWithoutGrowingTheFile()
    {
        var original = JpegWithAspectApp0();

        var patched = JpegDensity.Set(original, 72);

        Assert.Equal(original.Length, patched.Length);
        Assert.Null(JpegDensity.TryGet(original)); // aspect units are not a density
        Assert.Equal(72d, JpegDensity.TryGet(patched));
    }

    [Fact]
    public void JpegDensity_KeepsTheRestOfTheStreamByteIdentical()
    {
        var original = JpegWithoutApp0();

        var patched = JpegDensity.Set(original, 150);

        Assert.Equal(original[..2], patched[..2]); // SOI untouched
        Assert.Equal(original[2..], patched[(2 + 18)..]); // and everything after the new APP0
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(65_534)]
    [InlineData(double.NaN)]
    public void JpegDensity_RejectsOutOfRangeDensity(double dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => JpegDensity.Set(JpegWithoutApp0(), dpi));

    [Fact]
    public void JpegDensity_RejectsNonJpegAndReportsUnknownForShortStreams()
    {
        Assert.Throws<ArgumentException>(() => JpegDensity.Set([1, 2, 3, 4], 72));
        Assert.Null(JpegDensity.TryGet([0xFF, 0xD8]));
    }

    // ----- EXIF orientation tag -----

    private static byte[] ExifJpeg(byte orientation, bool bigEndian = false)
    {
        var tiff = new byte[26];
        tiff[0] = bigEndian ? (byte)'M' : (byte)'I';
        tiff[1] = bigEndian ? (byte)'M' : (byte)'I';
        if (bigEndian)
        {
            tiff[2] = 0x00;
            tiff[3] = 0x2A;
            tiff[7] = 0x08; // IFD offset, big-endian
            tiff[8] = 0x00;
            tiff[9] = 0x01; // one entry
            tiff[10] = 0x01;
            tiff[11] = 0x12; // tag 0x0112
            tiff[12] = 0x00;
            tiff[13] = 0x03; // SHORT
            tiff[16] = 0x00;
            tiff[17] = 0x01; // count
            tiff[18] = 0x00;
            tiff[19] = orientation; // value, big-endian
        }
        else
        {
            tiff[2] = 0x2A;
            tiff[4] = 0x08;
            tiff[8] = 0x01;
            tiff[10] = 0x12;
            tiff[11] = 0x01;
            tiff[12] = 0x03;
            tiff[14] = 0x01;
            tiff[18] = orientation;
        }

        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((payload.Length + 2) >> 8);
        segment[3] = (byte)(payload.Length + 2);
        payload.CopyTo(segment, 4);

        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(segment);
        jpeg.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        jpeg.AddRange("JFIF\0"u8.ToArray());
        jpeg.AddRange(new byte[] { 1, 1, 1, 0, 72, 0, 72, 0, 0 });
        jpeg.AddRange(new byte[8]);
        jpeg.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x00, 0xFF, 0xD9 });
        return jpeg.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    public void Exif_ReadsOrientationFromLittleEndianApp1(byte orientation) =>
        Assert.Equal(orientation, ExifOrientation.Read(ExifJpeg(orientation)));

    [Fact]
    public void Exif_ReadsOrientationFromBigEndianTiffToo() =>
        Assert.Equal(5, ExifOrientation.Read(ExifJpeg(5, bigEndian: true)));

    [Fact]
    public void Exif_StopsLookingAfterStartOfScan()
    {
        // A valid APP1/EXIF run, then SOS, then the same metadata again. Whatever the entropy
        // data looks like, a reader that keeps walking past SOS would find a second orientation
        // tag; the first one (2) is the answer, and 6 must never be reachable.
        var withTag = ExifJpeg(2);
        var head = withTag[..^2]; // everything up to and including its own first SOS
        var bytes = new List<byte>();
        bytes.AddRange(head);
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x04, 0x00, 0x01 });
        bytes.AddRange(ExifJpeg(6).Skip(2));
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });

        Assert.Equal(2, ExifOrientation.Read(withTag)); // control: the parser does read the tag
        Assert.Equal(2, ExifOrientation.Read(bytes.ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-2)]
    [InlineData(int.MaxValue)]
    public void Exif_Normalize_FallsBackToIdentity(int orientation) =>
        Assert.Equal(ExifOrientation.Identity, ExifOrientation.Normalize(orientation));

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Exif_Normalize_KeepsValuesInTheTable(int orientation) =>
        Assert.Equal(orientation, ExifOrientation.Normalize(orientation));

    [Fact]
    public void Exif_GarbageAndShortInputNeverThrow()
    {
        Assert.Equal(ExifOrientation.Identity, ExifOrientation.Read([]));
        Assert.Equal(ExifOrientation.Identity, ExifOrientation.Read([0xFF, 0xD8, 0xFF]));
        var random = new Random(3);
        var noise = new byte[96];
        for (var trial = 0; trial < 200; trial++)
        {
            random.NextBytes(noise);
            noise[0] = 0xFF;
            noise[1] = 0xD8;
            var read = ExifOrientation.Read(noise);
            Assert.InRange(read, 1, 8);
        }
    }

    [Fact]
    public void Exif_ReadsABareTiffHeader()
    {
        var tiff = new byte[26];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        tiff[2] = 0x2A;
        tiff[4] = 0x08;
        tiff[8] = 0x01;
        tiff[10] = 0x12;
        tiff[11] = 0x01;
        tiff[12] = 0x03;
        tiff[14] = 0x01;
        tiff[18] = 3;

        Assert.Equal(3, ExifOrientation.Read(tiff));
    }

    // ----- PNG density (pHYs) -----

    [Theory]
    [InlineData(72, 2835u)]
    [InlineData(300, 11811u)]
    [InlineData(96, 3780u)]
    [InlineData(1, 39u)]
    public void Png_ConvertsDpiToPixelsPerMetre(double dpi, uint expected) =>
        Assert.Equal(expected, Png.PixelsPerMeter(dpi));

    [Fact]
    public void Png_RoundTripsTheDocumentResolution()
    {
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 7;
            rgba[i + 3] = 255;
        }

        using var withDpi = new MemoryStream();
        Png.Encode(withDpi, 4, 4, rgba, dpi: 300);
        withDpi.Position = 0;
        Png.Decode(withDpi, out var readBack);

        Assert.Equal(300d, readBack!.Value, 1);

        using var noDpi = new MemoryStream();
        Png.Encode(noDpi, 4, 4, rgba);
        noDpi.Position = 0;
        Png.Decode(noDpi, out var absent);

        Assert.Null(absent);
    }

    [Fact]
    public void Png_DoesNotWritePhysWhenDpiIsMissingOrAbsurd()
    {
        var rgba = new byte[4];
        foreach (double? dpi in new double?[] { null, 0, -1, double.NaN, double.PositiveInfinity })
        {
            using var stream = new MemoryStream();
            Png.Encode(stream, 1, 1, rgba, dpi);
            Assert.False(
                ContainsBytes(stream.ToArray(), "pHYs"u8),
                $"pHYs written for dpi {dpi}");
        }
    }

    private static bool ContainsBytes(byte[] haystack, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public void Png_PixelsPerMetreAndBack_IsStableAtDocumentDefaults()
    {
        Assert.Equal(72d, Png.DpiFromPixelsPerMeter(Png.PixelsPerMeter(72)), 1);
        Assert.Equal(300d, Png.DpiFromPixelsPerMeter(Png.PixelsPerMeter(300)), 1);
    }
}
