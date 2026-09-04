using System.Collections.ObjectModel;

namespace SceneMaker.Core;

/// <summary>
/// One address in the WorldVoxMaker grid. X and Z span the ground plane and Y
/// is elevation, matching Godot's 3D coordinate convention. Coordinates count
/// whole voxels; authored metric positions are converted at the tool boundary.
/// </summary>
public readonly record struct VoxelCoordinate(int X, int Y, int Z)
    : IComparable<VoxelCoordinate>
{
    public int CompareTo(VoxelCoordinate other)
    {
        var y = Y.CompareTo(other.Y);
        if (y != 0) return y;
        var z = Z.CompareTo(other.Z);
        return z != 0 ? z : X.CompareTo(other.X);
    }
}

/// <summary>A filled cubic metre and the Workspace Asset material it uses.</summary>
public sealed record VoxelCell(VoxelCoordinate Coordinate, string AssetKey);

public enum VoxelEditMode
{
    Additive,
    Subtractive,
}

/// <summary>
/// Immutable sparse voxel storage. Horizontal bounds belong to the Scene;
/// elevation is deliberately sparse and unbounded so tunnels may pass below
/// the ground plane and towers may grow without resizing the map footprint.
/// </summary>
public sealed class VoxelGrid
{
    private readonly SortedDictionary<VoxelCoordinate, VoxelCell> _cells;
    private readonly IReadOnlyDictionary<VoxelCoordinate, VoxelCell> _readOnlyCells;

    private VoxelGrid(
        int widthVoxels,
        int depthVoxels,
        decimal voxelSizeMeters,
        decimal subgridMeters,
        SortedDictionary<VoxelCoordinate, VoxelCell> cells)
    {
        WidthVoxels = widthVoxels;
        DepthVoxels = depthVoxels;
        VoxelSizeMeters = voxelSizeMeters;
        SubgridMeters = subgridMeters;
        SubgridStepsPerVoxel = checked((int)(voxelSizeMeters / subgridMeters));
        _cells = cells;
        _readOnlyCells = new ReadOnlyDictionary<VoxelCoordinate, VoxelCell>(_cells);
    }

    public int WidthVoxels { get; }
    public int DepthVoxels { get; }
    public decimal VoxelSizeMeters { get; }
    public decimal SubgridMeters { get; }
    public int SubgridStepsPerVoxel { get; }
    public int Count => _cells.Count;
    public IReadOnlyDictionary<VoxelCoordinate, VoxelCell> Cells => _readOnlyCells;

    public static VoxelGrid Empty(
        int widthVoxels,
        int depthVoxels,
        decimal voxelSizeMeters,
        decimal subgridMeters)
    {
        if (widthVoxels <= 0 || depthVoxels <= 0)
            throw new SceneMakerDocumentException("Voxel grid width and depth must be positive.");
        if (voxelSizeMeters <= 0m || subgridMeters <= 0m)
            throw new SceneMakerDocumentException("Voxel and subgrid sizes must be positive.");

        var steps = voxelSizeMeters / subgridMeters;
        if (steps != decimal.Truncate(steps))
        {
            throw new SceneMakerDocumentException(
                "voxel_size_meters must contain a whole number of subgrid steps.");
        }

        return new VoxelGrid(
            widthVoxels,
            depthVoxels,
            voxelSizeMeters,
            subgridMeters,
            []);
    }

    public bool Contains(VoxelCoordinate coordinate) => _cells.ContainsKey(coordinate);

    public bool TryGet(VoxelCoordinate coordinate, out VoxelCell? cell) =>
        _cells.TryGetValue(coordinate, out cell);

    /// <summary>
    /// Applies one canonical volume edit. Repeated coordinates are harmless;
    /// additive edits replace a cell's material and subtractive edits remove it.
    /// Returning this instance for a no-op lets the editor detect that no undo
    /// entry is needed.
    /// </summary>
    public VoxelGrid Apply(
        IEnumerable<VoxelCoordinate> coordinates,
        VoxelEditMode mode,
        string? assetKey = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == VoxelEditMode.Additive && string.IsNullOrWhiteSpace(assetKey))
            throw new SceneMakerDocumentException("Additive voxel edits require an asset_key.");

        SortedDictionary<VoxelCoordinate, VoxelCell>? changed = null;
        foreach (var coordinate in coordinates.Distinct().Order())
        {
            RequireHorizontalBounds(coordinate);
            if (mode == VoxelEditMode.Subtractive)
            {
                if (!_cells.ContainsKey(coordinate)) continue;
                changed ??= new SortedDictionary<VoxelCoordinate, VoxelCell>(_cells);
                changed.Remove(coordinate);
                continue;
            }

            if (_cells.TryGetValue(coordinate, out var existing)
                && existing.AssetKey == assetKey)
            {
                continue;
            }

            changed ??= new SortedDictionary<VoxelCoordinate, VoxelCell>(_cells);
            changed[coordinate] = new VoxelCell(coordinate, assetKey!);
        }

        return changed is null
            ? this
            : new VoxelGrid(
                WidthVoxels,
                DepthVoxels,
                VoxelSizeMeters,
                SubgridMeters,
                changed);
    }

    /// <summary>Snaps an authored metre value to the 0.2 m tool subgrid.</summary>
    public decimal SnapToSubgrid(decimal meters) =>
        Math.Round(meters / SubgridMeters, MidpointRounding.AwayFromZero) * SubgridMeters;

    /// <summary>Returns the voxel containing a metric position.</summary>
    public int VoxelIndex(decimal meters) =>
        checked((int)decimal.Floor(meters / VoxelSizeMeters));

    private void RequireHorizontalBounds(VoxelCoordinate coordinate)
    {
        if (coordinate.X < 0 || coordinate.X >= WidthVoxels
            || coordinate.Z < 0 || coordinate.Z >= DepthVoxels)
        {
            throw new SceneMakerDocumentException(
                $"Voxel ({coordinate.X}, {coordinate.Y}, {coordinate.Z}) lies outside the Scene footprint.");
        }
    }
}
