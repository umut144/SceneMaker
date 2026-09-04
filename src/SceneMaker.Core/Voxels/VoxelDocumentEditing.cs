namespace SceneMaker.Core;

/// <summary>Moves the persisted canonical voxel list across the edit boundary.</summary>
public static class VoxelDocumentEditing
{
    public static VoxelGrid ToGrid(SceneDocument scene, WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        DocumentValidation.Validate(scene);
        var grid = VoxelGrid.Empty(
            scene.SizeCells.Width,
            scene.SizeCells.Height,
            metrics.VoxelSizeMeters,
            metrics.VoxelSubgridMeters);
        return grid.Add(scene.VoxelCells.Select(cell => new VoxelCell(
            new VoxelCoordinate(cell.X, cell.Y, cell.Z),
            cell.AssetKey)));
    }

    public static SceneDocument WithGrid(SceneDocument scene, VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.WidthVoxels != scene.SizeCells.Width
            || grid.DepthVoxels != scene.SizeCells.Height)
        {
            throw new SceneMakerDocumentException(
                "Voxel grid footprint must match the Scene size_cells.");
        }

        return scene with
        {
            VoxelCells = grid.Cells.Values.Select(cell => new VoxelCellDocument
            {
                X = cell.Coordinate.X,
                Y = cell.Coordinate.Y,
                Z = cell.Coordinate.Z,
                AssetKey = cell.AssetKey,
            }).ToList(),
        };
    }

    public static void ValidateAssetReferences(
        SceneDocument scene,
        WorkspaceConfiguration workspace)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(workspace);
        foreach (var assetKey in scene.VoxelCells
            .Select(static cell => cell.AssetKey)
            .Distinct(StringComparer.Ordinal))
        {
            var profile = workspace.ResolveAssetProfile(assetKey);
            if (profile.Role != WorkspaceAssetRole.Terrain)
            {
                throw new SceneMakerDocumentException(
                    $"Voxel asset_key '{assetKey}' must have the Terrain role.");
            }
        }
    }
}
