using SceneMaker.Core;

namespace SceneMaker.Editor;

public enum VoxelViewportMode
{
    TopDown,
    CrossSection,
}

public readonly record struct VoxelPlanEdge(
    int StartX,
    int StartZ,
    int EndX,
    int EndZ);

public sealed record VoxelColumnView(
    int X,
    int Z,
    VoxelCell? VisibleCell,
    bool HasSolidAbovePlane);

public sealed record VoxelViewportSlice(
    VoxelViewportMode Mode,
    decimal ElevationMeters,
    int? CutVoxelY,
    IReadOnlyList<VoxelColumnView> Columns,
    IReadOnlyList<VoxelPlanEdge> OverheadOutline);

/// <summary>
/// Derives the smart horizontal editing view from the current metric pointer
/// target. Nothing here knows Godot nodes or drawing APIs; the App only renders
/// the returned columns and edges.
/// </summary>
public sealed class VoxelCrossSectionView
{
    public VoxelViewportSlice Current { get; private set; } = new(
        VoxelViewportMode.TopDown,
        0m,
        null,
        [],
        []);

    public VoxelViewportSlice Follow(
        VoxelGrid grid,
        VoxelPointMeters? editPoint)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (editPoint is not { } point)
            return Current = BuildTopDown(grid, 0m);

        var pointerX = grid.VoxelIndex(point.X);
        var pointerZ = grid.VoxelIndex(point.Z);
        var insideFootprint = pointerX >= 0 && pointerX < grid.WidthVoxels
            && pointerZ >= 0 && pointerZ < grid.DepthVoxels;
        var solidAbovePointer = insideFootprint && grid.Cells.Keys.Any(cell =>
            cell.X == pointerX
            && cell.Z == pointerZ
            && (cell.Y + 1) * grid.VoxelSizeMeters > point.Y);

        return Current = solidAbovePointer
            ? BuildCrossSection(grid, point.Y)
            : BuildTopDown(grid, point.Y);
    }

    private static VoxelViewportSlice BuildCrossSection(
        VoxelGrid grid,
        decimal elevationMeters)
    {
        var cutY = grid.VoxelIndex(elevationMeters);
        var cellsByCoordinate = grid.Cells;
        var overhead = grid.Cells.Keys
            .Where(cell => (cell.Y + 1) * grid.VoxelSizeMeters > elevationMeters)
            .Select(static cell => (cell.X, cell.Z))
            .ToHashSet();
        var columns = new List<VoxelColumnView>(grid.WidthVoxels * grid.DepthVoxels);
        for (var z = 0; z < grid.DepthVoxels; z++)
        {
            for (var x = 0; x < grid.WidthVoxels; x++)
            {
                cellsByCoordinate.TryGetValue(
                    new VoxelCoordinate(x, cutY, z),
                    out var cutCell);
                columns.Add(new VoxelColumnView(
                    x,
                    z,
                    cutCell,
                    overhead.Contains((x, z))));
            }
        }

        return new VoxelViewportSlice(
            VoxelViewportMode.CrossSection,
            elevationMeters,
            cutY,
            columns,
            Outline(overhead));
    }

    private static VoxelViewportSlice BuildTopDown(
        VoxelGrid grid,
        decimal elevationMeters)
    {
        var topByColumn = grid.Cells.Values
            .GroupBy(cell => (cell.Coordinate.X, cell.Coordinate.Z))
            .ToDictionary(
                static group => group.Key,
                static group => group.MaxBy(cell => cell.Coordinate.Y)!);
        var columns = new List<VoxelColumnView>(grid.WidthVoxels * grid.DepthVoxels);
        for (var z = 0; z < grid.DepthVoxels; z++)
        {
            for (var x = 0; x < grid.WidthVoxels; x++)
            {
                topByColumn.TryGetValue((x, z), out var top);
                columns.Add(new VoxelColumnView(x, z, top, false));
            }
        }
        return new VoxelViewportSlice(
            VoxelViewportMode.TopDown,
            elevationMeters,
            null,
            columns,
            []);
    }

    private static IReadOnlyList<VoxelPlanEdge> Outline(
        HashSet<(int X, int Z)> occupied)
    {
        var edges = new List<VoxelPlanEdge>();
        foreach (var (x, z) in occupied.OrderBy(static cell => cell.Z).ThenBy(static cell => cell.X))
        {
            if (!occupied.Contains((x, z - 1)))
                edges.Add(new VoxelPlanEdge(x, z, x + 1, z));
            if (!occupied.Contains((x + 1, z)))
                edges.Add(new VoxelPlanEdge(x + 1, z, x + 1, z + 1));
            if (!occupied.Contains((x, z + 1)))
                edges.Add(new VoxelPlanEdge(x + 1, z + 1, x, z + 1));
            if (!occupied.Contains((x - 1, z)))
                edges.Add(new VoxelPlanEdge(x, z + 1, x, z));
        }
        return edges;
    }
}
