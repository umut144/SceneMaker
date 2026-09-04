using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class ToolPathTests
{
    [Fact]
    public void AFreeOpenCurveAuthorsAnAscendingPath()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 9m), 32, 32);
        interaction.State.SetPathWidth(3m);
        interaction.State.SetPathGrade(RouteGradePreset.UpTwentyFivePercent);
        Place(interaction, Context(workspace, scene, 9m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 9m), ToolKey.Enter));

        var route = Assert.Single(edit.Apply(scene).RouteSurfaces);
        Assert.Equal("route_0001", route.RouteSurfaceId);
        Assert.Equal("grass", route.AssetKey);
        Assert.Equal([32, 160], route.Points.Select(static point => point.PositionAuthoringPx.X));
        Assert.Equal([32, 32], route.Points.Select(static point => point.PositionAuthoringPx.Y));
        Assert.Equal([1m, 2m], route.Points.Select(static point => point.ElevationMeters));
        Assert.Equal([2m, 3m], route.Points.Select(static point => point.WidthMeters));
        var segment = Assert.Single(route.Segments);
        Assert.Equal("route_0001.segment_0001", segment.SegmentId);
        Assert.Equal(25, segment.GradePercent);
        Assert.Empty(interaction.PathDraft);
        Assert.Null(edit.StrokeKey);
    }

    [Fact]
    public void DescendingIsTheSameAuthoringOperationInTheOtherDirection()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();
        interaction.State.SetPathStartElevationOverride(2m);

        Place(interaction, Context(workspace, scene, 9m), 32, 32);
        interaction.State.SetPathGrade(RouteGradePreset.DownTwentyFivePercent);
        Place(interaction, Context(workspace, scene, 9m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 9m), ToolKey.Enter));

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
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 1m), 32, 32);
        Place(interaction, Context(workspace, scene, 2m), 160, 32);
        var changed = Context(workspace, scene, 2m, "river");

        Assert.Equal(2, interaction.PathDraft.Count);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(changed, ToolKey.Enter));
        Assert.Equal("river", Assert.Single(edit.Apply(scene).RouteSurfaces).AssetKey);
    }

    [Fact]
    public void AnAlignedDragAuthorsAHandleWithoutMovingThePoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();
        var context = Context(workspace, scene, 1m);
        interaction.State.SetRoutePointMode(RoutePointMode.Aligned);

        interaction.PointerPressed(context, Point(35, 47), Cell(1, 1));
        interaction.PointerDragged(context, Point(67, 79), Cell(2, 2));
        interaction.PointerReleased(context);

        var point = Assert.Single(interaction.PathDraft);
        Assert.Equal(new AuthoringPixelOffset { X = 32, Y = 32 }, point.DraggedHandleOut);
        Assert.Equal((35, 47), (point.X, point.Y));
    }

    [Fact]
    public void ARepeatedPointNeedsAHandleToCreateHorizontalRun()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();
        var context = Context(workspace, scene, 1m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 32, 32);

        Assert.Single(interaction.PathDraft);
    }

    [Fact]
    public void ThePreviewUsesTheSamePreparedSurfaceAsTheCommit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 1m), 32, 64);
        interaction.State.SetPathGrade(RouteGradePreset.UpTwentyFivePercent);
        Place(interaction, Context(workspace, scene, 2m), 160, 64);

        var preview = interaction.PathPreview(Context(workspace, scene, 2m));
        var surface = Assert.IsType<PreparedRouteSurface>(preview.Surface);
        Assert.Equal(2, preview.Curve.Count);
        Assert.Equal(1m, surface.Points[0].ElevationMeters);
        Assert.Equal(2m, surface.Points[1].ElevationMeters);
    }

    [Fact]
    public void TheFirstPointCopiesTheEffectiveTerrainTopIncludingAHill()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            [
                ElevationRegionEditing.Point(0, 0),
                ElevationRegionEditing.Point(96, 0),
                ElevationRegionEditing.Point(96, 96),
                ElevationRegionEditing.Point(0, 96),
            ],
            4m);
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 99m), 32, 32);
        Place(interaction, Context(workspace, scene, 99m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 99m), ToolKey.Enter));

        Assert.Equal(
            [4m, 4m],
            Assert.Single(edit.Apply(scene).RouteSurfaces).Points
                .Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void EmptyGroundNeedsAnExplicitStartingHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = Path();

        var blocked = interaction.PointerPressed(
            Context(workspace, scene, 99m),
            Point(32, 32),
            Cell(1, 1));

        Assert.Contains(
            "set a Start elevation",
            Assert.IsType<ToolOutcome.Message>(blocked).Text,
            StringComparison.Ordinal);
        Assert.Empty(interaction.PathDraft);

        interaction.State.SetPathStartElevationOverride(1.25m);
        Place(interaction, Context(workspace, scene, 99m), 32, 32);
        Place(interaction, Context(workspace, scene, 99m), 96, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 99m), ToolKey.Enter));
        Assert.Equal(
            [1.25m, 1.25m],
            Assert.Single(edit.Apply(scene).RouteSurfaces).Points
                .Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void AnOffQuantumManualStartIsRejectedBeforeItBecomesADraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = Path();
        interaction.State.SetPathStartElevationOverride(1.1m);

        var outcome = interaction.PointerPressed(
            Context(workspace, scene, 99m),
            Point(32, 32),
            Cell(1, 1));

        Assert.Contains(
            "align to 0.125 m",
            Assert.IsType<ToolOutcome.Message>(outcome).Text,
            StringComparison.Ordinal);
        Assert.Empty(interaction.PathDraft);
        Assert.Null(interaction.PathPendingPoint);
    }

    [Fact]
    public void GradeChangesApplyToTheNextSegmentOnly()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 99m), 32, 32);
        interaction.State.SetPathGrade(RouteGradePreset.UpTwentyFivePercent);
        Place(interaction, Context(workspace, scene, 99m), 96, 32);
        interaction.State.SetPathGrade(RouteGradePreset.DownTwentyFivePercent);
        Place(interaction, Context(workspace, scene, 99m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 99m), ToolKey.Enter));

        Assert.Equal(
            [1m, 1.5m, 1m],
            Assert.Single(edit.Apply(scene).RouteSurfaces).Points
                .Select(static point => point.ElevationMeters));
        Assert.Equal(
            [25, -25],
            Assert.Single(edit.Apply(scene).RouteSurfaces).Segments
                .Select(static segment => segment.GradePercent));
    }

    [Fact]
    public void OperationAndClearanceApplyToTheNextSegmentOnly()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();

        Place(interaction, Context(workspace, scene, 1m), 32, 32);
        interaction.State.SetPathOperation(RouteSegmentOperation.Subtractive);
        interaction.State.SetPathClearanceAbove(1.5m);
        Place(interaction, Context(workspace, scene, 1m), 96, 32);
        interaction.State.SetPathOperation(RouteSegmentOperation.Additive);
        Place(interaction, Context(workspace, scene, 1m), 160, 32);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(Context(workspace, scene, 1m), ToolKey.Enter));

        var segments = Assert.Single(edit.Apply(scene).RouteSurfaces).Segments;
        Assert.Equal(
            [RouteSegmentOperation.Subtractive, RouteSegmentOperation.Additive],
            segments.Select(static segment => segment.Operation));
        Assert.Equal([1.5m, null], segments.Select(static segment => segment.ClearanceAboveMeters));
    }

    [Fact]
    public void EnablingTheEraserDiscardsTheDraftAndSaysSo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = Path();
        Place(interaction, Context(workspace, scene, 1m), 32, 32);

        var outcome = interaction.SetEraserEnabled(true);

        Assert.Equal(
            "The unfinished Path of 1 point was discarded.",
            Assert.IsType<ToolOutcome.Message>(outcome).Text);
        Assert.Empty(interaction.PathDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    [Fact]
    public void TheEraserRemovesTheWholePathBand()
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
            [RouteGradePreset.UpTwentyFivePercent],
            "grass");
        var interaction = Path();
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene, 1m), Point(96, 88), Cell(3, 2)));

        Assert.Empty(edit.Apply(scene).RouteSurfaces);
    }

    private static ToolInteraction Path()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.Path);
        interaction.SelectTool(EditorTool.DrawPath);
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
