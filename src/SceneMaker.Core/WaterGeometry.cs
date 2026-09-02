namespace SceneMaker.Core;

/// <summary>
/// One cell of the water grid and what the body occupies there, vertically.
///
/// <para>Two spans, sharing a floor: the water fills
/// <c>[Bed, Surface]</c>, and <c>[Bed, CutTop]</c> is removed from the Terrain
/// so that the water has somewhere to be and something to travel through. An
/// open river, a cut channel and a tunnel are the same two spans against
/// different ground.</para>
/// </summary>
public readonly record struct WaterCellSpan(
    int X,
    int Y,
    decimal BedMeters,
    decimal SurfaceMeters,
    decimal CutTopMeters);

/// <summary>
/// A point on a flattened centerline, in authoring pixels. Sub-pixel on
/// purpose: the curve runs between authored points, and rounding it to whole
/// pixels first would put a visible stair into the corridor it produces.
/// </summary>
public readonly record struct CenterlinePoint(double X, double Y);

/// <summary>
/// A flattened centerline together with where the authored points sit on it,
/// measured in authoring pixels travelled from the source.
///
/// <para>That measure - arc length - is what the vertical values are
/// interpolated over. The obvious alternative, the curve's own parameter, runs
/// unevenly: long handles make the middle of a segment pass quicker, so the
/// river's gradient would depend on how the author shaped its bend. Arc length
/// is what they see.</para>
/// </summary>
public sealed record WaterCenterline(
    IReadOnlyList<CenterlinePoint> Points,
    IReadOnlyList<double> AnchorStations);

/// <summary>The vertical section and width somewhere along a curve.</summary>
public readonly record struct WaterProfileSample(
    decimal ElevationMeters,
    decimal ChannelDepthMeters,
    decimal ClearanceAboveMeters,
    decimal WidthMeters)
{
    /// <summary>The floor of the channel: the water's bottom and the cut's.</summary>
    public decimal BedMeters => ElevationMeters - ChannelDepthMeters;

    /// <summary>How high the Terrain is carved away above the bed.</summary>
    public decimal CutTopMeters => ElevationMeters + ClearanceAboveMeters;
}

/// <summary>
/// Turns an authored water curve into the cells a simulation reads, and into
/// what the body occupies vertically in each of them.
///
/// <para>This is the only place the corridor is defined, and it is defined
/// once: a water cell belongs to a body when its centre lies no further than
/// half the interpolated width from its projection on the centerline. At the
/// two ends the corridor is
/// cut off square rather than rounded - the disc around the first segment is
/// clipped at the source, the disc around the last one at the mouth - so a
/// river does not begin and end with a half-circle.</para>
///
/// <para>The curve itself is evaluated exactly as the PolyTools Bezier tool
/// evaluates its chains, so a river drawn here has the shape the author drew
/// there. The vertical values are interpolated linearly over arc length, which
/// is the one rule that cannot make a river run uphill between two points that
/// both fall.</para>
///
/// <para>Everything is derived. Nothing in this file is stored, which is why an
/// authored river stays reshapeable and why no consumer has to agree with a
/// rasterization it cannot see.</para>
/// </summary>
public static class WaterGeometry
{
    /// <summary>
    /// How far the flattened polyline may deviate from the true curve, in
    /// authoring pixels. A quarter of a 16-pixel water cell: fine enough that
    /// the flattening never decides a cell, coarse enough that a straight
    /// stretch stays one segment.
    /// </summary>
    public const double FlattenToleranceAuthoringPixels = 4.0;

    /// <summary>
    /// Heights are rounded to the millimetre. Interpolation is done in
    /// doubles and the document counts in decimals; without a fixed rounding
    /// the same river would export as 1.5 one day and 1.4999999999 the next.
    /// </summary>
    public const int HeightDecimals = 3;

    private const int MaximumSubdivisionDepth = 12;

    /// <summary>
    /// The body's centerline as a polyline in authoring pixels, from source to
    /// mouth. Consecutive duplicates are dropped, so every segment has a
    /// direction.
    /// </summary>
    public static IReadOnlyList<CenterlinePoint> Centerline(WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Centerline(body.Points);
    }

    /// <summary>
    /// The same, for a curve that is not a body yet - the one being drawn. The
    /// tool previews what it is about to author with the code that will author
    /// it, so the preview cannot promise a shape the export then disagrees with.
    /// </summary>
    public static IReadOnlyList<CenterlinePoint> Centerline(
        IReadOnlyList<WaterCurvePointDocument> points) => Flatten(points).Points;

    /// <summary>
    /// The centerline together with the arc length at which each authored point
    /// sits on it.
    /// </summary>
    public static WaterCenterline Flatten(IReadOnlyList<WaterCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A water curve needs at least two points to have a centerline.");
        }

        var first = points[0];
        List<CenterlinePoint> polyline =
            [new CenterlinePoint(first.PositionAuthoringPx.X, first.PositionAuthoringPx.Y)];
        var anchorIndices = new int[points.Count];
        for (var index = 0; index + 1 < points.Count; index++)
        {
            anchorIndices[index] = polyline.Count - 1;
            var start = points[index];
            var end = points[index + 1];
            var p0 = new CenterlinePoint(start.PositionAuthoringPx.X, start.PositionAuthoringPx.Y);
            var p1 = new CenterlinePoint(
                p0.X + start.HandleOutAuthoringPx.X,
                p0.Y + start.HandleOutAuthoringPx.Y);
            var p3 = new CenterlinePoint(end.PositionAuthoringPx.X, end.PositionAuthoringPx.Y);
            var p2 = new CenterlinePoint(
                p3.X + end.HandleInAuthoringPx.X,
                p3.Y + end.HandleInAuthoringPx.Y);
            Subdivide(p0, p1, p2, p3, 0, polyline);
        }
        anchorIndices[^1] = polyline.Count - 1;
        if (polyline.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A water curve that collapses to a single point has no centerline.");
        }

        var stations = new double[polyline.Count];
        for (var index = 1; index < polyline.Count; index++)
            stations[index] = stations[index - 1] + Distance(polyline[index - 1], polyline[index]);

        var anchorStations = new double[points.Count];
        for (var index = 0; index < points.Count; index++)
            anchorStations[index] = stations[anchorIndices[index]];
        return new WaterCenterline(polyline, anchorStations);
    }

    /// <summary>
    /// The three values at one station, linear between the two authored points
    /// it falls between and clamped to the outermost ones beyond them.
    ///
    /// <para>Linear rather than smooth on purpose. A spline through the heights
    /// would look softer at the authored points and can overshoot - and an
    /// overshoot in a water surface is a stretch of river running uphill
    /// between two points that both fall. Nothing about a curve's plan should
    /// be able to do that to its section.</para>
    /// </summary>
    public static WaterProfileSample SampleAt(
        IReadOnlyList<WaterCurvePointDocument> points,
        IReadOnlyList<double> anchorStations,
        double station)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(anchorStations);
        if (points.Count == 0)
            throw new SceneMakerDocumentException("A water curve has no points to sample.");

        if (station <= anchorStations[0]) return Sample(points[0]);
        if (station >= anchorStations[^1]) return Sample(points[^1]);

        for (var index = 0; index + 1 < points.Count; index++)
        {
            var from = anchorStations[index];
            var to = anchorStations[index + 1];
            if (station > to) continue;
            var span = to - from;
            // Two anchors at the same station cannot be interpolated between;
            // the later one wins, which is what a zero-length stretch means.
            var fraction = span <= 0.0 ? 1.0 : (station - from) / span;
            return Mix(points[index], points[index + 1], fraction);
        }
        return Sample(points[^1]);
    }

    /// <summary>
    /// The water cells this body covers and what it occupies in each of them,
    /// canonically ordered by Y then X. Cells outside the Scene are dropped
    /// rather than refused: a river is allowed to run off the edge of the map,
    /// it simply stops being authored there.
    /// </summary>
    public static IReadOnlyList<WaterCellSpan> Corridor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Corridor(scene, metrics, body.Points);
    }

    /// <summary>The corridor of a curve that is not a body yet.</summary>
    public static IReadOnlyList<WaterCellSpan> Corridor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<WaterCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);

        var shape = CorridorShape.For(metrics, points);
        var step = metrics.AuthoringPixelsPerWaterCell;
        var sceneWidth = metrics.SceneWidthWaterCells(scene);
        var sceneHeight = metrics.SceneHeightWaterCells(scene);

        var firstX = Math.Max(0, FloorDivide((int)Math.Floor(shape.MinX), step));
        var lastX = Math.Min(sceneWidth - 1, FloorDivide((int)Math.Ceiling(shape.MaxX), step));
        var firstY = Math.Max(0, FloorDivide((int)Math.Floor(shape.MinY), step));
        var lastY = Math.Min(sceneHeight - 1, FloorDivide((int)Math.Ceiling(shape.MaxY), step));

        List<WaterCellSpan> cells = [];
        List<CorridorSegment> row = [];
        var half = step / 2.0;
        for (var y = firstY; y <= lastY; y++)
        {
            // The polyline is long and a row of cells only ever meets a little
            // of it, so each row keeps the segments it can possibly touch and
            // the cells in it test against those instead of all of them.
            var centreY = y * step + half;
            row.Clear();
            foreach (var segment in shape.Segments)
            {
                if (centreY >= segment.MinY - shape.MaximumHalfWidth
                    && centreY <= segment.MaxY + shape.MaximumHalfWidth)
                {
                    row.Add(segment);
                }
            }
            if (row.Count == 0) continue;

            for (var x = firstX; x <= lastX; x++)
            {
                var centreX = x * step + half;
                if (shape.NearestStation(centreX, centreY, row) is not { } station) continue;
                var sample = SampleAt(points, shape.AnchorStations, station);
                cells.Add(new WaterCellSpan(
                    x, y, sample.BedMeters, sample.ElevationMeters, sample.CutTopMeters));
            }
        }
        return cells;
    }

    /// <summary>
    /// Whether an authoring-pixel position lies in the body's corridor. The
    /// same predicate the raster is built from, so what the pointer picks and
    /// what the Scene exports can never disagree.
    /// </summary>
    public static bool Contains(
        WorkspaceMetrics metrics,
        WaterBodyDocument body,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(body);
        var shape = CorridorShape.For(metrics, body.Points);
        return shape.NearestStation(authoringX, authoringY, shape.Segments) is not null;
    }

    /// <summary>The Terrain cells a body's corridor lies over.</summary>
    public static IReadOnlyList<TerrainCellCoordinate> CoveredTerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        WaterBodyDocument body)
    {
        var perTerrainCell = metrics.WaterCellsPerTerrainCell;
        HashSet<TerrainCellCoordinate> covered = [];
        foreach (var cell in Corridor(scene, metrics, body))
        {
            covered.Add(new TerrainCellCoordinate(
                FloorDivide(cell.X, perTerrainCell),
                FloorDivide(cell.Y, perTerrainCell)));
        }
        return covered
            .OrderBy(static cell => cell.Y)
            .ThenBy(static cell => cell.X)
            .ToList();
    }

    private static WaterProfileSample Sample(WaterCurvePointDocument point) => new(
        point.ElevationMeters,
        point.ChannelDepthMeters,
        point.ClearanceAboveMeters,
        point.WidthMeters);

    private static WaterProfileSample Mix(
        WaterCurvePointDocument from,
        WaterCurvePointDocument to,
        double fraction) => new(
        Between(from.ElevationMeters, to.ElevationMeters, fraction),
        Between(from.ChannelDepthMeters, to.ChannelDepthMeters, fraction),
        Between(from.ClearanceAboveMeters, to.ClearanceAboveMeters, fraction),
        Between(from.WidthMeters, to.WidthMeters, fraction));

    private static decimal Between(decimal from, decimal to, double fraction)
    {
        if (from == to) return from;
        var value = (double)from + ((double)to - (double)from) * fraction;
        return decimal.Round((decimal)value, HeightDecimals, MidpointRounding.AwayFromZero);
    }

    private static void Subdivide(
        CenterlinePoint p0,
        CenterlinePoint p1,
        CenterlinePoint p2,
        CenterlinePoint p3,
        int depth,
        List<CenterlinePoint> output)
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

    private static bool IsFlat(
        CenterlinePoint p0,
        CenterlinePoint p1,
        CenterlinePoint p2,
        CenterlinePoint p3)
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

    private static void Append(List<CenterlinePoint> output, CenterlinePoint point)
    {
        var last = output[^1];
        if (DistanceSquared(last, point) <= 1e-18) return;
        output.Add(point);
    }

    private static CenterlinePoint Midpoint(CenterlinePoint a, CenterlinePoint b) =>
        new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private static double DistanceSquared(CenterlinePoint a, CenterlinePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Distance(CenterlinePoint a, CenterlinePoint b) =>
        Math.Sqrt(DistanceSquared(a, b));

    internal static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    /// <summary>
    /// One flattened piece of the centerline, with the end caps it carries and
    /// where it starts along the curve.
    ///
    /// <para>The caps belong to the two outermost segments rather than to the
    /// corridor as a whole. Clipping the whole corridor by the plane at the
    /// mouth was wrong in a way that only showed on a river that bends back
    /// near its own end: the plane reaches across the map and cuts away the
    /// body it passes over, and the author sees a river that stops halfway.
    /// A cap trims the disc around its own segment and nothing else.</para>
    /// </summary>
    private readonly record struct CorridorSegment(
        double StartX,
        double StartY,
        double DeltaX,
        double DeltaY,
        double LengthSquared,
        double MinY,
        double MaxY,
        double StartStation,
        bool CapsAtStart,
        bool CapsAtEnd)
    {
        /// <summary>Behind the source, on the far side of the line across it.</summary>
        public bool IsBeforeSource(double x, double y) =>
            CapsAtStart && (x - StartX) * DeltaX + (y - StartY) * DeltaY < 0.0;

        /// <summary>Past the mouth, on the far side of the line across it.</summary>
        public bool IsBeyondMouth(double x, double y) =>
            CapsAtEnd
            && (x - (StartX + DeltaX)) * DeltaX + (y - (StartY + DeltaY)) * DeltaY > 0.0;

        /// <summary>
        /// How far along this segment the nearest point to (x, y) lies, and how
        /// far away it is. The fraction is what turns a position into a station
        /// on the curve, and with it into a water level.
        /// </summary>
        public (double DistanceSquared, double Station) NearestTo(double x, double y)
        {
            var toPointX = x - StartX;
            var toPointY = y - StartY;
            if (LengthSquared <= 0.0)
                return (toPointX * toPointX + toPointY * toPointY, StartStation);
            var t = (toPointX * DeltaX + toPointY * DeltaY) / LengthSquared;
            t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
            var closestX = toPointX - t * DeltaX;
            var closestY = toPointY - t * DeltaY;
            return (
                closestX * closestX + closestY * closestY,
                StartStation + t * Math.Sqrt(LengthSquared));
        }
    }

    /// <summary>
    /// The corridor of one body, prepared once so that testing a cell costs
    /// nothing but arithmetic.
    /// </summary>
    private sealed record CorridorShape
    {
        public required IReadOnlyList<CorridorSegment> Segments { get; init; }
        public required IReadOnlyList<WaterCurvePointDocument> Points { get; init; }
        public required IReadOnlyList<double> AnchorStations { get; init; }
        public required double AuthoringPixelsPerMeter { get; init; }
        public required double MaximumHalfWidth { get; init; }
        public required double MinX { get; init; }
        public required double MaxX { get; init; }
        public required double MinY { get; init; }
        public required double MaxY { get; init; }

        public static CorridorShape For(
            WorkspaceMetrics metrics,
            IReadOnlyList<WaterCurvePointDocument> points)
        {
            var centerline = Flatten(points);
            var polyline = centerline.Points;
            var authoringPixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
            var maximumHalfWidth = points.Max(static point => (double)point.WidthMeters)
                * authoringPixelsPerMeter / 2.0;

            List<CorridorSegment> segments = new(polyline.Count - 1);
            var minX = double.MaxValue;
            var maxX = double.MinValue;
            var minY = double.MaxValue;
            var maxY = double.MinValue;
            var station = 0.0;
            for (var index = 0; index + 1 < polyline.Count; index++)
            {
                var start = polyline[index];
                var end = polyline[index + 1];
                var deltaX = end.X - start.X;
                var deltaY = end.Y - start.Y;
                var lengthSquared = deltaX * deltaX + deltaY * deltaY;
                segments.Add(new CorridorSegment(
                    start.X,
                    start.Y,
                    deltaX,
                    deltaY,
                    lengthSquared,
                    Math.Min(start.Y, end.Y),
                    Math.Max(start.Y, end.Y),
                    station,
                    CapsAtStart: index == 0,
                    CapsAtEnd: index + 2 == polyline.Count));
                station += Math.Sqrt(lengthSquared);
            }
            foreach (var point in polyline)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            return new CorridorShape
            {
                Segments = segments,
                Points = points,
                AnchorStations = centerline.AnchorStations,
                AuthoringPixelsPerMeter = authoringPixelsPerMeter,
                MaximumHalfWidth = maximumHalfWidth,
                MinX = minX - maximumHalfWidth,
                MaxX = maxX + maximumHalfWidth,
                MinY = minY - maximumHalfWidth,
                MaxY = maxY + maximumHalfWidth,
            };
        }

        /// <summary>
        /// The station of the nearest point on the corridor's centerline, or
        /// null when the position is not in the corridor at all.
        ///
        /// <para>Nearest rather than first: which stretch of river a cell
        /// belongs to decides its water level, so on the inside of a bend, where
        /// two stretches both reach it, the closer one has to win.</para>
        /// </summary>
        public double? NearestStation(
            double x,
            double y,
            IReadOnlyList<CorridorSegment> candidates)
        {
            var best = double.MaxValue;
            double? station = null;
            foreach (var segment in candidates)
            {
                // The caps trim the two outermost discs, so that the corridor
                // ends square. They are asked per segment: a cap that reached
                // beyond its own segment would cut the river wherever the curve
                // happens to pass behind it.
                if (segment.IsBeforeSource(x, y) || segment.IsBeyondMouth(x, y)) continue;
                var (distanceSquared, candidate) = segment.NearestTo(x, y);
                var width = SampleAt(Points, AnchorStations, candidate).WidthMeters;
                var halfWidth = (double)width * AuthoringPixelsPerMeter / 2.0;
                var limit = halfWidth * halfWidth;
                if (distanceSquared > limit || distanceSquared >= best) continue;
                best = distanceSquared;
                station = candidate;
            }
            return station;
        }
    }
}
