using Compositor.Core;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Transform box geometry and drag maths. Every expected number here is derived by hand from the upstream
/// formulas (<c>LayerTransform.point</c>, <c>handles</c>, <c>TransformDrag.updated</c>), never recorded from
/// a run.
/// </summary>
public class TransformHandlesTests
{
    // A 200x100 box with its top-left at (100,100), so its centre is (200,150).
    private static readonly LayerTransform Box = new(100, 100, 200, 100, 0, false, false);

    [Theory]
    [InlineData(0.0, 0.0, 100, 100)]
    [InlineData(0.5, 0.0, 200, 100)]
    [InlineData(1.0, 0.0, 300, 100)]
    [InlineData(1.0, 0.5, 300, 150)]
    [InlineData(1.0, 1.0, 300, 200)]
    [InlineData(0.5, 1.0, 200, 200)]
    [InlineData(0.0, 1.0, 100, 200)]
    [InlineData(0.0, 0.5, 100, 150)]
    [InlineData(0.5, 0.5, 200, 150)]
    public void Point_MapsTheUnitSquareIntoDocumentPixels(double ux, double uy, double ex, double ey)
    {
        var (x, y) = Box.Point(ux, uy);
        Assert.Equal(ex, x, 6);
        Assert.Equal(ey, y, 6);
    }

    [Fact]
    public void Point_OnARotatedBox_SpinsAboutTheCentre()
    {
        // 90 degrees: cos 0, sin 1. Unit (1,0) is 100 right and 50 up of the centre before the turn, so it
        // lands 50 right and 100 down of it after: (200+50, 150+100).
        var turned = Box with { RotationDegrees = 90 };

        var (x, y) = turned.Point(1.0, 0.0);

        Assert.Equal(250, x, 6);
        Assert.Equal(250, y, 6);
    }

    [Fact]
    public void Points_AreTheEightCornersAndEdgeMidpoints()
    {
        var points = TransformHandles.Points(Box);

        Assert.Equal(8, points.Length);
        Assert.Equal((100, 100), (points[0].X, points[0].Y));
        Assert.Equal((200, 100), (points[1].X, points[1].Y));
        Assert.Equal((300, 100), (points[2].X, points[2].Y));
        Assert.Equal((300, 150), (points[3].X, points[3].Y));
        Assert.Equal((300, 200), (points[4].X, points[4].Y));
        Assert.Equal((200, 200), (points[5].X, points[5].Y));
        Assert.Equal((100, 200), (points[6].X, points[6].Y));
        Assert.Equal((100, 150), (points[7].X, points[7].Y));
    }

    [Fact]
    public void RotationHandle_SitsAboveTheTopMiddleHandle()
    {
        var (x, y) = TransformHandles.RotationHandle(Box, offset: 28);

        Assert.Equal(200, x, 6);
        Assert.Equal(72, y, 6);
    }

    [Fact]
    public void RotationHandle_FollowsTheBoxRotation()
    {
        // The top-middle handle itself turns with the box: at 90 degrees Point(0.5, 0) is (250,150), not
        // (200,100). The handle then sits 28 px along the box's outward normal, which now points right.
        var turned = Box with { RotationDegrees = 90 };

        var (handleX, handleY) = turned.Point(0.5, 0.0);
        Assert.Equal(250, handleX, 6);
        Assert.Equal(150, handleY, 6);

        var (x, y) = TransformHandles.RotationHandle(turned, offset: 28);

        Assert.Equal(278, x, 6);
        Assert.Equal(150, y, 6);
    }

    [Fact]
    public void Hit_OnACorner_ReturnsThatCorner()
    {
        var hit = TransformHandles.Hit(Box, 300, 200, radius: 10, rotationOffset: 28);

        Assert.Equal(TransformDragMode.Resize, hit.Mode);
        Assert.Equal(4, hit.Index);
    }

    [Fact]
    public void Hit_OnTheRotationHandle_ReturnsRotate()
    {
        var hit = TransformHandles.Hit(Box, 200, 72, radius: 10, rotationOffset: 28);

        Assert.Equal(TransformDragMode.Rotate, hit.Mode);
    }

    [Fact]
    public void Hit_OnAnEdgeAwayFromTheMidpoint_StillResizesThatEdge()
    {
        // (250,100) is 75% along the top edge, nowhere near the midpoint square at (200,100).
        var hit = TransformHandles.Hit(Box, 250, 100, radius: 10, rotationOffset: 28);

        Assert.Equal(TransformDragMode.Resize, hit.Mode);
        Assert.Equal(1, hit.Index);
    }

    [Fact]
    public void Hit_FarFromTheBox_ReturnsNone()
    {
        Assert.True(TransformHandles.Hit(Box, 500, 500, radius: 10, rotationOffset: 28).IsNone);
    }

    [Fact]
    public void Hit_UsesTheRadiusItIsGiven()
    {
        // 15 px from the top-left corner: out of reach at radius 10, in reach at radius 20.
        Assert.True(TransformHandles.Hit(Box, 85, 100, radius: 10, rotationOffset: 28).IsNone);
        Assert.Equal(0, TransformHandles.Hit(Box, 85, 100, radius: 20, rotationOffset: 28).Index);
    }

    [Fact]
    public void Hit_OnARotatedBox_FindsTheRotatedCorner()
    {
        // At 90 degrees the top-left corner sits at (250,50), not (100,100).
        var turned = Box with { RotationDegrees = 90 };

        var hit = TransformHandles.Hit(turned, 250, 50, radius: 10, rotationOffset: 28);

        Assert.Equal(TransformDragMode.Resize, hit.Mode);
        Assert.Equal(0, hit.Index);
    }
}

/// <summary>
/// Drag maths. Hand-derived: a 200x100 box at (100,100), grabbed by a known handle, dragged a known delta.
/// </summary>
public class TransformDragTests
{
    private static readonly LayerTransform Box = new(100, 100, 200, 100, 0, false, false);

    [Fact]
    public void Move_ShiftsTheOriginByThePointerDelta()
    {
        // Grabbed at (300,200), dragged to (350,220): +50 right, +20 down.
        var moved = TransformDrag.Move(Box, startX: 300, startY: 200, pointX: 350, pointY: 220);

        Assert.Equal(150, moved.OriginX, 6);
        Assert.Equal(120, moved.OriginY, 6);
        Assert.Equal(200, moved.Width, 6);
        Assert.Equal(100, moved.Height, 6);
    }

    [Fact]
    public void Move_WithShift_KeepsTheDominantAxisOnly()
    {
        var moved = TransformDrag.Move(Box, 300, 200, 350, 220, shift: true);

        Assert.Equal(150, moved.OriginX, 6);
        Assert.Equal(100, moved.OriginY, 6);   // the smaller axis is dropped
    }

    [Fact]
    public void Resize_FromTheBottomRightCorner_LeavesTheOppositeCornerExactlyWhereItWas()
    {
        // Grab handle 4 (bottom-right, at 300,200) and drag +50 right, +20 down. The top-left corner is the
        // anchor, so it must not move and the size must grow by exactly the delta.
        var resized = TransformDrag.Resize(Box, 4, startX: 300, startY: 200, pointX: 350, pointY: 220);

        Assert.Equal(100, resized.OriginX, 6);
        Assert.Equal(100, resized.OriginY, 6);
        Assert.Equal(250, resized.Width, 6);
        Assert.Equal(120, resized.Height, 6);

        var (anchorX, anchorY) = resized.Point(0.0, 0.0);
        Assert.Equal(100, anchorX, 6);
        Assert.Equal(100, anchorY, 6);
    }

    [Fact]
    public void Resize_FromTheTopLeftCorner_LeavesTheBottomRightWhereItWas()
    {
        // Handle 0 is the top-left; its anchor is the bottom-right at (300,200).
        var resized = TransformDrag.Resize(Box, 0, startX: 100, startY: 100, pointX: 120, pointY: 130);

        Assert.Equal(120, resized.OriginX, 6);
        Assert.Equal(130, resized.OriginY, 6);
        Assert.Equal(180, resized.Width, 6);
        Assert.Equal(70, resized.Height, 6);

        var (anchorX, anchorY) = resized.Point(1.0, 1.0);
        Assert.Equal(300, anchorX, 6);
        Assert.Equal(200, anchorY, 6);
    }

    [Fact]
    public void Resize_FromCentre_KeepsTheCentreAndGrowsBothSides()
    {
        // Same grab and delta as the corner case, but anchored on the centre: each side grows by the delta and
        // the centre stays at (200,150).
        var resized = TransformDrag.Resize(
            Box, 4, startX: 300, startY: 200, pointX: 350, pointY: 220, fromCenter: true);

        Assert.Equal(50, resized.OriginX, 6);
        Assert.Equal(80, resized.OriginY, 6);
        Assert.Equal(300, resized.Width, 6);
        Assert.Equal(140, resized.Height, 6);
        Assert.Equal(200, resized.CenterX, 6);
        Assert.Equal(150, resized.CenterY, 6);
    }

    [Fact]
    public void Resize_FromTheTopEdge_LeavesTheBottomEdgeWhereItWas()
    {
        // Handle 1 is the top edge; its anchor is the bottom edge at y = 200. Dragged up by 20.
        var resized = TransformDrag.Resize(Box, 1, startX: 200, startY: 100, pointX: 200, pointY: 80);

        Assert.Equal(100, resized.OriginX, 6);
        Assert.Equal(80, resized.OriginY, 6);
        Assert.Equal(200, resized.Width, 6);   // an edge handle leaves the other axis alone
        Assert.Equal(120, resized.Height, 6);
        Assert.Equal(200, resized.OriginY + resized.Height, 6);
    }

    [Fact]
    public void Resize_PastTheOppositeSide_TurnsTheBoxOverInsteadOfStopping()
    {
        // Drag the bottom-right handle 250 px left of its start: 200 past the anchor, so the box ends up on the
        // other side of it with a positive 50 px width and the horizontal axis flipped.
        var resized = TransformDrag.Resize(Box, 4, startX: 300, startY: 200, pointX: 50, pointY: 200);

        Assert.Equal(50, resized.Width, 6);
        Assert.Equal(100, resized.Height, 6);
        Assert.True(resized.FlipH);
        Assert.False(resized.FlipV);
        Assert.Equal(50, resized.OriginX, 6);          // the box now lies left of the anchor
        Assert.Equal(100, resized.OriginX + resized.Width, 6);   // the anchor is its right edge
    }

    [Fact]
    public void Resize_WithAnOutOfRangeHandle_ReturnsTheOriginal()
        => Assert.Equal(Box, TransformDrag.Resize(Box, 99, 0, 0, 10, 10));

    [Fact]
    public void Resize_WithANonFinitePointer_ReturnsTheOriginal()
    {
        var resized = TransformDrag.Resize(Box, 4, startX: 300, startY: 200, pointX: double.NaN, pointY: 220);

        Assert.Equal(Box, resized);
    }

    [Fact]
    public void Rotate_TurnsByTheAngleSweptFromTheCentre()
    {
        // Start due right of the centre (angle 0), end due above it (angle -90).
        var rotated = TransformDrag.Rotate(Box, startX: 300, startY: 150, pointX: 200, pointY: 50);

        Assert.Equal(-90, rotated.RotationDegrees, 6);
    }

    [Fact]
    public void Rotate_WithShift_SnapsToFifteenDegrees()
    {
        // A point at 100 degrees from the centre: 100 / 15 = 6.67, which rounds away from zero to 7 -> 105.
        var radius = 100.0;
        var pointX = 200 + Math.Cos(100 * Math.PI / 180) * radius;
        var pointY = 150 + Math.Sin(100 * Math.PI / 180) * radius;

        var rotated = TransformDrag.Rotate(Box, 300, 150, pointX, pointY, shift: true);

        Assert.Equal(105, rotated.RotationDegrees, 6);
    }

    [Fact]
    public void Resize_OnARotatedBox_MovesTheHandleAlongTheRotatedAxis()
    {
        // Turned 90 degrees, the box's own width now runs down the screen. Dragging the bottom-right handle
        // 50 px further down grows the width by 50, not the height.
        var turned = Box with { RotationDegrees = 90 };
        var handle = turned.Point(1.0, 1.0);

        var resized = TransformDrag.Resize(turned, 4, handle.X, handle.Y, handle.X, handle.Y + 50);

        Assert.Equal(250, resized.Width, 6);
        Assert.Equal(100, resized.Height, 6);
        Assert.Equal(90, resized.RotationDegrees, 6);
    }
}
