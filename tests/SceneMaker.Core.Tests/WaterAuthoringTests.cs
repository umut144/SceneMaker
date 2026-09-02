using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The authored curve and the raster derived from it. The fixture's water grid
/// is 16 authoring pixels, so a water cell's centre sits at 16n + 8 - which is
/// what makes the expected cells in these tests something one can count out by
/// hand rather than record from a run.
/// </summary>
public sealed class WaterAuthoringTests
{
    [Fact]
    public void AStraightRiverCoversExactlyTheCorridorItsWidthDescribes()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace), widthMeters: 1.0m);

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        // One metre of width is two rows of water cells, and the butt caps end
        // the corridor at the source and the mouth instead of rounding past them.
        List<(int X, int Y)> expected = [];
        for (var y = 1; y <= 2; y++)
        {
            for (var x = 2; x <= 9; x++) expected.Add((x, y));
        }
        Assert.Equal(expected, cells.Select(static cell => (cell.X, cell.Y)));
    }

    [Fact]
    public void AWiderRiverCoversProportionallyMoreRows()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace), widthMeters: 2.0m);

        var rows = WaterGeometry
            .Corridor(scene, workspace.Metrics, scene.WaterBodies[0])
            .Select(static cell => cell.Y)
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal([0, 1, 2, 3], rows);
    }

    [Fact]
    public void WidthIsInterpolatedBetweenTheAuthoredPoints()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 96, WaterPointMode.Linear, widthMeters: 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, widthMeters: 3.0m),
            ],
            "river");

        var body = Assert.Single(scene.WaterBodies);
        var centerline = WaterGeometry.Flatten(body.Points);
        Assert.Equal(
            2.0m,
            WaterGeometry.SampleAt(body.Points, centerline.AnchorStations, 64.0).WidthMeters);

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, body);
        Assert.Equal(2, cells.Count(cell => cell.X == 2));
        Assert.Equal(6, cells.Count(cell => cell.X == 9));
    }

    [Fact]
    public void TheCorridorIsCanonicallyOrderedByRowThenColumn()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace), widthMeters: 2.0m);

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        Assert.Equal(
            cells.OrderBy(static cell => cell.Y).ThenBy(static cell => cell.X).ToList(),
            cells);
        Assert.Equal(cells.Count, cells.Select(static cell => (cell.X, cell.Y)).Distinct().Count());
    }

    [Fact]
    public void ARiverRunningOffTheMapIsClippedRatherThanRefused()
    {
        using var workspace = TestWorkspace.Create();
        // The Scene is 6 cells - 12 water cells - high, so a 2 m river along its
        // northern edge reaches past the last row.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 176, WaterPointMode.Linear, widthMeters: 2.0m),
                WaterEditing.Point(160, 176, WaterPointMode.Linear, widthMeters: 2.0m),
            ],
            "river");

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        Assert.NotEmpty(cells);
        Assert.Equal(11, cells.Max(static cell => cell.Y));
    }

    [Fact]
    public void AnAlignedPointBendsTheCurveAwayFromTheStraightLine()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(
                    32, 32, WaterPointMode.Aligned,
                    widthMeters: 1.0m,
                    handleIn: new AuthoringPixelOffset { X = 0, Y = -64 },
                    handleOut: new AuthoringPixelOffset { X = 0, Y = 64 }),
                WaterEditing.Point(
                    160, 32, WaterPointMode.Aligned,
                    widthMeters: 1.0m,
                    handleIn: new AuthoringPixelOffset { X = 0, Y = 64 },
                    handleOut: new AuthoringPixelOffset { X = 0, Y = -64 }),
            ],
            "river");
        var body = scene.WaterBodies[0];

        // The curve's midpoint sits at (96, 80), well north of the straight line
        // between the two authored points.
        Assert.True(WaterGeometry.Contains(workspace.Metrics, body, 96, 80));
        Assert.False(WaterGeometry.Contains(workspace.Metrics, body, 96, 32));
    }

    [Fact]
    public void ACurveThatBendsBackNearItsMouthKeepsItsWholeBody()
    {
        using var workspace = TestWorkspace.Create();
        // A hook whose last segment runs back down to the south-west, over the
        // half of the map its own source sits in. An end cap that reached
        // beyond its own segment would cut that half away, and the author would
        // see a river that stops halfway - which is exactly what it did.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, widthMeters: 1.0m),
                WaterEditing.Point(32, 160, WaterPointMode.Linear, widthMeters: 1.0m),
                WaterEditing.Point(160, 160, WaterPointMode.Linear, widthMeters: 1.0m),
                WaterEditing.Point(96, 96, WaterPointMode.Linear, widthMeters: 1.0m),
            ],
            "river");
        var body = scene.WaterBodies[0];

        // Every authored point lies on its own centerline, so every one of them
        // is in its own corridor. Nothing else about a curve's shape can make
        // that untrue.
        foreach (var point in body.Points)
        {
            Assert.True(
                WaterGeometry.Contains(
                    workspace.Metrics,
                    body,
                    point.PositionAuthoringPx.X,
                    point.PositionAuthoringPx.Y),
                $"({point.PositionAuthoringPx.X}, {point.PositionAuthoringPx.Y}) fell out of its own corridor.");
        }
    }

    [Fact]
    public void TheCapsStillEndTheCorridorSquareAtBothEnds()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace));
        var body = scene.WaterBodies[0];

        // The river runs from (32, 32) to (160, 32). Beside it is water,
        // before the source and past the mouth is not.
        Assert.True(WaterGeometry.Contains(workspace.Metrics, body, 96, 40));
        Assert.False(WaterGeometry.Contains(workspace.Metrics, body, 24, 32));
        Assert.False(WaterGeometry.Contains(workspace.Metrics, body, 168, 32));
    }

    [Fact]
    public void RiversTakeStableIdsInCanonicalOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace));
        scene = River(workspace, scene);

        Assert.Equal(
            ["river_0001", "river_0002"],
            scene.WaterBodies.Select(static body => body.WaterBodyId));
    }

    [Fact]
    public void ThePointerPicksTheBodyItIsOverAndNothingElsewhere()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace));

        Assert.Equal(
            "river_0001",
            WaterEditing.FindAt(scene, workspace.Metrics, 96, 32)?.WaterBodyId);
        Assert.Null(WaterEditing.FindAt(scene, workspace.Metrics, 96, 160));
    }

    [Fact]
    public void RemovingABodyLeavesTheRestOfTheSceneAlone()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace));

        var without = WaterEditing.Remove(scene, "river_0001");

        Assert.Empty(without.WaterBodies);
        Assert.Equal(scene.TerrainCells.Count, without.TerrainCells.Count);
        Assert.Same(scene, WaterEditing.Remove(scene, "river_0009"));
    }

    [Fact]
    public void ALinearPointCannotCarryHandles()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace, TestScenes.Instance(workspace));
        var broken = scene with
        {
            WaterBodies =
            [
                scene.WaterBodies[0] with
                {
                    Points =
                    [
                        scene.WaterBodies[0].Points[0] with
                        {
                            HandleOutAuthoringPx = new AuthoringPixelOffset { X = 8, Y = 0 },
                        },
                        scene.WaterBodies[0].Points[1],
                    ],
                },
            ],
        };

        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(broken));
    }

    [Fact]
    public void ARiverNeedsASourceAndAMouth()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Throws<SceneMakerDocumentException>(() => WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [WaterEditing.Point(32, 32, WaterPointMode.Linear)],
            "river"));
    }

    [Fact]
    public void CurvePointsMustSitOnTheWaterGrid()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(33, 32, WaterPointMode.Linear, widthMeters: 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, widthMeters: 1.0m),
            ],
            "river");

        // The shape of the curve is fine; where it sits is a grid question, and
        // that is decided at the IO boundary.
        DocumentValidation.Validate(scene);
        Assert.Throws<SceneMakerDocumentException>(
            () => DocumentValidation.ValidateGrid(scene, workspace.Metrics));
    }

    [Fact]
    public void ARiverOverUnauthoredTerrainWarnsInsteadOfBlockingTheExport()
    {
        using var workspace = TestWorkspace.Create();
        var covered = River(workspace, TestScenes.Instance(workspace));
        var bare = River(workspace, TestScenes.EmptyInstance());

        Assert.Empty(SceneExport.Warnings(covered, workspace.Metrics));
        var warning = Assert.Single(SceneExport.Warnings(bare, workspace.Metrics));
        Assert.Contains("river_0001", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAuthoredCurveSurvivesBeingWrittenAndReadBack()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear),
                WaterEditing.Point(
                    96, 96, WaterPointMode.Aligned,
                    handleIn: new AuthoringPixelOffset { X = -21, Y = 0 },
                    handleOut: new AuthoringPixelOffset { X = 21, Y = 0 }),
                WaterEditing.Point(160, 32, WaterPointMode.Linear),
            ],
            "river");

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));

        var body = Assert.Single(restored.WaterBodies);
        Assert.Equal("river_0001", body.WaterBodyId);
        Assert.Equal(WaterKind.River, body.WaterKind);
        Assert.Equal("river", body.AssetKey);
        Assert.All(body.Points, point => Assert.Equal(4.0m, point.WidthMeters));
        // Compared as a sequence rather than as whole bodies: a record holding a
        // List compares that List by reference, so equal curves would not be
        // equal documents.
        Assert.Equal(scene.WaterBodies[0].Points, body.Points);
    }

    private static SceneDocument River(
        TestWorkspace workspace,
        SceneDocument scene,
        decimal widthMeters = 1.0m) =>
        WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, widthMeters: widthMeters),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, widthMeters: widthMeters),
            ],
            "river");
}
