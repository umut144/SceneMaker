using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelPathEditingTests
{
    [Fact]
    public void GradeOfQuarterRisesEightMetresOverThirtyTwoMetres()
    {
        var elevation = VoxelPathEditing.ElevationAtGrade(
            new VoxelPlanPointMeters(0m, 0m),
            0m,
            new VoxelPlanPointMeters(32m, 0m),
            0.25m);

        Assert.Equal(8m, elevation);
    }

    [Fact]
    public void FloatingAdditivePathLeavesAirBelowIt()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(12, 12, 1m, 0.2m);

        var changed = VoxelPathEditing.Apply(
            grid,
            workspace.Configuration,
            StraightPath(4m),
            VoxelEditMode.Additive,
            VoxelPathSupportMode.Floating,
            "sand");

        Assert.True(changed.Contains(new VoxelCoordinate(4, 4, 4)));
        Assert.False(changed.Contains(new VoxelCoordinate(4, 0, 4)));
    }

    [Fact]
    public void SupportedAdditivePathFillsGapToGroundPlane()
    {
        using var workspace = TestWorkspace.Create();
        var changed = VoxelPathEditing.Apply(
            VoxelGrid.Empty(12, 12, 1m, 0.2m),
            workspace.Configuration,
            StraightPath(4m),
            VoxelEditMode.Additive,
            VoxelPathSupportMode.FillToGround,
            "sand");

        for (var y = 0; y <= 4; y++)
            Assert.True(changed.Contains(new VoxelCoordinate(4, y, 4)));
    }

    [Fact]
    public void SupportedPathStartsAboveExistingSupportWithoutRepaintingIt()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(12, 12, 1m, 0.2m);
        grid = VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(4, 2, 4),
            VoxelEditMode.Additive,
            "grass");

        var changed = VoxelPathEditing.Apply(
            grid,
            workspace.Configuration,
            StraightPath(4m),
            VoxelEditMode.Additive,
            VoxelPathSupportMode.FillToGround,
            "sand");

        Assert.Equal("grass", changed.Cells[new VoxelCoordinate(4, 2, 4)].AssetKey);
        Assert.False(changed.Contains(new VoxelCoordinate(4, 1, 4)));
        Assert.Equal("sand", changed.Cells[new VoxelCoordinate(4, 3, 4)].AssetKey);
    }

    [Fact]
    public void SubtractivePathCutsTunnelThroughExistingVolume()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(12, 12, 1m, 0.2m);
        grid = VoxelTileEditing.ApplyVolume(
            grid,
            workspace.Configuration,
            VoxelPrimitiveLibrary.Box(
                grid,
                new VoxelPointMeters(0m, 0m, 0m),
                new VoxelSizeMeters(12m, 8m, 12m)),
            VoxelEditMode.Additive,
            "grass");

        var cut = VoxelPathEditing.Apply(
            grid,
            workspace.Configuration,
            StraightPath(2m, height: 3m),
            VoxelEditMode.Subtractive,
            VoxelPathSupportMode.Floating);

        Assert.False(cut.Contains(new VoxelCoordinate(4, 2, 4)));
        Assert.False(cut.Contains(new VoxelCoordinate(4, 4, 4)));
        Assert.True(cut.Contains(new VoxelCoordinate(4, 1, 4)));
        Assert.True(cut.Contains(new VoxelCoordinate(4, 5, 4)));
    }

    [Fact]
    public void BezierHandlesBendTheVoxelCorridor()
    {
        var grid = VoxelGrid.Empty(12, 12, 1m, 0.2m);
        var points = new[]
        {
            Point(2m, 2m, 0m) with { HandleOut = new VoxelPlanOffsetMeters(0m, 4m) },
            Point(8m, 2m, 0m) with { HandleIn = new VoxelPlanOffsetMeters(0m, 4m) },
        };

        var volume = VoxelPathEditing.Rasterize(grid, points);

        Assert.Contains(volume, cell => cell.Z >= 4);
        Assert.DoesNotContain(new VoxelCoordinate(5, 0, 1), volume);
    }

    [Fact]
    public void PathParametersMustAlignToFineSubgrid()
    {
        var grid = VoxelGrid.Empty(12, 12, 1m, 0.2m);
        var points = StraightPath(0m).ToArray();
        points[1] = points[1] with { ElevationMeters = 0.1m };

        Assert.Throws<SceneMakerDocumentException>(
            () => VoxelPathEditing.Rasterize(grid, points));
    }

    private static IReadOnlyList<VoxelPathPoint> StraightPath(
        decimal elevation,
        decimal height = 1m) =>
        [Point(2m, 4m, elevation, height), Point(8m, 4m, elevation, height)];

    private static VoxelPathPoint Point(
        decimal x,
        decimal z,
        decimal elevation,
        decimal height = 1m) =>
        new(
            new VoxelPlanPointMeters(x, z),
            VoxelPlanOffsetMeters.Zero,
            VoxelPlanOffsetMeters.Zero,
            WidthMeters: 2m,
            HeightMeters: height,
            ElevationMeters: elevation);
}
