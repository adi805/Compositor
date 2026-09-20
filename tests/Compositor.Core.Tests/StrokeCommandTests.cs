using Compositor.Core;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>StrokeCommand: region-exact undo/redo for live-feedback strokes.</summary>
public sealed class StrokeCommandTests
{
    private static RasterSurface Surface() => new(64, 64);

    private static (float X, float Y)[] Path() =>
        [(10f, 10f), (40f, 40f)];

    private static StrokeCommand AppliedCommand(RasterSurface surface, out byte[] beforeFull)
    {
        beforeFull = (byte[])surface.Pixels.Clone();
        BrushStroke.Apply(surface, Path(), 6f, 255, 0, 0, 255, 1f); // live-painted
        return new StrokeCommand(
            surface, Path(), 6f, 255, 0, 0, 255, 1f, beforeFull, alreadyApplied: true);
    }

    [Fact]
    public void AppliedCommand_Undo_RestoresExactBeforeBytes()
    {
        var surface = Surface();
        var cmd = AppliedCommand(surface, out var beforeFull);
        Assert.NotEqual(beforeFull, surface.Pixels);

        cmd.Undo();

        Assert.Equal(beforeFull, surface.Pixels);
    }

    [Fact]
    public void AppliedCommand_RedoAfterUndo_RestoresPaintedBytes()
    {
        var surface = Surface();
        var cmd = AppliedCommand(surface, out _);
        var painted = (byte[])surface.Pixels.Clone();

        cmd.Undo();
        Assert.NotEqual(painted, surface.Pixels);

        cmd.Redo();
        Assert.Equal(painted, surface.Pixels);

        cmd.Redo(); // repeated redo stays idempotent
        Assert.Equal(painted, surface.Pixels);
    }

    [Fact]
    public void DeferredCommand_FirstRedo_AppliesStroke()
    {
        var surface = Surface();
        var before = (byte[])surface.Pixels.Clone();
        var cmd = new StrokeCommand(
            surface, Path(), 6f, 255, 0, 0, 255, 1f, before, alreadyApplied: false);

        cmd.Redo(); // deferred execution flow (e.g. UndoHistory.Push)

        Assert.NotEqual(before, surface.Pixels);
        cmd.Undo();
        Assert.Equal(before, surface.Pixels);
    }

    [Fact]
    public void Constructor_SnapshotLengthMismatch_Throws()
    {
        var surface = Surface();
        Assert.Throws<ArgumentException>(() => new StrokeCommand(
            surface, Path(), 6f, 255, 0, 0, 255, 1f, new byte[8], alreadyApplied: true));
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        var surface = Surface();
        var before = (byte[])surface.Pixels.Clone();
        Assert.Throws<ArgumentException>(() => new StrokeCommand(
            surface, [], 6f, 255, 0, 0, 255, 1f, before, alreadyApplied: true));
    }

    [Fact]
    public void Undo_IncrementsSurfaceVersion()
    {
        var surface = Surface();
        var cmd = AppliedCommand(surface, out _);
        var versionAfterPaint = surface.Version;

        cmd.Undo();

        Assert.True(surface.Version > versionAfterPaint);
    }
}
