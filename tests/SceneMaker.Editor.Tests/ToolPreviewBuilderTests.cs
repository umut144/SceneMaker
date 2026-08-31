using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The preview decisions that used to sit inside the canvas next to the drawing
/// code. The fixture sizes one Prop to exactly one Terrain cell, so an anchor at
/// (64, 64) is cell (2, 2).
/// </summary>
public sealed class ToolPreviewBuilderTests
{
    [Fact]
    public void ThePencilPreviewsTheCellUnderThePointer()
    {
        var scene = TestScenes.EmptyInstance();

        var preview = ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Pencil, eraserEnabled: false,
            new TerrainCellCoordinate(2, 3), lineStart: null);

        Assert.Equal(new[] { (2, 3) }, preview.Cells.Select(static c => (c.X, c.Y)).ToArray());
        Assert.False(preview.Erasing);
    }

    [Fact]
    public void ADraggedTerrainLinePreviewsEveryCellItCrosses()
    {
        var scene = TestScenes.EmptyInstance();

        var preview = ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Line, eraserEnabled: false,
            new TerrainCellCoordinate(3, 0), new TerrainCellCoordinate(0, 0));

        Assert.Equal(
            new[] { (0, 0), (1, 0), (2, 0), (3, 0) },
            preview.Cells.Select(static c => (c.X, c.Y)).ToArray());
    }

    [Fact]
    public void CellsOutsideTheSceneAreNotPreviewed()
    {
        var scene = TestScenes.EmptyInstance(sizeCells: 6);

        var preview = ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Pencil, eraserEnabled: false,
            new TerrainCellCoordinate(7, 7), lineStart: null);

        Assert.Empty(preview.Cells);
    }

    [Fact]
    public void TheEraserIsReportedSoTheCanvasCanColourTheHighlight()
    {
        var scene = TestScenes.EmptyInstance();

        var preview = ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Pencil, eraserEnabled: true,
            new TerrainCellCoordinate(0, 0), lineStart: null);

        Assert.True(preview.Erasing);
    }

    [Fact]
    public void ToolsWithoutATerrainPreviewAndAPointerOutsideTheCanvasPreviewNothing()
    {
        var scene = TestScenes.EmptyInstance();

        Assert.Empty(ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Fill, false, new TerrainCellCoordinate(0, 0), null).Cells);
        Assert.Empty(ToolPreviewBuilder.BuildTerrain(
            scene, EditorTool.Pencil, false, pointer: null, lineStart: null).Cells);
    }

    [Fact]
    public void APropOnCompleteTerrainIsReady()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        var preview = Assert.Single(Props(workspace, scene, EditorTool.Pencil, new AuthoringPoint(64, 64)));

        Assert.Equal(PropPreviewKind.Ready, preview.Kind);
        Assert.Equal(64, preview.Bounds.Left);
        Assert.Null(preview.Explanation);
    }

    [Fact]
    public void APropWithoutTerrainUnderneathIsFlaggedButStillAuthorable()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        var preview = Assert.Single(Props(workspace, scene, EditorTool.Pencil, new AuthoringPoint(64, 64)));

        Assert.Equal(PropPreviewKind.MissingTerrain, preview.Kind);
        Assert.Contains("lacks Terrain", preview.Explanation ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void APropOverlappingAnExistingOneIsBlocked()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 64, 64, "stone");

        var preview = Assert.Single(Props(workspace, scene, EditorTool.Pencil, new AuthoringPoint(64, 64)));

        Assert.Equal(PropPreviewKind.Blocked, preview.Kind);
        Assert.Contains("overlaps", preview.Explanation ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutASelectedAssetNothingIsPreviewed()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        Assert.Empty(ToolPreviewBuilder.BuildProps(
            scene, workspace.Props, TerrainCoverage.AuthoredCells(scene),
            selectedAssetKey: null, EditorTool.Pencil,
            new AuthoringPoint(64, 64), null, null, 0));
        Assert.Empty(Props(workspace, scene, EditorTool.Selector, new AuthoringPoint(64, 64)));
    }

    [Fact]
    public void AFixedLineStartPreviewsTheWholeRunOfAnchors()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        var previews = ToolPreviewBuilder.BuildProps(
            scene, workspace.Props, TerrainCoverage.AuthoredCells(scene),
            "stone", EditorTool.Line,
            pointer: null,
            lineStart: new AuthoringPoint(0, 0),
            lineEnd: new AuthoringPoint(128, 0),
            lineOffsetAuthoringPixels: 0);

        Assert.Equal(
            new[] { 0, 32, 64, 96, 128 },
            previews.Select(static preview => preview.Anchor.X).ToArray());
        Assert.All(previews, preview => Assert.Equal(PropPreviewKind.Ready, preview.Kind));
    }

    [Fact]
    public void TheLineOffsetSpacesTheAnchorsFurtherApart()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        var previews = ToolPreviewBuilder.BuildProps(
            scene, workspace.Props, TerrainCoverage.AuthoredCells(scene),
            "stone", EditorTool.Line,
            pointer: null,
            lineStart: new AuthoringPoint(0, 0),
            lineEnd: new AuthoringPoint(128, 0),
            lineOffsetAuthoringPixels: 16);

        Assert.Equal(
            new[] { 0, 48, 96 },
            previews.Select(static preview => preview.Anchor.X).ToArray());
    }

    [Fact]
    public void TheLineToolBeforeItsStartIsFixedPreviewsOnlyThePointer()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        var preview = Assert.Single(Props(workspace, scene, EditorTool.Line, new AuthoringPoint(64, 64)));

        Assert.Equal(64, preview.Anchor.X);
    }

    [Fact]
    public void CountOfReportsHowManyPreviewsShareAKind()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        var previews = ToolPreviewBuilder.BuildProps(
            scene, workspace.Props, TerrainCoverage.AuthoredCells(scene),
            "stone", EditorTool.Line,
            pointer: null,
            lineStart: new AuthoringPoint(0, 0),
            lineEnd: new AuthoringPoint(128, 0),
            lineOffsetAuthoringPixels: 0);

        Assert.Equal(5, ToolPreviewBuilder.CountOf(previews, PropPreviewKind.MissingTerrain));
        Assert.Equal(0, ToolPreviewBuilder.CountOf(previews, PropPreviewKind.Ready));
    }

    private static IReadOnlyList<PropPreview> Props(
        TestWorkspace workspace,
        SceneDocument scene,
        EditorTool tool,
        AuthoringPoint pointer) =>
        ToolPreviewBuilder.BuildProps(
            scene,
            workspace.Props,
            TerrainCoverage.AuthoredCells(scene),
            "stone",
            tool,
            pointer,
            lineStart: null,
            lineEnd: null,
            lineOffsetAuthoringPixels: 0);
}
