using Compositor.App;
using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.Core.Filters;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Filter sheet flow on the view-model: open → live preview → commit/cancel, plus the mutual
/// exclusion the sheets rely on. Pixel math is covered by the Core filter tests; these test the
/// orchestration around it.
/// </summary>
public class EditorViewModelFilterTests
{
    private static EditorViewModel VmWithGrayLayer()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        var surface = vm.ActiveLayer.Pixels;
        for (var i = 0; i < surface.Width * surface.Height; i++)
        {
            surface.Pixels[(i * 4) + 0] = 100;
            surface.Pixels[(i * 4) + 1] = 100;
            surface.Pixels[(i * 4) + 2] = 100;
            surface.Pixels[(i * 4) + 3] = 255;
        }

        surface.MarkDirty();
        return vm;
    }

    private static int AlphaAt(RasterSurface s, int x, int y) => s.GetPixel(x, y).A;

    [Fact]
    public void GaussianBlurSheet_PreviewThenCommit_ChangesPixelsAndUndoes()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.GaussianBlur));
        Assert.Equal(EditorViewModel.FilterMenuKind.GaussianBlur, vm.OpenFilter);
        Assert.NotNull(vm.AdjustmentPreviewSurface); // radius 1 is not identity, so a preview exists
        Assert.Equal(original, surface.Pixels);      // the layer itself is untouched until commit

        vm.SetFilterState(vm.FilterState with { Radius = 4 });
        var preview = vm.AdjustmentPreviewSurface!;
        Assert.False(preview.Pixels.AsSpan().SequenceEqual(original));

        Assert.True(vm.CommitFilter());
        Assert.Null(vm.OpenFilter);

        // Premultiplied blur over a uniform color cannot shift that color, so the only thing a
        // blur can move here is coverage: full in the middle, faded at the canvas border.
        var middle = surface.GetPixel(surface.Width / 2, surface.Height / 2);
        Assert.Equal((byte)100, middle.R);
        Assert.Equal((byte)255, middle.A);
        Assert.True(AlphaAt(surface, 0, surface.Height / 2) < 255);
        Assert.Equal((byte)100, surface.GetPixel(0, surface.Height / 2).R);

        vm.Undo();
        Assert.Equal(original, surface.Pixels);
        vm.Redo();
        Assert.False(surface.Pixels.AsSpan().SequenceEqual(original));
    }

    [Fact]
    public void NoiseSheet_KeepsOneSeedWhileTheSliderMoves()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.AddNoise));
        var firstSeed = vm.FilterSeed;

        vm.SetFilterState(vm.FilterState with { Amount = 40 });
        var a = vm.AdjustmentPreviewSurface!;
        vm.SetFilterState(vm.FilterState with { Amount = 80 });
        var b = vm.AdjustmentPreviewSurface!;

        Assert.False(a.Pixels.AsSpan().SequenceEqual(b.Pixels)); // strength really changed
        Assert.Equal(firstSeed, vm.FilterSeed); // and the pattern did not re-roll under the slider
    }

    [Fact]
    public void EachSheetOpen_DrawsItsOwnNoiseSeed()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.AddNoise));
        var first = vm.FilterSeed;
        Assert.True(vm.CancelFilter());

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.AddNoise));
        Assert.NotEqual(first, vm.FilterSeed); // a re-run is a new application, as upstream
    }

    [Fact]
    public void FilterSheet_CancelLeavesPixelsAndHistoryAlone()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();
        var undoableBefore = vm.CanUndo;

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.MotionBlur));
        vm.SetFilterState(vm.FilterState with { Angle = 45, Distance = 60 });
        Assert.NotNull(vm.AdjustmentPreviewSurface);

        Assert.True(vm.CancelFilter());
        Assert.Null(vm.OpenFilter);
        Assert.Null(vm.AdjustmentPreviewSurface);
        Assert.Equal(original, surface.Pixels);
        Assert.Equal(undoableBefore, vm.CanUndo);
    }

    [Fact]
    public void FilterAndAdjustmentSheets_AreMutuallyExclusive()
    {
        // Upstream keeps exactly one of these in flight; committing through two previews at once
        // would be the sort of bug that only shows up as silently wrong pixels.
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        Assert.False(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.GaussianBlur));
        Assert.True(vm.CancelAdjustment());

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.AddNoise));
        Assert.False(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Curves));
        Assert.False(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.LensCorrection));
        Assert.True(vm.CancelFilter());
    }

    [Fact]
    public void FilterSheet_OnLockedLayer_IsRefused()
    {
        var vm = VmWithGrayLayer();
        vm.ActiveLayer!.IsLocked = true;
        Assert.False(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.GaussianBlur));
        Assert.Null(vm.OpenFilter);
    }

    [Fact]
    public void CommitFilter_CostsExactlyOneUndoStep()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();
        var originalSurface = new RasterSurface(surface.Width, surface.Height, original);
        var depthBefore = vm.HistoryDepth;

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.LensCorrection));
        vm.SetFilterState(vm.FilterState with { Distortion = -80 });
        Assert.True(vm.CommitFilter());
        Assert.Equal(depthBefore + 1, vm.HistoryDepth);
        Assert.True(AlphaAt(surface, 0, 0) < AlphaAt(originalSurface, 0, 0));

        vm.Undo();
        Assert.Equal(depthBefore, vm.HistoryDepth);
        Assert.Equal(original, surface.Pixels);
        Assert.Equal((byte)255, AlphaAt(surface, 0, 0));
    }

    [Fact]
    public void GrainRunsThroughTheAdjustmentSheetAndUndoes()
    {
        // Grain is an image adjustment upstream, not a Filter-menu entry, so it shares that path.
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();

        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Grain));
        Assert.NotNull(vm.AdjustmentPreviewSurface); // upstream's default amount is 25, not zero

        vm.SetGrainState(vm.GrainState with { Amount = 80, Size = 3, Roughness = 25 });
        Assert.True(vm.CommitAdjustment());
        Assert.False(surface.Pixels.AsSpan().SequenceEqual(original));

        vm.Undo();
        Assert.Equal(original, surface.Pixels);
    }

    [Fact]
    public void GrainWithZeroAmountIsIdentityAndWritesNoHistory()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();

        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Grain));
        vm.SetGrainState(vm.GrainState with { Amount = 0 });
        Assert.Null(vm.AdjustmentPreviewSurface);
        Assert.True(vm.CommitAdjustment());
        Assert.Equal(original, surface.Pixels);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void StraighteningByZeroWritesNoHistoryStep()
    {
        // Every other filter is clamped away from doing nothing, but Remove Distortion has a
        // legitimate zero. Committing it must close the sheet, not add a blank undo step.
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;
        var original = (byte[])surface.Pixels.Clone();

        Assert.True(vm.OpenFilterSheet(EditorViewModel.FilterMenuKind.LensCorrection));
        vm.SetFilterState(vm.FilterState with { Distortion = 0 });
        Assert.Null(vm.AdjustmentPreviewSurface);
        Assert.True(vm.CommitFilter());
        Assert.Null(vm.OpenFilter);
        Assert.Equal(original, surface.Pixels);
        Assert.False(vm.CanUndo);
    }
}
