using Compositor.App;
using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// File operations (save/load/export/import) and view-transform math.
/// All path-based, no platform or dialogs required.
/// </summary>
public sealed class EditorViewModelFileViewTests : IDisposable
{
    private readonly string _dir;

    public EditorViewModelFileViewTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"compositor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private static byte[] SolidImage(int w, int h, byte r, byte g, byte b, byte a)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = r;
            px[i + 1] = g;
            px[i + 2] = b;
            px[i + 3] = a;
        }

        return px;
    }

    // ----- Export -----

    [Fact]
    public void ExportPng_WritesDecodableImageWithDocSize()
    {
        var vm = new EditorViewModel();
        var path = Path.Combine(_dir, "out.png");

        vm.ExportPng(path);

        using var input = File.OpenRead(path);
        var (w, h, _) = Png.Decode(input);
        Assert.Equal(vm.Doc.Width, w);
        Assert.Equal(vm.Doc.Height, h);
    }

    [Fact]
    public void ExportPng_FlattensVisibleLayerPixels()
    {
        var vm = new EditorViewModel();
        var layer = vm.ActiveLayer!;

        layer.Pixels ??= new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        layer.Pixels.SetPixel(10, 10, 255, 0, 0, 255);
        layer.Pixels.MarkDirty();
        var path = Path.Combine(_dir, "flat.png");

        vm.ExportPng(path);

        using var input = File.OpenRead(path);
        var (_, _, rgba) = Png.Decode(input);
        var offset = ((10 * vm.Doc.Width) + 10) * 4;
        Assert.Equal(255, rgba[offset]);     // red channel painted
        Assert.Equal(255, rgba[offset + 3]); // alpha opaque at the stroke dot
    }

    // ----- Project save/load -----

    [Fact]
    public void SaveThenLoad_RestoresDocumentAndFilePath()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.SetBrushColor(1, 2, 3);
        var path = Path.Combine(_dir, "proj.comp");

        vm.SaveProject(path);
        Assert.Equal(path, vm.CurrentFilePath);

        var loaded = EditorViewModel.LoadProject(path);
        Assert.Equal(path, loaded.CurrentFilePath);
        Assert.Equal(vm.Doc.Width, loaded.Doc.Width);
        Assert.Equal(vm.Doc.Height, loaded.Doc.Height);
        Assert.Equal(vm.Doc.Layers.Count, loaded.Doc.Layers.Count);
    }

    // ----- Import -----

    [Fact]
    public void ImportImage_AddsTopmostSelectedLayerCentredOnCanvas()
    {
        var vm = new EditorViewModel();
        var imgPath = Path.Combine(_dir, "img.png");
        using (var fs = File.Create(imgPath))
        {
            Png.Encode(fs, 3, 2, SolidImage(3, 2, 10, 20, 30, 255));
        }

        var before = vm.Doc.Layers.Count;
        var layer = vm.ImportImage(imgPath);

        Assert.NotNull(layer);
        Assert.Equal(before + 1, vm.Doc.Layers.Count);
        Assert.Equal(vm.Doc.ActiveLayerId, layer!.Id);
        Assert.NotNull(layer.Pixels);
        Assert.Equal(vm.Doc.Width, layer.Pixels!.Width); // surface sized to canvas
        Assert.Equal("img", layer.Name);

        // Upstream placement with no drop point: centred on the canvas, so the corner is empty.
        var originX = (int)Math.Floor((vm.Doc.Width / 2f) - (3 / 2f));
        var originY = (int)Math.Floor((vm.Doc.Height / 2f) - (2 / 2f));
        Assert.Equal((10, 20, 30, 255), layer.Pixels!.GetPixel(originX, originY));
        Assert.Equal((0, 0, 0, 0), layer.Pixels.GetPixel(0, 0));
        Assert.Same(layer, vm.Selected?.Layer);
    }

    [Fact]
    public void ImportImage_AtDropPoint_LandsUnderTheCursor()
    {
        var vm = new EditorViewModel();
        var imgPath = Path.Combine(_dir, "dot.png");
        using (var fs = File.Create(imgPath))
        {
            Png.Encode(fs, 4, 4, SolidImage(4, 4, 200, 10, 10, 255));
        }

        var layer = vm.ImportImage(imgPath, at: (100, 80));

        Assert.NotNull(layer);
        // centre (100,80) minus half the 4x4 image = origin (98,78)
        Assert.Equal((200, 10, 10, 255), layer!.Pixels!.GetPixel(98, 78));
        Assert.Equal((0, 0, 0, 0), layer.Pixels.GetPixel(102, 82));
    }

    [Fact]
    public void ImportImage_PartlyOffCanvas_CopiesOnlyTheVisiblePart()
    {
        var vm = new EditorViewModel();
        var imgPath = Path.Combine(_dir, "edge.png");
        using (var fs = File.Create(imgPath))
        {
            Png.Encode(fs, 4, 4, SolidImage(4, 4, 1, 2, 3, 255));
        }

        var layer = vm.ImportImage(imgPath, at: (1, 1));

        Assert.NotNull(layer);
        // Centre (1,1) puts the image origin at (-1,-1), so the bottom-right 3x3 of the image
        // is what fits: it starts at the canvas corner and the pixel past it stays empty.
        Assert.Equal((1, 2, 3, 255), layer!.Pixels!.GetPixel(0, 0));
        Assert.Equal((1, 2, 3, 255), layer.Pixels.GetPixel(2, 2));
        Assert.Equal((0, 0, 0, 0), layer.Pixels.GetPixel(3, 3));
    }

    [Fact]
    public void ImportImage_UnreadableFile_SetsImportErrorAndAddsNothing()
    {
        var vm = new EditorViewModel();
        var junkPath = Path.Combine(_dir, "junk.png");
        File.WriteAllBytes(junkPath, new byte[48]);

        var before = vm.Doc.Layers.Count;
        var layer = vm.ImportImage(junkPath);

        Assert.Null(layer);
        Assert.Equal(before, vm.Doc.Layers.Count);
        Assert.NotNull(vm.ImportError);
        Assert.Contains("junk.png", vm.ImportError, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportImage_KeepsCanvasSizedSurface()
    {
        var vm = new EditorViewModel();
        var imgPath = Path.Combine(_dir, "big.png");
        using (var fs = File.Create(imgPath))
        {
            Png.Encode(fs, 99, 50, SolidImage(99, 50, 1, 2, 3, 255));
        }

        var layer = vm.ImportImage(imgPath);

        Assert.NotNull(layer);
        Assert.Equal(vm.Doc.Width, layer!.Pixels!.Width);
        Assert.Equal(vm.Doc.Height, layer.Pixels.Height);
    }

    [Fact]
    public void ImportRgba_RejectsBadBuffer()
    {
        var vm = new EditorViewModel();
        Assert.Throws<ArgumentException>(() => vm.ImportRgba("x", 2, 2, new byte[3]));
    }

    // ----- View transform -----

    [Fact]
    public void CanvasRect_AtDefaultView_IsCenteredFit()
    {
        var vm = new EditorViewModel(); // 1280x720 doc
        var (x, y, w, h) = vm.CanvasRect(1280, 720);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(1280, w);
        Assert.Equal(720, h);

        var (x2, y2, w2, h2) = vm.CanvasRect(640, 720);
        Assert.Equal(640, w2, 5);  // fit scale 0.5: 1280*0.5
        Assert.Equal(360, h2, 5);
        Assert.Equal(0, x2, 5);
        Assert.Equal(180, y2, 5); // (720-360)/2: vertical letterbox
    }

    [Fact]
    public void ViewScale_ClampedToBounds()
    {
        var vm = new EditorViewModel();
        vm.ViewScale = 999;
        Assert.Equal(EditorViewModel.MaxViewScale, vm.ViewScale);
        vm.ViewScale = 0.0001;
        Assert.Equal(EditorViewModel.MinViewScale, vm.ViewScale);
    }

    [Fact]
    public void ZoomAt_KeepsAnchorPointStable()
    {
        var vm = new EditorViewModel();
        const double vw = 800, vh = 600;
        const double ax = 500, ay = 300;

        var before = vm.ScreenToDoc(ax, ay, vw, vh);
        vm.ZoomAt(ax, ay, vw, vh, 2.0);
        var after = vm.ScreenToDoc(ax, ay, vw, vh);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.Value.X, after!.Value.X, 2);
        Assert.Equal(before.Value.Y, after.Value.Y, 2);
        Assert.True(vm.ViewScale > 1.9);
    }

    [Fact]
    public void PanBy_ShiftsCanvasRect()
    {
        var vm = new EditorViewModel();
        var (x1, y1, _, _) = vm.CanvasRect(800, 600);

        vm.PanBy(40, -15);
        var (x2, y2, _, _) = vm.CanvasRect(800, 600);

        Assert.Equal(x1 + 40, x2, 3);
        Assert.Equal(y1 - 15, y2, 3);
    }

    [Fact]
    public void ResetView_RestoresFitAndNoPan()
    {
        var vm = new EditorViewModel();
        vm.ZoomAt(400, 300, 800, 600, 3.0);
        vm.PanBy(100, 100);

        vm.ResetView();

        Assert.Equal(1.0, vm.ViewScale, 5);
        // 1280x720 in an 800x600 viewport fits by width: 800x450, centered.
        var (x, y, w, h) = vm.CanvasRect(800, 600);
        Assert.Equal(0, x, 5);
        Assert.Equal(75, y, 5);   // (600-450)/2
        Assert.Equal(800, w, 5);
        Assert.Equal(450, h, 5);
    }

    [Fact]
    public void ScreenToDoc_OutsideCanvas_ReturnsNull()
    {
        var vm = new EditorViewModel();
        vm.PanBy(-5000, -5000); // push the canvas far off-screen
        Assert.Null(vm.ScreenToDoc(10, 10, 800, 600));
    }
}
