using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class EditorInteractionStateTests
{
    [Fact]
    public void FreshStateStartsOnTerrainWithItsDefaultTool()
    {
        var state = new EditorInteractionState();

        Assert.Equal(EditorMode.Terrain, state.Mode);
        Assert.Equal(EditorTool.Pencil, state.ActiveTool);
        Assert.False(state.EraserEnabled);
        Assert.Equal(0, state.PropLineOffsetAuthoringPixels);
    }

    [Fact]
    public void EveryModeRemembersItsOwnLastTool()
    {
        var state = new EditorInteractionState();
        state.SelectMode(EditorMode.Terrain);
        state.SelectTool(EditorTool.Fill);

        state.SelectMode(EditorMode.Props);
        Assert.Equal(EditorTool.Pencil, state.ActiveTool);
        state.SelectTool(EditorTool.Selector);

        state.SelectMode(EditorMode.Terrain);
        Assert.Equal(EditorTool.Fill, state.ActiveTool);

        state.SelectMode(EditorMode.Props);
        Assert.Equal(EditorTool.Selector, state.ActiveTool);
    }

    [Fact]
    public void SelectingAToolTheModeDoesNotSupportIsRejected()
    {
        var state = new EditorInteractionState();
        state.SelectMode(EditorMode.Props);

        Assert.Throws<InvalidOperationException>(() => state.SelectTool(EditorTool.Fill));
        Assert.Equal(EditorTool.Pencil, state.ActiveTool);
    }

    [Fact]
    public void EraserIsAnIndependentStateAcrossModes()
    {
        var state = new EditorInteractionState();
        state.SetEraserEnabled(true);

        state.SelectMode(EditorMode.Props);
        Assert.True(state.EraserEnabled);

        state.SetEraserEnabled(false);
        state.SelectMode(EditorMode.Terrain);
        Assert.False(state.EraserEnabled);
    }

    [Fact]
    public void PropLineOffsetAcceptsZeroAndRejectsNegativeValues()
    {
        var state = new EditorInteractionState();

        state.SetPropLineOffset(0);
        Assert.Equal(0, state.PropLineOffsetAuthoringPixels);

        state.SetPropLineOffset(12);
        Assert.Equal(12, state.PropLineOffsetAuthoringPixels);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetPropLineOffset(-1));
        Assert.Equal(12, state.PropLineOffsetAuthoringPixels);
    }
}
