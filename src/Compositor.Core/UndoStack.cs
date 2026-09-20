namespace Compositor.Core;

/// <summary>
/// Undo/redo for document mutations via the command pattern.
/// Every document change in the app goes through here so Ctrl+Z/Ctrl+Y is
/// consistent across tools.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoCommand> _undo = new();
    private readonly Stack<IUndoCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undo.Pop();
        cmd.Undo();
        _redo.Push(cmd);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redo.Pop();
        cmd.Redo();
        _undo.Push(cmd);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public interface IUndoCommand
{
    void Undo();
    void Redo();
}
