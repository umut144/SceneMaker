using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// Drawing a river. The fixture's water grid is 16 authoring pixels, so a click
/// at (20, 20) belongs to the grid position (16, 16).
/// </summary>
public sealed class ToolRiverTests
{
    [Fact]
    public void ClickingTwiceAndConfirmingAuthorsOneRiver()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));

        var authored = edit.Apply(scene);
        var body = Assert.Single(authored.WaterBodies);
        Assert.Equal("river_0001", body.WaterBodyId);
        Assert.Equal(WaterKind.River, body.WaterKind);
        Assert.Equal("river", body.AssetKey);
        Assert.Equal(2, body.Points.Count);
        // One curve is one edit: undo takes back the river, not its last point.
        Assert.Null(edit.StrokeKey);
        Assert.Empty(interaction.RiverDraft);
    }

    [Fact]
    public void CurvePointsSnapToTheWaterGridAndHandlesDoNot()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);
        interaction.State.SetWaterPointMode(WaterPointMode.Aligned);

        interaction.PointerPressed(context, Point(20, 20), Cell(0, 0));
        interaction.PointerDragged(context, Point(21, 61), Cell(0, 1));
        interaction.PointerReleased(context);

        var placed = Assert.Single(interaction.RiverDraft);
        Assert.Equal(16, placed.X);
        Assert.Equal(16, placed.Y);
        Assert.Equal(new AuthoringPixelOffset { X = 5, Y = 45 }, placed.DraggedHandleOut);
    }

    [Fact]
    public void ADragShorterThanHalfAWaterCellIsAClickRatherThanAHandle()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);
        interaction.State.SetWaterPointMode(WaterPointMode.Aligned);

        interaction.PointerPressed(context, Point(32, 32), Cell(1, 1));
        interaction.PointerDragged(context, Point(35, 34), Cell(1, 1));
        interaction.PointerReleased(context);

        Assert.Null(Assert.Single(interaction.RiverDraft).DraggedHandleOut);
    }

    [Fact]
    public void ThePointModeAppliesToTheNextPointAndLeavesThePlacedOnesAlone()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        interaction.State.SetWaterPointMode(WaterPointMode.Aligned);
        Place(interaction, context, 96, 96);

        Assert.Equal(WaterPointMode.Linear, interaction.RiverDraft[0].Mode);
        Assert.Equal(WaterPointMode.Aligned, interaction.RiverDraft[1].Mode);
    }

    [Fact]
    public void EscapeStepsBackThroughTheDraftOnePointAtATime()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 96, 32);
        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Escape));

        Assert.Single(interaction.RiverDraft);
        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Empty(interaction.RiverDraft);
    }

    [Fact]
    public void ASinglePointIsNotARiverYet()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);

        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Enter));
        Assert.Single(interaction.RiverDraft);
    }

    [Fact]
    public void TheSamePointTwiceInARowIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 32, 32);

        Assert.Single(interaction.RiverDraft);
    }

    [Fact]
    public void ChoosingAnotherToolDropsTheDraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();

        Place(interaction, Context(workspace, scene), 32, 32);
        interaction.SelectTool(EditorTool.Pencil);

        Assert.Empty(interaction.RiverDraft);
    }

    [Fact]
    public void TheRiverTakesTheHeightFromTheContextBar()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene) with { ElevationMeters = 0.0m };
        interaction.State.SetRiverWidth(8.0m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));

        var body = Assert.Single(edit.Apply(scene).WaterBodies);
        Assert.Equal(0.0m, body.ElevationMeters);
        Assert.Equal(8.0m, body.WidthMeters);
    }

    [Fact]
    public void TheEraserRemovesTheWholeBodyUnderThePointer()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            WaterEditing.ResolveCurve(
            [
                new WaterDraftPoint(32, 32, WaterPointMode.Linear),
                new WaterDraftPoint(160, 32, WaterPointMode.Linear),
            ]),
            "river",
            4.0m,
            0.0m);
        var interaction = River();
        interaction.SetEraserEnabled(true);
        var context = Context(workspace, scene);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(96, 32), Cell(3, 1)));
        Assert.Empty(edit.Apply(scene).WaterBodies);

        Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 160), Cell(3, 5)));
    }

    [Fact]
    public void ASceneTemplateRefusesWaterBeforeThePointIsEvenPlaced()
    {
        using var workspace = TestWorkspace.Create();
        var template = TestScenes.Template(workspace, "grove", groupNumber: 1);
        var interaction = River();

        var outcome = interaction.PointerPressed(
            Context(workspace, template), Point(16, 16), Cell(0, 0));

        Assert.IsType<ToolOutcome.Message>(outcome);
        Assert.Empty(interaction.RiverDraft);
    }

    [Fact]
    public void APointOutsideTheSceneIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();

        // The Scene is 6 cells - 192 authoring pixels - wide.
        interaction.PointerPressed(Context(workspace, scene), Point(240, 32), Cell(7, 1));

        Assert.Null(interaction.RiverPendingPoint);
    }

    [Fact]
    public void ThePreviewShowsTheCorridorAsSoonAsThereAreTwoPoints()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 96);
        var single = Preview(workspace, scene, interaction);
        Assert.Single(single.Points);
        Assert.Empty(single.Cells);

        Place(interaction, context, 160, 96);
        var pair = Preview(workspace, scene, interaction);

        Assert.Equal(2, pair.Points.Count);
        Assert.Equal(2, pair.Centerline.Count);
        // Four metres of width is eight rows of water cells, and the Scene is
        // tall enough here that none of them are clipped away.
        Assert.Equal(8, pair.Cells.Select(static cell => cell.Y).Distinct().Count());
    }

    [Fact]
    public void AnotherToolPreviewsNoWater()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        var preview = ToolPreviewBuilder.BuildWaterDraft(
            scene,
            workspace.Metrics,
            EditorTool.Pencil,
            [new WaterDraftPoint(32, 32, WaterPointMode.Linear)],
            pending: null,
            widthMeters: 4.0m);

        Assert.Same(WaterDraftPreview.Empty, preview);
    }

    private static WaterDraftPreview Preview(
        TestWorkspace workspace,
        SceneDocument scene,
        ToolInteraction interaction) =>
        ToolPreviewBuilder.BuildWaterDraft(
            scene,
            workspace.Metrics,
            interaction.ActiveTool,
            interaction.RiverDraft,
            interaction.RiverPendingPoint,
            interaction.State.RiverWidthMeters);

    private static void Place(ToolInteraction interaction, ToolContext context, int x, int y)
    {
        interaction.PointerPressed(context, Point(x, y), Cell(x / 32, y / 32));
        interaction.PointerReleased(context);
    }

    private static ToolInteraction River()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.Terrain);
        interaction.SelectTool(EditorTool.River);
        return interaction;
    }

    private static ToolContext Context(TestWorkspace workspace, SceneDocument scene) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        TerrainCoverage.AuthoredCells(scene),
        SelectedTerrainAssetKey: "river",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: scene.DefaultElevationMeters);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
