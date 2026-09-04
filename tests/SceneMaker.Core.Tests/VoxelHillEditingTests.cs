using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelHillEditingTests
{
    private static readonly VoxelPointMeters[] Footprint =
    [
        new(0m, 0m, 0m),
        new(4m, 0m, 0m),
        new(4m, 0m, 2m),
        new(0m, 0m, 2m),
    ];

    [Fact]
    public void AdditiveHillInheritsMaterialSeparatelyForEveryGroundColumn()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m);
        grid = VoxelTileEditing.ApplyVolume(
            grid,
            workspace.Configuration,
            [
                new VoxelCoordinate(0, 0, 0),
                new VoxelCoordinate(1, 0, 0),
                new VoxelCoordinate(2, 0, 0),
            ],
            VoxelEditMode.Additive,
            "grass");
        grid = VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(3, 0, 0),
            VoxelEditMode.Additive,
            "sand");

        var hill = VoxelHillEditing.Apply(
            grid, Footprint, 0m, 3m, VoxelEditMode.Additive);

        Assert.Equal("grass", hill.Cells[new VoxelCoordinate(2, 2, 0)].AssetKey);
        Assert.Equal("sand", hill.Cells[new VoxelCoordinate(3, 2, 0)].AssetKey);
        Assert.False(hill.Contains(new VoxelCoordinate(0, 1, 1)));
    }

    [Fact]
    public void AdditiveHillDoesNotInventMaterialOverEmptyGround()
    {
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m);

        var hill = VoxelHillEditing.Apply(
            grid, Footprint, 0m, 3m, VoxelEditMode.Additive);

        Assert.Same(grid, hill);
    }

    [Fact]
    public void SubtractiveHillCutsTheSameVolumeRegardlessOfMaterial()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m);
        var box = VoxelPrimitiveLibrary.Box(
            grid,
            new VoxelPointMeters(0m, 0m, 0m),
            new VoxelSizeMeters(4m, 4m, 2m));
        grid = VoxelTileEditing.ApplyVolume(
            grid,
            workspace.Configuration,
            box,
            VoxelEditMode.Additive,
            "grass");

        var cut = VoxelHillEditing.Apply(
            grid, Footprint, 1m, 3m, VoxelEditMode.Subtractive);

        Assert.Equal(16, cut.Count);
        Assert.True(cut.Contains(new VoxelCoordinate(2, 0, 1)));
        Assert.False(cut.Contains(new VoxelCoordinate(2, 1, 1)));
        Assert.True(cut.Contains(new VoxelCoordinate(2, 3, 1)));
    }
}
