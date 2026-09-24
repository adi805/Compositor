using Compositor.App;
using Compositor.App.Imaging;
using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// WS10 file IO on the view-model: JPEG export with a working quality setting, the matte
/// that stands in for transparency, the document resolution carried into both formats,
/// batch import with per-file failure reporting, and the remembered quality.
/// </summary>
public sealed class EditorViewModelIoTests : IDisposable
{
    private readonly string _dir;

    public EditorViewModelIoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"compositor-io-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path_(string name) => Path.Combine(_dir, name);

    /// <summary>A document whose layers hold real detail, so JPEG quality actually matters.</summary>
    private static EditorViewModel VmWithNoise(int seed = 5)
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        var surface = vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        var random = new Random(seed);
        for (var i = 0; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = (byte)random.Next(256);
            surface.Pixels[i + 1] = (byte)random.Next(256);
            surface.Pixels[i + 2] = (byte)random.Next(256);
            surface.Pixels[i + 3] = 255;
        }

        surface.MarkDirty();
        return vm;
    }

    private static EditorViewModel VmAllTransparent()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        return vm;
    }

    [Fact]
    public void ExportJpeg_WritesDecodableImageAtDocumentResolution()
    {
        var vm = VmWithNoise();
        var file = Path_("out.jpg");

        vm.ExportJpeg(file, new JpegOptions());

        var bytes = File.ReadAllBytes(file);
        var decoded = SkiaCodec.Decode(bytes);
        Assert.Equal(vm.Doc.Width, decoded.Width);
        Assert.Equal(vm.Doc.Height, decoded.Height);
        Assert.Equal(vm.Doc.Resolution, JpegDensity.TryGet(bytes)!.Value, 0);
    }

    [Fact]
    public void ExportJpeg_LowerQualityWritesFewerBytes()
    {
        var vm = VmWithNoise();

        var high = vm.EncodeJpeg(new JpegOptions { Quality = 0.95 }).Length;
        var low = vm.EncodeJpeg(new JpegOptions { Quality = 0.05 }).Length;

        Assert.True(
            low < (high / 3),
            $"quality had no real effect: q0.05 produced {low} bytes against q0.95 at {high}");
    }

    [Fact]
    public void ExportJpeg_ReadsBackTheMatteWhereTheCanvasIsTransparent()
    {
        var vm = VmAllTransparent();

        var black = SkiaCodec.Decode(vm.EncodeJpeg(new JpegOptions { Red = 0, Green = 0, Blue = 0 }));
        var white = SkiaCodec.Decode(vm.EncodeJpeg(new JpegOptions { Red = 1, Green = 1, Blue = 1 }));

        Assert.True(black.Rgba[0] < 20 && black.Rgba[1] < 20 && black.Rgba[2] < 20,
            $"black matte read back as {black.Rgba[0]},{black.Rgba[1]},{black.Rgba[2]}");
        Assert.True(white.Rgba[0] > 235 && white.Rgba[1] > 235 && white.Rgba[2] > 235,
            $"white matte read back as {white.Rgba[0]},{white.Rgba[1]},{white.Rgba[2]}");
    }

    [Fact]
    public void EncodeJpeg_DoesNotTouchDisk()
    {
        var vm = VmWithNoise();

        var bytes = vm.EncodeJpeg(new JpegOptions());

        Assert.NotEmpty(bytes);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ExportPng_StampsDocumentResolution()
    {
        var vm = VmWithNoise();
        vm.Doc.Resolution = 300;
        var file = Path_("out.png");

        vm.ExportPng(file);

        using var input = File.OpenRead(file);
        Png.Decode(input, out var dpi);
        Assert.Equal(300d, dpi!.Value, 1);
    }

    [Fact]
    public void ImportImages_BatchAddsGoodFilesAndReportsTheBadOnes()
    {
        var vm = new EditorViewModel();
        var good = Path_("good.png");
        using (var stream = File.Create(good))
        {
            Png.Encode(stream, 8, 8, new byte[8 * 8 * 4]);
        }

        var alsoGood = Path_("also.png");
        using (var stream = File.Create(alsoGood))
        {
            Png.Encode(stream, 4, 4, new byte[4 * 4 * 4]);
        }

        var broken = Path_("broken.png");
        File.WriteAllBytes(broken, new byte[40]);

        var added = vm.ImportImages([good, broken, alsoGood]);

        Assert.Equal(2, added.Count);
        Assert.NotNull(vm.ImportError);
        Assert.Contains("broken.png", vm.ImportError, StringComparison.Ordinal);
        Assert.DoesNotContain("good.png", vm.ImportError, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportImages_CleanBatchClearsThePreviousError()
    {
        var vm = new EditorViewModel();
        var broken = Path_("junk.png");
        File.WriteAllBytes(broken, new byte[40]);
        vm.ImportImages([broken]);
        Assert.NotNull(vm.ImportError);

        var good = Path_("ok.png");
        using (var stream = File.Create(good))
        {
            Png.Encode(stream, 4, 4, new byte[4 * 4 * 4]);
        }

        vm.ImportImages([good]);

        Assert.Null(vm.ImportError);
    }

    [Fact]
    public void UsedPixels_GrowsWithEveryImportedLayer()
    {
        var vm = new EditorViewModel();
        var file = Path_("one.png");
        using (var stream = File.Create(file))
        {
            Png.Encode(stream, 4, 4, new byte[4 * 4 * 4]);
        }

        var before = vm.UsedPixels;
        vm.ImportImage(file);

        Assert.True(vm.UsedPixels > before, "an imported layer must spend the document budget");
    }

    [Fact]
    public void MissingImportFile_IsReportedNotThrown()
    {
        var vm = new EditorViewModel();

        var layer = vm.ImportImage(Path_("does-not-exist.png"));

        Assert.Null(layer);
        Assert.Contains("does-not-exist.png", vm.ImportError, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportUnreadableDrop_NamesTheFormatsThisBuildReads()
    {
        var vm = new EditorViewModel();

        vm.ReportUnreadableDrop();

        Assert.NotNull(vm.ImportError);
        Assert.Contains("PNG", vm.ImportError, StringComparison.Ordinal);
        Assert.Contains("JPEG", vm.ImportError, StringComparison.Ordinal);
        Assert.DoesNotContain("HEIC", vm.ImportError, StringComparison.Ordinal);
    }

    [Fact]
    public void JpegSettings_RoundTripsQualityThroughAFile()
    {
        var file = Path_("settings.txt");

        JpegExportSettings.Save(new JpegOptions { Quality = 0.62 }, file);
        var loaded = JpegExportSettings.Load(file);

        Assert.Equal(0.62, loaded.Quality, 2);
    }

    [Fact]
    public void JpegSettings_FallsBackToUpstreamDefaultWhenAbsentOrNonsense()
    {
        Assert.Equal(0.85, JpegExportSettings.Load(Path_("nope.txt")).Quality);

        var junk = Path_("junk-settings.txt");
        File.WriteAllText(junk, "jpegExportQuality=not-a-number");
        Assert.Equal(0.85, JpegExportSettings.Load(junk).Quality);

        var outOfRange = Path_("over.txt");
        File.WriteAllText(outOfRange, "jpegExportQuality=9.5");
        Assert.Equal(1d, JpegExportSettings.Load(outOfRange).Quality); // clamped like upstream
    }

    [Fact]
    public void JpegSettings_KeepsOtherOptionsAtDefaults()
    {
        var file = Path_("settings2.txt");
        JpegExportSettings.Save(new JpegOptions { Quality = 0.3, Red = 0, Green = 0, Blue = 0 }, file);

        var loaded = JpegExportSettings.Load(file);

        Assert.Equal(0.3, loaded.Quality, 2);
        Assert.Equal(1d, loaded.Red); // only quality is remembered upstream
    }
}
