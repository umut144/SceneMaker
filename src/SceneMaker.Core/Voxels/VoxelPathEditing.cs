namespace SceneMaker.Core;

public readonly record struct VoxelPlanPointMeters(decimal X, decimal Z);
public readonly record struct VoxelPlanOffsetMeters(decimal X, decimal Z)
{
    public static VoxelPlanOffsetMeters Zero { get; } = new(0m, 0m);
}

/// <summary>
/// One editable Bezier point of a VoxelPath. Elevation is the lower face of
/// the path section; height extends upward from it. Width, height and elevation
/// interpolate to the next point over the flattened curve.
/// </summary>
public sealed record VoxelPathPoint(
    VoxelPlanPointMeters Position,
    VoxelPlanOffsetMeters HandleIn,
    VoxelPlanOffsetMeters HandleOut,
    decimal WidthMeters,
    decimal HeightMeters,
    decimal ElevationMeters);

public enum VoxelPathSupportMode
{
    Floating,
    FillToGround,
}

/// <summary>
/// Bezier-corridor voxelization shared by additive ramps and subtractive
/// tunnels. The two modes operate on the same derived volume.
/// </summary>
public static class VoxelPathEditing
{
    private sealed record Sample(
        decimal X,
        decimal Z,
        decimal Width,
        decimal Height,
        decimal Elevation);

    public static VoxelGrid Apply(
        VoxelGrid grid,
        WorkspaceConfiguration workspace,
        IReadOnlyList<VoxelPathPoint> points,
        VoxelEditMode mode,
        VoxelPathSupportMode supportMode,
        string? assetKey = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(supportMode)) throw new ArgumentOutOfRangeException(nameof(supportMode));

        if (mode == VoxelEditMode.Additive)
        {
            var profile = workspace.ResolveAssetProfile(assetKey ?? string.Empty);
            if (profile.Role != WorkspaceAssetRole.Terrain)
            {
                throw new SceneMakerDocumentException(
                    $"Voxel path material '{assetKey}' must have the Terrain role.");
            }
        }

        var volume = Rasterize(grid, points);
        if (mode == VoxelEditMode.Subtractive)
            return grid.Apply(volume, mode);
        if (supportMode == VoxelPathSupportMode.Floating)
            return grid.Apply(volume, mode, assetKey);

        var supported = FillColumnsToGround(grid, volume);
        return grid.Apply(supported, mode, assetKey);
    }

    public static IReadOnlyList<VoxelCoordinate> Rasterize(
        VoxelGrid grid,
        IReadOnlyList<VoxelPathPoint> points)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(points);
        ValidatePoints(grid, points);
        var samples = Flatten(grid, points);
        var volume = new SortedSet<VoxelCoordinate>();

        for (var index = 0; index + 1 < samples.Count; index++)
        {
            var start = samples[index];
            var end = samples[index + 1];
            RasterizeSegment(grid, start, end, volume);
        }
        return [.. volume];
    }

    /// <summary>
    /// Computes a following elevation from a signed rise per horizontal metre.
    /// The caller can use it per segment, so one path may change grade at every
    /// authored control point.
    /// </summary>
    public static decimal ElevationAtGrade(
        VoxelPlanPointMeters start,
        decimal startElevationMeters,
        VoxelPlanPointMeters end,
        decimal grade)
    {
        var deltaX = end.X - start.X;
        var deltaZ = end.Z - start.Z;
        var run = (decimal)Math.Sqrt((double)(deltaX * deltaX + deltaZ * deltaZ));
        if (run == 0m)
            throw new SceneMakerDocumentException("A graded VoxelPath segment requires horizontal run.");
        return startElevationMeters + run * grade;
    }

    private static IReadOnlyList<Sample> Flatten(
        VoxelGrid grid,
        IReadOnlyList<VoxelPathPoint> points)
    {
        var samples = new List<Sample>();
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var control0 = start.Position;
            var control1 = Add(start.Position, start.HandleOut);
            var control2 = Add(end.Position, end.HandleIn);
            var control3 = end.Position;
            var controlLength = Distance(control0, control1)
                + Distance(control1, control2)
                + Distance(control2, control3);
            var steps = Math.Max(1, checked((int)Math.Ceiling(
                (double)(controlLength / grid.SubgridMeters))));

            for (var step = index == 0 ? 0 : 1; step <= steps; step++)
            {
                var t = (decimal)step / steps;
                var position = Cubic(control0, control1, control2, control3, t);
                samples.Add(new Sample(
                    position.X,
                    position.Z,
                    Lerp(start.WidthMeters, end.WidthMeters, t),
                    Lerp(start.HeightMeters, end.HeightMeters, t),
                    Lerp(start.ElevationMeters, end.ElevationMeters, t)));
            }
        }
        return samples;
    }

    private static void RasterizeSegment(
        VoxelGrid grid,
        Sample start,
        Sample end,
        SortedSet<VoxelCoordinate> volume)
    {
        var radius = Math.Max(start.Width, end.Width) / 2m;
        var minimumX = Math.Max(0, grid.VoxelIndex(Math.Min(start.X, end.X) - radius));
        var maximumX = Math.Min(
            grid.WidthVoxels - 1,
            grid.VoxelIndex(Math.Max(start.X, end.X) + radius));
        var minimumZ = Math.Max(0, grid.VoxelIndex(Math.Min(start.Z, end.Z) - radius));
        var maximumZ = Math.Min(
            grid.DepthVoxels - 1,
            grid.VoxelIndex(Math.Max(start.Z, end.Z) + radius));
        var voxelSize = grid.VoxelSizeMeters;

        for (var z = minimumZ; z <= maximumZ; z++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                var centerX = (x + 0.5m) * voxelSize;
                var centerZ = (z + 0.5m) * voxelSize;
                var t = Projection(start, end, centerX, centerZ);
                var nearestX = Lerp(start.X, end.X, t);
                var nearestZ = Lerp(start.Z, end.Z, t);
                var width = Lerp(start.Width, end.Width, t);
                var deltaX = centerX - nearestX;
                var deltaZ = centerZ - nearestZ;
                if (deltaX * deltaX + deltaZ * deltaZ > width * width / 4m)
                    continue;

                var elevation = Lerp(start.Elevation, end.Elevation, t);
                var height = Lerp(start.Height, end.Height, t);
                var minimumY = grid.VoxelIndex(elevation);
                var maximumY = grid.VoxelIndex(elevation + height - decimal.One / 1_000_000m);
                for (var y = minimumY; y <= maximumY; y++)
                {
                    var centerY = (y + 0.5m) * voxelSize;
                    if (centerY >= elevation && centerY < elevation + height)
                        volume.Add(new VoxelCoordinate(x, y, z));
                }
            }
        }
    }

    private static IReadOnlyList<VoxelCoordinate> FillColumnsToGround(
        VoxelGrid grid,
        IReadOnlyList<VoxelCoordinate> volume)
    {
        var filled = new SortedSet<VoxelCoordinate>(volume);
        foreach (var column in volume.GroupBy(static cell => (cell.X, cell.Z)))
        {
            var minimumPathY = column.Min(static cell => cell.Y);
            var maximumPathY = column.Max(static cell => cell.Y);
            var supportY = grid.Cells.Keys
                .Where(cell => cell.X == column.Key.X
                    && cell.Z == column.Key.Z
                    && cell.Y < minimumPathY)
                .Select(static cell => (int?)cell.Y)
                .Max();
            var firstY = supportY.HasValue
                ? supportY.Value + 1
                : Math.Min(0, minimumPathY);
            for (var y = firstY; y <= maximumPathY; y++)
                filled.Add(new VoxelCoordinate(column.Key.X, y, column.Key.Z));
        }
        return [.. filled];
    }

    private static void ValidatePoints(VoxelGrid grid, IReadOnlyList<VoxelPathPoint> points)
    {
        if (points.Count < 2)
            throw new SceneMakerDocumentException("A VoxelPath requires at least two points.");
        foreach (var point in points)
        {
            if (point.WidthMeters <= 0m || point.HeightMeters <= 0m)
                throw new SceneMakerDocumentException("Every VoxelPath point requires positive width and height.");
            RequireSubgrid(grid, point.Position.X, "position X");
            RequireSubgrid(grid, point.Position.Z, "position Z");
            RequireSubgrid(grid, point.HandleIn.X, "handle_in X");
            RequireSubgrid(grid, point.HandleIn.Z, "handle_in Z");
            RequireSubgrid(grid, point.HandleOut.X, "handle_out X");
            RequireSubgrid(grid, point.HandleOut.Z, "handle_out Z");
            RequireSubgrid(grid, point.WidthMeters, "width");
            RequireSubgrid(grid, point.HeightMeters, "height");
            RequireSubgrid(grid, point.ElevationMeters, "elevation");
        }
    }

    private static void RequireSubgrid(VoxelGrid grid, decimal value, string label)
    {
        if (value % grid.SubgridMeters != 0m)
        {
            throw new SceneMakerDocumentException(FormattableString.Invariant(
                $"VoxelPath {label} must align to the {grid.SubgridMeters} m subgrid."));
        }
    }

    private static decimal Projection(Sample start, Sample end, decimal x, decimal z)
    {
        var deltaX = end.X - start.X;
        var deltaZ = end.Z - start.Z;
        var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
        if (lengthSquared == 0m) return 0m;
        return Math.Clamp(
            ((x - start.X) * deltaX + (z - start.Z) * deltaZ) / lengthSquared,
            0m,
            1m);
    }

    private static VoxelPlanPointMeters Add(
        VoxelPlanPointMeters point,
        VoxelPlanOffsetMeters offset) =>
        new(point.X + offset.X, point.Z + offset.Z);

    private static decimal Distance(VoxelPlanPointMeters start, VoxelPlanPointMeters end)
    {
        var x = end.X - start.X;
        var z = end.Z - start.Z;
        return (decimal)Math.Sqrt((double)(x * x + z * z));
    }

    private static VoxelPlanPointMeters Cubic(
        VoxelPlanPointMeters point0,
        VoxelPlanPointMeters point1,
        VoxelPlanPointMeters point2,
        VoxelPlanPointMeters point3,
        decimal t)
    {
        var inverse = 1m - t;
        var a = inverse * inverse * inverse;
        var b = 3m * inverse * inverse * t;
        var c = 3m * inverse * t * t;
        var d = t * t * t;
        return new VoxelPlanPointMeters(
            a * point0.X + b * point1.X + c * point2.X + d * point3.X,
            a * point0.Z + b * point1.Z + c * point2.Z + d * point3.Z);
    }

    private static decimal Lerp(decimal start, decimal end, decimal t) =>
        start + (end - start) * t;
}
