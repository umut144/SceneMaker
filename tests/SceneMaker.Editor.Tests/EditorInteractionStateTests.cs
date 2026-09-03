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
    /// Each area keeps its own brush: leaving one and coming back should find
    /// it where it was left.
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

        state.SelectMode(EditorMode.Props);
        Assert.Null(state.SelectedTerrainAssetKey);

        state.SelectMode(EditorMode.Terrain);
        Assert.Equal("grass", state.SelectedTerrainAssetKey);
        state.SelectMode(EditorMode.River);
        Assert.Equal("river", state.SelectedTerrainAssetKey);
    }

    /// <summary>
    /// Mountain is an area that draws and still holds no Asset: the contour
    /// says where and how high, the painted Terrain says what of.
    /// </summary>
    [Fact]
    public void TheMountainAreaHoldsNoTerrainAsset()
    {
        using var workspace = TestWorkspace.Create();
        var state = new EditorInteractionState();
        state.SelectMode(EditorMode.Mountain);

        Assert.Null(state.SelectedTerrainAssetKey);
        Assert.Empty(TerrainAreaAssets.Offered(EditorMode.Mountain, workspace.Terrain));
        Assert.Null(
            TerrainAreaAssets.Choose(EditorMode.Mountain, workspace.Terrain, remembered: "grass"));
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
        Assert.Empty(TerrainAreaAssets.Offered(EditorMode.Mountain, workspace.Terrain));
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

    /// <summary>
    /// An area that paints cells hands the author a palette; an area that draws
    /// a curve carries its material as one property of the body, beside width
    /// and heights. The field therefore follows the authoring kind rather than a
    /// list of areas, and a later closed-curve area gets it for the same reason.
    /// </summary>
    [Fact]
    public void OnlyACurveAreaCarriesItsMaterialAsASurfaceField()
    {
        using var workspace = TestWorkspace.Create();

        Assert.NotNull(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.River, workspace.Terrain, remembered: null));
        Assert.Null(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.Terrain, workspace.Terrain, remembered: null));
        Assert.Null(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.Mountain, workspace.Terrain, remembered: null));
        Assert.Null(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.Props, workspace.Terrain, remembered: null));
        Assert.Null(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.Templates, workspace.Terrain, remembered: null));
    }

    /// <summary>
    /// One offered Asset is not a choice. The field stays - the tool has the
    /// same shape either way - and says which Asset that is, but it cannot be
    /// opened.
    /// </summary>
    [Fact]
    public void ASingleOfferedSurfaceIsShownAndCannotBeChanged()
    {
        using var workspace = TestWorkspace.Create();

        var field = Assert.IsType<TerrainSurfaceField>(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.River, workspace.Terrain, remembered: null));

        Assert.Equal(["river"], field.Options.Select(static asset => asset.AssetKey));
        Assert.Equal("river", field.SelectedAssetKey);
        Assert.False(field.Changeable);
    }

    /// <summary>
    /// With more than one curve-authored Asset the field is a real choice, and
    /// it offers exactly the Assets the area can author - never a cell-authored
    /// one. The Surface is a choice of Asset; the runtime `surface` token is
    /// never read here, and nothing about it says water.
    /// </summary>
    [Fact]
    public void SeveralOfferedSurfacesMakeTheFieldAChoice()
    {
        using var workspace = TestWorkspace.Create(secondCurveAsset: true);

        var field = Assert.IsType<TerrainSurfaceField>(TerrainAreaAssets.SurfaceFieldFor(
            EditorMode.River, workspace.Terrain, remembered: null));

        Assert.Equal(["lava", "river"], field.Options.Select(static asset => asset.AssetKey));
        Assert.True(field.Changeable);
        Assert.All(
            field.Options,
            static asset => Assert.Equal(TerrainAuthoring.Curve, asset.Authoring));
    }

    /// <summary>
    /// The field gives the answer `Choose` gives: the Asset the area was left
    /// with while it still suits, and otherwise the first it offers. One rule
    /// for both, so a palette and a Surface field can never pick differently.
    /// </summary>
    [Fact]
    public void TheSurfaceFieldKeepsTheRememberedAssetAndOtherwiseTakesTheFirst()
    {
        using var workspace = TestWorkspace.Create(secondCurveAsset: true);

        Assert.Equal(
            "river",
            TerrainAreaAssets.SurfaceFieldFor(
                EditorMode.River, workspace.Terrain, remembered: "river")?.SelectedAssetKey);
        // A cell-authored Asset cannot surface a corridor, so it is refused and
        // the field falls back rather than carrying it over.
        Assert.Equal(
            TerrainAreaAssets.Choose(EditorMode.River, workspace.Terrain, remembered: null),
            TerrainAreaAssets.SurfaceFieldFor(
                EditorMode.River, workspace.Terrain, remembered: "grass")?.SelectedAssetKey);
    }
}
