using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// North and East only grow the far edge, so nothing existing has to move.
/// West and South grow the near edge instead, which moves the origin - these
/// tests exist because that difference is easy to miss: a West/South
/// implementation that only touched size_cells would validate cleanly while
/// silently sliding every authored Placement, water body, route, bridge and
/// Anchor toward the wrong corner.
/// </summary>
public sealed class MapEditingTests
{
    [Fact]
    public void ExtendNorthAndEastGrowSizeAndTouchNothingElse()
    {
        using var workspace = TestWorkspace.Create();
        var scene = BuildScene(workspace);

        var north = MapEditing.ExtendNorth(scene, 2);
        Assert.Equal(scene.SizeCells.Width, north.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height + 2, north.SizeCells.Height);
        Assert.Equal(scene.TerrainCells, north.TerrainCells);
        Assert.Equal(scene.Props, north.Props);
        Assert.Equal(scene.Bridges, north.Bridges);
        Assert.Equal(scene.WaterBodies, north.WaterBodies);

        var east = MapEditing.ExtendEast(scene, 3);
        Assert.Equal(scene.SizeCells.Width + 3, east.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height, east.SizeCells.Height);
        Assert.Equal(scene.TerrainCells, east.TerrainCells);
        Assert.Equal(scene.Props, east.Props);
    }

    [Fact]
    public void ExtendWestGrowsWidthAndShiftsEveryAuthoredPositionOnXOnly()
    {
        using var workspace = TestWorkspace.Create();
        var scene = BuildScene(workspace);
        const int cells = 2;
        var offset = cells * TestWorkspace.AuthoringPixelsPerCell;

        var shifted = MapEditing.ExtendWest(scene, cells, workspace.Metrics);

        Assert.Equal(scene.SizeCells.Width + cells, shifted.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height, shifted.SizeCells.Height);

        for (var index = 0; index < scene.TerrainCells.Count; index++)
        {
            Assert.Equal(scene.TerrainCells[index].X + cells, shifted.TerrainCells[index].X);
            Assert.Equal(scene.TerrainCells[index].Y, shifted.TerrainCells[index].Y);
        }

        var prop = Assert.Single(shifted.Props);
        var originalProp = Assert.Single(scene.Props);
        Assert.Equal(originalProp.PositionAuthoringPx.X + offset, prop.PositionAuthoringPx.X);
        Assert.Equal(originalProp.PositionAuthoringPx.Y, prop.PositionAuthoringPx.Y);

        var anchor = Assert.Single(shifted.TemplateAnchors);
        var originalAnchor = Assert.Single(scene.TemplateAnchors);
        Assert.Equal(originalAnchor.PositionAuthoringPx.X + offset, anchor.PositionAuthoringPx.X);
        Assert.Equal(originalAnchor.PositionAuthoringPx.Y, anchor.PositionAuthoringPx.Y);

        var bridge = Assert.Single(shifted.Bridges);
        var originalBridge = Assert.Single(scene.Bridges);
        Assert.Equal(originalBridge.StartAuthoringPx.X + offset, bridge.StartAuthoringPx.X);
        Assert.Equal(originalBridge.StartAuthoringPx.Y, bridge.StartAuthoringPx.Y);
        Assert.Equal(originalBridge.EndAuthoringPx.X + offset, bridge.EndAuthoringPx.X);
        Assert.Equal(originalBridge.EndAuthoringPx.Y, bridge.EndAuthoringPx.Y);

        var region = Assert.Single(shifted.ElevationRegions);
        var originalRegion = Assert.Single(scene.ElevationRegions);
        for (var index = 0; index < originalRegion.Points.Count; index++)
        {
            Assert.Equal(
                originalRegion.Points[index].PositionAuthoringPx.X + offset,
                region.Points[index].PositionAuthoringPx.X);
            Assert.Equal(
                originalRegion.Points[index].PositionAuthoringPx.Y,
                region.Points[index].PositionAuthoringPx.Y);
        }

        var route = Assert.Single(shifted.RouteSurfaces);
        var originalRoute = Assert.Single(scene.RouteSurfaces);
        for (var index = 0; index < originalRoute.Points.Count; index++)
        {
            Assert.Equal(
                originalRoute.Points[index].PositionAuthoringPx.X + offset,
                route.Points[index].PositionAuthoringPx.X);
        }

        var water = Assert.Single(shifted.WaterBodies);
        var originalWater = Assert.Single(scene.WaterBodies);
        for (var index = 0; index < originalWater.Points.Count; index++)
        {
            var before = originalWater.Points[index];
            var after = water.Points[index];
            Assert.Equal(before.PositionAuthoringPx.X + offset, after.PositionAuthoringPx.X);
            Assert.Equal(before.PositionAuthoringPx.Y, after.PositionAuthoringPx.Y);

            // Handles are a direction and a length away from their point, never
            // a place on the map, and must not shift with it.
            Assert.Equal(before.HandleInAuthoringPx, after.HandleInAuthoringPx);
            Assert.Equal(before.HandleOutAuthoringPx, after.HandleOutAuthoringPx);
        }
    }

    [Fact]
    public void ExtendSouthGrowsHeightAndShiftsEveryAuthoredPositionOnYOnly()
    {
        using var workspace = TestWorkspace.Create();
        var scene = BuildScene(workspace);
        const int cells = 3;
        var offset = cells * TestWorkspace.AuthoringPixelsPerCell;

        var shifted = MapEditing.ExtendSouth(scene, cells, workspace.Metrics);

        Assert.Equal(scene.SizeCells.Width, shifted.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height + cells, shifted.SizeCells.Height);

        for (var index = 0; index < scene.TerrainCells.Count; index++)
        {
            Assert.Equal(scene.TerrainCells[index].X, shifted.TerrainCells[index].X);
            Assert.Equal(scene.TerrainCells[index].Y + cells, shifted.TerrainCells[index].Y);
        }

        var prop = Assert.Single(shifted.Props);
        var originalProp = Assert.Single(scene.Props);
        Assert.Equal(originalProp.PositionAuthoringPx.X, prop.PositionAuthoringPx.X);
        Assert.Equal(originalProp.PositionAuthoringPx.Y + offset, prop.PositionAuthoringPx.Y);

        var bridge = Assert.Single(shifted.Bridges);
        var originalBridge = Assert.Single(scene.Bridges);
        Assert.Equal(originalBridge.StartAuthoringPx.Y + offset, bridge.StartAuthoringPx.Y);
        Assert.Equal(originalBridge.EndAuthoringPx.Y + offset, bridge.EndAuthoringPx.Y);

        var water = Assert.Single(shifted.WaterBodies);
        var originalWater = Assert.Single(scene.WaterBodies);
        for (var index = 0; index < originalWater.Points.Count; index++)
        {
            var before = originalWater.Points[index];
            var after = water.Points[index];
            Assert.Equal(before.PositionAuthoringPx.X, after.PositionAuthoringPx.X);
            Assert.Equal(before.PositionAuthoringPx.Y + offset, after.PositionAuthoringPx.Y);
            Assert.Equal(before.HandleInAuthoringPx, after.HandleInAuthoringPx);
            Assert.Equal(before.HandleOutAuthoringPx, after.HandleOutAuthoringPx);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExtendWestAndSouthRejectANonPositiveCellCount(int cells)
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapEditing.ExtendWest(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapEditing.ExtendSouth(scene, cells, workspace.Metrics));
    }

    [Fact]
    public void ExtendWestAndSouthRequireMetrics()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendWest(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendSouth(scene, 1, null!));
    }

    /// <summary>
    /// A 6 x 6 Instance carrying one of every kind of authored position: a
    /// Placement, a Template Anchor, a bridge, an elevation region, a route and
    /// a river with non-zero (Aligned) handles.
    /// </summary>
    private static SceneDocument BuildScene(TestWorkspace workspace)
    {
        var scene = TestScenes.Instance(workspace);
        scene = PropEditing.Place(scene, workspace.Props, 64, 64, "stone");
        scene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 96, 96, groupNumber: 1);
        scene = BridgeEditing.Place(
            scene,
            workspace.Props,
            32, 96,
            160, 96,
            "portal",
            "portal",
            BridgeEditing.DefaultWidthMeters,
            1m,
            BridgeEditing.DefaultPlankCount,
            BridgeEditing.DefaultPlankGapMeters);
        scene = ElevationRegionEditing.Place(
            scene,
            workspace.Metrics,
            [
                ElevationRegionEditing.Point(32, 32),
                ElevationRegionEditing.Point(96, 32),
                ElevationRegionEditing.Point(96, 96),
                ElevationRegionEditing.Point(32, 96),
            ],
            1m);
        scene = RouteSurfaceEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Metrics,
            [
                RouteSurfaceEditing.Point(32, 160, RoutePointMode.Linear, elevationMeters: 1m),
                RouteSurfaceEditing.Point(96, 160, RoutePointMode.Linear, elevationMeters: 1m),
            ],
            [RouteSegmentAuthoring.Additive(RouteGradePreset.Level)],
            "grass");
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(
                    32, 144, WaterPointMode.Aligned,
                    elevationMeters: 1m,
                    channelDepthMeters: 0.3m,
                    clearanceAboveMeters: 0.2m,
                    widthMeters: 1.0m,
                    handleIn: new AuthoringPixelOffset { X = -4, Y = 2 },
                    handleOut: new AuthoringPixelOffset { X = 4, Y = -2 }),
                WaterEditing.Point(
                    96, 144, WaterPointMode.Aligned,
                    elevationMeters: 1.125m,
                    channelDepthMeters: 0.3m,
                    clearanceAboveMeters: 0.2m,
                    widthMeters: 1.0m,
                    handleIn: new AuthoringPixelOffset { X = -4, Y = 2 },
                    handleOut: new AuthoringPixelOffset { X = 4, Y = -2 }),
            ],
            "river");
        return scene;
    }
}
