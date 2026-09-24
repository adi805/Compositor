namespace Compositor.Core;

/// <summary>
/// Turning a pointer drag into a new <see cref="LayerTransform"/>. Ported from
/// <c>Document/LayerTransform.swift</c> (<c>TransformDrag.updated(to:lockRatio:shift:option:)</c>).
/// </summary>
/// <remarks>
/// The maths is in the layer's own rotated frame, so a drag behaves the same however the layer is turned.
/// Distances are document pixels. A resize keeps the OPPOSITE corner (or edge) fixed, which is what upstream
/// does: the anchor is the handle mirrored through the centre, so dragging the bottom-right corner leaves the
/// top-left corner exactly where it was. Holding the centre option makes the centre the anchor instead.
/// </remarks>
public static class TransformDrag
{
    /// <summary>
    /// The layer moved so the point under the pointer follows it. <paramref name="shift"/> keeps the drag on
    /// whichever axis moved further.
    /// </summary>
    public static LayerTransform Move(
        LayerTransform original, double startX, double startY, double pointX, double pointY, bool shift = false)
    {
        var dx = pointX - startX;
        var dy = pointY - startY;
        if (shift)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }

        return original with { OriginX = original.OriginX + dx, OriginY = original.OriginY + dy };
    }

    /// <summary>
    /// The layer turned so the pointer's direction from the centre becomes the new angle.
    /// <paramref name="shift"/> snaps to 15 degree steps.
    /// </summary>
    public static LayerTransform Rotate(
        LayerTransform original, double startX, double startY, double pointX, double pointY, bool shift = false)
    {
        var delta = Math.Atan2(pointY - original.CenterY, pointX - original.CenterX)
                    - Math.Atan2(startY - original.CenterY, startX - original.CenterX);
        var rotation = original.RotationDegrees + delta * 180.0 / Math.PI;
        if (shift) rotation = Math.Round(rotation / 15, MidpointRounding.AwayFromZero) * 15;
        return original with { RotationDegrees = rotation };
    }

    /// <summary>
    /// The layer resized by dragging handle <paramref name="index"/>. The opposite corner (or, for an edge
    /// handle, the opposite edge) stays put. Dragging a handle past the opposite side turns the layer over
    /// rather than stopping at nothing: the size stays positive and that axis flips, as a negative scale would.
    /// </summary>
    /// <param name="lockRatio">
    /// Proportional scaling. Note the upstream quirk kept deliberately: proportional scaling happens when
    /// <paramref name="lockRatio"/> and <paramref name="shift"/> DIFFER, not when lockRatio alone is set, so
    /// holding shift overrides a locked ratio rather than enforcing one.
    /// </param>
    /// <param name="fromCenter">Anchor on the centre instead of the opposite corner (upstream's option key).</param>
    public static LayerTransform Resize(
        LayerTransform original,
        int index,
        double startX,
        double startY,
        double pointX,
        double pointY,
        bool lockRatio = false,
        bool shift = false,
        bool fromCenter = false)
    {
        if (index < 0 || index >= TransformHandles.Unit.Length) return original;

        var handle = TransformHandles.Unit[index];
        var (anchorUnitX, anchorUnitY) = fromCenter ? (0.5, 0.5) : (1 - handle.X, 1 - handle.Y);
        var anchor = original.Point(anchorUnitX, anchorUnitY);

        // Use the initial handle plus the pointer delta so grabbing a handle does not make it jump.
        var initialHandle = original.Point(handle.X, handle.Y);
        var dx = initialHandle.X + pointX - startX - anchor.X;
        var dy = initialHandle.Y + pointY - startY - anchor.Y;

        // Centre-to-handle distances cover half the size on each axis.
        var span = fromCenter ? 2.0 : 1.0;
        var cos = Math.Cos(original.Radians);
        var sin = Math.Sin(original.Radians);
        var localX = (dx * cos + dy * sin) * span;
        var localY = (-dx * sin + dy * cos) * span;

        var sx = handle.X * 2 - 1;
        var sy = handle.Y * 2 - 1;
        var rawWidth = sx == 0 ? original.Width : localX * sx;
        var rawHeight = sy == 0 ? original.Height : localY * sy;

        var mirroredX = rawWidth < 0;
        var mirroredY = rawHeight < 0;
        var width = Math.Max(1, Math.Abs(rawWidth));
        var height = Math.Max(1, Math.Abs(rawHeight));

        if (lockRatio != shift)
        {
            double factor;
            if (sx == 0)
            {
                factor = height / original.Height;
            }
            else if (sy == 0)
            {
                factor = width / original.Width;
            }
            else
            {
                // Project onto the original diagonal for proportional scaling.
                factor = Math.Max(
                    1 / Math.Min(original.Width, original.Height),
                    (localX * sx * original.Width + localY * sy * original.Height)
                    / (original.Width * original.Width + original.Height * original.Height));
            }

            width = original.Width * factor;
            height = original.Height * factor;
        }

        var result = original with
        {
            Width = width,
            Height = height,
            FlipH = mirroredX ? !original.FlipH : original.FlipH,
            FlipV = mirroredY ? !original.FlipV : original.FlipV,
        };

        // Turned over, the box lies on the other side of the anchor.
        var offsetX = (0.5 - anchorUnitX) * width * (mirroredX ? -1 : 1);
        var offsetY = (0.5 - anchorUnitY) * height * (mirroredY ? -1 : 1);
        var centerX = anchor.X + offsetX * cos - offsetY * sin;
        var centerY = anchor.Y + offsetX * sin + offsetY * cos;

        result = result with { OriginX = centerX - width / 2, OriginY = centerY - height / 2 };
        return result.IsValid ? result : original;
    }
}
