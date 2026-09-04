using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelDocumentEditingTests
{
    [Fact]
    public void GridRoundTripPreservesCanonicalCellsAndMetrics()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 8);
        var grid = VoxelGrid.Empty(
            8,
            8,
            workspace.Metrics.VoxelSizeMeters,
            workspace.Metrics.VoxelSubgridMeters).Add(
            [
                new VoxelCell(new VoxelCoordinate(4, 2, 1), "grass"),
                new VoxelCell(new VoxelCoordinate(1, -1, 7), "sand"),
            ]);

        var changed = VoxelDocumentEditing.WithGrid(scene, grid);
        var restored = VoxelDocumentEditing.ToGrid(changed, workspace.Metrics);

        Assert.Equal(grid.Cells, restored.Cells);
        Assert.Equal(1m, restored.VoxelSizeMeters);
        Assert.Equal(0.2m, restored.SubgridMeters);
        DocumentValidation.Validate(changed);
    }

    [Fact]
    public void SerializationPersistsYAndZWithoutAuthoringPixels()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var grid = VoxelGrid.Empty(6, 6, 1m, 0.2m).Add(
            [new VoxelCell(new VoxelCoordinate(2, -3, 4), "grass")]);
        scene = VoxelDocumentEditing.WithGrid(scene, grid);

        var json = DocumentJson.Serialize(scene);
        var restored = DocumentJson.DeserializeScene(json);

        Assert.Contains("\"voxel_cells\"", json, StringComparison.Ordinal);
        Assert.Equal(-3, Assert.Single(restored.VoxelCells).Y);
        Assert.DoesNotContain("position_authoring_px", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsNonCanonicalOrDuplicateVoxels()
    {
        var scene = TestScenes.EmptyInstance() with
        {
            VoxelCells =
            [
                new() { X = 1, Y = 0, Z = 0, AssetKey = "grass" },
                new() { X = 0, Y = 0, Z = 0, AssetKey = "grass" },
            ],
        };

        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
    }

    [Fact]
    public void VoxelMaterialsMustReferenceTerrainAssets()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance() with
        {
            VoxelCells = [new() { X = 0, Y = 0, Z = 0, AssetKey = "stone" }],
        };

        Assert.Throws<SceneMakerDocumentException>(() =>
            VoxelDocumentEditing.ValidateAssetReferences(scene, workspace.Configuration));
    }
}
