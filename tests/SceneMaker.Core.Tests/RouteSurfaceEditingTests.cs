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
    public void PlaceRefusesAnOffQuantumHeight()
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

    private static RouteSurfacePointDocument Point(
        int x,
        int y,
        decimal elevation,
        decimal width = RouteSurfaceEditing.DefaultWidthMeters) =>
        RouteSurfaceEditing.Point(x, y, RoutePointMode.Linear, elevation, width);
}
