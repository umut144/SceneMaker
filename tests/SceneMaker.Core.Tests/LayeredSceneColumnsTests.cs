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

    /// <summary>
    /// A deck is a level two-point Path in everything but its record, so the
    /// Section clips it like one: gone above the plane, the river visible
    /// beneath, and the same triangles the export bakes. Before this the
    /// Section view simply had no bridges in it.
    /// </summary>
    [Fact]
    public void ABridgeDeckIsASurfaceTheSectionClipsLikeAPath()
    {
        using var workspace = TestWorkspace.Create();
        // A river running east along y = 3 m, and a bridge crossing it north
        // to south at x = 6 m, four metres wide and four metres up.
        var river = new WaterBodyDocument
        {
            WaterBodyId = "river_0001",
            WaterKind = WaterKind.River,
            AssetKey = "river",
            Points =
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, 2m, 0.5m, 5m, 1m),
                WaterEditing.Point(384, 96, WaterPointMode.Linear, 2m, 0.5m, 5m, 1m),
            ],
        };
        var scene = BridgeEditing.Place(
            River(TestScenes.EmptyInstance(sizeCells: 12), river),
            workspace.Props,
            192, 32, 192, 352,
            "portal", "stone",
            BridgeEditing.DefaultWidthMeters,
            4m,
            BridgeEditing.DefaultPlankCount,
            BridgeEditing.DefaultPlankGapMeters);
        var bridge = Assert.Single(scene.Bridges);
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        var underTheDeck = columns.AtWaterCell(12, 5);
        var deck = underTheDeck.VisibleAt();
        Assert.NotNull(deck);
        Assert.Equal(4m, deck.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, deck.Kind);
        Assert.Equal("portal", deck.AssetKey);
        Assert.Equal(bridge.BridgeId, deck.SourceId);

        // Clip below the deck and the river it crosses is what remains.
        var beneath = underTheDeck.VisibleAt(3m);
        Assert.NotNull(beneath);
        Assert.Equal(2m, beneath.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentFill, beneath.Kind);

        // Beside the deck the river was never covered.
        var beside = columns.AtWaterCell(2, 5).VisibleAt();
        Assert.NotNull(beside);
        Assert.Equal("river", beside.AssetKey);
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

    [Fact]
    public void ASubtractivePathCutsTerrainAndLeavesItsOwnFloor()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(32, 8, 2m, 0.5m),
                    operation: RouteSegmentOperation.Subtractive,
                    clearance: 3m),
            ],
        };

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(
            [
                new LayeredColumnSpan(
                    null, 2m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
                new LayeredColumnSpan(
                    5m, 10m, "grass", LayeredColumnSpanKind.TerrainSolid, null),
            ],
            column.TerrainSolids);
        Assert.Equal(2m, Assert.Single(column.Surfaces).TopMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, column.VisibleAt(4m)!.Kind);
        Assert.Equal(2m, column.VisibleAt(4m)!.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.TerrainSolid, column.VisibleAt(6m)!.Kind);
    }

    [Fact]
    public void AnAdditiveSegmentAfterAPortalDoesNotContinueExcavating()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 8, 2m, 0.5m),
                RoutePoint(32, 8, 2m, 0.5m),
                RoutePoint(64, 8, 2m, 0.5m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Subtractive, 3m),
                Segment("route_0001", 2, RouteSegmentOperation.Additive),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 64) with
        {
            RouteSurfaces = [route],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        Assert.Equal(2, columns.AtWaterCell(0, 0).TerrainSolids.Count);
        Assert.Single(columns.AtWaterCell(2, 0).TerrainSolids);
        Assert.Equal(10m, columns.AtWaterCell(2, 0).TerrainSolids[0].TopMeters);
        Assert.Single(columns.AtWaterCell(3, 0).TerrainSolids);
        Assert.Equal(10m, columns.AtWaterCell(3, 0).TerrainSolids[0].TopMeters);
        Assert.Equal(10m, columns.AtWaterCell(3, 0).VisibleAt()!.ElevationMeters);
    }

    [Fact]
    public void TwoSubtractiveSidesKeepTheirOwnClearanceAtThePortal()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 8, 2m, 0.5m),
                RoutePoint(32, 8, 2m, 0.5m),
                RoutePoint(64, 8, 2m, 0.5m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Subtractive, 1m),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 64) with
        {
            RouteSurfaces = [route],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        Assert.Equal(
            [(decimal?)null, 2m, 3m, 10m],
            columns.AtWaterCell(1, 0).TerrainSolids
                .SelectMany(static span => new[] { span.BottomMeters, span.TopMeters }));
        Assert.Equal(
            [(decimal?)null, 2m, 5m, 10m],
            columns.AtWaterCell(2, 0).TerrainSolids
                .SelectMany(static span => new[] { span.BottomMeters, span.TopMeters }));
    }

    [Fact]
    public void ACornerPortalDoesNotLetTheTunnelCutBackIntoTheAdditiveSide()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 32, 2m, 2m),
                RoutePoint(64, 32, 2m, 2m),
                RoutePoint(64, 96, 2m, 2m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Additive),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 128) with
        {
            RouteSurfaces = [route],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        Assert.Single(columns.AtWaterCell(2, 2).TerrainSolids);
        Assert.Equal(10m, columns.AtWaterCell(2, 2).TerrainSolids[0].TopMeters);
        Assert.Equal(
            [(decimal?)null, 2m, 5m, 10m],
            columns.AtWaterCell(3, 3).TerrainSolids
                .SelectMany(static span => new[] { span.BottomMeters, span.TopMeters }));
    }

    [Fact]
    public void ACurvedTunnelKeepsThePortalRuleOnEveryFlattenedPrimitive()
    {
        using var workspace = TestWorkspace.Create();
        var portal = RoutePoint(64, 64, 2m, 2m) with
        {
            Mode = RoutePointMode.Aligned,
            HandleOutAuthoringPx = new AuthoringPixelOffset { X = 32, Y = 0 },
        };
        var returnPoint = RoutePoint(32, 64, 2m, 2m) with
        {
            Mode = RoutePointMode.Aligned,
            HandleInAuthoringPx = new AuthoringPixelOffset { X = 0, Y = 32 },
        };
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 64, 2m, 2m),
                portal,
                returnPoint,
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Additive),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 128) with
        {
            RouteSurfaces = [route],
        };
        var bake = RouteSurfaceBake.Build(workspace.Metrics, route);

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(3, 4);

        Assert.True(
            bake.Segments[1].EndSampleIndex - bake.Segments[1].StartSampleIndex > 1);
        Assert.Single(column.TerrainSolids);
        Assert.Equal(10m, column.TerrainSolids[0].TopMeters);
        Assert.Contains(column.Surfaces, static surface => surface.SourceId == "route_0001");
    }

    [Fact]
    public void ACellCentreExactlyOnThePortalPlaneIsOpenedByTheTunnel()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 8, 2m, 0.5m),
                RoutePoint(24, 8, 2m, 0.5m),
                RoutePoint(64, 8, 2m, 0.5m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Additive),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 64) with
        {
            RouteSurfaces = [route],
        };

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(1, 0);

        Assert.Equal(2, column.TerrainSolids.Count);
        Assert.Equal([(decimal?)null, 2m, 5m, 10m], column.TerrainSolids
            .SelectMany(static span => new[] { span.BottomMeters, span.TopMeters }));
    }

    [Fact]
    public void AShortTunnelBetweenAdditiveSegmentsHonoursBothPortalPlanes()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 8, 2m, 0.5m),
                RoutePoint(16, 8, 2m, 0.5m),
                RoutePoint(32, 8, 2m, 0.5m),
                RoutePoint(64, 8, 2m, 0.5m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Additive),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
                Segment("route_0001", 3, RouteSegmentOperation.Additive),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 64) with
        {
            RouteSurfaces = [route],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        Assert.Single(columns.AtWaterCell(0, 0).TerrainSolids);
        Assert.Equal(2, columns.AtWaterCell(1, 0).TerrainSolids.Count);
        Assert.Single(columns.AtWaterCell(2, 0).TerrainSolids);
    }

    [Fact]
    public void AHairpinPortalUsesTheOutgoingDirectionDeterministically()
    {
        using var workspace = TestWorkspace.Create();
        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = "route_0001",
            AssetKey = "grass",
            Points =
            [
                RoutePoint(0, 8, 2m, 0.5m),
                RoutePoint(32, 8, 2m, 0.5m),
                RoutePoint(0, 8, 2m, 0.5m),
            ],
            Segments =
            [
                Segment("route_0001", 1, RouteSegmentOperation.Additive),
                Segment("route_0001", 2, RouteSegmentOperation.Subtractive, 3m),
            ],
        };
        var scene = Hill(TestScenes.Instance(workspace), workspace, 10m, 64) with
        {
            RouteSurfaces = [route],
        };
        var columns = LayeredSceneColumns.Prepare(scene, workspace.Metrics);

        Assert.Equal(2, columns.AtWaterCell(1, 0).TerrainSolids.Count);
        Assert.Single(columns.AtWaterCell(2, 0).TerrainSolids);
    }

    [Fact]
    public void ACutEndingAtTheTerrainTopLeavesNoZeroHeightRoof()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Hill(TestScenes.Instance(workspace), workspace, 5m) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(32, 8, 2m, 0.5m),
                    operation: RouteSegmentOperation.Subtractive,
                    clearance: 3m),
            ],
        };

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Equal(
            [new LayeredColumnSpan(
                null, 2m, "grass", LayeredColumnSpanKind.TerrainSolid, null)],
            column.TerrainSolids);
        Assert.Equal(2m, column.VisibleAt()!.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, column.VisibleAt()!.Kind);
    }

    [Fact]
    public void APathCutNeverRemovesWaterOrAnotherIndependentSurface()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(
            Hill(TestScenes.Instance(workspace), workspace, 10m),
            WaterBody("river_0001", surface: 4m, depth: 0.5m, clearance: 0m)) with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(32, 8, 2m, 0.5m),
                    operation: RouteSegmentOperation.Subtractive,
                    clearance: 3m),
                Route(
                    "route_0002",
                    RoutePoint(0, 8, 3m, 0.5m),
                    RoutePoint(32, 8, 3m, 0.5m)),
            ],
        };

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Single(column.Fills);
        Assert.Equal("river_0001", column.Fills[0].SourceId);
        Assert.Equal(2, column.Surfaces.Count);
        Assert.Equal(["route_0001", "route_0002"],
            column.Surfaces.Select(static surface => surface.SourceId));
        Assert.Equal(
            [(null, 2m), (5m, 10m)],
            column.TerrainSolids.Select(static span => (span.BottomMeters, span.TopMeters)));
    }

    [Fact]
    public void ASubtractivePathOverEmptySpaceStillPresentsItsFloor()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance() with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 2m, 0.5m),
                    RoutePoint(32, 8, 2m, 0.5m),
                    operation: RouteSegmentOperation.Subtractive,
                    clearance: 3m),
            ],
        };

        var column = LayeredSceneColumns.Prepare(scene, workspace.Metrics).AtWaterCell(0, 0);

        Assert.Empty(column.TerrainSolids);
        Assert.Equal(2m, column.VisibleAt()!.ElevationMeters);
        Assert.Equal(LayeredColumnSpanKind.IndependentSurface, column.VisibleAt()!.Kind);
    }

    [Fact]
    public void ASubtractiveRampCutUsesTheBakedContinuousFloor()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance() with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    RoutePoint(0, 8, 1m, 0.5m),
                    RoutePoint(64, 8, 3m, 0.5m),
                    operation: RouteSegmentOperation.Subtractive,
                    clearance: 1.5m),
            ],
        };

        var cuts = RouteSurfaceRaster.Cuts(scene, workspace.Metrics);

        Assert.Equal([0, 1, 2, 3], cuts.Select(static cut => cut.X));
        Assert.All(cuts, static cut => Assert.Equal(0, cut.Y));
        Assert.Equal([1.25m, 1.75m, 2.25m, 2.75m],
            cuts.Select(static cut => cut.BottomMeters));
        Assert.Equal([2.75m, 3.25m, 3.75m, 4.25m],
            cuts.Select(static cut => cut.TopMeters));
    }

    private static SceneDocument Hill(
        SceneDocument scene,
        TestWorkspace workspace,
        decimal elevation,
        int sizeAuthoringPixels = 32) =>
        ElevationRegionEditing.Place(
            scene,
            workspace.Metrics,
            [
                ElevationRegionEditing.Point(0, 0),
                ElevationRegionEditing.Point(sizeAuthoringPixels, 0),
                ElevationRegionEditing.Point(sizeAuthoringPixels, sizeAuthoringPixels),
                ElevationRegionEditing.Point(0, sizeAuthoringPixels),
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
        string assetKey = "grass",
        RouteSegmentOperation operation = RouteSegmentOperation.Additive,
        decimal? clearance = null) => new()
    {
        RouteSurfaceId = id,
        AssetKey = assetKey,
        Points = [first, second],
        Segments =
        [
            Segment(id, 1, operation, clearance),
        ],
    };

    private static RouteSurfaceSegmentDocument Segment(
        string routeId,
        int ordinal,
        RouteSegmentOperation operation,
        decimal? clearance = null) => new()
    {
        SegmentId = $"{routeId}.segment_{ordinal:0000}",
        GradePercent = 0,
        Operation = operation,
        ClearanceAboveMeters = clearance,
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
