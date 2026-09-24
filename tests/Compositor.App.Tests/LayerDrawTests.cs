using Avalonia;
using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The canvas spins a layer with a matrix; the exporter spins it with the sampler in
/// Compositor.Core.Imaging.LayerPlacement. Those two have to turn the same way or a
/// rotated layer looks right on screen and comes out mirrored in the PNG, so the
/// expectations below are the same hand-derived ones the Core tests use.
/// </summary>
public class LayerDrawTests
{
    private static Point Spin(double degrees, Point about, Point point) =>
        LayerGeometry.RotationAbout(degrees, about).Transform(point);

    [Fact]
    public void ZeroDegrees_ChangesNothing()
    {
        var p = Spin(0, new Point(4, 4), new Point(1.25, 6));
        Assert.Equal(1.25, p.X, 6);
        Assert.Equal(6, p.Y, 6);
    }

    [Fact]
    public void PositiveAngles_TurnClockwiseOnScreen()
    {
        // y grows downwards here, exactly like document space, so clockwise means
        // the point on the +x axis ends up on the +y axis.
        var p = Spin(90, new Point(0, 0), new Point(1, 0));
        Assert.Equal(0, p.X, 6);
        Assert.Equal(1, p.Y, 6);
    }

    [Fact]
    public void ThePivot_StaysPut()
    {
        var p = Spin(37, new Point(4, 4), new Point(4, 4));
        Assert.Equal(4, p.X, 6);
        Assert.Equal(4, p.Y, 6);
    }

    [Fact]
    public void QuarterTurn_AboutTheCanvasCentre_MovesTheTopRightCornerToTheBottomRight()
    {
        // Core's Rotate90 test says a marker in the source's top-right corner of an 8x8
        // layer lands in the bottom-right. Same mapping, same pivot: the rect centre.
        var p = Spin(90, new Point(4, 4), new Point(7.5, 0.5));
        Assert.Equal(7.5, p.X, 6);
        Assert.Equal(7.5, p.Y, 6);
    }

    [Fact]
    public void HalfTurn_SendsTheTopLeftCornerToTheBottomRight()
    {
        var p = Spin(180, new Point(4, 4), new Point(0.5, 0.5));
        Assert.Equal(7.5, p.X, 6);
        Assert.Equal(7.5, p.Y, 6);
    }

    [Fact]
    public void NegativeAngle_TurnsTheOtherWay()
    {
        var p = Spin(-90, new Point(4, 4), new Point(7.5, 0.5));
        Assert.Equal(0.5, p.X, 6);
        Assert.Equal(0.5, p.Y, 6);
    }
}
