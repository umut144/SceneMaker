using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class ToolMountainTests
{
    [Fact]
    public void ThreePointsAndEnterAuthorOneClosedMountain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Mountain();
        var context = Context(workspace, scene, elevation: 4.0m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));

        var body = Assert.Single(edit.Apply(scene).MountainBodies);
        Assert.Equal("mountain_0001", body.MountainBodyId);
        Assert.Equal("grass", body.AssetKey);
        Assert.Equal(4.0m, body.ElevationMeters);
        Assert.Equal(3, body.Points.Count);
        Assert.Empty(interaction.MountainDraft);
        Assert.Null(edit.StrokeKey);
    }

    [Fact]
    public void PointsSnapToTerrainGridButHandlesDoNot()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Mountain();
        var context = Context(workspace, scene);
        interaction.State.SetMountainPointMode(MountainPointMode.Aligned);

        interaction.PointerPressed(context, Point(20, 20), Cell(0, 0));
        interaction.PointerDragged(context, Point(21, 61), Cell(0, 1));
        interaction.PointerReleased(context);

        var placed = Assert.Single(interaction.MountainDraft);
        Assert.Equal(32, placed.X);
        Assert.Equal(32, placed.Y);
        Assert.Equal(new AuthoringPixelOffset { X = -11, Y = 29 }, placed.DraggedHandleOut);
    }

    [Fact]
    public void AlignedClicksResolveCyclicHandlesAcrossTheClosingEdge()
    {
        var points = MountainEditing.ResolveContour(
        [
            new MountainDraftPoint(0, 0, MountainPointMode.Aligned),
            new MountainDraftPoint(96, 0, MountainPointMode.Aligned),
            new MountainDraftPoint(96, 96, MountainPointMode.Aligned),
        ]);

        Assert.False(points[0].HandleInAuthoringPx.IsZero());
        Assert.False(points[0].HandleOutAuthoringPx.IsZero());
        Assert.False(points[^1].HandleInAuthoringPx.IsZero());
        Assert.False(points[^1].HandleOutAuthoringPx.IsZero());
    }

    [Fact]
    public void InvalidContourStaysADraftAndShowsNoFill()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Mountain();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 160);
        Place(interaction, context, 32, 160);
        Place(interaction, context, 160, 32);

        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Enter));
        Assert.Equal(4, interaction.MountainDraft.Count);
        var preview = ToolPreviewBuilder.BuildMountainDraft(
            scene,
            workspace.Metrics,
            interaction.ActiveTool,
            interaction.MountainDraft,
            interaction.MountainPendingPoint);
        Assert.False(preview.IsValid);
        Assert.Empty(preview.Cells);
        Assert.NotEmpty(preview.Outline);
    }

    [Fact]
    public void EscapeAndDraftUndoStepBackOnePointAtATime()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Mountain();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Assert.True(interaction.HasUnfinishedDraft);
        Assert.NotNull(interaction.UndoDraftStep());
        Assert.Single(interaction.MountainDraft);
        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Empty(interaction.MountainDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    [Fact]
    public void EraserRemovesTheTopmostWholeMountain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var lower = MountainEditing.Place(
            scene, workspace.Metrics, workspace.Terrain, Triangle(32, 32, 160), "grass", 2m);
        var upper = MountainEditing.Place(
            lower, workspace.Metrics, workspace.Terrain, Triangle(64, 64, 96), "grass", 4m);
        var interaction = Mountain();
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, upper), Point(96, 80), Cell(3, 2)));
        var erased = edit.Apply(upper);

        Assert.Single(erased.MountainBodies);
        Assert.Equal("mountain_0001", erased.MountainBodies[0].MountainBodyId);
    }

    [Fact]
    public void PreviewUsesTheSameFilledCellsAsTheAuthoredBody()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var draft = new[]
        {
            new MountainDraftPoint(32, 32, MountainPointMode.Linear),
            new MountainDraftPoint(160, 32, MountainPointMode.Linear),
            new MountainDraftPoint(96, 160, MountainPointMode.Linear),
        };
        var preview = ToolPreviewBuilder.BuildMountainDraft(
            scene, workspace.Metrics, EditorTool.DrawMountain, draft, pending: null);
        var authored = MountainEditing.Place(
            scene,
            workspace.Metrics,
            workspace.Terrain,
            MountainEditing.ResolveContour(draft),
            "grass",
            4m);

        Assert.True(preview.IsValid);
        Assert.Equal(
            MountainGeometry.TerrainCells(scene, workspace.Metrics, authored.MountainBodies[^1]),
            preview.Cells);
    }

    private static IReadOnlyList<MountainCurvePointDocument> Triangle(int x, int y, int size) =>
    [
        MountainEditing.Point(x, y),
        MountainEditing.Point(x + size, y),
        MountainEditing.Point(x + size / 2, y + size),
    ];

    private static ToolInteraction Mountain()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.Terrain);
        interaction.SelectTool(EditorTool.DrawMountain);
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        decimal elevation = 4m) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: "grass",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: elevation);

    private static void Place(ToolInteraction interaction, ToolContext context, int x, int y)
    {
        interaction.PointerPressed(context, Point(x, y), Cell(x / 32, y / 32));
        interaction.PointerReleased(context);
    }

    private static AuthoringPoint Point(int x, int y) => new(x, y);
    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
