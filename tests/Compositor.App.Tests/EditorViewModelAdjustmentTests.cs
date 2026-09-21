using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>Adjustment sheet flow on the view-model: open → preview → commit/cancel.</summary>
public class EditorViewModelAdjustmentTests
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

    [Fact]
    public void LevelsSheet_PreviewThenCommit_ChangesPixelsAndUndoes()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;

        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        Assert.Equal(EditorViewModel.AdjustmentKind.Levels, vm.OpenAdjustment);
        Assert.Null(vm.AdjustmentPreviewSurface); // identity: no preview yet

        vm.LevelsState.Ranges[0] = new LevelRange(120, 1, 255, 0, 255);
        vm.UpdateAdjustmentPreview();
        Assert.NotNull(vm.AdjustmentPreviewSurface);

        // commit: gray 100 falls below the black point → 0
        Assert.True(vm.CommitAdjustment());
        Assert.Null(vm.OpenAdjustment);
        Assert.Equal(0, surface.Pixels[0]);

        // one undo restores the exact original buffer
        vm.Undo();
        Assert.Equal(100, surface.Pixels[0]);
    }

    [Fact]
    public void LevelsCommitAtIdentityCancelsInsteadOfWriting()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        Assert.True(vm.CommitAdjustment());
        Assert.Null(vm.OpenAdjustment);
        Assert.Equal(100, vm.ActiveLayer!.Pixels!.Pixels[0]);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void HueSatSheet_CancelLeavesPixelsUntouched()
    {
        var vm = VmWithGrayLayer();
        var before = (byte[])vm.ActiveLayer!.Pixels!.Pixels.Clone();

        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.HueSaturation));
        vm.HueSatState.Hue = 180;
        vm.UpdateAdjustmentPreview();
        Assert.NotNull(vm.AdjustmentPreviewSurface); // gray is unaffected by hue but preview exists

        Assert.True(vm.CancelAdjustment());
        Assert.Null(vm.OpenAdjustment);
        Assert.Null(vm.AdjustmentPreviewSurface);
        Assert.Equal(before, vm.ActiveLayer.Pixels.Pixels);
    }

    [Fact]
    public void CurvesSheet_CommitAppliesChannelCurve()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Curves));
        vm.CurvesState.Channels[0] = [new CurvePoint(0, 0), new CurvePoint(128, 255), new CurvePoint(255, 255)];
        vm.UpdateAdjustmentPreview();
        Assert.NotNull(vm.AdjustmentPreviewSurface);

        Assert.True(vm.CommitAdjustment());
        // Hermite at x=100 in segment [0..128]: ≈233 (rises to 255 only at x=128)
        Assert.Equal(233, vm.ActiveLayer!.Pixels!.Pixels[0]);
    }

    [Fact]
    public void ExposureSheet_InvalidOrIdentitySettingsAreHandled()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Exposure));
        Assert.Null(vm.AdjustmentPreviewSurface); // default exposure is identity

        vm.ExposureState.Exposure = 1;
        vm.UpdateAdjustmentPreview();
        Assert.NotNull(vm.AdjustmentPreviewSurface);

        Assert.True(vm.CommitAdjustment());
        // gray 100 → decode 0.1274, ×2 = 0.2548, encode ≈ 0.5418 → 138
        Assert.InRange(vm.ActiveLayer!.Pixels!.Pixels[0], (byte)137, (byte)139);
    }

    [Fact]
    public void GradientMapSheet_MapsLumaToPickers()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.GradientMap));
        vm.GradientMapState.Shadows = new AdjustmentColor(0, 0, 0);
        vm.GradientMapState.Highlights = new AdjustmentColor(1, 0, 0);
        vm.UpdateAdjustmentPreview();
        Assert.NotNull(vm.AdjustmentPreviewSurface);

        Assert.True(vm.CommitAdjustment());
        var p = vm.ActiveLayer!.Pixels!;
        Assert.Equal(100, p.Pixels[0]); // luma 100 → table[100] = lerp(black→red, 100/255) = 100
        Assert.Equal(0, p.Pixels[1]);
    }

    [Fact]
    public void SecondSheetRefusesWhileOneIsOpen()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        Assert.False(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Curves));
    }

    [Fact]
    public void ApplyInvertCommandsAndUndoes()
    {
        var vm = VmWithGrayLayer();
        var surface = vm.ActiveLayer!.Pixels!;

        Assert.True(vm.ApplyInvert());
        Assert.Equal(155, surface.Pixels[0]); // 255 − 100
        Assert.Equal(155, surface.Pixels[1]);

        vm.Undo();
        Assert.Equal(100, surface.Pixels[0]);
    }

    [Fact]
    public void InvertRefusesWhileSheetOpen()
    {
        var vm = VmWithGrayLayer();
        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        Assert.False(vm.ApplyInvert());
    }

    [Fact]
    public void SelectionConstrainsCommittedAdjustment()
    {
        var vm = VmWithGrayLayer();
        vm.Tool = EditorViewModel.EditorTool.RectangleSelect;
        Assert.True(vm.BeginMarquee(0, 0));
        vm.ContinueMarquee(1, vm.Doc.Height);
        vm.EndMarquee();
        Assert.NotNull(vm.Doc.Selection);

        Assert.True(vm.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels));
        vm.LevelsState.Ranges[0] = new LevelRange(120, 1, 255, 0, 255);
        vm.UpdateAdjustmentPreview();
        Assert.True(vm.CommitAdjustment());

        var surface = vm.ActiveLayer!.Pixels!;
        Assert.Equal(0, surface.Pixels[0]); // selected column → clipped to black
        Assert.Equal(100, surface.Pixels[((0 * vm.Doc.Width) + 2) * 4]); // outside → untouched
    }
}
