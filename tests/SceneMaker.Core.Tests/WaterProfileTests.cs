using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The section of a river: what it occupies vertically, and how that is filled
/// in between the points the author actually set.
/// </summary>
public sealed class WaterProfileTests
{
    [Fact]
    public void TheWorkedExampleFromTheContractHolds()
    {
        using var workspace = TestWorkspace.Create();
        // Water at 2 m, half a metre deep, five metres of headroom.
        var scene = River(workspace, (2.0m, 2.0m));

        var cell = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0])[0];

        // The bed sits under the water, the cut reaches from the bed to the
        // headroom - Terrain below stays as floor, Terrain above as ceiling.
        Assert.Equal(1.5m, cell.BedMeters);
        Assert.Equal(2.0m, cell.SurfaceMeters);
        Assert.Equal(7.0m, cell.CutTopMeters);
    }

    [Fact]
    public void HeightsAreLinearAlongTheCurveAndFollowArcLengthRatherThanTheParameter()
    {
        using var workspace = TestWorkspace.Create();
        // Three points, deliberately unevenly spaced: 32 authoring pixels from
        // the source to the middle, 128 from there to the mouth. Interpolating
        // over the curve's own parameter would put the middle value halfway
        // along the river; over arc length it stays where it was drawn.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, 3.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(32, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, 1.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        // A cell centre at 24 px is three quarters of the way through the first,
        // short stretch: 3 m falling to 2 m gives 2.25 m.
        Assert.Equal(2.25m, SurfaceAt(cells, x: 1));
        // A centre at 88 px is 56 of the second stretch's 128 pixels: 2 m
        // falling to 1 m gives 1.5625 m, rounded to the millimetre.
        Assert.Equal(1.563m, SurfaceAt(cells, x: 5));
    }

    [Fact]
    public void DepthAndHeadroomAreInterpolatedTogetherWithTheSurface()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, 4.0m, 0.5m, 1.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, 2.0m, 1.5m, 5.0m, 1.0m),
            ],
            "river");

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);
        var cell = cells.First(cell => cell.X == 5);

        // 88 of 160 pixels: surface 4 → 2 gives 2.9, depth 0.5 → 1.5 gives 1.05,
        // headroom 1 → 5 gives 3.2.
        Assert.Equal(2.9m, cell.SurfaceMeters);
        Assert.Equal(2.9m - 1.05m, cell.BedMeters);
        Assert.Equal(2.9m + 3.2m, cell.CutTopMeters);
    }

    [Fact]
    public void BeyondTheEndsTheOutermostPointsHold()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, (4.0m, 2.0m));
        var points = scene.WaterBodies[0].Points;
        var stations = WaterGeometry.Flatten(points).AnchorStations;

        // Inside the butt caps a cell can still project a little past either
        // end; there the outermost authored point holds rather than the line
        // being extended past what the author drew.
        Assert.Equal(4.0m, WaterGeometry.SampleAt(points, stations, -10.0).ElevationMeters);
        Assert.Equal(2.0m, WaterGeometry.SampleAt(points, stations, 1000.0).ElevationMeters);
        Assert.Equal(3.0m, WaterGeometry.SampleAt(points, stations, 80.0).ElevationMeters);
    }

    [Fact]
    public void WaterWhoseBedSitsAboveTheGroundIsAWarning()
    {
        using var workspace = TestWorkspace.Create();
        // The fixture's Terrain stands at 1 m; a river at 5 m has nothing
        // holding it in.
        var scene = River(workspace, (5.0m, 5.0m));

        var warning = Assert.Single(
            SceneExport.Warnings(scene, workspace.Metrics),
            message => message.Contains("floats above the Terrain", StringComparison.Ordinal));

        Assert.Contains("river_0001", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ASurfaceThatClimbsTowardsTheMouthIsAWarning()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, (0.5m, 0.9m));

        var warning = Assert.Single(
            SceneExport.Warnings(scene, workspace.Metrics),
            message => message.Contains("upstream of its own mouth", StringComparison.Ordinal));

        Assert.Contains("river_0001", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ARiverThatFallsAndSitsInItsValleyWarnsAboutNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, (0.9m, 0.5m));

        Assert.Empty(SceneExport.Warnings(scene, workspace.Metrics));
    }

    private static decimal SurfaceAt(IReadOnlyList<WaterCellSpan> cells, int x) =>
        cells.First(cell => cell.X == x).SurfaceMeters;

    /// <summary>
    /// A straight river across the fixture, falling from the first height to the
    /// second. Half a metre deep with five metres of headroom throughout.
    /// </summary>
    private static SceneDocument River(
        TestWorkspace workspace,
        (decimal Source, decimal Mouth) elevations) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, elevations.Source, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, elevations.Mouth, 0.5m, 5.0m, 1.0m),
            ],
            "river");
}
