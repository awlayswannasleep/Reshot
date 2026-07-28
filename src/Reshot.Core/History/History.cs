namespace Reshot.Core.History;

/// <summary>A reversible edit (ARCHITECTURE §5).</summary>
public interface IUndoableCommand
{
    void Undo();
    void Redo();
}

/// <summary>
/// Bounded undo/redo stack (SPEC §11: 32 steps). Pushing a new command clears the
/// redo stack, as usual. Commands own their own before/after state.
/// </summary>
public sealed class UndoHistory
{
    private const int MaxDepth = 32;

    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoableCommand command)
    {
        _undo.AddLast(command);
        while (_undo.Count > MaxDepth)
            _undo.RemoveFirst();
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_undo.Last is not { } node)
            return false;

        _undo.RemoveLast();
        node.Value.Undo();
        _redo.Push(node.Value);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        var command = _redo.Pop();
        command.Redo();
        _undo.AddLast(command);
        return true;
    }
}
