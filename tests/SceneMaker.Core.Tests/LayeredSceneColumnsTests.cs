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
}
