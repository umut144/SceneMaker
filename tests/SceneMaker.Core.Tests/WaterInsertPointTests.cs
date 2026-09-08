using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Putting a point into a river that is already drawn. The whole difficulty is
/// that it must change nothing except that there is now somewhere to take hold.
/// </summary>
public sealed class WaterInsertPointTests
{
    /// <summary>
    /// The load-bearing one. A point dropped into a bend with zero handles
    /// would straighten what the author drew, so the segment is split by de
    /// Casteljau and the two neighbours give up exactly the part of their
    /// handles the split takes. What is left is the grid: a curve point belongs
    /// on it, so the new point moves to the nearest grid position and carries
    /// the curve with it, by less than a water cell.
    /// </summary>
    [Fact]
    public void InsertingAPointLeavesTheBendWhereItWas()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bend(workspace);
        var before = WaterGeometry.Centerline(scene.WaterBodies[0].Points);

        var after = WaterEditing.InsertPoint(scene, workspace.Metrics, "river_0001", 240, 160);
        var body = after.WaterBodies[0];

        Assert.Equal(3, body.Points.Count);
        var moved = WaterGeometry.Centerline(body.Points);
        Assert.All(
            moved,
            point => Assert.True(
                DistanceToPolyline(point, before) <= workspace.Metrics.AuthoringPixelsPerWaterCell,
                $"the curve moved {DistanceToPolyline(point, before):0.##} authoring pixels"));

        // The neighbours kept their far handles and gave up only what the split
        // took from the near ones.
        Assert.Equal(scene.WaterBodies[0].Points[0].HandleInAuthoringPx, body.Points[0].HandleInAuthoringPx);
        Assert.Equal(scene.WaterBodies[0].Points[^1].HandleOutAuthoringPx, body.Points[^1].HandleOutAuthoringPx);
        DocumentValidation.ValidateGrid(after, workspace.Metrics);
    }

    /// <summary>
    /// The section and the width come from the curve, not from a tool's
    /// defaults - inserting a point is not authoring a value.
    /// </summary>
    [Fact]
    public void TheNewPointTakesItsValuesFromTheRiverItSitsOn()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 3.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(288, 32, WaterPointMode.Linear, 1.0m, 1.5m, 9.0m, 6.0m),
            ],
            "river");

        // Halfway along, so every value should be halfway between the two ends.
        var inserted = WaterEditing
            .InsertPoint(scene, workspace.Metrics, "river_0001", 160, 32)
            .WaterBodies[0].Points[1];

        Assert.Equal(160, inserted.PositionAuthoringPx.X);
        Assert.Equal(2.0m, inserted.ElevationMeters);
        Assert.Equal(1.0m, inserted.ChannelDepthMeters);
        Assert.Equal(7.0m, inserted.ClearanceAboveMeters);
        Assert.Equal(4.0m, inserted.WidthMeters);
    }

    /// <summary>A straight river stays straight: the split of a line is a line.</summary>
    [Fact]
    public void AStraightRiverKeepsItsStraightness()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(288, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");

        var body = WaterEditing
            .InsertPoint(scene, workspace.Metrics, "river_0001", 160, 32)
            .WaterBodies[0];

        Assert.All(body.Points, point => Assert.Equal(32, point.PositionAuthoringPx.Y));
        Assert.All(
            body.Points,
            point => Assert.True(
                point.HandleInAuthoringPx.IsZero() && point.HandleOutAuthoringPx.IsZero(),
                "a split line needs no handles, and a Linear point is the absence of them"));
        Assert.All(body.Points, point => Assert.Equal(WaterPointMode.Linear, point.Mode));
    }

    [Fact]
    public void APositionThatIsNotOnThatRiverIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bend(workspace);

        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.InsertPoint(scene, workspace.Metrics, "river_0001", 32, 640));
        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.InsertPoint(scene, workspace.Metrics, "river_0404", 240, 160));

        // And where a point already is there is nothing to insert.
        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.InsertPoint(scene, workspace.Metrics, "river_0001", 32, 32));
    }

    /// <summary>A river with a real bend in it, so a split has something to preserve.</summary>
    private static SceneDocument Bend(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(
                    32, 32, WaterPointMode.Aligned, 2.0m, 0.5m, 5.0m, 2.0m,
                    handleIn: new AuthoringPixelOffset { X = -64, Y = 0 },
                    handleOut: new AuthoringPixelOffset { X = 160, Y = 96 }),
                WaterEditing.Point(
                    416, 320, WaterPointMode.Aligned, 2.0m, 0.5m, 5.0m, 2.0m,
                    handleIn: new AuthoringPixelOffset { X = -96, Y = -160 },
                    handleOut: new AuthoringPixelOffset { X = 64, Y = 0 }),
            ],
            "river");

    private static double DistanceToPolyline(ChainPoint point, IReadOnlyList<ChainPoint> line)
    {
        var best = double.MaxValue;
        for (var index = 0; index + 1 < line.Count; index++)
        {
            var ax = line[index].X;
            var ay = line[index].Y;
            var dx = line[index + 1].X - ax;
            var dy = line[index + 1].Y - ay;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared <= 0.0
                ? 0.0
                : Math.Clamp(((point.X - ax) * dx + (point.Y - ay) * dy) / lengthSquared, 0.0, 1.0);
            var x = ax + t * dx - point.X;
            var y = ay + t * dy - point.Y;
            best = Math.Min(best, Math.Sqrt(x * x + y * y));
        }
        return best;
    }
}
