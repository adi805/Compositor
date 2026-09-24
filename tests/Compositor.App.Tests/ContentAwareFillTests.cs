using System.IO;
using Compositor.App;
using Compositor.Core;
using Compositor.Core.Imaging;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The menu operation, not the kernel: a selection becomes a filled hole in the active layer,
/// one command deep, and refuses cleanly when the editor is busy or there is nothing to fill.
/// </summary>
public sealed class ContentAwareFillTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "compositor-fill-" + Path.GetRandomFileName());

    public ContentAwareFillTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static EditorViewModel OpaqueLayer(string path)
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        vm.Tool = EditorViewModel.EditorTool.Brush;
        // Paint a solid block so the fill has real neighbours to copy from.
        vm.BeginStroke(200, 200);
        vm.ContinueStroke(600, 600);
        vm.EndStroke();
        return vm;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(EditorViewModel vm, int x, int y)
    {
        var pixels = vm.ActiveLayer!.Pixels!.Pixels;
        var i = (y * (vm.Doc.Width * 4)) + (x * 4);
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    [Fact]
    public void WithoutASelection_RefusesAndLeavesTheLayerAlone()
    {
        var vm = OpaqueLayer(_dir);
        var before = (byte[])vm.ActiveLayer!.Pixels!.Pixels.Clone();
        Assert.False(vm.ContentAwareFill());
        Assert.Equal(before, vm.ActiveLayer.Pixels.Pixels);
    }

    [Fact]
    public void SelectionIsFilledAndNothingOutsideItChanges()
    {
        var vm = OpaqueLayer(_dir);
        var width = vm.Doc.Width;
        var height = vm.Doc.Height;
        var before = (byte[])vm.ActiveLayer!.Pixels!.Pixels.Clone();

        vm.Doc.Selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(300, 300, 40, 40, SelectionMode.Replace));
        Assert.True(vm.ContentAwareFill());

        var outsideChanged = 0;
        var insideChanged = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * (width * 4)) + (x * 4);
                var same = before[i] == vm.ActiveLayer.Pixels.Pixels[i]
                    && before[i + 1] == vm.ActiveLayer.Pixels.Pixels[i + 1]
                    && before[i + 2] == vm.ActiveLayer.Pixels.Pixels[i + 2]
                    && before[i + 3] == vm.ActiveLayer.Pixels.Pixels[i + 3];
                if (same)
                {
                    continue;
                }

                if (x >= 300 && x < 340 && y >= 300 && y < 340)
                {
                    insideChanged++;
                }
                else
                {
                    outsideChanged++;
                }
            }
        }

        Assert.Equal(0, outsideChanged);
        Assert.True(insideChanged > 0, "the fill ran but wrote nothing inside the selection");

        // Nothing is invented and nothing is half-baked: a donor is by definition an opaque
        // pixel, so a filled position must end up opaque.
        Assert.Equal((byte)255, Pixel(vm, 320, 320).A);
    }

    [Fact]
    public void LockedActiveLayer_Refuses()
    {
        var vm = OpaqueLayer(_dir);
        vm.Doc.Selection = DocumentSelection.All(vm.Doc.Width, vm.Doc.Height);
        vm.ActiveLayer!.IsLocked = true;
        Assert.False(vm.ContentAwareFill());
    }

    [Fact]
    public void WholeCanvasSelected_HasNoDonorAndLeavesTheLayerAlone()
    {
        var vm = OpaqueLayer(_dir);
        var before = (byte[])vm.ActiveLayer!.Pixels!.Pixels.Clone();
        vm.Doc.Selection = DocumentSelection.All(vm.Doc.Width, vm.Doc.Height);
        Assert.False(vm.ContentAwareFill());
        Assert.Equal(before, vm.ActiveLayer.Pixels.Pixels);
    }
}
