using System.Globalization;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class LayeredSceneColumnsTests
{
    [Fact]
    public void FlatTerrainIsAnUnboundedSolidAndClipsAtThePlane()
    {
        using var workspace = TestWorkspace.Create();
        var columns = LayeredSceneColumns.Prepare(
            TestScenes.Instance(workspace), workspace.Metrics);

        var column = columns.AtWaterCell(0, 0);

        Assert.Equal(
            [new LayeredColumnSpan(null, 1m, "grass", LayeredColumnSpanKind.TerrainSolid, null)],
            column.TerrainSolids);
        Assert.Empty(column.Fills);
        Assert.Empty(column.Surfaces);
        Assert.Equal(
            new VisibleLayeredSurface(
                1m, "grass", LayeredColumnSpanKind.TerrainSolid, null, false),
            column.VisibleAt());
        Assert.Equal(
            new VisibleLayeredSurface(
                0.5m, "grass", LayeredColumnSpanKind.TerrainSolid, null, true),
            column.VisibleAt(0.5m));
    }

    [Fact]
    public void ASectionClipsAHillAndAPlaneAboveItLeavesTheWholeHill()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m);
        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(5m, column.VisibleAt(5m)!.ElevationMeters);
        Assert.True(column.VisibleAt(5m)!.IsSectionFace);
        Assert.Equal(8m, column.VisibleAt(8m)!.ElevationMeters);
        Assert.True(column.VisibleAt(8m)!.IsSectionFace);
        Assert.Equal(10m, column.VisibleAt(10m)!.ElevationMeters);
        Assert.False(column.VisibleAt(10m)!.IsSectionFace);
        Assert.Equal(column.VisibleAt(), column.VisibleAt(12m));
    }

    [Fact]
    public void AWaterTunnelLeavesFloorAndRoofAndRetainsItsFill()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            Hill(TestScenes.Instance(workspace), workspace, 10m),
            WaterBody("river_0001", surface: 2m, depth: 0.5m, clearance: 5m));

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(
            [
                new LayeredColumnSpan(null, 1.5m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
                new LayeredColumnSpan(7m, 10m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
            ],
            column.TerrainSolids);
        Assert.Equal(
            [new LayeredColumnSpan(
                1.5m, 2m, "river", LayeredColumnSpanKind.IndependentFill, "river_0001")],
            column.Fills);
    }

    [Theory]
    [InlineData("1", "1", LayeredColumnSpanKind.TerrainSolid, true)]
    [InlineData("1.5", "1.5", LayeredColumnSpanKind.IndependentFill, true)]
    [InlineData("1.75", "1.75", LayeredColumnSpanKind.IndependentFill, true)]
    [InlineData("2", "2", LayeredColumnSpanKind.IndependentFill, false)]
    [InlineData("5", "2", LayeredColumnSpanKind.IndependentFill, false)]
    [InlineData("7", "7", LayeredColumnSpanKind.TerrainSolid, true)]
    [InlineData("8", "8", LayeredColumnSpanKind.TerrainSolid, true)]
    [InlineData("10", "10", LayeredColumnSpanKind.TerrainSolid, false)]
    [InlineData("12", "10", LayeredColumnSpanKind.TerrainSolid, false)]
    public void ASectionFindsTheHighestRemainingTunnelSurface(
        string clipText,
        string expectedElevationText,
        LayeredColumnSpanKind expectedKind,
        bool expectedSectionFace)
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            Hill(TestScenes.Instance(workspace), workspace, 10m),
            WaterBody("river_0001", surface: 2m, depth: 0.5m, clearance: 5m));
        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        var visible = column.VisibleAt(decimal.Parse(clipText, CultureInfo.InvariantCulture));

        Assert.NotNull(visible);
        Assert.Equal(
            decimal.Parse(expectedElevationText, CultureInfo.InvariantCulture),
            visible.ElevationMeters);
        Assert.Equal(expectedKind, visible.Kind);
        Assert.Equal(expectedSectionFace, visible.IsSectionFace);
    }

    [Theory]
    [InlineData("1", "1", "2", "2", LayeredColumnSpanKind.IndependentFill)]
    [InlineData("2", "1", "3", "2", LayeredColumnSpanKind.IndependentFill)]
    [InlineData("2.125", "1", "3.125", null, null)]
    [InlineData("6", "1", "7", "7", LayeredColumnSpanKind.TerrainSolid)]
    [InlineData("7", "1", "8", "8", LayeredColumnSpanKind.TerrainSolid)]
    public void AFiniteSectionBandShowsNothingBelowItsStart(
        string startText,
        string offsetText,
        string upperText,
        string? expectedElevationText,
        LayeredColumnSpanKind? expectedKind)
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            Hill(TestScenes.Instance(workspace), workspace, 10m),
            WaterBody("river_0001", surface: 2m, depth: 0.5m, clearance: 5m));
        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);
        var start = decimal.Parse(startText, CultureInfo.InvariantCulture);
        var offset = decimal.Parse(offsetText, CultureInfo.InvariantCulture);

        Assert.Equal(
            decimal.Parse(upperText, CultureInfo.InvariantCulture),
            start + offset);
        var visible = column.VisibleBetween(start, start + offset);

        if (expectedElevationText is null)
        {
            Assert.Null(visible);
            return;
        }
        Assert.NotNull(visible);
        Assert.Equal(
            decimal.Parse(expectedElevationText, CultureInfo.InvariantCulture),
            visible.ElevationMeters);
        Assert.Equal(expectedKind, visible.Kind);
    }

    [Fact]
    public void AFiniteSectionBandRejectsReversedBounds()
    {
        using var workspace = TestWorkspace.Create();
        var column = LayeredSceneColumns
            .Prepare(TestScenes.Instance(workspace), workspace.Metrics)
            .AtWaterCell(0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => column.VisibleBetween(2m, 1m));
    }

    [Fact]
    public void AFillWinsAnExactTieWithTheTerrainAtItsBed()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            TestScenes.Instance(workspace),
            WaterBody("river_0001", surface: 1m, depth: 0.5m, clearance: 0m));

        var visible = LayeredSceneColumns
            .Prepare(scene, workspace.Metrics)
            .AtWaterCell(0, 0)
            .VisibleAt(0.5m);

        Assert.NotNull(visible);
        Assert.Equal(0.5m, visible.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentFill, visible.Kind);
        Assert.Equal("river_0001", visible.SourceId);
    }

    [Fact]
    public void EqualFillSurfacesChooseTheOrdinallyFirstSourceId()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            TestScenes.EmptyInstance(),
            WaterBody("river_0002", surface: 2m, depth: 0.5m, clearance: 1m),
            WaterBody("river_0001", surface: 2m, depth: 1m, clearance: 1m));

        var visible = LayeredSceneColumns
            .Prepare(scene, workspace.Metrics)
            .AtWaterCell(0, 0)
            .VisibleAt();

        Assert.NotNull(visible);
        Assert.Equal("river_0001", visible.SourceId);
    }

    [Fact]
    public void WaterRemainsAVisibleFillWhereNoTerrainWasPainted()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            TestScenes.EmptyInstance(),
            WaterBody("river_0001", surface: 2m, depth: 0.5m, clearance: 5m));

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Empty(column.TerrainSolids);
        Assert.Equal(2m, column.VisibleAt()!.ElevationMeters);
        Assert.Null(column.VisibleAt(1m));
    }

    [Fact]
    public void OverlappingCutsResolveIndependentlyOfBodyOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m);
        var first = WaterBody("river_0001", surface: 2m, depth: 1m, clearance: 2m);
        var second = WaterBody("river_0002", surface: 3.5m, depth: 0.5m, clearance: 3.5m);

        var forward = LayeredSceneColumns
            .Prepare(River(scene, first, second), workspace.Metrics)
            .AtWaterCell(0, 0);
        var reverse = LayeredSceneColumns
            .Prepare(River(scene, second, first), workspace.Metrics)
            .AtWaterCell(0, 0);

        Assert.Equal(forward.TerrainSolids, reverse.TerrainSolids);
        Assert.Equal(forward.Fills, reverse.Fills);
        Assert.Equal(
            [
                new LayeredColumnSpan(null, 1m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
                new LayeredColumnSpan(7m, 10m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
            ],
            forward.TerrainSolids);
    }

    [Fact]
    public void DisjointCutsLeaveThreeTerrainSolids()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            Hill(TestScenes.Instance(workspace), workspace, 10m),
            WaterBody("river_0001", surface: 1.5m, depth: 0.5m, clearance: 0.5m),
            WaterBody("river_0002", surface: 5m, depth: 1m, clearance: 1m));

        var solids = LayeredSceneColumns
            .Prepare(scene, workspace.Metrics)
            .AtWaterCell(0, 0)
            .TerrainSolids;

        Assert.Equal(
            [
                new LayeredColumnSpan(null, 1m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
                new LayeredColumnSpan(2m, 4m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
                new LayeredColumnSpan(6m, 10m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
            ],
            solids);
    }

    [Fact]
    public void ACompletelyFloatingCutLeavesTerrainWholeAndItsFillVisible()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            TestScenes.Instance(workspace),
            WaterBody("river_0001", surface: 6m, depth: 1m, clearance: 1m));

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(
            [new LayeredColumnSpan(null, 1m, "grass", LayeredColumnSpanKind.TerrainSolid, null)],
            column.TerrainSolids);
        Assert.Equal(6m, column.VisibleAt()!.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentFill, column.VisibleAt()!.Kind);
    }

    [Fact]
    public void ArbitraryAuthoringPositionsUseTheWaterRasterAndSceneBounds()
    {
        using var workspace = TestWorkspace.Create();
        var columns = LayeredSceneColumns.Prepare(
            TestScenes.Instance(workspace), workspace.Metrics);

        Assert.Equal(
            columns.AtWaterCell(1, 1).TerrainSolids,
            columns.AtAuthoringPosition(31.999, 31.999).TerrainSolids);
        Assert.Empty(columns.AtAuthoringPosition(-0.001, 0).TerrainSolids);
        Assert.Empty(columns.AtAuthoringPosition(192, 0).TerrainSolids);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            columns.AtAuthoringPosition(double.NaN, 0));
    }

    [Fact]
    public void WaterCellsMapToTheContainingTerrainCellAwayFromTheOrigin()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            2,
            1,
            "sand",
            9m);
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        var mapped = columns.AtWaterCell(5, 3).VisibleAt();
        var neighbour = columns.AtWaterCell(3, 3).VisibleAt();

        Assert.NotNull(mapped);
        Assert.Equal("sand", mapped.AssetKey);
        Assert.Equal(9m, mapped.ElevationMeters);
        Assert.NotNull(neighbour);
        Assert.Equal("grass", neighbour.AssetKey);
        Assert.Equal(1m, neighbour.ElevationMeters);
    }

    [Fact]
    public void APreparedSceneResolvesEachInBoundsColumnOnlyOnce()
    {
        using var workspace = TestWorkspace.Create();
        var columns = LayeredSceneColumns.Prepare(
            TestScenes.Instance(workspace), workspace.Metrics);

        var first = columns.AtWaterCell(3, 2);
        var second = columns.AtWaterCell(3, 2);
        var throughAuthoringPosition = columns.AtAuthoringPosition(56, 40);

        Assert.Same(first, second);
        Assert.Same(first, throughAuthoringPosition);
    }

    [Fact]
    public void AnAdditiveRampUsesTheBakedBandAndContinuousElevation()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 1m, 0.5m),
                    RoutePoint(64, 8, 3m, 0.5m)),
            ],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        var elevations = Enumerable.Range(0, 4)
            .Select(x => Assert.Single(columns.AtWaterCell(x, 0).Surfaces).TopMeters)
            .ToArray();

        Assert.Equal([1.25m, 1.75m, 2.25m, 2.75m], elevations);
        Assert.All(
            Enumerable.Range(0, 4),
            x => Assert.Equal(
                LayeredColumnSpanKind.IndependentSurface,
                columns.AtWaterCell(x, 0).VisibleAt()!.Kind));
        Assert.Empty(columns.AtWaterCell(0, 1).Surfaces);
    }

    [Fact]
    public void AnAdditivePathIsOccludedByAHillUntilItIsAboveTheTerrain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(64, 8, 2m, 0.5m)),
            ],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        var underHill = columns.AtWaterCell(0, 0);
        Assert.Equal(10m, underHill.VisibleAt()!.ElevationMeters);
        Assert.Equal(5m, underHill.VisibleAt(5m)!.ElevationMeters);
        Assert.Equal(2.5m, underHill.VisibleBetween(1.5m, 2.5m)!.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.TerrainSolid, underHill.VisibleAt()!.Kind);

        var beyondHill = columns.AtWaterCell(2, 0).VisibleAt();
        Assert.NotNull(beyondHill);
        Assert.Equal(2m, beyondHill.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, beyondHill.Kind);
        Assert.Equal(
            beyondHill,
            columns.AtWaterCell(2, 0).VisibleBetween(1.5m, 2.5m));
        Assert.Null(columns.AtWaterCell(2, 0).VisibleBetween(2.125m, 3.125m));
    }

    [Fact]
    public void StackedPathSurfacesRemainSeparateAndClippingCanRevealTheLowerOne()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(32, 8, 2m, 0.5m)),
                Route(
                    "route_0002",
                    RoutePoint(0, 8, 4m, 0.5m),
                    RoutePoint(32, 8, 4m, 0.5m)),
            ],
        };
        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(2, column.Surfaces.Count);
        Assert.Equal(4m, column.VisibleAt()!.ElevationMeters);
        Assert.Equal(2m, column.VisibleAt(3m)!.ElevationMeters);
        Assert.Null(column.VisibleBetween(2.5m, 3m));
    }

    [Fact]
    public void APathSurfaceWinsATieWithTerrainAndRoutesUseOrdinalIdsToBreakTheirTie()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0002",
                    RoutePoint(0, 8, 1m, 0.5m),
                    RoutePoint(32, 8, 1m, 0.5m),
                    assetKey: "sand"),
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 1m, 0.5m),
                    RoutePoint(32, 8, 1m, 0.5m)),
            ],
        };

        var visible = LayeredSceneColumns
            .Prepare(scene, workspace.Metrics)
            .AtWaterCell(0, 0)
            .VisibleAt();

        Assert.NotNull(visible);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, visible.Kind);
        Assert.Equal("route_0001", visible.SourceId);
        Assert.Equal("grass", visible.AssetKey);
    }

    private static SceneDocument Hill(
        SceneDocument scene,
        TestWorkspace workspace,
        decimal elevation) =>
        ElevationRegionEditing.Place(
            scene,
            workspace.Metrics,
            [
                ElevationRegionEditing.Point(0, 0),
                ElevationRegionEditing.Point(32, 0),
                ElevationRegionEditing.Point(32, 32),
                ElevationRegionEditing.Point(0, 32),
            ],
            elevation);

    private static SceneDocument River(
        SceneDocument scene,
        params WaterBodyDocument[] bodies) =>
        scene with
        {
            WaterBodies = [.. bodies],
        };

    private static WaterBodyDocument WaterBody(
        string id,
        decimal surface,
        decimal depth,
        decimal clearance) =>
        new()
        {
            WaterBodyId = id,
            WaterKind = WaterKind.River,
            AssetKey = "river",
            Points =
            [
                WaterEditing.Point(
                    0, 16, WaterPointMode.Linear, surface, depth, clearance, 1m),
                WaterEditing.Point(
                    32, 16, WaterPointMode.Linear, surface, depth, clearance, 1m),
            ],
        };

    private static RouteSurfaceDocument Route(
        string id,
        RouteSurfacePointDocument first,
        RouteSurfacePointDocument second,
        string assetKey = "grass") => new()
    {
        RouteSurfaceId = id,
        AssetKey = assetKey,
        Points = [first, second],
        Segments =
        [
            new RouteSurfaceSegmentDocument
            {
                SegmentId = $"{id}.segment_0001",
                GradePercent = 0,
                Operation = RouteSegmentOperation.Additive,
            },
        ],
    };

    private static RouteSurfacePointDocument RoutePoint(
        int x,
        int y,
        decimal elevation,
        decimal width) =>
        RouteSurfaceEditing.Point(
            x,
            y,
            RoutePointMode.Linear,
            elevation,
            width);
}
