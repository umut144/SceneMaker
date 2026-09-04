using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelGridTests
{
    [Fact]
    public void WorldGridUsesOneMetreVoxelsAndFiveSubgridSteps()
    {
        var grid = VoxelGrid.Empty(20, 30, 1m, 0.2m);

        Assert.Equal(1m, grid.VoxelSizeMeters);
        Assert.Equal(0.2m, grid.SubgridMeters);
        Assert.Equal(5, grid.SubgridStepsPerVoxel);
        Assert.Equal(0.4m, grid.SnapToSubgrid(0.33m));
        Assert.Equal(-1, grid.VoxelIndex(-0.01m));
    }

    [Fact]
    public void SubgridMustNestExactlyInsideAVoxel()
    {
        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => VoxelGrid.Empty(10, 10, 1m, 0.3m));

        Assert.Contains("whole number", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditiveTileStoresWorkspaceMaterialAtExact3DCoordinate()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m);

        var changed = VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(2, 0, 4),
            VoxelEditMode.Additive,
            "grass");

        var cell = Assert.Single(changed.Cells).Value;
        Assert.Equal(new VoxelCoordinate(2, 0, 4), cell.Coordinate);
        Assert.Equal("grass", cell.AssetKey);
    }

    [Fact]
    public void AdditiveTileRejectsPlacementAssets()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m);

        Assert.Throws<SceneMakerDocumentException>(() => VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(2, 0, 4),
            VoxelEditMode.Additive,
            "stone"));
    }

    [Fact]
    public void SubtractiveTileRemovesADeepVoxelWithoutMaterial()
    {
        using var workspace = TestWorkspace.Create();
        var coordinate = new VoxelCoordinate(2, -3, 4);
        var grid = VoxelTileEditing.Apply(
            VoxelGrid.Empty(6, 6, 1m, 0.2m),
            workspace.Configuration,
            coordinate,
            VoxelEditMode.Additive,
            "grass");

        var changed = VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            coordinate,
            VoxelEditMode.Subtractive);

        Assert.Empty(changed.Cells);
    }

    [Fact]
    public void ANoOpReturnsTheSameGrid()
    {
        using var workspace = TestWorkspace.Create();
        var coordinate = new VoxelCoordinate(1, 0, 1);
        var grid = VoxelTileEditing.Apply(
            VoxelGrid.Empty(4, 4, 1m, 0.2m),
            workspace.Configuration,
            coordinate,
            VoxelEditMode.Additive,
            "grass");

        Assert.Same(grid, VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            coordinate,
            VoxelEditMode.Additive,
            "grass"));
        Assert.Same(grid, VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(3, 9, 3),
            VoxelEditMode.Subtractive));
    }

    [Fact]
    public void HorizontalFootprintIsBoundedButElevationIsNot()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelGrid.Empty(4, 4, 1m, 0.2m);

        Assert.Throws<SceneMakerDocumentException>(() => VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(4, 0, 0),
            VoxelEditMode.Additive,
            "grass"));

        var tower = VoxelTileEditing.Apply(
            grid,
            workspace.Configuration,
            new VoxelCoordinate(3, 10_000, 3),
            VoxelEditMode.Additive,
            "grass");
        Assert.True(tower.Contains(new VoxelCoordinate(3, 10_000, 3)));
    }

    [Fact]
    public void VolumeEditsDeduplicateCoordinatesAndRemainCanonical()
    {
        using var workspace = TestWorkspace.Create();
        var grid = VoxelTileEditing.ApplyVolume(
            VoxelGrid.Empty(4, 4, 1m, 0.2m),
            workspace.Configuration,
            [
                new VoxelCoordinate(2, 1, 0),
                new VoxelCoordinate(1, 0, 3),
                new VoxelCoordinate(2, 1, 0),
                new VoxelCoordinate(0, 0, 0),
            ],
            VoxelEditMode.Additive,
            "sand");

        Assert.Equal(
            [
                new VoxelCoordinate(0, 0, 0),
                new VoxelCoordinate(1, 0, 3),
                new VoxelCoordinate(2, 1, 0),
            ],
            grid.Cells.Keys);
    }
}
