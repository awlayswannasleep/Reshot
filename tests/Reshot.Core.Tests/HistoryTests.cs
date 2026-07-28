using Reshot.Core.History;
using Xunit;

namespace Reshot.Core.Tests;

public class HistoryTests
{
    private sealed class Counter : IUndoableCommand
    {
        private readonly Action _undo;
        private readonly Action _redo;
        public Counter(Action undo, Action redo) { _undo = undo; _redo = redo; }
        public void Undo() => _undo();
        public void Redo() => _redo();
    }

    [Fact]
    public void Undo_and_redo_roundtrip()
    {
        var history = new UndoHistory();
        var value = 0;
        history.Push(new Counter(() => value--, () => value++));
        value = 1; // simulate the initial apply

        Assert.True(history.CanUndo);
        Assert.True(history.Undo());
        Assert.Equal(0, value);
        Assert.True(history.CanRedo);
        Assert.True(history.Redo());
        Assert.Equal(1, value);
    }

    [Fact]
    public void Pushing_clears_redo()
    {
        var history = new UndoHistory();
        history.Push(new Counter(() => { }, () => { }));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Push(new Counter(() => { }, () => { }));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Caps_at_32_steps()
    {
        var history = new UndoHistory();
        var undos = 0;
        for (var i = 0; i < 40; i++)
            history.Push(new Counter(() => undos++, () => { }));

        var count = 0;
        while (history.Undo())
            count++;

        Assert.Equal(32, count);
        Assert.Equal(32, undos);
    }

    [Fact]
    public void Undo_on_empty_returns_false()
    {
        var history = new UndoHistory();
        Assert.False(history.Undo());
        Assert.False(history.Redo());
    }
}
