namespace SceneMaker.Core;

/// <summary>
/// One independent Path surface sampled at a water-cell centre. More than one
/// sample may occupy the same X/Y when a Path crosses itself or Paths overlap.
/// </summary>
public readonly record struct RouteSurfaceRasterCell(
    int X,
    int Y,
    decimal ElevationMeters,
    string AssetKey,
    string RouteSurfaceId);

/// <summary>
/// Samples the runtime Path bake onto the finest authored volumetric raster.
/// The Canvas and layered queries therefore use the same triangles, joins,
/// caps and interpolated elevation that the export already carries.
/// </summary>
public static class RouteSurfaceRaster
{
    public static IReadOnlyList<RouteSurfaceRasterCell> Cells(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        HashSet<RouteSurfaceRasterCell> cells = [];
        foreach (var route in scene.RouteSurfaces)
        {
            var bake = RouteSurfaceBake.Build(metrics, route);
            for (var index = 0; index < bake.TriangleIndices.Count; index += 3)
            {
                var first = bake.Vertices[bake.TriangleIndices[index]];
                var second = bake.Vertices[bake.TriangleIndices[index + 1]];
                var third = bake.Vertices[bake.TriangleIndices[index + 2]];
                AddTriangleCells(
                    cells,
                    scene,
                    metrics,
                    bake.RouteSurfaceId,
                    bake.AssetKey,
                    first,
                    second,
                    third);
            }
        }

        return [.. cells
            .OrderBy(static cell => cell.Y)
            .ThenBy(static cell => cell.X)
            .ThenBy(static cell => cell.ElevationMeters)
            .ThenBy(static cell => cell.RouteSurfaceId, StringComparer.Ordinal)
            .ThenBy(static cell => cell.AssetKey, StringComparer.Ordinal)];
    }

    private static void AddTriangleCells(
        HashSet<RouteSurfaceRasterCell> cells,
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string routeSurfaceId,
        string assetKey,
        RouteSurfaceBakeVertex first,
        RouteSurfaceBakeVertex second,
        RouteSurfaceBakeVertex third)
    {
        var step = metrics.WaterCellMeters;
        var half = step / 2m;
        var minX = Math.Min(first.XMeters, Math.Min(second.XMeters, third.XMeters));
        var maxX = Math.Max(first.XMeters, Math.Max(second.XMeters, third.XMeters));
        var minY = Math.Min(first.YMeters, Math.Min(second.YMeters, third.YMeters));
        var maxY = Math.Max(first.YMeters, Math.Max(second.YMeters, third.YMeters));
        var firstX = CellAtOrAfter(minX, step, half, metrics.SceneWidthWaterCells(scene));
        var lastX = CellAtOrBefore(maxX, step, half, metrics.SceneWidthWaterCells(scene));
        var firstY = CellAtOrAfter(minY, step, half, metrics.SceneHeightWaterCells(scene));
        var lastY = CellAtOrBefore(maxY, step, half, metrics.SceneHeightWaterCells(scene));
        if (firstX > lastX || firstY > lastY) return;

        for (var y = firstY; y <= lastY; y++)
        {
            var centreY = y * step + half;
            for (var x = firstX; x <= lastX; x++)
            {
                var centreX = x * step + half;
                if (ElevationAt(
                        centreX,
                        centreY,
                        first,
                        second,
                        third) is not { } elevation)
                {
                    continue;
                }
                cells.Add(new RouteSurfaceRasterCell(
                    x,
                    y,
                    elevation,
                    assetKey,
                    routeSurfaceId));
            }
        }
    }

    private static int CellAtOrAfter(
        decimal boundary,
        decimal step,
        decimal half,
        int count) =>
        checked((int)Math.Clamp(
            decimal.Ceiling((boundary - half) / step),
            0m,
            count));

    private static int CellAtOrBefore(
        decimal boundary,
        decimal step,
        decimal half,
        int count) =>
        checked((int)Math.Clamp(
            decimal.Floor((boundary - half) / step),
            -1m,
            count - 1m));

    /// <summary>
    /// Barycentric interpolation over the baked triangle. Boundaries are
    /// inclusive, so adjacent triangles agree on their shared edge and the
    /// HashSet removes the identical sample.
    /// </summary>
    private static decimal? ElevationAt(
        decimal x,
        decimal y,
        RouteSurfaceBakeVertex first,
        RouteSurfaceBakeVertex second,
        RouteSurfaceBakeVertex third)
    {
        var abX = second.XMeters - first.XMeters;
        var abY = second.YMeters - first.YMeters;
        var acX = third.XMeters - first.XMeters;
        var acY = third.YMeters - first.YMeters;
        var apX = x - first.XMeters;
        var apY = y - first.YMeters;
        var determinant = Cross(abX, abY, acX, acY);
        var alongAb = Cross(apX, apY, acX, acY);
        var alongAc = Cross(abX, abY, apX, apY);
        var inside = determinant > 0m
            ? alongAb >= 0m && alongAc >= 0m && alongAb + alongAc <= determinant
            : alongAb <= 0m && alongAc <= 0m && alongAb + alongAc >= determinant;
        if (!inside) return null;

        var abWeight = alongAb / determinant;
        var acWeight = alongAc / determinant;
        return first.ElevationMeters
            + abWeight * (second.ElevationMeters - first.ElevationMeters)
            + acWeight * (third.ElevationMeters - first.ElevationMeters);
    }

    private static decimal Cross(decimal ax, decimal ay, decimal bx, decimal by) =>
        ax * by - ay * bx;
}
