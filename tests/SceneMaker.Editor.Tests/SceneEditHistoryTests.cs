using SceneMaker.Core;
using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class SceneEditHistoryTests
{
    [Fact]
    public void AFreshHistoryHoldsOneSavedState()
    {
        var history = new SceneEditHistory(Scene(1));

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.IsDirty);
        Assert.Equal(0, history.UndoDepth);
        Assert.Equal(1, history.Current.SizeCells.Width);
    }

    [Fact]
    public void PushingAnEditMakesItUndoableAndDirty()
    {
        var history = new SceneEditHistory(Scene(1));

        history.Push(Scene(2));

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.IsDirty);
        Assert.Equal(2, history.Current.SizeCells.Width);
    }

    [Fact]
    public void UndoAndRedoWalkTheRecordedStates()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2));
        history.Push(Scene(3));

        Assert.Equal(2, history.Undo().SizeCells.Width);
        Assert.Equal(1, history.Undo().SizeCells.Width);
        Assert.False(history.CanUndo);
        Assert.Equal(2, history.RedoDepth);

        Assert.Equal(2, history.Redo().SizeCells.Width);
        Assert.Equal(3, history.Redo().SizeCells.Width);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoAndRedoAtTheEndsAreRejected()
    {
        var history = new SceneEditHistory(Scene(1));

        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Throws<InvalidOperationException>(() => history.Redo());
    }

    [Fact]
    public void EditingAfterAnUndoDiscardsTheRedoTail()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2));
        history.Push(Scene(3));
        history.Undo();

        history.Push(Scene(4));

        Assert.False(history.CanRedo);
        Assert.Equal(4, history.Current.SizeCells.Width);
        Assert.Equal(2, history.Undo().SizeCells.Width);
    }

    [Fact]
    public void SavingClearsTheDirtyFlagAndUndoingRaisesItAgain()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2));

        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void OneStrokeCollapsesIntoOneUndoStep()
    {
        var history = new SceneEditHistory(Scene(1));

        history.Push(Scene(2), strokeKey: "terrain");
        history.Push(Scene(3), strokeKey: "terrain");
        history.Push(Scene(4), strokeKey: "terrain");

        Assert.Equal(4, history.Current.SizeCells.Width);
        Assert.Equal(1, history.UndoDepth);
        Assert.Equal(1, history.Undo().SizeCells.Width);
    }

    [Fact]
    public void ReleasingThePointerStartsANewUndoStep()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2), strokeKey: "terrain");
        history.BreakStroke();

        history.Push(Scene(3), strokeKey: "terrain");

        Assert.Equal(2, history.UndoDepth);
        Assert.Equal(2, history.Undo().SizeCells.Width);
        Assert.Equal(1, history.Undo().SizeCells.Width);
    }

    [Fact]
    public void ADifferentStrokeKeyStartsANewUndoStep()
    {
        var history = new SceneEditHistory(Scene(1));

        history.Push(Scene(2), strokeKey: "terrain");
        history.Push(Scene(3), strokeKey: "prop-erase");

        Assert.Equal(2, history.UndoDepth);
    }

    [Fact]
    public void UndoEndsTheOpenStrokeSoTheNextEditDoesNotOverwriteIt()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2), strokeKey: "terrain");
        history.Push(Scene(3), strokeKey: "terrain");
        history.Undo();

        history.Push(Scene(4), strokeKey: "terrain");

        Assert.Equal(4, history.Current.SizeCells.Width);
        Assert.Equal(1, history.Undo().SizeCells.Width);
    }

    [Fact]
    public void ReachingCapacityDropsTheOldestStates()
    {
        var history = new SceneEditHistory(Scene(1), capacity: 3);

        history.Push(Scene(2));
        history.Push(Scene(3));
        history.Push(Scene(4));

        Assert.Equal(4, history.Current.SizeCells.Width);
        Assert.Equal(2, history.UndoDepth);
        Assert.Equal(3, history.Undo().SizeCells.Width);
        Assert.Equal(2, history.Undo().SizeCells.Width);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ASavedStateTrimmedOutOfTheHistoryCountsAsDirty()
    {
        var history = new SceneEditHistory(Scene(1), capacity: 3);
        history.MarkSaved();

        history.Push(Scene(2));
        history.Push(Scene(3));
        history.Push(Scene(4));

        Assert.True(history.IsDirty);
    }

    [Fact]
    public void ResetStartsOverFromASavedState()
    {
        var history = new SceneEditHistory(Scene(1));
        history.Push(Scene(2), strokeKey: "terrain");

        history.Reset(Scene(9));

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.IsDirty);
        Assert.Equal(9, history.Current.SizeCells.Width);
    }

    [Fact]
    public void CapacityBelowTwoIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SceneEditHistory(Scene(1), capacity: 1));
    }

    private static SceneDocument Scene(int widthCells) =>
        SceneDocument.Create("scene", widthCells, 1);
}
