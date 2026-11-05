using SnapDesk.Core;

namespace SnapDesk.Tests;

public class HistoryTests
{
    [Fact]
    public void Push_ThenUndo_ReturnsPreviousState()
    {
        var history = new History<string>();
        history.Push("bos");
        history.Push("kutu");

        Assert.True(history.CanUndo);
        Assert.Equal("kutu", history.Undo("kutu+ok"));
        Assert.Equal("bos", history.Undo("kutu"));
    }

    [Fact]
    public void Undo_ThenRedo_RestoresForwardState()
    {
        var history = new History<string>();
        history.Push("bos");
        history.Push("kutu");
        var undone = history.Undo("kutu+ok");

        Assert.Equal("kutu", undone);
        Assert.True(history.CanRedo);
        Assert.Equal("kutu+ok", history.Redo("bos"));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NewChange_ClearsRedoStack()
    {
        var history = new History<string>();
        history.Push("a");
        history.Undo("b");
        Assert.True(history.CanRedo);

        history.Push("c");

        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void Capacity_DropsOldestEntries()
    {
        var history = new History<string>(3);
        history.Push("1");
        history.Push("2");
        history.Push("3");
        history.Push("4");

        Assert.Equal(3, history.UndoCount);
        Assert.Equal("4", history.Undo("x"));
        Assert.Equal("3", history.Undo("4"));
        Assert.Equal("2", history.Undo("3"));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_OnEmpty_ReturnsNull()
    {
        var history = new History<string>();
        Assert.Null(history.Undo("x"));
        Assert.Null(history.Redo("x"));
    }
}
