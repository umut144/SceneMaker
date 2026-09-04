namespace SceneMaker.Core;

/// <summary>
/// The foundational WorldVoxMaker brush: paint or remove a 1 m voxel at an
/// explicit 3D address. Material identity comes only from the Workspace Asset
/// catalog; the grid never invents or infers one.
/// </summary>
public static class VoxelTileEditing
{
    public static VoxelGrid Apply(
        VoxelGrid grid,
        WorkspaceConfiguration workspace,
        VoxelCoordinate coordinate,
        VoxelEditMode mode,
        string? assetKey = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(workspace);

        if (mode == VoxelEditMode.Additive)
        {
            var profile = workspace.ResolveAssetProfile(assetKey ?? string.Empty);
            if (profile.Role != WorkspaceAssetRole.Terrain)
            {
                throw new SceneMakerDocumentException(
                    $"Voxel material '{assetKey}' must have the Terrain role.");
            }
        }

        return grid.Apply([coordinate], mode, assetKey);
    }

    public static VoxelGrid ApplyVolume(
        VoxelGrid grid,
        WorkspaceConfiguration workspace,
        IEnumerable<VoxelCoordinate> coordinates,
        VoxelEditMode mode,
        string? assetKey = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (mode == VoxelEditMode.Additive)
        {
            var profile = workspace.ResolveAssetProfile(assetKey ?? string.Empty);
            if (profile.Role != WorkspaceAssetRole.Terrain)
            {
                throw new SceneMakerDocumentException(
                    $"Voxel material '{assetKey}' must have the Terrain role.");
            }
        }

        return grid.Apply(coordinates, mode, assetKey);
    }
}
