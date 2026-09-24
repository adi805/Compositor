using Compositor.App;
using Compositor.Core;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Canvas interaction at the ends of the zoom range. WS11's acceptance is "canvas interaction tests
/// pass at high zoom", so the control-point to document-pixel mapping, the zoom anchor, and where a
/// stroke actually lands are pinned here at minimum, fit and maximum scale. Tolerance stays well
/// under one document pixel: at 32x one screen pixel is about 0.03 document pixels, so anything
/// looser would hide a real drift.
/// </summary>
public class HighZoomInteractionTests
{
    private const double ViewportW = 1200;
    private const double ViewportH = 800;

    private static EditorViewModel VmAtScale(double scale)
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        vm.Tool = EditorViewModel.EditorTool.Brush;
        vm.ViewScale = scale;
        return vm;
    }

    /// <summary>Document point to control-space point, the inverse of <c>ScreenToDoc</c>.</summary>
    private static (double X, double Y) DocToScreen(EditorViewModel vm, double docX, double docY)
    {
        var r = vm.CanvasRect(ViewportW, ViewportH);
        return (r.X + (docX / vm.Doc.Width * r.W), r.Y + (docY / vm.Doc.Height * r.H));
    }

    [Theory]
    [InlineData(EditorViewModel.MinViewScale)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(8.0)]
    [InlineData(EditorViewModel.MaxViewScale)]
    public void ScreenToDoc_RoundTripsAtEveryZoom(double scale)
    {
        var vm = VmAtScale(scale);
        var docX = vm.Doc.Width * 0.37;
        var docY = vm.Doc.Height * 0.61;
        var screen = DocToScreen(vm, docX, docY);

        var back = vm.ScreenToDoc(screen.X, screen.Y, ViewportW, ViewportH);

        Assert.NotNull(back);
        Assert.True(
            Math.Abs(back!.Value.X - docX) < 0.02 && Math.Abs(back.Value.Y - docY) < 0.02,
            $"scale {scale}: {docX},{docY} came back as {back.Value.X},{back.Value.Y}");
    }

    [Fact]
    public void ScreenToDoc_AtMaxZoom_JustInsideTheEdgeIsStillOnCanvas()
    {
        var vm = VmAtScale(EditorViewModel.MaxViewScale);
        var r = vm.CanvasRect(ViewportW, ViewportH);

        var inside = vm.ScreenToDoc(r.X + r.W - 0.5, r.Y + r.H - 0.5, ViewportW, ViewportH);

        Assert.NotNull(inside);
        Assert.InRange(inside!.Value.X, 0f, (float)vm.Doc.Width);
        Assert.InRange(inside.Value.Y, 0f, (float)vm.Doc.Height);
    }

    [Fact]
    public void ScreenToDoc_AtMaxZoom_JustOutsideTheEdgeIsRejected()
    {
        var vm = VmAtScale(EditorViewModel.MaxViewScale);
        var r = vm.CanvasRect(ViewportW, ViewportH);

        Assert.Null(vm.ScreenToDoc(r.X + r.W + 0.5, r.Y + r.H + 0.5, ViewportW, ViewportH));
        Assert.Null(vm.ScreenToDoc(r.X - 0.5, r.Y - 0.5, ViewportW, ViewportH));
    }

    [Fact]
    public void ZoomAt_KeepsTheDocumentPointUnderTheCursor()
    {
        var vm = VmAtScale(1.0);
        var cursor = (X: ViewportW * 0.62, Y: ViewportH * 0.38);
        var before = vm.ScreenToDoc(cursor.X, cursor.Y, ViewportW, ViewportH);
        Assert.NotNull(before);

        for (var step = 0; step < 6; step++)
        {
            vm.ZoomAt(cursor.X, cursor.Y, ViewportW, ViewportH, 1.5);
        }

        var after = vm.ScreenToDoc(cursor.X, cursor.Y, ViewportW, ViewportH);

        // Six steps of 1.5 from fit is 11.39x: inside the 32x ceiling, so nothing was clamped away.
        Assert.Equal(Math.Pow(1.5, 6), vm.ViewScale, 3);
        Assert.NotNull(after);
        Assert.True(
            Math.Abs(after!.Value.X - before!.Value.X) < 0.2 && Math.Abs(after.Value.Y - before.Value.Y) < 0.2,
            $"zooming to {vm.ViewScale} slid the anchor from {before} to {after}");
    }

    [Fact]
    public void ZoomOutAt_KeepsTheDocumentPointUnderTheCursor()
    {
        var vm = VmAtScale(EditorViewModel.MaxViewScale);
        var cursor = (X: ViewportW * 0.4, Y: ViewportH * 0.55);
        var before = vm.ScreenToDoc(cursor.X, cursor.Y, ViewportW, ViewportH);
        Assert.NotNull(before);

        for (var step = 0; step < 8; step++)
        {
            vm.ZoomAt(cursor.X, cursor.Y, ViewportW, ViewportH, 0.8);
        }

        var after = vm.ScreenToDoc(cursor.X, cursor.Y, ViewportW, ViewportH);

        Assert.NotNull(after);
        Assert.True(
            Math.Abs(after!.Value.X - before!.Value.X) < 0.2 && Math.Abs(after.Value.Y - before.Value.Y) < 0.2,
            $"zooming out slid the anchor from {before} to {after}");
    }

    [Fact]
    public void BrushAtMaxZoom_PaintsThePixelUnderTheCursor()
    {
        var vm = VmAtScale(EditorViewModel.MaxViewScale);
        var visible = vm.ScreenToDoc(ViewportW / 2, ViewportH / 2, ViewportW, ViewportH);
        Assert.NotNull(visible);

        Assert.True(vm.BeginTool(visible!.Value.X, visible.Value.Y));
        vm.ContinueTool(visible.Value.X, visible.Value.Y);
        vm.EndTool();

        var surface = vm.ActiveLayer!.Pixels!;
        var painted = surface.GetPixel((int)visible.Value.X, (int)visible.Value.Y);
        Assert.True(painted.A > 200, $"centre pixel alpha was {painted.A}");

        // The default brush is 40 px wide, so a point 200 document pixels away must be untouched.
        Assert.Equal(0, surface.GetPixel((int)visible.Value.X + 200, (int)visible.Value.Y).A);
    }

    [Fact]
    public void BrushAfterPanningAtMaxZoom_PaintsWhereTheCursorIs()
    {
        var vm = VmAtScale(EditorViewModel.MaxViewScale);
        vm.ViewPanX = -420;
        vm.ViewPanY = -260;
        var visible = vm.ScreenToDoc(ViewportW * 0.55, ViewportH * 0.45, ViewportW, ViewportH);
        Assert.NotNull(visible);

        Assert.True(vm.BeginTool(visible!.Value.X, visible.Value.Y));
        vm.ContinueTool(visible.Value.X + 1, visible.Value.Y + 1);
        vm.EndTool();

        var surface = vm.ActiveLayer!.Pixels!;
        var at = surface.GetPixel((int)visible.Value.X, (int)visible.Value.Y);
        Assert.True(at.A > 200, $"after panning, centre pixel alpha was {at.A}");
    }

    [Fact]
    public void MinZoom_FitsTheWholeCanvasInASmallSpan()
    {
        var vm = VmAtScale(EditorViewModel.MinViewScale);
        var r = vm.CanvasRect(ViewportW, ViewportH);

        // Fit for a 1280x720 document in 1200x800 is 0.9375, and min scale is 0.05, so the whole
        // canvas is about 60 px wide: less than a tenth of the viewport.
        Assert.True(r.W < 61 && r.H < 61, $"canvas measured {r.W}x{r.H} px on screen");
        var halfPixelInDocX = (0.5 / r.W) * vm.Doc.Width;
        var halfPixelInDocY = (0.5 / r.H) * vm.Doc.Height;

        var corner = vm.ScreenToDoc(r.X + 0.5, r.Y + 0.5, ViewportW, ViewportH);
        var opposite = vm.ScreenToDoc(r.X + r.W - 0.5, r.Y + r.H - 0.5, ViewportW, ViewportH);

        Assert.NotNull(corner);
        Assert.NotNull(opposite);
        Assert.Equal(halfPixelInDocX, corner!.Value.X, 1);
        Assert.Equal(halfPixelInDocY, corner.Value.Y, 1);
        Assert.Equal(vm.Doc.Width - halfPixelInDocX, opposite!.Value.X, 1);
        Assert.Equal(vm.Doc.Height - halfPixelInDocY, opposite.Value.Y, 1);
    }
}
