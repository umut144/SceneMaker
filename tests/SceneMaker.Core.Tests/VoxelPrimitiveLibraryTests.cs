using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelPrimitiveLibraryTests
{
    private static VoxelGrid Grid() => VoxelGrid.Empty(20, 20, 1m, 0.2m);

    [Fact]
    public void BoxRasterizesMetricBoundsByVoxelCenter()
    {
        var cells = VoxelPrimitiveLibrary.Box(
            Grid(),
            new VoxelPointMeters(1.2m, 0m, 2.2m),
            new VoxelSizeMeters(2.6m, 1m, 1.6m));

        Assert.Equal(
            [
                new VoxelCoordinate(1, 0, 2),
                new VoxelCoordinate(2, 0, 2),
                new VoxelCoordinate(3, 0, 2),
                new VoxelCoordinate(1, 0, 3),
                new VoxelCoordinate(2, 0, 3),
                new VoxelCoordinate(3, 0, 3),
            ],
            cells);
    }

    [Fact]
    public void SphereIsAVolumetricApproximation()
    {
        var cells = VoxelPrimitiveLibrary.Sphere(
            Grid(),
            new VoxelPointMeters(5m, 5m, 5m),
            1m);

        Assert.Equal(8, cells.Count);
        Assert.Contains(new VoxelCoordinate(4, 4, 4), cells);
        Assert.DoesNotContain(new VoxelCoordinate(3, 4, 4), cells);
    }

    [Fact]
    public void CylinderAndTowerShareTheSamePrimitive()
    {
        var cylinder = VoxelPrimitiveLibrary.Cylinder(
            Grid(), new VoxelPointMeters(5m, 0m, 5m), 2m, 3m);
        var tower = VoxelPrimitiveLibrary.Tower(
            Grid(), new VoxelPointMeters(5m, 0m, 5m), 2m, 3m);

        Assert.Equal(cylinder, tower);
        Assert.Equal(36, cylinder.Count);
    }

    [Fact]
    public void PyramidNarrowsTowardItsTop()
    {
        var cells = VoxelPrimitiveLibrary.Pyramid(
            Grid(), new VoxelPointMeters(5m, 0m, 5m), 6m, 6m, 3m);

        var layerCounts = cells.GroupBy(static cell => cell.Y)
            .OrderBy(static group => group.Key)
            .Select(static group => group.Count())
            .ToArray();
        Assert.Equal([36, 16, 4], layerCounts);
    }

    [Fact]
    public void StairsAreSolidBelowEveryStep()
    {
        var cells = VoxelPrimitiveLibrary.Stairs(
            Grid(),
            new VoxelCoordinate(2, 0, 3),
            widthVoxels: 2,
            stepCount: 3,
            riseVoxelsPerStep: 1,
            VoxelStairDirection.PositiveX);

        Assert.Equal(12, cells.Count);
        Assert.Contains(new VoxelCoordinate(4, 2, 4), cells);
        Assert.DoesNotContain(new VoxelCoordinate(2, 1, 3), cells);
    }

    [Fact]
    public void PrimitiveInputsMustUseTheConfiguredSubgrid()
    {
        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            VoxelPrimitiveLibrary.Sphere(
                Grid(), new VoxelPointMeters(5.1m, 5m, 5m), 1m));

        Assert.Contains("0.2 m", exception.Message, StringComparison.Ordinal);
    }
}
