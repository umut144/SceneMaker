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
    public void UndoTakesBackTheLastPointWhileTheRiverIsStillBeingDrawn()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 96, 32);
        Assert.True(interaction.HasUnfinishedDraft);

        Assert.NotNull(interaction.UndoDraftStep());
        Assert.Single(interaction.RiverDraft);
        Assert.NotNull(interaction.UndoDraftStep());
        Assert.Empty(interaction.RiverDraft);

        // With the draft gone the undo belongs to the document history again,
        // which is what a null says.
        Assert.False(interaction.HasUnfinishedDraft);
        Assert.Null(interaction.UndoDraftStep());
    }

    [Fact]
    public void AFinishedRiverIsUndoneByTheDocumentHistory()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        interaction.KeyPressed(context, ToolKey.Enter);

        // One river is one edit: once it is authored the tool holds nothing,
        // and undo takes back the whole river rather than its last point.
        Assert.False(interaction.HasUnfinishedDraft);
        Assert.Null(interaction.UndoDraftStep());
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
    public void LeavingTheAreaDropsTheDraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();

        Place(interaction, Context(workspace, scene), 32, 32);
        var outcome = interaction.SelectMode(EditorMode.Terrain);

        Assert.Empty(interaction.RiverDraft);
        Assert.Equal(
            "The unfinished river of 1 point was discarded.",
            Assert.IsType<ToolOutcome.Message>(outcome).Text);
    }

    [Fact]
    public void APlacedPointTakesTheHeightOfTheTerrainUnderIt()
    {
        using var workspace = TestWorkspace.Create();
        // The fixture's Terrain stands at 1 m everywhere.
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);
        interaction.State.SetRiverWidth(8.0m);
        interaction.State.SetWaterChannelDepth(0.75m);
        interaction.State.SetWaterClearanceAbove(3.0m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));

        var body = Assert.Single(edit.Apply(scene).WaterBodies);
        Assert.All(body.Points, point =>
        {
            Assert.Equal(8.0m, point.WidthMeters);
            Assert.Equal(1.0m, point.ElevationMeters);
            Assert.Equal(0.75m, point.ChannelDepthMeters);
            Assert.Equal(3.0m, point.ClearanceAboveMeters);
        });
    }

    [Fact]
    public void WithoutSnappingThePointTakesTheHeightFromTheContextBar()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);
        interaction.State.SetSnapWaterToTerrain(false);
        interaction.State.SetWaterElevation(4.0m);

        Place(interaction, context, 32, 32);
        interaction.State.SetWaterElevation(2.0m);
        Place(interaction, context, 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));

        // Driving a river into a mountain is exactly the case where the water
        // must not follow the ground.
        Assert.Equal(
            [4.0m, 2.0m],
            Assert.Single(edit.Apply(scene).WaterBodies).Points
                .Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void ChangingWidthWhileDrawingAppliesToTheNextPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        var context = Context(workspace, scene);

        interaction.State.SetRiverWidth(4.0m);
        Place(interaction, context, 32, 32);
        interaction.State.SetRiverWidth(8.0m);
        Place(interaction, context, 160, 32);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));
        var points = Assert.Single(edit.Apply(scene).WaterBodies).Points;
        Assert.Equal([4.0m, 8.0m], points.Select(static point => point.WidthMeters));
    }

    [Fact]
    public void SnappingOverUnpaintedTerrainKeepsThePreviousPointsHeight()
    {
        using var workspace = TestWorkspace.Create();
        // Terrain only in the lower left corner; the second point lands beyond
        // it, where snapping has nothing to read.
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 1, 1, "grass", 2.5m);
        var interaction = River();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));

        var points = Assert.Single(edit.Apply(scene).WaterBodies).Points;
        Assert.Equal(2.5m, points[0].ElevationMeters);
        Assert.Equal(2.5m, points[1].ElevationMeters);
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
                Draft(32, 32, WaterPointMode.Linear),
                Draft(160, 32, WaterPointMode.Linear),
            ]),
            "river");
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
        Assert.Single(single.Curve);
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
            [Draft(32, 32, WaterPointMode.Linear)],
            pending: null);

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
            interaction.RiverPendingPoint);

    private static void Place(ToolInteraction interaction, ToolContext context, int x, int y)
    {
        interaction.PointerPressed(context, Point(x, y), Cell(x / 32, y / 32));
        interaction.PointerReleased(context);
    }

    /// <summary>
    /// The same rule the mountain contour follows: the eraser ends the curve it
    /// interrupts rather than keeping it alive behind a mode that would give the
    /// next click another meaning.
    /// </summary>
    [Fact]
    public void EnablingTheEraserDiscardsTheDraftAndSaysSo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = River();
        Place(interaction, Context(workspace, scene), 32, 32);
        Place(interaction, Context(workspace, scene), 160, 32);

        var enabled = interaction.SetEraserEnabled(true);
        var disabled = interaction.SetEraserEnabled(false);

        Assert.Equal(
            "The unfinished river of 2 points was discarded.",
            Assert.IsType<ToolOutcome.Message>(enabled).Text);
        Assert.IsType<ToolOutcome.Idle>(disabled);
        Assert.Empty(interaction.RiverDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    /// <summary>
    /// The Surface is the material and not the shape. Changing it while a river
    /// is being drawn keeps the curve and decides only what the finished body
    /// carries - which is why it can sit in the tool's context bar beside the
    /// width and the heights.
    /// </summary>
    [Fact]
    public void ChangingTheSurfaceWhileDrawingKeepsTheCurve()
    {
        using var workspace = TestWorkspace.Create(secondCurveAsset: true);
        var scene = TestScenes.Instance(workspace);
        var interaction = River();

        Place(interaction, Context(workspace, scene), 32, 32);
        Place(interaction, Context(workspace, scene), 160, 32);
        var afterChange = Context(workspace, scene, surfaceAssetKey: "lava");

        Assert.Equal(2, interaction.RiverDraft.Count);
        Assert.True(interaction.HasUnfinishedDraft);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(afterChange, ToolKey.Enter));
        var body = Assert.Single(edit.Apply(scene).WaterBodies);
        Assert.Equal("lava", body.AssetKey);
        Assert.Equal(2, body.Points.Count);
    }

    private static ToolInteraction River()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.DrawRiver);
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        string surfaceAssetKey = "river") => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: surfaceAssetKey,
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: scene.DefaultElevationMeters);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);

    /// <summary>
    /// A draft point at the fixture's usual heights: water at ground level,
    /// half a metre deep, five metres of headroom. The tests here are about
    /// plan geometry, so the section stays out of their way.
    /// </summary>
    private static WaterDraftPoint Draft(
        int x,
        int y,
        WaterPointMode mode,
        AuthoringPixelOffset? handle = null) =>
        new(x, y, mode, 1.0m, 0.5m, 5.0m, 4.0m, handle);

}
