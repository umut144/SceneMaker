using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public enum VoxelExportFormat
{
    Heightfield,
    SurfaceMesh,
    CompressedVoxels,
}

public interface IVoxelExportDocument
{
    string Format { get; }
    int Version { get; }
}

public sealed record VoxelHeightfieldCell(
    int X,
    int Z,
    decimal SurfaceElevationMeters,
    string AssetKey);

public sealed record VoxelHeightfieldExport(
    string Format,
    int Version,
    int WidthVoxels,
    int DepthVoxels,
    decimal CellMeters,
    bool Lossy,
    IReadOnlyList<VoxelHeightfieldCell> Cells) : IVoxelExportDocument;

public readonly record struct VoxelMeshVertex(decimal X, decimal Y, decimal Z);

public sealed record VoxelMeshFace(
    int A,
    int B,
    int C,
    int D,
    string AssetKey);

public sealed record VoxelSurfaceMeshExport(
    string Format,
    int Version,
    IReadOnlyList<VoxelMeshVertex> Vertices,
    IReadOnlyList<VoxelMeshFace> Faces) : IVoxelExportDocument;

public sealed record VoxelRun(
    int StartX,
    int Y,
    int Z,
    int Length,
    string AssetKey);

public sealed record CompressedVoxelExport(
    string Format,
    int Version,
    int WidthVoxels,
    int DepthVoxels,
    decimal VoxelSizeMeters,
    IReadOnlyList<VoxelRun> Runs) : IVoxelExportDocument;

/// <summary>
/// Converts the one lossless authoring grid into consumer-specific contracts.
/// The heightfield intentionally collapses caves and overhangs; both 3D
/// formats preserve them.
/// </summary>
public static class VoxelExportPipeline
{
    public const int HeightfieldVersion = 1;
    public const int SurfaceMeshVersion = 1;
    public const int CompressedVoxelsVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static IVoxelExportDocument Build(VoxelGrid grid, VoxelExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return format switch
        {
            VoxelExportFormat.Heightfield => Heightfield(grid),
            VoxelExportFormat.SurfaceMesh => SurfaceMesh(grid),
            VoxelExportFormat.CompressedVoxels => CompressedVoxels(grid),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public static string Serialize(IVoxelExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document switch
        {
            VoxelHeightfieldExport value => JsonSerializer.Serialize(value, JsonOptions) + "\n",
            VoxelSurfaceMeshExport value => JsonSerializer.Serialize(value, JsonOptions) + "\n",
            CompressedVoxelExport value => JsonSerializer.Serialize(value, JsonOptions) + "\n",
            _ => throw new ArgumentException("Unsupported voxel export document.", nameof(document)),
        };
    }

    public static void Write(string path, VoxelGrid grid, VoxelExportFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AtomicTextFile.Write(Path.GetFullPath(path), Serialize(Build(grid, format)));
    }

    public static VoxelHeightfieldExport Heightfield(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var cells = grid.Cells.Values
            .GroupBy(cell => (cell.Coordinate.X, cell.Coordinate.Z))
            .Select(group => group.MaxBy(cell => cell.Coordinate.Y)!)
            .OrderBy(static cell => cell.Coordinate.Z)
            .ThenBy(static cell => cell.Coordinate.X)
            .Select(cell => new VoxelHeightfieldCell(
                cell.Coordinate.X,
                cell.Coordinate.Z,
                (cell.Coordinate.Y + 1) * grid.VoxelSizeMeters,
                cell.AssetKey))
            .ToArray();
        return new VoxelHeightfieldExport(
            "world_vox_maker.heightfield",
            HeightfieldVersion,
            grid.WidthVoxels,
            grid.DepthVoxels,
            grid.VoxelSizeMeters,
            Lossy: true,
            cells);
    }

    public static VoxelSurfaceMeshExport SurfaceMesh(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var vertices = new List<VoxelMeshVertex>();
        var vertexIndices = new Dictionary<VoxelMeshVertex, int>();
        var faces = new List<VoxelMeshFace>();

        foreach (var cell in grid.Cells.Values)
        {
            foreach (var face in ExposedFaces(grid, cell.Coordinate))
            {
                var indices = face
                    .Select(point => Scale(point, grid.VoxelSizeMeters))
                    .Select(point => IndexOf(point, vertices, vertexIndices))
                    .ToArray();
                faces.Add(new VoxelMeshFace(
                    indices[0], indices[1], indices[2], indices[3], cell.AssetKey));
            }
        }

        return new VoxelSurfaceMeshExport(
            "world_vox_maker.surface_mesh",
            SurfaceMeshVersion,
            vertices,
            faces);
    }

    public static CompressedVoxelExport CompressedVoxels(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var runs = new List<VoxelRun>();
        VoxelRun? current = null;
        foreach (var cell in grid.Cells.Values)
        {
            var coordinate = cell.Coordinate;
            if (current is not null
                && current.Y == coordinate.Y
                && current.Z == coordinate.Z
                && current.StartX + current.Length == coordinate.X
                && current.AssetKey == cell.AssetKey)
            {
                current = current with { Length = current.Length + 1 };
                runs[^1] = current;
                continue;
            }

            current = new VoxelRun(
                coordinate.X,
                coordinate.Y,
                coordinate.Z,
                1,
                cell.AssetKey);
            runs.Add(current);
        }

        return new CompressedVoxelExport(
            "world_vox_maker.compressed_voxels",
            CompressedVoxelsVersion,
            grid.WidthVoxels,
            grid.DepthVoxels,
            grid.VoxelSizeMeters,
            runs);
    }

    private static IEnumerable<VoxelMeshVertex[]> ExposedFaces(
        VoxelGrid grid,
        VoxelCoordinate cell)
    {
        var x = cell.X;
        var y = cell.Y;
        var z = cell.Z;
        if (!grid.Contains(new VoxelCoordinate(x - 1, y, z)))
            yield return [new(x, y, z), new(x, y, z + 1), new(x, y + 1, z + 1), new(x, y + 1, z)];
        if (!grid.Contains(new VoxelCoordinate(x + 1, y, z)))
            yield return [new(x + 1, y, z + 1), new(x + 1, y, z), new(x + 1, y + 1, z), new(x + 1, y + 1, z + 1)];
        if (!grid.Contains(new VoxelCoordinate(x, y - 1, z)))
            yield return [new(x, y, z + 1), new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1)];
        if (!grid.Contains(new VoxelCoordinate(x, y + 1, z)))
            yield return [new(x, y + 1, z), new(x, y + 1, z + 1), new(x + 1, y + 1, z + 1), new(x + 1, y + 1, z)];
        if (!grid.Contains(new VoxelCoordinate(x, y, z - 1)))
            yield return [new(x + 1, y, z), new(x, y, z), new(x, y + 1, z), new(x + 1, y + 1, z)];
        if (!grid.Contains(new VoxelCoordinate(x, y, z + 1)))
            yield return [new(x, y, z + 1), new(x + 1, y, z + 1), new(x + 1, y + 1, z + 1), new(x, y + 1, z + 1)];
    }

    private static VoxelMeshVertex Scale(VoxelMeshVertex point, decimal scale) =>
        new(point.X * scale, point.Y * scale, point.Z * scale);

    private static int IndexOf(
        VoxelMeshVertex point,
        List<VoxelMeshVertex> vertices,
        Dictionary<VoxelMeshVertex, int> indices)
    {
        if (indices.TryGetValue(point, out var existing)) return existing;
        var index = vertices.Count;
        vertices.Add(point);
        indices.Add(point, index);
        return index;
    }
}
