using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>Brush v2 view-model flow: diameter/hardness/opacity/eraser through strokes.</summary>
public class EditorViewModelBrushV2Tests
{
    private static EditorViewModel VmWithLayer()
    {
        var vm = new EditorViewModel(new Compositor.Core.Document(64, 64));
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new Compositor.Core.RasterSurface(64, 64);
        return vm;
    }

    [Fact]
    public void BrushDefaults_MatchUpstream()
    {
        var vm = new EditorViewModel();
        Assert.Equal(40f, vm.BrushDiameter);   // upstream BrushSettings.diameter
        Assert.Equal(1f, vm.BrushHardness);    // upstream hardness
        Assert.Equal(1f, vm.BrushOpacity);
        Assert.False(vm.IsErasing);
    }

    [Fact]
    public void Stroke_WithOpacityCap_SaturatesToCap()
    {
        var vm = VmWithLayer();
        vm.BrushDiameter = 12;
        vm.BrushOpacity = 0.5f;
        vm.SetBrushColor(0, 0, 255);

        Assert.True(vm.BeginStroke(20, 20));
        vm.ContinueStroke(40, 22);
        Assert.True(vm.EndStroke());

        var (_, _, _, a) = vm.ActiveLayer!.Pixels!.GetPixel(30, 21);
        Assert.InRange(a, 126, 130); // cap exact: 255 * 0.5
    }

    [Fact]
    public void SoftStroke_HardnessHalf_BlendsPartially()
    {
        var vm = VmWithLayer();
        vm.BrushDiameter = 30;
        vm.BrushHardness = 0.2f;

        Assert.True(vm.BeginStroke(32, 32));
        Assert.True(vm.EndStroke());

        // Center: full coverage; just outside the hardness edge: partial falloff.
        Assert.Equal(255, vm.ActiveLayer!.Pixels!.GetPixel(32, 32).A);
        var (_, _, _, edge) = vm.ActiveLayer.Pixels.GetPixel(32, 44); // dist 12 of radius 15
        Assert.InRange(edge, 1, 254);
    }

    [Fact]
    public void EraserStroke_ClearsPaintedLayer()
    {
        var vm = VmWithLayer();
        // Paint first.
        vm.SetBrushColor(255, 0, 0);
        Assert.True(vm.BeginStroke(32, 32));
        Assert.True(vm.EndStroke());
        Assert.Equal(255, vm.ActiveLayer!.Pixels!.GetPixel(32, 32).A);

        // Erase the same spot.
        vm.IsErasing = true;
        Assert.True(vm.BeginStroke(32, 32));
        Assert.True(vm.EndStroke());

        var (_, _, _, a) = vm.ActiveLayer.Pixels.GetPixel(32, 32);
        Assert.Equal(0, a);
    }

    [Fact]
    public void EraserStroke_Undone()
    {
        var vm = VmWithLayer();
        vm.SetBrushColor(0, 255, 0);
        Assert.True(vm.BeginStroke(32, 32));
        Assert.True(vm.EndStroke());

        vm.IsErasing = true;
        Assert.True(vm.BeginStroke(32, 32));
        Assert.True(vm.EndStroke());
        Assert.Equal(0, vm.ActiveLayer!.Pixels!.GetPixel(32, 32).A);

        vm.Undo();
        Assert.Equal(255, vm.ActiveLayer.Pixels.GetPixel(32, 32).A);
    }

    [Fact]
    public void ClickDot_SinglePoint_StillPaints()
    {
        var vm = VmWithLayer();
        vm.SetBrushColor(0, 0, 0);
        Assert.True(vm.BeginStroke(32, 32)); // no movement
        Assert.True(vm.EndStroke());

        Assert.Equal(255, vm.ActiveLayer!.Pixels!.GetPixel(32, 32).A);
    }
}
