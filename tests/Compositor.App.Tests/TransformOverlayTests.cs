using Compositor.App;
using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Drives the transform overlay the way the canvas does: grab a handle, drag, release. The point of these is
/// that the drag lands in document pixels and the composited render follows it, so the box the user sees is
/// the placement that gets saved. Expected positions are derived by hand from the handle layout.
/// </summary>
public class TransformOverlayTests
{
    private const double ViewportW = 1200;
    private const double ViewportH = 800;

    /// <summary>One layer covering the whole canvas, move tool active. Sizes come from the document itself.</summary>
    private static EditorViewModel MoveVm()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        vm.Tool = EditorViewModel.EditorTool.Move;
        return vm;
    }

    private static (double X, double Y) DocToScreen(EditorViewModel vm, double docX, double docY)
    {
        var r = vm.CanvasRect(ViewportW, ViewportH);
        return (r.X + docX / vm.Doc.Width * r.W, r.Y + docY / vm.Doc.Height * r.H);
    }

    [Fact]
    public void Overlay_OnlyShowsForTheMoveTool()
    {
        var vm = MoveVm();
        Assert.NotNull(vm.TransformOverlay(ViewportW, ViewportH));

        vm.Tool = EditorViewModel.EditorTool.Brush;
        Assert.Null(vm.TransformOverlay(ViewportW, ViewportH));
    }

    [Fact]
    public void Overlay_HasEightHandlesAndARotationHandle()
    {
        var vm = MoveVm();

        var overlay = vm.TransformOverlay(ViewportW, ViewportH);

        Assert.NotNull(overlay);
        Assert.Equal(8, overlay!.Value.Handles.Length);
        // The layer covers the whole canvas, so handle 0 is the document's top-left corner.
        Assert.Equal(0, overlay.Value.Handles[0].X, 6);
        Assert.Equal(0, overlay.Value.Handles[0].Y, 6);
        Assert.Equal(vm.Doc.Width, overlay.Value.Handles[4].X, 6);
        Assert.Equal(vm.Doc.Height, overlay.Value.Handles[4].Y, 6);
    }

    [Fact]
    public void Drag_InsideTheBody_MovesRatherThanResizing()
    {
        var vm = MoveVm();
        var centerX = vm.Doc.Width / 2.0;
        var centerY = vm.Doc.Height / 2.0;

        Assert.True(vm.BeginTransformDrag(centerX, centerY, ViewportW, ViewportH));
        vm.ContinueTransformDrag(centerX + 25, centerY + 15);
        Assert.True(vm.EndTransformDrag());

        var after = vm.ActiveLayer!.Transform;
        Assert.Equal(25, after.OriginX, 6);
        Assert.Equal(15, after.OriginY, 6);
        Assert.Equal(vm.Doc.Width, after.Width, 6);    // the size is untouched: this was a move, not a resize
        Assert.Equal(vm.Doc.Height, after.Height, 6);
    }

    [Fact]
    public void Drag_OutsideTheBox_StartsNothing()
    {
        var vm = MoveVm();

        // Well clear of the box and every handle: no drag, and no stray undo step.
        Assert.False(vm.BeginTransformDrag(vm.Doc.Width + 500, vm.Doc.Height + 500, ViewportW, ViewportH));
        Assert.False(vm.IsTransformDragActive);
        Assert.False(vm.EndTransformDrag());
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void Drag_FromInsideTheBox_MovesTheOriginByThePointerDelta()
    {
        var vm = MoveVm();
        var before = vm.ActiveLayer!.Transform;
        var centerX = vm.Doc.Width / 2.0;
        var centerY = vm.Doc.Height / 2.0;

        // Grab the body, not a handle, and drag it 40 right and 30 down.
        Assert.True(vm.BeginTransformDrag(centerX, centerY, ViewportW, ViewportH));
        Assert.True(vm.IsTransformDragActive);

        vm.ContinueTransformDrag(centerX + 40, centerY + 30);
        Assert.True(vm.EndTransformDrag());

        var after = vm.ActiveLayer!.Transform;
        Assert.Equal(before.OriginX + 40, after.OriginX, 6);
        Assert.Equal(before.OriginY + 30, after.OriginY, 6);
        // The layer carried a zero-size cover-canvas box; the drag writes a real one at the canvas size.
        Assert.Equal(vm.Doc.Width, after.Width, 6);
        Assert.Equal(vm.Doc.Height, after.Height, 6);
    }

    [Fact]
    public void Drag_ThatMovesNothing_LeavesNoUndoStep()
    {
        var vm = MoveVm();
        var before = vm.ActiveLayer!.Transform;

        Assert.True(vm.BeginTransformDrag(vm.Doc.Width / 2.0, vm.Doc.Height / 2.0, ViewportW, ViewportH));
        Assert.False(vm.EndTransformDrag());          // released without moving

        Assert.Equal(before, vm.ActiveLayer!.Transform);
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void Drag_IsOneUndoStep_AndUndoPutsTheLayerBack()
    {
        var vm = MoveVm();
        var before = vm.ActiveLayer!.Transform;

        var centerX = vm.Doc.Width / 2.0;
        var centerY = vm.Doc.Height / 2.0;
        Assert.True(vm.BeginTransformDrag(centerX, centerY, ViewportW, ViewportH));
        vm.ContinueTransformDrag(centerX + 40, centerY + 30);
        vm.ContinueTransformDrag(centerX + 70, centerY + 55);   // several moves in one drag
        Assert.True(vm.EndTransformDrag());

        var dragged = vm.ActiveLayer!.Transform;
        Assert.Equal(before.OriginX + 70, dragged.OriginX, 6);

        vm.History.Undo();

        Assert.Equal(before, vm.ActiveLayer!.Transform);
    }

    [Fact]
    public void Drag_FromACorner_LeavesTheOppositeCornerWhereItWas()
    {
        var vm = MoveVm();

        // Handle 4 is the bottom-right corner; its anchor is the top-left corner.
        var width = vm.Doc.Width;
        var height = vm.Doc.Height;
        Assert.True(vm.BeginTransformDrag(width, height, ViewportW, ViewportH));
        vm.ContinueTransformDrag(width - 50, height - 50);   // pulled in by 50, 50
        Assert.True(vm.EndTransformDrag());

        var after = vm.ActiveLayer!.Transform;
        Assert.Equal(width - 50, after.Width, 6);
        Assert.Equal(height - 50, after.Height, 6);

        var (anchorX, anchorY) = after.Point(0.0, 0.0);
        Assert.Equal(0, anchorX, 6);
        Assert.Equal(0, anchorY, 6);
    }

    [Fact]
    public void Drag_FromTheRotationHandle_TurnsTheLayer()
    {
        var vm = MoveVm();
        var overlay = vm.TransformOverlay(ViewportW, ViewportH)!.Value;

        // Grab the rotation handle, which sits above the top edge, and swing it to the right of the centre.
        var centerX = vm.Doc.Width / 2.0;
        var centerY = vm.Doc.Height / 2.0;
        Assert.True(vm.BeginTransformDrag(overlay.Rotation.X, overlay.Rotation.Y, ViewportW, ViewportH));
        vm.ContinueTransformDrag(centerX + 200, centerY);   // due right of the centre
        Assert.True(vm.EndTransformDrag());

        Assert.Equal(90, vm.ActiveLayer!.Transform.RotationDegrees, 6);
    }

    [Fact]
    public void Drag_ScalesInDocumentPixelsNotScreenPixels()
    {
        // At 2x zoom one screen pixel is half a document pixel, so the same screen delta must move the layer
        // half as far in document space. This is the bug the DocPerScreenPixel conversion exists to prevent.
        var vm = MoveVm();
        vm.ViewScale = 2.0;

        var perPixel = vm.DocPerScreenPixel(ViewportW, ViewportH);
        Assert.True(perPixel < 1.0, "at 2x zoom a screen pixel must be less than a document pixel");

        var centerX = vm.Doc.Width / 2.0;
        var centerY = vm.Doc.Height / 2.0;
        var screenCenter = DocToScreen(vm, centerX, centerY);
        var target = vm.ScreenToDocUnclamped(screenCenter.X + 40, screenCenter.Y + 30, ViewportW, ViewportH);

        Assert.True(vm.BeginTransformDrag(centerX, centerY, ViewportW, ViewportH));
        vm.ContinueTransformDrag(target.X, target.Y);
        Assert.True(vm.EndTransformDrag());

        // The invariant: a 40 screen px drag lands as 40 * perPixel document px, not 40. The finished drag is
        // recorded on whole pixels, so the expectation is rounded the same way.
        Assert.Equal(Math.Round(40 * perPixel, MidpointRounding.AwayFromZero), vm.ActiveLayer!.Transform.OriginX, 6);
        Assert.Equal(Math.Round(30 * perPixel, MidpointRounding.AwayFromZero), vm.ActiveLayer!.Transform.OriginY, 6);
    }

    [Fact]
    public void Drag_ThenFlatten_PutsThePixelsWhereTheBoxSays()
    {
        // The render has to follow the box, or the canvas is a lie. A 100x100 white block at the top-left of
        // the layer, moved 100 right and 50 down, must come out of the flatten at (100,50) with the old corner
        // now empty.
        var vm = MoveVm();
        var surface = vm.ActiveLayer!.Pixels!;
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                surface.SetPixel(x, y, 255, 255, 255, 255);
            }
        }

        var (_, _, beforePixels) = Flatten.ToRgba(vm.Doc);
        Assert.Equal(255, Alpha(beforePixels, vm.Doc.Width, 0, 0));

        // Grab the block's interior (inside the box), so this moves rather than resizing from a corner.
        Assert.True(vm.BeginTransformDrag(50, 50, ViewportW, ViewportH));
        vm.ContinueTransformDrag(150, 100);
        Assert.True(vm.EndTransformDrag());

        var (_, _, afterPixels) = Flatten.ToRgba(vm.Doc);
        Assert.Equal(0, Alpha(afterPixels, vm.Doc.Width, 0, 0));          // the old corner is empty
        Assert.Equal(255, Alpha(afterPixels, vm.Doc.Width, 150, 100));    // the block now covers 100..200 x 50..150
        Assert.Equal(255, Alpha(afterPixels, vm.Doc.Width, 199, 149));
        Assert.Equal(0, Alpha(afterPixels, vm.Doc.Width, 201, 151));
    }

    private static byte Alpha(byte[] rgba, int width, int x, int y) => rgba[(y * width + x) * 4 + 3];
}
