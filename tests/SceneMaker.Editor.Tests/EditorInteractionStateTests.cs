using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
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

    /// <summary>
    /// Terrain and Mountain offer the same Assets and are still two choices:
    /// leaving one to draw a mountain and coming back should find the brush
    /// where it was left.
    /// </summary>
    [Fact]
    public void EveryAreaRemembersItsOwnTerrainAsset()
    {
        var state = new EditorInteractionState();
        state.SelectMode(EditorMode.Terrain);
        state.SelectTerrainAsset("grass");

        state.SelectMode(EditorMode.River);
        Assert.Null(state.SelectedTerrainAssetKey);
        state.SelectTerrainAsset("river");

        state.SelectMode(EditorMode.Mountain);
        Assert.Null(state.SelectedTerrainAssetKey);
        state.SelectTerrainAsset("sand");

        state.SelectMode(EditorMode.Terrain);
        Assert.Equal("grass", state.SelectedTerrainAssetKey);
        state.SelectMode(EditorMode.River);
        Assert.Equal("river", state.SelectedTerrainAssetKey);
        state.SelectMode(EditorMode.Mountain);
        Assert.Equal("sand", state.SelectedTerrainAssetKey);
    }

    [Fact]
    public void AnAreaThatAuthorsNoTerrainHoldsNoAsset()
    {
        var state = new EditorInteractionState();
        state.SelectMode(EditorMode.Props);

        Assert.Null(state.SelectedTerrainAssetKey);
    }

    [Fact]
    public void AnAreaOffersOnlyTheAssetsItCanAuthor()
    {
        using var workspace = TestWorkspace.Create();

        Assert.All(
            TerrainAreaAssets.Offered(EditorMode.Terrain, workspace.Terrain),
            asset => Assert.Equal(TerrainAuthoring.Cells, asset.Authoring));
        Assert.All(
            TerrainAreaAssets.Offered(EditorMode.Mountain, workspace.Terrain),
            asset => Assert.Equal(TerrainAuthoring.Cells, asset.Authoring));
        Assert.Equal(
            ["river"],
            TerrainAreaAssets.Offered(EditorMode.River, workspace.Terrain)
                .Select(static asset => asset.AssetKey));
        Assert.Empty(TerrainAreaAssets.Offered(EditorMode.Props, workspace.Terrain));
    }

    /// <summary>
    /// Entering an area keeps the Asset it was left with when that Asset still
    /// suits it, and otherwise takes the first one it offers. Nothing else may
    /// choose - least of all the code that builds buttons.
    /// </summary>
    [Fact]
    public void AnAreaKeepsASuitableAssetAndOtherwiseTakesItsFirst()
    {
        using var workspace = TestWorkspace.Create();
        var offeredByTerrain = TerrainAreaAssets
            .Offered(EditorMode.Terrain, workspace.Terrain)[0].AssetKey;

        Assert.Equal(
            "sand",
            TerrainAreaAssets.Choose(EditorMode.Terrain, workspace.Terrain, "sand"));
        // A curve Asset cannot surface painted cells, so Terrain refuses it and
        // falls back rather than carrying it over.
        Assert.Equal(
            offeredByTerrain,
            TerrainAreaAssets.Choose(EditorMode.Terrain, workspace.Terrain, "river"));
        Assert.Equal(
            "river",
            TerrainAreaAssets.Choose(EditorMode.River, workspace.Terrain, "grass"));
        Assert.Equal(
            offeredByTerrain,
            TerrainAreaAssets.Choose(EditorMode.Mountain, workspace.Terrain, remembered: null));
        Assert.Null(TerrainAreaAssets.Choose(EditorMode.Props, workspace.Terrain, "grass"));
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
