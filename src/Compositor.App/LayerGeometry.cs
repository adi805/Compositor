using Avalonia;

namespace Compositor.App;

/// <summary>
/// Screen-space geometry for drawing a layer, kept apart from the control itself so it can
/// be tested without bringing up a UI thread. The mapping matches
/// <see cref="Compositor.Core.Imaging.LayerPlacement"/>: positive angles turn clockwise with
/// y growing downwards, around the centre of the layer's placement rect.
/// </summary>
public static class LayerGeometry
{
    /// <summary>
    /// Rotation about an arbitrary point, built from the origin-centred primitives Avalonia
    /// ships. <c>Append</c> returns a new matrix rather than mutating, so each step is
    /// assigned; forgetting that silently leaves a pure translation.
    /// </summary>
    public static Matrix RotationAbout(double degrees, Point center)
    {
        var matrix = Matrix.CreateTranslation(-center.X, -center.Y);
        matrix = matrix.Append(Matrix.CreateRotation(degrees * Math.PI / 180.0));
        matrix = matrix.Append(Matrix.CreateTranslation(center.X, center.Y));
        return matrix;
    }
}
