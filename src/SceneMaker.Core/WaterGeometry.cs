namespace SceneMaker.Core;

/// <summary>
/// A cell of the water grid. It is finer than a Terrain cell and nests a whole
/// number of times inside one, so it never straddles two of them.
/// </summary>
public readonly record struct WaterCellCoordinate(int X, int Y);

/// <summary>
/// A point on a flattened centerline, in authoring pixels. Sub-pixel on
/// purpose: the curve runs between authored points, and rounding it to whole
/// pixels first would put a visible stair into the corridor it produces.
/// </summary>
public readonly record struct CenterlinePoint(double X, double Y);

/// <summary>
/// Turns an authored water curve into the cells a simulation reads.
///
/// <para>This is the only place the corridor is defined, and it is defined
/// once: a water cell belongs to a body when its centre lies no further than
/// half the body's width from the centerline. At the two ends the corridor is
/// cut off square rather than rounded - the disc around the first segment is
/// clipped at the source, the disc around the last one at the mouth - so a
/// river does not begin and end with a half-circle.</para>
///
/// <para>The curve itself is evaluated exactly as the PolyTools Bezier tool
/// evaluates its chains - one cubic per pair of points, with the control points
/// taken from the handles - so a river drawn here has the shape the author drew
/// there.</para>
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
        IReadOnlyList<WaterCurvePointDocument> points)
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
        for (var index = 0; index + 1 < points.Count; index++)
        {
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
            Flatten(p0, p1, p2, p3, 0, polyline);
        }
        if (polyline.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A water curve that collapses to a single point has no centerline.");
        }
        return polyline;
    }

    /// <summary>
    /// The water cells this body covers, canonically ordered by Y then X. Cells
    /// outside the Scene are dropped rather than refused: a river is allowed to
    /// run off the edge of the map, it simply stops being authored there.
    /// </summary>
    public static IReadOnlyList<WaterCellCoordinate> Corridor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Corridor(scene, metrics, body.Points, body.WidthMeters);
    }

    /// <summary>The corridor of a curve that is not a body yet.</summary>
    public static IReadOnlyList<WaterCellCoordinate> Corridor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<WaterCurvePointDocument> points,
        decimal widthMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);

        var shape = CorridorShape.For(metrics, points, widthMeters);
        var step = metrics.AuthoringPixelsPerWaterCell;
        var sceneWidth = metrics.SceneWidthWaterCells(scene);
        var sceneHeight = metrics.SceneHeightWaterCells(scene);

        var firstX = Math.Max(0, FloorDivide((int)Math.Floor(shape.MinX), step));
        var lastX = Math.Min(sceneWidth - 1, FloorDivide((int)Math.Ceiling(shape.MaxX), step));
        var firstY = Math.Max(0, FloorDivide((int)Math.Floor(shape.MinY), step));
        var lastY = Math.Min(sceneHeight - 1, FloorDivide((int)Math.Ceiling(shape.MaxY), step));

        List<WaterCellCoordinate> cells = [];
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
                if (centreY >= segment.MinY - shape.HalfWidth
                    && centreY <= segment.MaxY + shape.HalfWidth)
                {
                    row.Add(segment);
                }
            }
            if (row.Count == 0) continue;

            for (var x = firstX; x <= lastX; x++)
            {
                var centreX = x * step + half;
                if (shape.Contains(centreX, centreY, row))
                    cells.Add(new WaterCellCoordinate(x, y));
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
        var shape = CorridorShape.For(metrics, body.Points, body.WidthMeters);
        return shape.Contains(authoringX, authoringY, shape.Segments);
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

    private static void Flatten(
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
        Flatten(p0, p01, p012, middle, depth + 1, output);
        Flatten(middle, p123, p23, p3, depth + 1, output);
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

    internal static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    /// <summary>
    /// One flattened piece of the centerline, with the end caps it carries.
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

        public double DistanceSquaredTo(double x, double y)
        {
            var toPointX = x - StartX;
            var toPointY = y - StartY;
            if (LengthSquared <= 0.0) return toPointX * toPointX + toPointY * toPointY;
            var t = (toPointX * DeltaX + toPointY * DeltaY) / LengthSquared;
            t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
            var closestX = toPointX - t * DeltaX;
            var closestY = toPointY - t * DeltaY;
            return closestX * closestX + closestY * closestY;
        }
    }

    /// <summary>
    /// The corridor of one body, prepared once so that testing a cell costs
    /// nothing but arithmetic.
    /// </summary>
    private sealed record CorridorShape
    {
        public required IReadOnlyList<CorridorSegment> Segments { get; init; }
        public required double HalfWidth { get; init; }
        public required double MinX { get; init; }
        public required double MaxX { get; init; }
        public required double MinY { get; init; }
        public required double MaxY { get; init; }

        public static CorridorShape For(
            WorkspaceMetrics metrics,
            IReadOnlyList<WaterCurvePointDocument> points,
            decimal widthMeters)
        {
            var polyline = Centerline(points);
            var halfWidth = (double)(widthMeters * metrics.AuthoringPixelsPerMeter) / 2.0;

            List<CorridorSegment> segments = new(polyline.Count - 1);
            var minX = double.MaxValue;
            var maxX = double.MinValue;
            var minY = double.MaxValue;
            var maxY = double.MinValue;
            for (var index = 0; index + 1 < polyline.Count; index++)
            {
                var start = polyline[index];
                var end = polyline[index + 1];
                var deltaX = end.X - start.X;
                var deltaY = end.Y - start.Y;
                segments.Add(new CorridorSegment(
                    start.X,
                    start.Y,
                    deltaX,
                    deltaY,
                    deltaX * deltaX + deltaY * deltaY,
                    Math.Min(start.Y, end.Y),
                    Math.Max(start.Y, end.Y),
                    CapsAtStart: index == 0,
                    CapsAtEnd: index + 2 == polyline.Count));
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
                HalfWidth = halfWidth,
                MinX = minX - halfWidth,
                MaxX = maxX + halfWidth,
                MinY = minY - halfWidth,
                MaxY = maxY + halfWidth,
            };
        }

        public bool Contains(double x, double y, IReadOnlyList<CorridorSegment> candidates)
        {
            var limit = HalfWidth * HalfWidth;
            foreach (var segment in candidates)
            {
                if (segment.DistanceSquaredTo(x, y) > limit) continue;
                // The caps trim the two outermost discs, so that the corridor
                // ends square. They are asked per segment: a cap that reached
                // beyond its own segment would cut the river wherever the curve
                // happens to pass behind it.
                if (segment.IsBeforeSource(x, y) || segment.IsBeyondMouth(x, y)) continue;
                return true;
            }
            return false;
        }
    }
}
