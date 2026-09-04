using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class ToolRampTests
{
    [Fact]
    public void AFreeOpenCurveAuthorsAnAscendingRamp()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();

        Place(interaction, Context(workspace, scene, 1m), 35, 47);
        interaction.State.SetRampWidth(3m);
        Place(interaction, Context(workspace, scene, 2m), 157, 91);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 2m), ToolKey.Enter));

        var route = Assert.Single(edit.Apply(scene).RouteSurfaces);
        Assert.Equal("route_0001", route.RouteSurfaceId);
        Assert.Equal("grass", route.AssetKey);
        Assert.Equal([35, 157], route.Points.Select(static point => point.PositionAuthoringPx.X));
        Assert.Equal([47, 91], route.Points.Select(static point => point.PositionAuthoringPx.Y));
        Assert.Equal([1m, 2m], route.Points.Select(static point => point.ElevationMeters));
        Assert.Equal([2m, 3m], route.Points.Select(static point => point.WidthMeters));
        Assert.Empty(interaction.RampDraft);
        Assert.Null(edit.StrokeKey);
    }

    [Fact]
    public void DescendingIsTheSameAuthoringOperationInTheOtherDirection()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();

        Place(interaction, Context(workspace, scene, 2m), 32, 32);
        Place(interaction, Context(workspace, scene, 1m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 1m), ToolKey.Enter));

        Assert.Equal(
            [2m, 1m],
            Assert.Single(edit.Apply(scene).RouteSurfaces).Points
                .Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void TheSurfaceMayChangeWithoutDiscardingTheDraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();

        Place(interaction, Context(workspace, scene, 1m), 32, 32);
        Place(interaction, Context(workspace, scene, 2m), 160, 32);
        var changed = Context(workspace, scene, 2m, "river");

        Assert.Equal(2, interaction.RampDraft.Count);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(changed, ToolKey.Enter));
        Assert.Equal("river", Assert.Single(edit.Apply(scene).RouteSurfaces).AssetKey);
    }

    [Fact]
    public void AnAlignedDragAuthorsAHandleWithoutMovingThePoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();
        var context = Context(workspace, scene, 1m);
        interaction.State.SetRoutePointMode(RoutePointMode.Aligned);

        interaction.PointerPressed(context, Point(35, 47), Cell(1, 1));
        interaction.PointerDragged(context, Point(67, 79), Cell(2, 2));
        interaction.PointerReleased(context);

        var point = Assert.Single(interaction.RampDraft);
        Assert.Equal(new AuthoringPixelOffset { X = 32, Y = 32 }, point.DraggedHandleOut);
        Assert.Equal((35, 47), (point.X, point.Y));
    }

    [Fact]
    public void ARepeatedPointNeedsAHandleToCreateHorizontalRun()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();
        var context = Context(workspace, scene, 1m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 32, 32);

        Assert.Single(interaction.RampDraft);
    }

    [Fact]
    public void ThePreviewUsesTheSamePreparedSurfaceAsTheCommit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();

        Place(interaction, Context(workspace, scene, 1m), 32, 64);
        Place(interaction, Context(workspace, scene, 2m), 160, 64);

        var preview = interaction.RampPreview(Context(workspace, scene, 2m));
        var surface = Assert.IsType<PreparedRouteSurface>(preview.Surface);
        Assert.Equal(2, preview.Curve.Count);
        Assert.Equal(1m, surface.Points[0].ElevationMeters);
        Assert.Equal(2m, surface.Points[1].ElevationMeters);
    }

    [Fact]
    public void EnablingTheEraserDiscardsTheDraftAndSaysSo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Ramp();
        Place(interaction, Context(workspace, scene, 1m), 32, 32);

        var outcome = interaction.SetEraserEnabled(true);

        Assert.Equal(
            "The unfinished Ramp of 1 point was discarded.",
            Assert.IsType<ToolOutcome.Message>(outcome).Text);
        Assert.Empty(interaction.RampDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    [Fact]
    public void TheEraserRemovesTheWholeRampBand()
    {
        using var workspace = TestWorkspace.Create();
        var scene = RouteSurfaceEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            workspace.Metrics,
            [
                RouteSurfaceEditing.Point(32, 64, RoutePointMode.Linear, 1m, 2m),
                RouteSurfaceEditing.Point(160, 64, RoutePointMode.Linear, 2m, 2m),
            ],
            "grass");
        var interaction = Ramp();
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene, 1m), Point(96, 88), Cell(3, 2)));

        Assert.Empty(edit.Apply(scene).RouteSurfaces);
    }

    private static ToolInteraction Ramp()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.Ramp);
        interaction.SelectTool(EditorTool.DrawRamp);
        interaction.State.SelectTerrainAsset("grass");
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        decimal elevation,
        string surfaceAssetKey = "grass") => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: surfaceAssetKey,
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
