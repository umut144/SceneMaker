namespace SceneMaker.Core;

/// <summary>One runtime vertex of a baked Path, in scene-local metres.</summary>
public readonly record struct RouteSurfaceBakeVertex(
    decimal XMeters,
    decimal YMeters,
    decimal ElevationMeters);

/// <summary>An explicit edge around one primitive of the baked Path union.</summary>
public readonly record struct RouteSurfaceBoundaryEdge(
    int StartVertexIndex,
    int EndVertexIndex);

/// <summary>
/// One flattened centerline sample. Authored point indices mark every segment
/// and grade transition; intermediate samples are introduced only by the
/// shared Bezier flattener.
/// </summary>
public readonly record struct RouteSurfaceCenterlineSample(
    decimal XMeters,
    decimal YMeters,
    decimal ElevationMeters,
    decimal WidthMeters,
    decimal StationMeters,
    int? AuthoredPointIndex);

/// <summary>
/// How one authored segment maps onto the baked centerline. It repeats the
/// authored operation and clearance so a consumer reading only the bake can
/// tell an excavated interval from a materialized one without joining back to
/// the authored Scene.
/// </summary>
public sealed record RouteSurfaceBakeSegment
{
    public required string SegmentId { get; init; }
    public required int GradePercent { get; init; }
    public required RouteSegmentOperation Operation { get; init; }
    public decimal? ClearanceAboveMeters { get; init; }
    public required int StartPointIndex { get; init; }
    public required int EndPointIndex { get; init; }
    public required int StartSampleIndex { get; init; }
    public required int EndSampleIndex { get; init; }
}

/// <summary>
/// The runtime geometry of one band, without a word about what the band is.
/// Triangle indices are a flat list of triples; boundary edges describe each
/// union primitive explicitly - one quad per flattened segment and one round
/// join at every interior sample.
///
/// <para>Every authored kind that is drawn as a band ends up here: a Path, a
/// bridge deck, and the surface of a water body. That is the point of the type
/// - a consumer reads one shape, and SceneMaker has one place where authored
/// points become triangles.</para>
/// </summary>
public sealed record BakedSurfaceBand
{
    public required IReadOnlyList<RouteSurfaceBakeVertex> Vertices { get; init; }
    public required IReadOnlyList<int> TriangleIndices { get; init; }
    public required IReadOnlyList<RouteSurfaceBoundaryEdge> BoundaryEdges { get; init; }
    public required IReadOnlyList<RouteSurfaceCenterlineSample> CenterlineSamples { get; init; }
}

/// <summary>
/// Runtime-ready geometry for one Path: the band above, plus the identity and
/// the authored intervals only a Path has.
/// </summary>
public sealed record BakedRouteSurface
{
    public required string RouteSurfaceId { get; init; }
    public required string AssetKey { get; init; }
    public required IReadOnlyList<RouteSurfaceBakeVertex> Vertices { get; init; }
    public required IReadOnlyList<int> TriangleIndices { get; init; }
    public required IReadOnlyList<RouteSurfaceBoundaryEdge> BoundaryEdges { get; init; }
    public required IReadOnlyList<RouteSurfaceCenterlineSample> CenterlineSamples { get; init; }
    public required IReadOnlyList<RouteSurfaceBakeSegment> Segments { get; init; }
}

/// <summary>
/// One baked triangle used by an authored segment. Every triangle remembers
/// the segment's portal samples so raster cuts can apply the boundary to any
/// Bezier primitive that returns through a portal footprint. Round-join
/// triangles at an authored boundary remain shared by both neighbours.
/// </summary>
internal readonly record struct RouteSurfaceBakeTriangle(
    int TriangleOffset,
    int? StartPortalSampleIndex,
    int? EndPortalSampleIndex);

/// <summary>
/// Bakes the exact band primitives used by the Canvas into engine-neutral
/// runtime geometry. Consumers never need to flatten Beziers, interpolate
/// width or elevation, or choose join and cap rules independently.
///
/// <para>A Path is what it was written for and no longer all it serves: a
/// bridge deck arrives as the two-point Path it is, and a water body's surface
/// arrives as bare band points. One flattener, one mesher, one shape - so a
/// second way of turning authored data into geometry never has to exist.</para>
/// </summary>
public static class RouteSurfaceBake
{
    public const int RoundJoinSides = 16;
    private const int DecimalPlaces = 6;

    public static BakedRouteSurface Build(
        WorkspaceMetrics metrics,
        RouteSurfaceDocument route)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(route);
        var surface = RouteSurfaceGeometry.Prepare(metrics, route);
        var centerline = surface.Centerline;
        var band = Bake(metrics, $"Path '{route.RouteSurfaceId}'", surface);
        var segments = route.Segments
            .Select((segment, index) => new RouteSurfaceBakeSegment
            {
                SegmentId = segment.SegmentId,
                GradePercent = segment.GradePercent,
                Operation = segment.Operation,
                ClearanceAboveMeters = segment.ClearanceAboveMeters,
                StartPointIndex = index,
                EndPointIndex = index + 1,
                StartSampleIndex = SampleIndex(centerline, centerline.AnchorStations[index]),
                EndSampleIndex = SampleIndex(centerline, centerline.AnchorStations[index + 1]),
            })
            .ToList();

        return new BakedRouteSurface
        {
            RouteSurfaceId = route.RouteSurfaceId,
            AssetKey = route.AssetKey,
            Vertices = band.Vertices,
            TriangleIndices = band.TriangleIndices,
            BoundaryEdges = band.BoundaryEdges,
            CenterlineSamples = band.CenterlineSamples,
            Segments = segments,
        };
    }

    /// <summary>
    /// The band alone, for an authored kind that is drawn as one without being
    /// a Path. A water body's surface is the case that asked for it: it has a
    /// centerline, a width and a height at every point, and no grade, no
    /// operation and no clearance that a baked interval could carry.
    ///
    /// <para><paramref name="subject"/> is how the caller names itself in a
    /// refusal - "Water body 'river_0001'", the way a Path says "Path
    /// 'route_0001'". A mesh that cannot be built has to say what it came
    /// from, and this layer deliberately does not know.</para>
    /// </summary>
    public static BakedSurfaceBand BuildBand(
        WorkspaceMetrics metrics,
        string subject,
        IReadOnlyList<RouteSurfacePoint> points)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentNullException.ThrowIfNull(points);
        return Bake(metrics, subject, RouteSurfaceGeometry.Prepare(metrics, points));
    }

    /// <summary>
    /// One prepared band, meshed and sampled: a quad per flattened segment, a
    /// round join at every interior sample, square outer caps, and a centerline
    /// sample carrying the interpolated elevation and width at each flattened
    /// point. Nothing here knows what the band is for.
    /// </summary>
    private static BakedSurfaceBand Bake(
        WorkspaceMetrics metrics,
        string subject,
        PreparedRouteSurface surface)
    {
        var centerline = surface.Centerline;
        var pixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var vertices = new List<RouteSurfaceBakeVertex>();
        var triangleIndices = new List<int>();
        var boundaryEdges = new List<RouteSurfaceBoundaryEdge>();

        for (var index = 0; index < surface.Corridor.Segments.Count; index++)
        {
            var segment = surface.Corridor.Segments[index];
            AddQuad(
                vertices,
                triangleIndices,
                boundaryEdges,
                segment,
                surface.ElevationAt(centerline.Stations[index]),
                surface.ElevationAt(centerline.Stations[index + 1]),
                pixelsPerMeter);
        }

        // A band has square outer caps. Every interior flattened point gets
        // one round join, matching the Canvas's historical visible union.
        for (var index = 1; index + 1 < centerline.Points.Count; index++)
        {
            AddRoundJoin(
                vertices,
                triangleIndices,
                boundaryEdges,
                centerline.Points[index],
                surface.Corridor.Segments[index - 1].EndHalfWidth,
                surface.ElevationAt(centerline.Stations[index]),
                pixelsPerMeter);
        }

        var samples = centerline.Points
            .Select((point, index) => new RouteSurfaceCenterlineSample(
                Round(point.X / pixelsPerMeter),
                Round(point.Y / pixelsPerMeter),
                Round(surface.ElevationAt(centerline.Stations[index])),
                Round(WidthAt(surface, index) / pixelsPerMeter * 2.0),
                Round(centerline.Stations[index] / pixelsPerMeter),
                AuthoredPointIndex(centerline, index)))
            .ToList();

        ValidateMesh(subject, vertices, triangleIndices, boundaryEdges);
        return new BakedSurfaceBand
        {
            Vertices = vertices,
            TriangleIndices = triangleIndices,
            BoundaryEdges = boundaryEdges,
            CenterlineSamples = samples,
        };
    }

    /// <summary>
    /// Baked triangles belonging to one authored interval. The bake writes
    /// every flattened-segment quad first and then every interior round join.
    /// A join on an authored boundary belongs to both adjacent intervals. Every
    /// returned triangle also carries the interval's portal sample indices;
    /// raster semantics decide whether one of its samples is inside a portal.
    /// </summary>
    internal static IEnumerable<RouteSurfaceBakeTriangle> TrianglesForSegment(
        BakedRouteSurface bake,
        RouteSurfaceBakeSegment segment)
    {
        ArgumentNullException.ThrowIfNull(bake);
        ArgumentNullException.ThrowIfNull(segment);
        var flattenedSegmentCount = bake.CenterlineSamples.Count - 1;
        if (segment.StartSampleIndex < 0
            || segment.EndSampleIndex > flattenedSegmentCount
            || segment.StartSampleIndex >= segment.EndSampleIndex)
        {
            throw new SceneMakerDocumentException(
                $"Path segment '{segment.SegmentId}' has an invalid baked sample range.");
        }
        var expectedTriangleIndexCount = flattenedSegmentCount * 6
            + Math.Max(0, flattenedSegmentCount - 1) * RoundJoinSides * 3;
        if (bake.TriangleIndices.Count != expectedTriangleIndexCount)
        {
            throw new SceneMakerDocumentException(
                $"Path '{bake.RouteSurfaceId}' has an unexpected baked triangle layout.");
        }

        int? startPortal = segment.StartSampleIndex > 0
            ? segment.StartSampleIndex
            : null;
        int? endPortal = segment.EndSampleIndex < flattenedSegmentCount
            ? segment.EndSampleIndex
            : null;
        for (var sample = segment.StartSampleIndex; sample < segment.EndSampleIndex; sample++)
        {
            var quadOffset = sample * 6;
            yield return new RouteSurfaceBakeTriangle(
                quadOffset,
                startPortal,
                endPortal);
            yield return new RouteSurfaceBakeTriangle(
                quadOffset + 3,
                startPortal,
                endPortal);
        }

        var joinsOffset = flattenedSegmentCount * 6;
        var firstJoin = Math.Max(1, segment.StartSampleIndex);
        var lastJoin = Math.Min(flattenedSegmentCount - 1, segment.EndSampleIndex);
        for (var sample = firstJoin; sample <= lastJoin; sample++)
        {
            var joinOffset = joinsOffset
                + (sample - 1) * RoundJoinSides * 3;
            for (var side = 0; side < RoundJoinSides; side++)
            {
                yield return new RouteSurfaceBakeTriangle(
                    joinOffset + side * 3,
                    startPortal,
                    endPortal);
            }
        }
    }

    private static void AddQuad(
        List<RouteSurfaceBakeVertex> vertices,
        List<int> triangles,
        List<RouteSurfaceBoundaryEdge> edges,
        OpenChainCorridorSegment segment,
        double startElevation,
        double endElevation,
        double pixelsPerMeter)
    {
        var chain = segment.Chain;
        var length = Math.Sqrt(chain.LengthSquared);
        if (!double.IsFinite(length) || length <= 0.0)
            throw new SceneMakerDocumentException("A baked band segment has no finite horizontal run.");
        var normalX = -chain.DeltaY / length;
        var normalY = chain.DeltaX / length;
        var endX = chain.StartX + chain.DeltaX;
        var endY = chain.StartY + chain.DeltaY;
        var first = vertices.Count;
        vertices.Add(Vertex(
            chain.StartX + normalX * segment.StartHalfWidth,
            chain.StartY + normalY * segment.StartHalfWidth,
            startElevation,
            pixelsPerMeter));
        vertices.Add(Vertex(
            endX + normalX * segment.EndHalfWidth,
            endY + normalY * segment.EndHalfWidth,
            endElevation,
            pixelsPerMeter));
        vertices.Add(Vertex(
            endX - normalX * segment.EndHalfWidth,
            endY - normalY * segment.EndHalfWidth,
            endElevation,
            pixelsPerMeter));
        vertices.Add(Vertex(
            chain.StartX - normalX * segment.StartHalfWidth,
            chain.StartY - normalY * segment.StartHalfWidth,
            startElevation,
            pixelsPerMeter));
        triangles.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        AddLoop(edges, first, 4);
    }

    private static void AddRoundJoin(
        List<RouteSurfaceBakeVertex> vertices,
        List<int> triangles,
        List<RouteSurfaceBoundaryEdge> edges,
        ChainPoint center,
        double radius,
        double elevation,
        double pixelsPerMeter)
    {
        if (!double.IsFinite(radius) || radius <= 0.0)
            throw new SceneMakerDocumentException("A baked band join needs a finite positive radius.");
        var centerIndex = vertices.Count;
        vertices.Add(Vertex(center.X, center.Y, elevation, pixelsPerMeter));
        var ringStart = vertices.Count;
        for (var side = 0; side < RoundJoinSides; side++)
        {
            var angle = side * Math.Tau / RoundJoinSides;
            vertices.Add(Vertex(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius,
                elevation,
                pixelsPerMeter));
        }
        for (var side = 0; side < RoundJoinSides; side++)
        {
            triangles.Add(centerIndex);
            triangles.Add(ringStart + side);
            triangles.Add(ringStart + (side + 1) % RoundJoinSides);
        }
        AddLoop(edges, ringStart, RoundJoinSides);
    }

    private static void AddLoop(
        List<RouteSurfaceBoundaryEdge> edges,
        int first,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            edges.Add(new RouteSurfaceBoundaryEdge(
                first + index,
                first + (index + 1) % count));
        }
    }

    private static RouteSurfaceBakeVertex Vertex(
        double x,
        double y,
        double elevation,
        double pixelsPerMeter) => new(
        Round(x / pixelsPerMeter),
        Round(y / pixelsPerMeter),
        Round(elevation));

    private static double WidthAt(PreparedRouteSurface surface, int sampleIndex) =>
        sampleIndex + 1 < surface.Centerline.Points.Count
            ? surface.Corridor.Segments[sampleIndex].StartHalfWidth
            : surface.Corridor.Segments[^1].EndHalfWidth;

    private static int? AuthoredPointIndex(FlattenedChain centerline, int sampleIndex)
    {
        var station = centerline.Stations[sampleIndex];
        for (var index = 0; index < centerline.AnchorStations.Count; index++)
        {
            if (centerline.AnchorStations[index] == station) return index;
        }
        return null;
    }

    private static int SampleIndex(FlattenedChain centerline, double station)
    {
        for (var index = 0; index < centerline.Stations.Count; index++)
        {
            if (centerline.Stations[index] == station) return index;
        }
        throw new SceneMakerDocumentException("A Path anchor is missing from its baked centerline.");
    }

    private static decimal Round(double value)
    {
        if (!double.IsFinite(value))
            throw new SceneMakerDocumentException("A baked band contains a non-finite value.");
        return Math.Round((decimal)value, DecimalPlaces, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// What a band has to be before it is written down. <paramref name="subject"/>
    /// is the caller's own name for itself, so the refusal names the Path or
    /// the water body rather than the layer that noticed.
    /// </summary>
    private static void ValidateMesh(
        string subject,
        IReadOnlyList<RouteSurfaceBakeVertex> vertices,
        IReadOnlyList<int> triangles,
        IReadOnlyList<RouteSurfaceBoundaryEdge> edges)
    {
        if (triangles.Count == 0 || triangles.Count % 3 != 0)
            throw new SceneMakerDocumentException($"{subject} has no valid baked triangles.");
        for (var index = 0; index < triangles.Count; index += 3)
        {
            var a = triangles[index];
            var b = triangles[index + 1];
            var c = triangles[index + 2];
            if ((uint)a >= vertices.Count || (uint)b >= vertices.Count || (uint)c >= vertices.Count)
                throw new SceneMakerDocumentException($"{subject} has an invalid triangle index.");
            var av = vertices[a];
            var bv = vertices[b];
            var cv = vertices[c];
            var twiceArea = (bv.XMeters - av.XMeters) * (cv.YMeters - av.YMeters)
                - (bv.YMeters - av.YMeters) * (cv.XMeters - av.XMeters);
            if (twiceArea == 0m)
                throw new SceneMakerDocumentException($"{subject} has a degenerate baked triangle.");
        }
        foreach (var edge in edges)
        {
            if ((uint)edge.StartVertexIndex >= vertices.Count
                || (uint)edge.EndVertexIndex >= vertices.Count
                || edge.StartVertexIndex == edge.EndVertexIndex)
            {
                throw new SceneMakerDocumentException($"{subject} has an invalid boundary edge.");
            }
        }
    }
}
