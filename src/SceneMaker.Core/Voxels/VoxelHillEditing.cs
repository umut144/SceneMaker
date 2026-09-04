namespace SceneMaker.Core;

/// <summary>Creates or cuts a polygonal solid while preserving ground materials.</summary>
public static class VoxelHillEditing
{
    public static VoxelGrid Apply(
        VoxelGrid grid,
        IReadOnlyList<VoxelPointMeters> footprint,
        decimal baseElevationMeters,
        decimal topElevationMeters,
        VoxelEditMode mode)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var volume = VoxelPrimitiveLibrary.ExtrudedPolygon(
            grid,
            footprint,
            baseElevationMeters,
            topElevationMeters);

        if (mode == VoxelEditMode.Subtractive)
            return grid.Apply(volume, mode);
        if (mode != VoxelEditMode.Additive)
            throw new ArgumentOutOfRangeException(nameof(mode));

        var firstY = volume.Min(static coordinate => coordinate.Y);
        var inheritedByColumn = grid.Cells.Values
            .Where(cell => cell.Coordinate.Y <= firstY)
            .GroupBy(cell => (cell.Coordinate.X, cell.Coordinate.Z))
            .ToDictionary(
                static group => group.Key,
                static group => group.MaxBy(cell => cell.Coordinate.Y)!.AssetKey);

        var materialized = volume
            .Where(coordinate => inheritedByColumn.ContainsKey((coordinate.X, coordinate.Z)))
            .Select(coordinate => new VoxelCell(
                coordinate,
                inheritedByColumn[(coordinate.X, coordinate.Z)]));
        return grid.Add(materialized);
    }
}
