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
/// One Terrain cut sampled from a subtractive authored Path segment. It is
/// the derived half of an excavated interval, in the same source/derived split
/// the water raster already makes: the authored operation and clearance stay
/// in the Scene, and these cells are what a runtime subtracts.
/// </summary>
public readonly record struct RouteSurfaceRasterCut(
    int X,
    int Y,
    decimal BottomMeters,
    decimal TopMeters,
    string RouteSurfaceId,
    string SegmentId);

internal sealed record PreparedRouteSurfaceRaster(
    IReadOnlyList<RouteSurfaceRasterCell> Cells,
    IReadOnlyList<RouteSurfaceRasterCut> Cuts);

/// <summary>
/// Samples the runtime Path bake onto the finest authored volumetric raster.
/// The Canvas and layered queries therefore use the same triangles, joins,
/// caps and interpolated elevation that the export already carries.
/// </summary>
public static class RouteSurfaceRaster
{
    public static IReadOnlyList<RouteSurfaceRasterCell> Cells(
        SceneDocument scene,
        WorkspaceMetrics metrics) => Prepare(scene, metrics).Cells;

    /// <summary>
    /// Samples only subtractive authored intervals from the same baked
    /// triangles used by <see cref="Cells"/>. Their Path surface remains a
    /// separate cell; this answer is only the Terrain volume above it.
    /// </summary>
    public static IReadOnlyList<RouteSurfaceRasterCut> Cuts(
        SceneDocument scene,
        WorkspaceMetrics metrics) => Prepare(scene, metrics).Cuts;

    internal static PreparedRouteSurfaceRaster Prepare(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        HashSet<RouteSurfaceRasterCell> cells = [];
        HashSet<RouteSurfaceRasterCut> cuts = [];
        // A deck is a level two-point Path in everything but its record, and
        // the export bakes it through this same route. Rasterizing it here too
        // is what lets the Section view clip a bridge the way it clips a Path -
        // gone above the plane, the river visible beneath - from the triangles
        // the consumer will build, rather than from a second reading of the
        // span. A deck is additive, so it contributes surface cells and no cuts.
        var bands = scene.RouteSurfaces.Concat(scene.Bridges.Select(BridgeGeometry.DeckRoute));
        foreach (var route in bands)
        {
            var bake = RouteSurfaceBake.Build(metrics, route);
            Dictionary<int, PortalPlane> portalPlanes = [];
            for (var index = 0; index < bake.TriangleIndices.Count; index += 3)
            {
                AddTriangleCells(
                    cells,
                    scene,
                    metrics,
                    bake.RouteSurfaceId,
                    bake.AssetKey,
                    bake.Vertices[bake.TriangleIndices[index]],
                    bake.Vertices[bake.TriangleIndices[index + 1]],
                    bake.Vertices[bake.TriangleIndices[index + 2]]);
            }
            for (var segmentIndex = 0; segmentIndex < route.Segments.Count; segmentIndex++)
            {
                var authored = route.Segments[segmentIndex];
                if (authored.Operation != RouteSegmentOperation.Subtractive) continue;
                if (authored.ClearanceAboveMeters is not > 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"Path segment '{authored.SegmentId}' needs positive clearance_above_meters.");
                }

                var baked = bake.Segments[segmentIndex];
                if (!StringComparer.Ordinal.Equals(authored.SegmentId, baked.SegmentId))
                {
                    throw new SceneMakerDocumentException(
                        $"Path segment '{authored.SegmentId}' does not match its baked interval.");
                }
                foreach (var triangle in RouteSurfaceBake.TrianglesForSegment(bake, baked))
                {
                    var triangleOffset = triangle.TriangleOffset;
                    AddTriangleCuts(
                        cuts,
                        scene,
                        metrics,
                        route.RouteSurfaceId,
                        authored.SegmentId,
                        authored.ClearanceAboveMeters.Value,
                        bake.Vertices[bake.TriangleIndices[triangleOffset]],
                        bake.Vertices[bake.TriangleIndices[triangleOffset + 1]],
                        bake.Vertices[bake.TriangleIndices[triangleOffset + 2]],
                        PortalClipsFor(bake, triangle, portalPlanes));
                }
            }
        }

        return new PreparedRouteSurfaceRaster(
            [.. cells
                .OrderBy(static cell => cell.Y)
                .ThenBy(static cell => cell.X)
                .ThenBy(static cell => cell.ElevationMeters)
                .ThenBy(static cell => cell.RouteSurfaceId, StringComparer.Ordinal)
                .ThenBy(static cell => cell.AssetKey, StringComparer.Ordinal)],
            [.. cuts
                .OrderBy(static cut => cut.Y)
                .ThenBy(static cut => cut.X)
                .ThenBy(static cut => cut.BottomMeters)
                .ThenBy(static cut => cut.TopMeters)
                .ThenBy(static cut => cut.RouteSurfaceId, StringComparer.Ordinal)
                .ThenBy(static cut => cut.SegmentId, StringComparer.Ordinal)]);
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
        foreach (var sample in TriangleSamples(scene, metrics, first, second, third))
        {
            cells.Add(new RouteSurfaceRasterCell(
                sample.X,
                sample.Y,
                sample.ElevationMeters,
                assetKey,
                routeSurfaceId));
        }
    }

    private static IEnumerable<TriangleSample> TriangleSamples(
        SceneDocument scene,
        WorkspaceMetrics metrics,
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
        if (firstX > lastX || firstY > lastY) yield break;

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
                yield return new TriangleSample(x, y, centreX, centreY, elevation);
            }
        }
    }

    private static void AddTriangleCuts(
        HashSet<RouteSurfaceRasterCut> cuts,
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string routeSurfaceId,
        string segmentId,
        decimal clearanceAboveMeters,
        RouteSurfaceBakeVertex first,
        RouteSurfaceBakeVertex second,
        RouteSurfaceBakeVertex third,
        PortalClips portalClips)
    {
        foreach (var sample in TriangleSamples(scene, metrics, first, second, third))
        {
            if (!portalClips.Contains(sample.XMeters, sample.YMeters))
            {
                continue;
            }
            cuts.Add(new RouteSurfaceRasterCut(
                sample.X,
                sample.Y,
                sample.ElevationMeters,
                sample.ElevationMeters + clearanceAboveMeters,
                routeSurfaceId,
                segmentId));
        }
    }

    private static PortalClips PortalClipsFor(
        BakedRouteSurface bake,
        RouteSurfaceBakeTriangle triangle,
        Dictionary<int, PortalPlane> portalPlanes)
    {
        return new PortalClips(
            triangle.StartPortalSampleIndex is { } start
                ? new PortalClip(
                    PortalPlaneFor(bake, start, portalPlanes),
                    KeepAfter: true)
                : null,
            triangle.EndPortalSampleIndex is { } end
                ? new PortalClip(
                    PortalPlaneFor(bake, end, portalPlanes),
                    KeepAfter: false)
                : null);
    }

    private static PortalPlane PortalPlaneFor(
        BakedRouteSurface bake,
        int sampleIndex,
        Dictionary<int, PortalPlane> portalPlanes)
    {
        if (portalPlanes.TryGetValue(sampleIndex, out var prepared)) return prepared;
        if (sampleIndex <= 0 || sampleIndex + 1 >= bake.CenterlineSamples.Count)
        {
            throw new SceneMakerDocumentException(
                $"Path '{bake.RouteSurfaceId}' has a portal outside its baked centerline.");
        }

        var before = bake.CenterlineSamples[sampleIndex - 1];
        var portal = bake.CenterlineSamples[sampleIndex];
        var after = bake.CenterlineSamples[sampleIndex + 1];
        var incomingX = portal.XMeters - before.XMeters;
        var incomingY = portal.YMeters - before.YMeters;
        var outgoingX = after.XMeters - portal.XMeters;
        var outgoingY = after.YMeters - portal.YMeters;
        var incomingLength = Math.Sqrt((double)(incomingX * incomingX + incomingY * incomingY));
        var outgoingLength = Math.Sqrt((double)(outgoingX * outgoingX + outgoingY * outgoingY));
        if (!double.IsFinite(incomingLength)
            || !double.IsFinite(outgoingLength)
            || incomingLength <= 0.0
            || outgoingLength <= 0.0)
        {
            throw new SceneMakerDocumentException(
                $"Path '{bake.RouteSurfaceId}' has no direction at a baked portal.");
        }

        var tangentX = (double)incomingX / incomingLength + (double)outgoingX / outgoingLength;
        var tangentY = (double)incomingY / incomingLength + (double)outgoingY / outgoingLength;
        if (tangentX * tangentX + tangentY * tangentY <= 1e-18)
        {
            tangentX = (double)outgoingX / outgoingLength;
            tangentY = (double)outgoingY / outgoingLength;
        }
        var plane = new PortalPlane(
            portal.XMeters,
            portal.YMeters,
            (decimal)tangentX,
            (decimal)tangentY,
            portal.WidthMeters / 2m);
        portalPlanes.Add(sampleIndex, plane);
        return plane;
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
    /// inclusive. Adjacent triangles can differ at the last decimal place on
    /// their shared edge; the raster sets make exact duplicates harmless and
    /// the later cut merge absorbs equivalent neighbouring samples.
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

    private readonly record struct PortalPlane(
        decimal XMeters,
        decimal YMeters,
        decimal TangentX,
        decimal TangentY,
        decimal RadiusMeters);

    private readonly record struct PortalClip(PortalPlane Plane, bool KeepAfter)
    {
        public bool Contains(decimal xMeters, decimal yMeters)
        {
            var offsetX = xMeters - Plane.XMeters;
            var offsetY = yMeters - Plane.YMeters;
            if (offsetX * offsetX + offsetY * offsetY
                > Plane.RadiusMeters * Plane.RadiusMeters)
            {
                return true;
            }
            var side = offsetX * Plane.TangentX + offsetY * Plane.TangentY;
            return KeepAfter ? side >= 0m : side <= 0m;
        }
    }

    private readonly record struct PortalClips(PortalClip? Start, PortalClip? End)
    {
        public bool Contains(decimal xMeters, decimal yMeters) =>
            (Start is not { } start || start.Contains(xMeters, yMeters))
            && (End is not { } end || end.Contains(xMeters, yMeters));
    }

    private readonly record struct TriangleSample(
        int X,
        int Y,
        decimal XMeters,
        decimal YMeters,
        decimal ElevationMeters);
}
