namespace SceneMaker.Core;

/// <summary>A metric point in Godot's X/Y/Z coordinate space.</summary>
public readonly record struct VoxelPointMeters(decimal X, decimal Y, decimal Z);

/// <summary>A metric extent. All three components must be positive.</summary>
public readonly record struct VoxelSizeMeters(decimal X, decimal Y, decimal Z);

public enum VoxelStairDirection
{
    PositiveX,
    NegativeX,
    PositiveZ,
    NegativeZ,
}

/// <summary>
/// Deterministic conversion of metric solids into 1 m voxel addresses. Shape
/// boundaries may use the configured subgrid; occupancy is decided at voxel
/// centres, so every primitive composes under the same rule.
/// </summary>
public static class VoxelPrimitiveLibrary
{
    public static IReadOnlyList<VoxelCoordinate> Box(
        VoxelGrid grid,
        VoxelPointMeters minimum,
        VoxelSizeMeters size)
    {
        RequireGrid(grid);
        RequirePositive(size);
        RequireSubgrid(grid, minimum);
        RequireSubgrid(grid, new VoxelPointMeters(size.X, size.Y, size.Z), "size");
        return RasterizeBounds(
            grid,
            minimum,
            new VoxelPointMeters(
                minimum.X + size.X,
                minimum.Y + size.Y,
                minimum.Z + size.Z),
            static _ => true);
    }

    public static IReadOnlyList<VoxelCoordinate> Cylinder(
        VoxelGrid grid,
        VoxelPointMeters baseCenter,
        decimal radiusMeters,
        decimal heightMeters)
    {
        RequireGrid(grid);
        RequirePositive(radiusMeters, nameof(radiusMeters));
        RequirePositive(heightMeters, nameof(heightMeters));
        RequireSubgrid(grid, baseCenter);
        RequireSubgrid(grid, radiusMeters, nameof(radiusMeters));
        RequireSubgrid(grid, heightMeters, nameof(heightMeters));
        var radiusSquared = radiusMeters * radiusMeters;
        return RasterizeBounds(
            grid,
            new VoxelPointMeters(
                baseCenter.X - radiusMeters,
                baseCenter.Y,
                baseCenter.Z - radiusMeters),
            new VoxelPointMeters(
                baseCenter.X + radiusMeters,
                baseCenter.Y + heightMeters,
                baseCenter.Z + radiusMeters),
            point => Square(point.X - baseCenter.X) + Square(point.Z - baseCenter.Z)
                <= radiusSquared);
    }

    public static IReadOnlyList<VoxelCoordinate> Tower(
        VoxelGrid grid,
        VoxelPointMeters baseCenter,
        decimal radiusMeters,
        decimal heightMeters) =>
        Cylinder(grid, baseCenter, radiusMeters, heightMeters);

    public static IReadOnlyList<VoxelCoordinate> Sphere(
        VoxelGrid grid,
        VoxelPointMeters center,
        decimal radiusMeters)
    {
        RequireGrid(grid);
        RequirePositive(radiusMeters, nameof(radiusMeters));
        RequireSubgrid(grid, center);
        RequireSubgrid(grid, radiusMeters, nameof(radiusMeters));
        var radiusSquared = radiusMeters * radiusMeters;
        return RasterizeBounds(
            grid,
            new VoxelPointMeters(
                center.X - radiusMeters,
                center.Y - radiusMeters,
                center.Z - radiusMeters),
            new VoxelPointMeters(
                center.X + radiusMeters,
                center.Y + radiusMeters,
                center.Z + radiusMeters),
            point => Square(point.X - center.X)
                + Square(point.Y - center.Y)
                + Square(point.Z - center.Z)
                <= radiusSquared);
    }

    public static IReadOnlyList<VoxelCoordinate> Pyramid(
        VoxelGrid grid,
        VoxelPointMeters baseCenter,
        decimal widthMeters,
        decimal depthMeters,
        decimal heightMeters)
    {
        RequireGrid(grid);
        RequirePositive(widthMeters, nameof(widthMeters));
        RequirePositive(depthMeters, nameof(depthMeters));
        RequirePositive(heightMeters, nameof(heightMeters));
        RequireSubgrid(grid, baseCenter);
        RequireSubgrid(grid, widthMeters, nameof(widthMeters));
        RequireSubgrid(grid, depthMeters, nameof(depthMeters));
        RequireSubgrid(grid, heightMeters, nameof(heightMeters));

        return RasterizeBounds(
            grid,
            new VoxelPointMeters(
                baseCenter.X - widthMeters / 2m,
                baseCenter.Y,
                baseCenter.Z - depthMeters / 2m),
            new VoxelPointMeters(
                baseCenter.X + widthMeters / 2m,
                baseCenter.Y + heightMeters,
                baseCenter.Z + depthMeters / 2m),
            point =>
            {
                // Sample taper at the voxel's lower face. A layer supports the
                // narrowing layer above it instead of losing its edge cells
                // to repeating-decimal centre arithmetic.
                var layerBase = point.Y - grid.VoxelSizeMeters / 2m;
                var remaining = 1m - (layerBase - baseCenter.Y) / heightMeters;
                return remaining > 0m
                    && Math.Abs(point.X - baseCenter.X) <= widthMeters * remaining / 2m
                    && Math.Abs(point.Z - baseCenter.Z) <= depthMeters * remaining / 2m;
            });
    }

    /// <summary>
    /// Builds a solid staircase. Each horizontal metre advances one step and
    /// adds <paramref name="riseVoxelsPerStep"/> filled voxels to its column.
    /// </summary>
    public static IReadOnlyList<VoxelCoordinate> Stairs(
        VoxelGrid grid,
        VoxelCoordinate origin,
        int widthVoxels,
        int stepCount,
        int riseVoxelsPerStep,
        VoxelStairDirection direction)
    {
        RequireGrid(grid);
        if (widthVoxels <= 0 || stepCount <= 0 || riseVoxelsPerStep <= 0)
            throw new SceneMakerDocumentException("Stair width, step count and rise must be positive.");
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));

        var cells = new SortedSet<VoxelCoordinate>();
        for (var step = 0; step < stepCount; step++)
        {
            for (var across = 0; across < widthVoxels; across++)
            {
                for (var rise = 0; rise < (step + 1) * riseVoxelsPerStep; rise++)
                {
                    cells.Add(direction switch
                    {
                        VoxelStairDirection.PositiveX =>
                            new VoxelCoordinate(origin.X + step, origin.Y + rise, origin.Z + across),
                        VoxelStairDirection.NegativeX =>
                            new VoxelCoordinate(origin.X - step, origin.Y + rise, origin.Z + across),
                        VoxelStairDirection.PositiveZ =>
                            new VoxelCoordinate(origin.X + across, origin.Y + rise, origin.Z + step),
                        VoxelStairDirection.NegativeZ =>
                            new VoxelCoordinate(origin.X + across, origin.Y + rise, origin.Z - step),
                        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
                    });
                }
            }
        }
        return [.. cells];
    }

    public static IReadOnlyList<VoxelCoordinate> ExtrudedPolygon(
        VoxelGrid grid,
        IReadOnlyList<VoxelPointMeters> footprint,
        decimal minimumElevationMeters,
        decimal maximumElevationMeters)
    {
        RequireGrid(grid);
        ArgumentNullException.ThrowIfNull(footprint);
        if (footprint.Count < 3)
            throw new SceneMakerDocumentException("A voxel polygon requires at least three points.");
        if (maximumElevationMeters <= minimumElevationMeters)
            throw new SceneMakerDocumentException("A voxel volume top must be above its base.");
        foreach (var point in footprint)
        {
            RequireSubgrid(grid, point.X, "footprint X");
            RequireSubgrid(grid, point.Z, "footprint Z");
        }
        RequireSubgrid(grid, minimumElevationMeters, nameof(minimumElevationMeters));
        RequireSubgrid(grid, maximumElevationMeters, nameof(maximumElevationMeters));

        var minimumX = footprint.Min(static point => point.X);
        var maximumX = footprint.Max(static point => point.X);
        var minimumZ = footprint.Min(static point => point.Z);
        var maximumZ = footprint.Max(static point => point.Z);
        return RasterizeBounds(
            grid,
            new VoxelPointMeters(minimumX, minimumElevationMeters, minimumZ),
            new VoxelPointMeters(maximumX, maximumElevationMeters, maximumZ),
            point => Contains2D(footprint, point.X, point.Z));
    }

    private static IReadOnlyList<VoxelCoordinate> RasterizeBounds(
        VoxelGrid grid,
        VoxelPointMeters minimum,
        VoxelPointMeters maximum,
        Func<VoxelPointMeters, bool> contains)
    {
        var voxelSize = grid.VoxelSizeMeters;
        var minimumX = Math.Max(0, grid.VoxelIndex(minimum.X));
        var maximumX = Math.Min(
            grid.WidthVoxels - 1,
            grid.VoxelIndex(maximum.X - decimal.One / 1_000_000m));
        var minimumY = grid.VoxelIndex(minimum.Y);
        var maximumY = grid.VoxelIndex(maximum.Y - decimal.One / 1_000_000m);
        var minimumZ = Math.Max(0, grid.VoxelIndex(minimum.Z));
        var maximumZ = Math.Min(
            grid.DepthVoxels - 1,
            grid.VoxelIndex(maximum.Z - decimal.One / 1_000_000m));
        var cells = new List<VoxelCoordinate>();
        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var z = minimumZ; z <= maximumZ; z++)
            {
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var center = new VoxelPointMeters(
                        (x + 0.5m) * voxelSize,
                        (y + 0.5m) * voxelSize,
                        (z + 0.5m) * voxelSize);
                    if (contains(center)) cells.Add(new VoxelCoordinate(x, y, z));
                }
            }
        }
        return cells;
    }

    private static bool Contains2D(
        IReadOnlyList<VoxelPointMeters> polygon,
        decimal x,
        decimal z)
    {
        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            if (OnSegment(start, end, x, z)) return true;
            if ((start.Z > z) == (end.Z > z)) continue;
            var intersectionX = start.X
                + (z - start.Z) * (end.X - start.X) / (end.Z - start.Z);
            if (intersectionX >= x) inside = !inside;
        }
        return inside;
    }

    private static bool OnSegment(
        VoxelPointMeters start,
        VoxelPointMeters end,
        decimal x,
        decimal z)
    {
        var cross = (x - start.X) * (end.Z - start.Z)
            - (z - start.Z) * (end.X - start.X);
        return cross == 0m
            && x >= Math.Min(start.X, end.X)
            && x <= Math.Max(start.X, end.X)
            && z >= Math.Min(start.Z, end.Z)
            && z <= Math.Max(start.Z, end.Z);
    }

    private static decimal Square(decimal value) => value * value;

    private static void RequireGrid(VoxelGrid? grid) => ArgumentNullException.ThrowIfNull(grid);

    private static void RequirePositive(VoxelSizeMeters size)
    {
        RequirePositive(size.X, "size X");
        RequirePositive(size.Y, "size Y");
        RequirePositive(size.Z, "size Z");
    }

    private static void RequirePositive(decimal value, string label)
    {
        if (value <= 0m)
            throw new SceneMakerDocumentException($"{label} must be positive.");
    }

    private static void RequireSubgrid(
        VoxelGrid grid,
        VoxelPointMeters point,
        string label = "point")
    {
        RequireSubgrid(grid, point.X, $"{label} X");
        RequireSubgrid(grid, point.Y, $"{label} Y");
        RequireSubgrid(grid, point.Z, $"{label} Z");
    }

    private static void RequireSubgrid(VoxelGrid grid, decimal value, string label)
    {
        if (value % grid.SubgridMeters != 0m)
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"{label} must align to the voxel subgrid of {grid.SubgridMeters} m."));
        }
    }
}
