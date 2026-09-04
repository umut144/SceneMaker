namespace SceneMaker.Core;

/// <summary>
/// A point on a flattened chain, in authoring pixels. Sub-pixel on purpose: the
/// curve runs between authored points, and rounding it to whole pixels first
/// would put a visible stair into whatever the chain is used to produce.
/// </summary>
public readonly record struct ChainPoint(double X, double Y);

/// <summary>
/// One authored point of a cubic Bezier chain: where it sits, and the two
/// handles that leave it as offsets from it.
///
/// <para>Geometry and nothing else. What a chain means - a river's centerline,
/// a route, the outline of a hill - lives in the document record that
/// converts into this, and each of those keeps its own fields. Sharing the
/// document record instead would force every one of them to carry the others'
/// values, and tie schemas together that have no reason to change at the same
/// time.</para>
/// </summary>
public readonly record struct BezierChainPoint(
    double X,
    double Y,
    double HandleInX,
    double HandleInY,
    double HandleOutX,
    double HandleOutY);

/// <summary>
/// A flattened chain together with where the authored points sit on it,
/// measured in authoring pixels travelled from the first.
///
/// <para>That measure - arc length - is what values along a chain are
/// interpolated over. The obvious alternative, the curve's own parameter, runs
/// unevenly: long handles make the middle of a segment pass quicker, so a
/// gradient would depend on how the author shaped the bend. Arc length is what
/// they see.</para>
/// </summary>
public sealed record FlattenedChain(
    IReadOnlyList<ChainPoint> Points,
    IReadOnlyList<double> Stations,
    IReadOnlyList<double> AnchorStations);

/// <summary>
/// A flattened closed chain: a ring of points, where each authored point sits
/// on it, and how long the whole way round is.
///
/// <para>The first point is not repeated at the end. A ring has no last point,
/// and writing one down invites every consumer to decide for itself whether to
/// skip it. The way back from <c>Points[^1]</c> to <c>Points[0]</c> is a
/// segment like any other; it is the reason <see cref="TotalLength"/> exists
/// separately from the last station, which only reaches the last point.</para>
///
/// <para>Derived geometry, never authored. The name says so on purpose: what an
/// author draws is a document, and this is what flattening made of it.</para>
/// </summary>
public sealed record FlattenedClosedChain(
    IReadOnlyList<ChainPoint> Points,
    IReadOnlyList<double> Stations,
    IReadOnlyList<double> AnchorStations,
    double TotalLength);

/// <summary>
/// One flattened piece of a chain, and where along the chain it starts.
///
/// <para>It carries no width and no ends. A segment knows how to answer where
/// the nearest point on it lies; what that distance is allowed to be, and
/// whether the chain stops there, belongs to whoever is drawing something along
/// it.</para>
/// </summary>
public readonly record struct ChainSegment(
    double StartX,
    double StartY,
    double DeltaX,
    double DeltaY,
    double LengthSquared,
    double MinY,
    double MaxY,
    double StartStation)
{
    /// <summary>
    /// Where the nearest point on this segment to (x, y) lies: how far away it
    /// is, how far along the segment it sits, and its station on the chain.
    ///
    /// <para>The fraction comes back alongside the station because a caller
    /// that varies something along the chain - a width, a height - has to
    /// interpolate it over the same fraction this projection used, not over one
    /// it works out again.</para>
    ///
    /// <para>A segment of no length has no direction to project onto, so it
    /// answers with its own start and a fraction of zero. Zero rather than
    /// anything else on purpose: a caller mixing two values by it must land
    /// exactly on the first.</para>
    /// </summary>
    public (double DistanceSquared, double T, double Station) ProjectTo(double x, double y)
    {
        var toPointX = x - StartX;
        var toPointY = y - StartY;
        if (LengthSquared <= 0.0)
            return (toPointX * toPointX + toPointY * toPointY, 0.0, StartStation);
        var t = (toPointX * DeltaX + toPointY * DeltaY) / LengthSquared;
        t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
        var closestX = toPointX - t * DeltaX;
        var closestY = toPointY - t * DeltaY;
        return (
            closestX * closestX + closestY * closestY,
            t,
            StartStation + t * Math.Sqrt(LengthSquared));
    }
}

/// <summary>
/// Cubic Bezier chains, flattened and measured. The arithmetic here is what
/// every authored curve in SceneMaker is made of, and it knows about none of
/// them.
///
/// <para>The evaluation is the one the PolyTools Bezier tool uses, so a curve
/// drawn here has the shape the same gestures draw there.</para>
///
/// <para>Open chains only. A closed loop is not this with the ends joined: it
/// has no first and last point to clamp against, its stations wrap, and it
/// needs an orientation before anything can be said about its inside. Giving it
/// its own entry point later is cheaper than letting it flow quietly through
/// code that clamps.</para>
/// </summary>
public static class BezierChain
{
    /// <summary>
    /// How far the flattened polyline may deviate from the true curve, in
    /// authoring pixels. A quarter of a 16-pixel water cell: fine enough that
    /// the flattening never decides a cell, coarse enough that a straight
    /// stretch stays one segment.
    /// </summary>
    public const double FlattenToleranceAuthoringPixels = 4.0;

    private const int MaximumSubdivisionDepth = 12;

    /// <summary>
    /// Flattens an open chain from its first point to its last, and measures
    /// where the authored points fall on the result. Consecutive duplicates are
    /// dropped, so every segment has a direction.
    ///
    /// <para>Two authored points can still end up sharing a station, when the
    /// stretch between them collapses to nothing. That is left as it is rather
    /// than tidied away: it is what a zero-length stretch means, and everything
    /// that interpolates over these stations already answers for it.</para>
    /// </summary>
    public static FlattenedChain FlattenOpen(IReadOnlyList<BezierChainPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A bezier chain needs at least two points to be flattened.");
        }

        var first = points[0];
        List<ChainPoint> polyline = [new ChainPoint(first.X, first.Y)];
        var anchorIndices = new int[points.Count];
        for (var index = 0; index + 1 < points.Count; index++)
        {
            anchorIndices[index] = polyline.Count - 1;
            var start = points[index];
            var end = points[index + 1];
            var p0 = new ChainPoint(start.X, start.Y);
            var p1 = new ChainPoint(p0.X + start.HandleOutX, p0.Y + start.HandleOutY);
            var p3 = new ChainPoint(end.X, end.Y);
            var p2 = new ChainPoint(p3.X + end.HandleInX, p3.Y + end.HandleInY);
            Subdivide(p0, p1, p2, p3, 0, polyline);
        }
        anchorIndices[^1] = polyline.Count - 1;
        if (polyline.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A bezier chain that collapses to a single point has no geometry.");
        }

        var stations = new double[polyline.Count];
        for (var index = 1; index < polyline.Count; index++)
            stations[index] = stations[index - 1] + Distance(polyline[index - 1], polyline[index]);

        var anchorStations = new double[points.Count];
        for (var index = 0; index < points.Count; index++)
            anchorStations[index] = stations[anchorIndices[index]];
        return new FlattenedChain(polyline, stations, anchorStations);
    }

    /// <summary>
    /// The flattened chain as segments, each carrying the station it starts at
    /// and the vertical extent a caller can band rows by.
    /// </summary>
    public static IReadOnlyList<ChainSegment> Segments(FlattenedChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var polyline = chain.Points;
        List<ChainSegment> segments = new(Math.Max(0, polyline.Count - 1));
        for (var index = 0; index + 1 < polyline.Count; index++)
        {
            var start = polyline[index];
            var end = polyline[index + 1];
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            segments.Add(new ChainSegment(
                start.X,
                start.Y,
                deltaX,
                deltaY,
                deltaX * deltaX + deltaY * deltaY,
                Math.Min(start.Y, end.Y),
                Math.Max(start.Y, end.Y),
                chain.Stations[index]));
        }
        return segments;
    }

    /// <summary>
    /// Flattens a closed chain: every authored point to the next, and then the
    /// last one back to the first.
    ///
    /// <para>The closing edge is an edge like any other. It runs from the last
    /// authored point to the first, and it is shaped by the last point's
    /// outgoing handle and the first point's incoming one - the two handles
    /// that have nowhere else to point on a ring.</para>
    ///
    /// <para>Two authored points are enough: two anchors with handles bulging
    /// opposite ways make a lens, which is a perfectly good loop. Whether what
    /// comes out is usable - three distinct points, an area, no self-contact -
    /// is a question about the flattened ring, and
    /// <see cref="ClosedChainGeometry.Validate"/> answers it. This throws only
    /// when there was never a chain to flatten.</para>
    /// </summary>
    public static FlattenedClosedChain FlattenClosed(IReadOnlyList<BezierChainPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A closed bezier chain needs at least two points to be flattened.");
        }

        var first = points[0];
        List<ChainPoint> polyline = [new ChainPoint(first.X, first.Y)];
        var anchorIndices = new int[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            anchorIndices[index] = polyline.Count - 1;
            var start = points[index];
            var end = points[(index + 1) % points.Count];
            var p0 = new ChainPoint(start.X, start.Y);
            var p1 = new ChainPoint(p0.X + start.HandleOutX, p0.Y + start.HandleOutY);
            var p3 = new ChainPoint(end.X, end.Y);
            var p2 = new ChainPoint(p3.X + end.HandleInX, p3.Y + end.HandleInY);
            Subdivide(p0, p1, p2, p3, 0, polyline);
        }

        // The closing edge ends where the ring began. Append only ever compares
        // against the point before it, so that repeat has to be dropped here -
        // the one place a closed chain does something an open one does not.
        if (polyline.Count > 1 && DistanceSquared(polyline[^1], polyline[0]) <= 1e-18)
            polyline.RemoveAt(polyline.Count - 1);

        var stations = new double[polyline.Count];
        for (var index = 1; index < polyline.Count; index++)
            stations[index] = stations[index - 1] + Distance(polyline[index - 1], polyline[index]);
        var totalLength = stations[^1] + Distance(polyline[^1], polyline[0]);

        var anchorStations = new double[points.Count];
        for (var index = 0; index < points.Count; index++)
            anchorStations[index] = stations[anchorIndices[index]];
        return new FlattenedClosedChain(polyline, stations, anchorStations, totalLength);
    }

    /// <summary>
    /// The ring as segments - one per point, the last of them the way back to
    /// the first.
    /// </summary>
    public static IReadOnlyList<ChainSegment> Segments(FlattenedClosedChain closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        var polyline = closed.Points;
        List<ChainSegment> segments = new(polyline.Count);
        for (var index = 0; index < polyline.Count; index++)
        {
            var start = polyline[index];
            var end = polyline[(index + 1) % polyline.Count];
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            segments.Add(new ChainSegment(
                start.X,
                start.Y,
                deltaX,
                deltaY,
                deltaX * deltaX + deltaY * deltaY,
                Math.Min(start.Y, end.Y),
                Math.Max(start.Y, end.Y),
                closed.Stations[index]));
        }
        return segments;
    }

    /// <summary>
    /// Brings a station onto the ring: the same place, expressed in
    /// <c>[0, totalLength)</c>.
    ///
    /// <para>This is how a station on a closed chain is normalised, and the only
    /// way it may be. An open chain clamps at its two ends because a river has a
    /// source and a mouth; a ring has neither, and running one through code that
    /// clamps would pin everything past the last authored point onto it instead
    /// of carrying on round. The total length never comes back - it is the same
    /// place as zero, and one answer for one place.</para>
    /// </summary>
    public static double WrapStation(double station, double totalLength)
    {
        if (totalLength <= 0.0) return 0.0;
        var wrapped = station % totalLength;
        if (wrapped < 0.0) wrapped += totalLength;
        // A tiny negative station lands exactly on the length once shifted.
        return wrapped >= totalLength ? 0.0 : wrapped;
    }

    private static void Subdivide(
        ChainPoint p0,
        ChainPoint p1,
        ChainPoint p2,
        ChainPoint p3,
        int depth,
        List<ChainPoint> output)
    {
        if (depth >= MaximumSubdivisionDepth || IsFlat(p0, p1, p2, p3))
        {
            Append(output, p3);
            return;
        }

        var p01 = Midpoint(p0, p1);
        var p12 = Midpoint(p1, p2);
        var p23 = Midpoint(p2, p3);
        var p012 = Midpoint(p01, p12);
        var p123 = Midpoint(p12, p23);
        var middle = Midpoint(p012, p123);
        Subdivide(p0, p01, p012, middle, depth + 1, output);
        Subdivide(middle, p123, p23, p3, depth + 1, output);
    }

    private static bool IsFlat(ChainPoint p0, ChainPoint p1, ChainPoint p2, ChainPoint p3)
    {
        var chordX = p3.X - p0.X;
        var chordY = p3.Y - p0.Y;
        var chordLengthSquared = chordX * chordX + chordY * chordY;
        var tolerance = FlattenToleranceAuthoringPixels;
        if (chordLengthSquared <= 1e-12)
        {
            // A loop back onto the same point has no chord to measure against,
            // so the handles themselves decide whether anything happens here.
            return DistanceSquared(p0, p1) <= tolerance * tolerance
                && DistanceSquared(p0, p2) <= tolerance * tolerance;
        }
        var first = Math.Abs((p1.X - p0.X) * chordY - (p1.Y - p0.Y) * chordX);
        var second = Math.Abs((p2.X - p0.X) * chordY - (p2.Y - p0.Y) * chordX);
        var deviation = first + second;
        return deviation * deviation <= tolerance * tolerance * chordLengthSquared;
    }

    private static void Append(List<ChainPoint> output, ChainPoint point)
    {
        var last = output[^1];
        if (DistanceSquared(last, point) <= 1e-18) return;
        output.Add(point);
    }

    private static ChainPoint Midpoint(ChainPoint a, ChainPoint b) =>
        new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private static double DistanceSquared(ChainPoint a, ChainPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Distance(ChainPoint a, ChainPoint b) =>
        Math.Sqrt(DistanceSquared(a, b));
}
