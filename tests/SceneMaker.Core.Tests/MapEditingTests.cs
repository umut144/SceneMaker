using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// North and East only grow or shrink the far edge, so nothing existing has to
/// move. West and South grow or shrink the near edge instead, which moves the
/// origin - these tests exist because that difference is easy to miss: a
/// West/South implementation that only touched size_cells would validate
/// cleanly while silently sliding every authored Placement, water body,
/// route, bridge and Anchor toward the wrong corner. A Shrink drops whatever
/// Terrain and Placements the removed strip carried, but still refuses to cut
/// into an Elevation Region, a Route, a Bridge, a Water Body or a Template
/// Anchor.
/// </summary>
public sealed class MapEditingTests
{
    [Fact]
    public void ExtendNorthAndEastGrowSizeAndTouchNothingElse()
    {
        using var workspace = TestWorkspace.Create();
        var scene = BuildScene(workspace);

        var north = MapEditing.ExtendNorth(scene, 2, workspace.Metrics);
        Assert.Equal(scene.SizeCells.Width, north.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height + 2, north.SizeCells.Height);
        Assert.Equal(scene.TerrainCells, north.TerrainCells);
        Assert.Equal(scene.Props, north.Props);
        Assert.Equal(scene.Bridges, north.Bridges);
        Assert.Equal(scene.WaterBodies, north.WaterBodies);

        var east = MapEditing.ExtendEast(scene, 3, workspace.Metrics);
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

    [Fact]
    public void ShrinkNorthAndEastShrinkSizeAndTouchNothingElseWhenTheEdgeIsClear()
    {
        using var workspace = TestWorkspace.Create();
        // 8 Cells of margin past where BuildScene's authored content reaches
        // (max 160 px = Cell 5 of 6), and no full Terrain paint, so shrinking
        // the far edge by 2 Cells removes only empty ground.
        var scene = BuildSparseScene(workspace, sizeCells: 8);

        var north = MapEditing.ShrinkNorth(scene, 2, workspace.Metrics);
        Assert.Equal(scene.SizeCells.Width, north.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height - 2, north.SizeCells.Height);
        Assert.Equal(scene.Props, north.Props);
        Assert.Equal(scene.Bridges, north.Bridges);
        Assert.Equal(scene.WaterBodies, north.WaterBodies);

        var east = MapEditing.ShrinkEast(scene, 2, workspace.Metrics);
        Assert.Equal(scene.SizeCells.Width - 2, east.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height, east.SizeCells.Height);
        Assert.Equal(scene.Props, east.Props);
    }

    [Fact]
    public void ShrinkWestAndSouthShrinkSizeAndShiftEveryAuthoredPositionOnItsOwnAxisWhenTheEdgeIsClear()
    {
        using var workspace = TestWorkspace.Create();
        var scene = BuildSparseScene(workspace, sizeCells: 8);
        // PlaceAuthoredContent's nearest authored position to the origin is
        // (32, 32) px - exactly 1 Cell in - so West/South can only shrink by
        // 1 Cell before it would cut into that content.
        const int cells = 1;
        var offset = cells * TestWorkspace.AuthoringPixelsPerCell;

        var west = MapEditing.ShrinkWest(scene, cells, workspace.Metrics);
        Assert.Equal(scene.SizeCells.Width - cells, west.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height, west.SizeCells.Height);
        var prop = Assert.Single(west.Props);
        var originalProp = Assert.Single(scene.Props);
        Assert.Equal(originalProp.PositionAuthoringPx.X - offset, prop.PositionAuthoringPx.X);
        Assert.Equal(originalProp.PositionAuthoringPx.Y, prop.PositionAuthoringPx.Y);

        var south = MapEditing.ShrinkSouth(scene, cells, workspace.Metrics);
        Assert.Equal(scene.SizeCells.Width, south.SizeCells.Width);
        Assert.Equal(scene.SizeCells.Height - cells, south.SizeCells.Height);
        var shiftedProp = Assert.Single(south.Props);
        Assert.Equal(originalProp.PositionAuthoringPx.X, shiftedProp.PositionAuthoringPx.X);
        Assert.Equal(originalProp.PositionAuthoringPx.Y - offset, shiftedProp.PositionAuthoringPx.Y);
    }

    [Fact]
    public void ShrinkDropsTerrainAndPropsInTheRemovedStripInsteadOfRefusing()
    {
        using var workspace = TestWorkspace.Create();
        // A 5 x 5 Instance, fully painted, with one 1 x 1 Cell Placement
        // sitting 2 Cells from each edge along the axis a 2-Cell Shrink on
        // that edge removes, and comfortably clear of the other three edges
        // - including of the near edge, which West and South also shift
        // every Placement toward - so shrinking any one edge by 2 Cells drops
        // exactly the Placement it reaches and leaves the other three be.
        var scene = SceneDocument.CreateInstance("base", 5, 5);
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                scene = TerrainEditing.Paint(scene, workspace.Terrain, x, y, "grass");
            }
        }
        scene = PropEditing.Place(scene, workspace.Props, 80, 116, "stone"); // reaches the north edge
        scene = PropEditing.Place(scene, workspace.Props, 116, 80, "stone"); // reaches the east edge
        scene = PropEditing.Place(scene, workspace.Props, 8, 80, "stone"); // reaches the west edge
        scene = PropEditing.Place(scene, workspace.Props, 80, 8, "stone"); // reaches the south edge
        Assert.Equal(25, scene.TerrainCells.Count);
        Assert.Equal(4, scene.Props.Count);

        var north = MapEditing.ShrinkNorth(scene, 2, workspace.Metrics);
        Assert.Equal(3, north.SizeCells.Height);
        Assert.Equal(15, north.TerrainCells.Count);
        Assert.DoesNotContain(north.TerrainCells, cell => cell.Y >= 3);
        Assert.Equal(3, north.Props.Count);
        Assert.DoesNotContain(north.Props, prop => prop.PositionAuthoringPx.Y > 96);

        var east = MapEditing.ShrinkEast(scene, 2, workspace.Metrics);
        Assert.Equal(3, east.SizeCells.Width);
        Assert.Equal(15, east.TerrainCells.Count);
        Assert.DoesNotContain(east.TerrainCells, cell => cell.X >= 3);
        Assert.Equal(3, east.Props.Count);
        Assert.DoesNotContain(east.Props, prop => prop.PositionAuthoringPx.X > 96);

        // West and South shift what survives, so the dropped Placement is the
        // one that would have landed past the origin rather than past the far
        // edge.
        var west = MapEditing.ShrinkWest(scene, 2, workspace.Metrics);
        Assert.Equal(3, west.SizeCells.Width);
        Assert.Equal(15, west.TerrainCells.Count);
        Assert.Equal(3, west.Props.Count);
        Assert.DoesNotContain(west.Props, prop => prop.PositionAuthoringPx.X < 0);

        var south = MapEditing.ShrinkSouth(scene, 2, workspace.Metrics);
        Assert.Equal(3, south.SizeCells.Height);
        Assert.Equal(15, south.TerrainCells.Count);
        Assert.Equal(3, south.Props.Count);
        Assert.DoesNotContain(south.Props, prop => prop.PositionAuthoringPx.Y < 0);
    }

    [Fact]
    public void ShrinkStillRefusesToCutIntoAnElevationRegionRouteBridgeOrWaterBody()
    {
        using var workspace = TestWorkspace.Create();
        // BuildScene's Terrain paint no longer matters to a Shrink - only
        // these five kinds still refuse it. 2 Cells reaches every one of
        // them from BuildScene's 6 x 6 grid: North cuts the Route (y = 160),
        // East cuts the Bridge's end (x = 160), and West and South both cut
        // the Elevation Region, checked before the rest.
        var scene = BuildScene(workspace);

        Assert.Throws<SceneMakerDocumentException>(() => MapEditing.ShrinkNorth(scene, 2, workspace.Metrics));
        Assert.Throws<SceneMakerDocumentException>(() => MapEditing.ShrinkEast(scene, 2, workspace.Metrics));
        Assert.Throws<SceneMakerDocumentException>(() => MapEditing.ShrinkWest(scene, 2, workspace.Metrics));
        Assert.Throws<SceneMakerDocumentException>(() => MapEditing.ShrinkSouth(scene, 2, workspace.Metrics));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExtendAndShrinkRejectANonPositiveCellCountOnEveryEdge(int cells)
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendNorth(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendEast(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendWest(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendSouth(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ShrinkNorth(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ShrinkEast(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ShrinkWest(scene, cells, workspace.Metrics));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ShrinkSouth(scene, cells, workspace.Metrics));
    }

    [Fact]
    public void ExtendAndShrinkRequireMetricsOnEveryEdge()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendNorth(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendEast(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendWest(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ExtendSouth(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ShrinkNorth(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ShrinkEast(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ShrinkWest(scene, 1, null!));
        Assert.Throws<ArgumentNullException>(() => MapEditing.ShrinkSouth(scene, 1, null!));
    }

    /// <summary>
    /// A 6 x 6 Instance carrying one of every kind of authored position: a
    /// Placement, a Template Anchor, a bridge, an elevation region, a route and
    /// a river with non-zero (Aligned) handles.
    /// </summary>
    private static SceneDocument BuildScene(TestWorkspace workspace)
    {
        var scene = TestScenes.Instance(workspace);
        return PlaceAuthoredContent(scene, workspace);
    }

    /// <summary>
    /// The same authored content as <see cref="BuildScene"/>, on a
    /// <paramref name="sizeCells"/> square Instance with no Terrain painted at
    /// all, so a Shrink test can remove a Cell margin without cutting into
    /// anything - <see cref="BuildScene"/>'s full Terrain paint would refuse
    /// every Shrink, which is a separate case its own test covers.
    /// </summary>
    private static SceneDocument BuildSparseScene(TestWorkspace workspace, int sizeCells)
    {
        var scene = SceneDocument.CreateInstance("base", sizeCells, sizeCells);
        return PlaceAuthoredContent(scene, workspace);
    }

    private static SceneDocument PlaceAuthoredContent(SceneDocument scene, TestWorkspace workspace)
    {
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
