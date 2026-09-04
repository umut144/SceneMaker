using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RouteSurfaceEditingTests
{
    [Fact]
    public void PlaceAuthorsOnePathWithPerPointHeightAndWidth()
    {
        using var workspace = TestWorkspace.Create();
        var scene = RouteSurfaceEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 32, 1m, 2m), Point(160, 32, 2m, 3m)],
            "grass");

        var route = Assert.Single(scene.RouteSurfaces);
        Assert.Equal("route_0001", route.RouteSurfaceId);
        Assert.Equal("grass", route.AssetKey);
        Assert.Equal([1m, 2m], route.Points.Select(static point => point.ElevationMeters));
        Assert.Equal([2m, 3m], route.Points.Select(static point => point.WidthMeters));
    }

    [Fact]
    public void ARouteMayPresentEitherCellOrCurveAuthoredTerrain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = RouteSurfaceEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 32, 1m), Point(160, 32, 1m)],
            "grass");
        scene = RouteSurfaceEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 96, 1m), Point(160, 96, 1m)],
            "river");

        Assert.Equal(["grass", "river"], scene.RouteSurfaces.Select(static route => route.AssetKey));
    }

    [Fact]
    public void ResolveCurveKeepsEachDraftPointsVerticalValues()
    {
        var curve = RouteSurfaceEditing.ResolveCurve(
        [
            new RouteDraftPoint(32, 32, RoutePointMode.Aligned, 1m, 2m),
            new RouteDraftPoint(96, 64, RoutePointMode.Aligned, 1.5m, 2.5m),
            new RouteDraftPoint(160, 32, RoutePointMode.Aligned, 2m, 3m),
        ]);

        Assert.Equal([1m, 1.5m, 2m], curve.Select(static point => point.ElevationMeters));
        Assert.Equal([2m, 2.5m, 3m], curve.Select(static point => point.WidthMeters));
        Assert.False(curve[1].HandleInAuthoringPx.IsZero());
        Assert.False(curve[1].HandleOutAuthoringPx.IsZero());
    }

    [Theory]
    [InlineData(RouteGradePreset.DownFiftyPercent, -0.5)]
    [InlineData(RouteGradePreset.DownTwentyFivePercent, -0.25)]
    [InlineData(RouteGradePreset.Level, 0.0)]
    [InlineData(RouteGradePreset.UpTwentyFivePercent, 0.25)]
    [InlineData(RouteGradePreset.UpFiftyPercent, 0.5)]
    public void GradePresetsHaveExactlyTheFiveAuthoringRatios(
        RouteGradePreset grade,
        double expected) =>
        Assert.Equal(expected, RouteSurfaceEditing.GradeRatio(grade));

    [Fact]
    public void AQuarterGradeRisesOneMeterOverFourMetersOfRun()
    {
        using var workspace = TestWorkspace.Create();

        var curve = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(32, 32, RouteGradePreset.Level),
                Draft(160, 32, RouteGradePreset.UpTwentyFivePercent),
            ]);

        Assert.Equal([1m, 2m], curve.Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void EveryNewPointMayChangeDirectionOrContinueLevel()
    {
        using var workspace = TestWorkspace.Create();

        var curve = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(0, 32, RouteGradePreset.Level),
                Draft(128, 32, RouteGradePreset.UpTwentyFivePercent),
                Draft(256, 32, RouteGradePreset.Level),
                Draft(384, 32, RouteGradePreset.DownFiftyPercent),
            ]);

        Assert.Equal([1m, 2m, 2m, 0m], curve.Select(static point => point.ElevationMeters));
    }

    [Fact]
    public void ADescentMayContinueBelowItsStartingHeight()
    {
        using var workspace = TestWorkspace.Create();

        var curve = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(0, 32, RouteGradePreset.Level),
                Draft(128, 32, RouteGradePreset.DownFiftyPercent),
            ]);

        Assert.Equal(-1m, curve[1].ElevationMeters);
    }

    [Fact]
    public void ACurvedSegmentUsesItsArcLengthRatherThanItsChord()
    {
        using var workspace = TestWorkspace.Create();
        var curve = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(
                    0,
                    0,
                    RouteGradePreset.Level,
                    RoutePointMode.Aligned,
                    new AuthoringPixelOffset { X = 0, Y = 96 }),
                Draft(
                    128,
                    0,
                    RouteGradePreset.UpTwentyFivePercent,
                    RoutePointMode.Aligned,
                    new AuthoringPixelOffset { X = 0, Y = 96 }),
            ]);

        Assert.True(curve[1].ElevationMeters > 2m);
        var geometry = RouteSurfaceGeometry.Prepare(workspace.Metrics, curve);
        Assert.Equal(
            geometry.TotalLengthMeters * 0.25,
            (double)(curve[1].ElevationMeters - curve[0].ElevationMeters),
            precision: 5);
    }

    [Fact]
    public void ReshapedAutomaticHandlesRecomputeEarlierAnchorHeights()
    {
        using var workspace = TestWorkspace.Create();
        var firstTwo = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(0, 0, RouteGradePreset.Level, RoutePointMode.Aligned),
                Draft(128, 0, RouteGradePreset.UpTwentyFivePercent, RoutePointMode.Aligned),
            ]);
        var withTurn = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            [
                Draft(0, 0, RouteGradePreset.Level, RoutePointMode.Aligned),
                Draft(128, 0, RouteGradePreset.UpTwentyFivePercent, RoutePointMode.Aligned),
                Draft(128, 128, RouteGradePreset.Level, RoutePointMode.Aligned),
            ]);

        Assert.Equal(2m, firstTwo[1].ElevationMeters);
        Assert.True(withTurn[1].ElevationMeters > firstTwo[1].ElevationMeters);
        Assert.Equal(withTurn[1].ElevationMeters, withTurn[2].ElevationMeters);
    }

    [Fact]
    public void LongDraftsNeverAccumulateRoundedAnchorHeights()
    {
        using var workspace = TestWorkspace.Create();
        var draft = Enumerable.Range(0, 101)
            .Select(index => Draft(
                index,
                index,
                index == 0
                    ? RouteGradePreset.Level
                    : RouteGradePreset.UpTwentyFivePercent))
            .ToArray();

        var curve = RouteSurfaceEditing.ResolveGradedCurve(
            workspace.Metrics,
            1m,
            draft);

        var expected = Math.Round(
            (decimal)(1.0 + 100.0 * Math.Sqrt(2.0) / 32.0 * 0.25),
            6,
            MidpointRounding.AwayFromZero);
        Assert.Equal(expected, curve[^1].ElevationMeters);
        Assert.Equal(1.011049m, curve[1].ElevationMeters);
    }

    [Fact]
    public void RemoveDropsExactlyTheNamedRoute()
    {
        using var workspace = TestWorkspace.Create();
        var scene = RouteSurfaceEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 32, 1m), Point(160, 32, 1m)],
            "grass");
        scene = RouteSurfaceEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 96, 1m), Point(160, 96, 1m)],
            "grass");

        var removed = RouteSurfaceEditing.Remove(scene, "route_0001");

        Assert.Equal("route_0002", Assert.Single(removed.RouteSurfaces).RouteSurfaceId);
    }

    [Fact]
    public void FindAtUsesTheAuthoredBandRatherThanOnlyTheCenterline()
    {
        using var workspace = TestWorkspace.Create();
        var scene = RouteSurfaceEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 64, 1m, 2m), Point(160, 64, 2m, 2m)],
            "grass");

        Assert.NotNull(RouteSurfaceEditing.FindAt(scene, workspace.Metrics, 96, 88));
        Assert.Null(RouteSurfaceEditing.FindAt(scene, workspace.Metrics, 96, 112));
    }

    [Fact]
    public void PlaceRefusesAnOffQuantumStartingHeight()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            RouteSurfaceEditing.Place(
                TestScenes.Instance(workspace),
                workspace.Terrain,
                workspace.Metrics,
                [Point(32, 32, 1.1m), Point(160, 32, 2m)],
                "grass"));

        Assert.Contains("0.125 m", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceAcceptsDerivedLaterHeightsBetweenTheQuantum()
    {
        using var workspace = TestWorkspace.Create();

        var scene = RouteSurfaceEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            workspace.Metrics,
            [Point(32, 32, 1m), Point(160, 32, 1.1m)],
            "grass");

        Assert.Equal(1.1m, Assert.Single(scene.RouteSurfaces).Points[1].ElevationMeters);
        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
    }

    private static GradedRouteDraftPoint Draft(
        int x,
        int y,
        RouteGradePreset grade,
        RoutePointMode mode = RoutePointMode.Linear,
        AuthoringPixelOffset? draggedHandleOut = null) => new(
            x,
            y,
            mode,
            RouteSurfaceEditing.DefaultWidthMeters,
            grade,
            draggedHandleOut);

    private static RouteSurfacePointDocument Point(
        int x,
        int y,
        decimal elevation,
        decimal width = RouteSurfaceEditing.DefaultWidthMeters) =>
        RouteSurfaceEditing.Point(x, y, RoutePointMode.Linear, elevation, width);
}
