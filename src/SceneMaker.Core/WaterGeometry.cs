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
/// A river's flattened centerline together with where its authored points sit
/// on it.
///
/// <para>A <see cref="FlattenedChain"/> under a name that says what it is here.
/// The chain arithmetic is shared - a mountain outline and a route will want
/// the same flattening - but a centerline is a river's word for it, and the
/// callers that read this were written against that word.</para>
/// </summary>
public sealed record WaterCenterline(
    IReadOnlyList<ChainPoint> Points,
    IReadOnlyList<double> Stations,
    IReadOnlyList<double> AnchorStations)
{
    internal static WaterCenterline From(FlattenedChain chain) =>
        new(chain.Points, chain.Stations, chain.AnchorStations);
}

/// <summary>
/// The vertical section somewhere along a curve.
///
/// <para>Width is deliberately not part of it. The corridor resolves its width
/// per flattened segment, unrounded, and that is the binding rule; a second
/// width here would be a second answer to the same question, and the two
/// rounded differently.</para>
/// </summary>
public readonly record struct WaterProfileSample(
    decimal ElevationMeters,
    decimal ChannelDepthMeters,
    decimal ClearanceAboveMeters)
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
/// <para>A water cell belongs to a body when its centre lies no further than
/// half the interpolated width from its projection on the centerline. At the
/// two ends the corridor is
/// cut off square rather than rounded - the disc around the first segment is
/// clipped at the source, the disc around the last one at the mouth - so a
/// river does not begin and end with a half-circle.</para>
///
/// <para>The curve itself is flattened and measured by
/// <see cref="BezierChain"/>, and its horizontal band comes from
/// <see cref="OpenChainCorridor"/>. Both know nothing about water. What stays
/// here is the water-grid raster and the vertical section. The vertical values
/// are interpolated linearly over arc length, which is the one rule that cannot
/// make a river run uphill between two points that both fall.</para>
///
/// <para>Everything is derived. Nothing in this file is stored, which is why an
/// authored river stays reshapeable and why no consumer has to agree with a
/// rasterization it cannot see.</para>
/// </summary>
public static class WaterGeometry
{
    /// <summary>
    /// How far the flattened centerline may deviate from the true curve, in
    /// authoring pixels. The chain's tolerance, under the name rivers have
    /// always asked for it by.
    /// </summary>
    public const double FlattenToleranceAuthoringPixels =
        BezierChain.FlattenToleranceAuthoringPixels;

    /// <summary>
    /// Heights are rounded to the millimetre. Interpolation is done in
    /// doubles and the document counts in decimals; without a fixed rounding
    /// the same river would export as 1.5 one day and 1.4999999999 the next.
    /// </summary>
    public const int HeightDecimals = 3;

    /// <summary>
    /// The body's centerline as a polyline in authoring pixels, from source to
    /// mouth.
    /// </summary>
    public static IReadOnlyList<ChainPoint> Centerline(WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Centerline(body.Points);
    }

    /// <summary>
    /// The same, for a curve that is not a body yet - the one being drawn. The
    /// tool previews what it is about to author with the code that will author
    /// it, so the preview cannot promise a shape the export then disagrees with.
    /// </summary>
    public static IReadOnlyList<ChainPoint> Centerline(
        IReadOnlyList<WaterCurvePointDocument> points) => FlattenChain(points).Points;

    /// <summary>
    /// The centerline together with the arc length at which each authored point
    /// sits on it.
    /// </summary>
    public static WaterCenterline Flatten(IReadOnlyList<WaterCurvePointDocument> points) =>
        WaterCenterline.From(FlattenChain(points));

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

        var shape = CorridorShape(metrics, points);
        var step = metrics.AuthoringPixelsPerWaterCell;
        var sceneWidth = metrics.SceneWidthWaterCells(scene);
        var sceneHeight = metrics.SceneHeightWaterCells(scene);

        var firstX = Math.Max(
            0, WorkspaceMetrics.FloorDivide((int)Math.Floor(shape.MinX), step));
        var lastX = Math.Min(
            sceneWidth - 1, WorkspaceMetrics.FloorDivide((int)Math.Ceiling(shape.MaxX), step));
        var firstY = Math.Max(
            0, WorkspaceMetrics.FloorDivide((int)Math.Floor(shape.MinY), step));
        var lastY = Math.Min(
            sceneHeight - 1, WorkspaceMetrics.FloorDivide((int)Math.Ceiling(shape.MaxY), step));

        List<WaterCellSpan> cells = [];
        List<OpenChainCorridorSegment> row = [];
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
                if (centreY >= segment.MinY - segment.MaximumHalfWidth
                    && centreY <= segment.MaxY + segment.MaximumHalfWidth)
                {
                    row.Add(segment);
                }
            }
            if (row.Count == 0) continue;

            for (var x = firstX; x <= lastX; x++)
            {
                var centreX = x * step + half;
                if (shape.NearestStation(centreX, centreY, row) is not { } station) continue;
                var sample = SampleAt(points, shape.Centerline.AnchorStations, station);
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
        var shape = CorridorShape(metrics, body.Points);
        return shape.NearestStation(authoringX, authoringY) is not null;
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
                WorkspaceMetrics.FloorDivide(cell.X, perTerrainCell),
                WorkspaceMetrics.FloorDivide(cell.Y, perTerrainCell)));
        }
        return covered
            .OrderBy(static cell => cell.Y)
            .ThenBy(static cell => cell.X)
            .ToList();
    }

    /// <summary>
    /// A river's curve as a plain Bezier chain. The one place a water document
    /// becomes geometry, so that nothing below here has to know what a river
    /// is - and so that a mountain outline can arrive through its own converter
    /// rather than through this one.
    /// </summary>
    private static BezierChainPoint[] ToChain(IReadOnlyList<WaterCurvePointDocument> points)
    {
        var chain = new BezierChainPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            chain[index] = new BezierChainPoint(
                point.PositionAuthoringPx.X,
                point.PositionAuthoringPx.Y,
                point.HandleInAuthoringPx.X,
                point.HandleInAuthoringPx.Y,
                point.HandleOutAuthoringPx.X,
                point.HandleOutAuthoringPx.Y);
        }
        return chain;
    }

    /// <summary>
    /// Flattens a river's curve. The count is checked here rather than left to
    /// the chain, so that an author who has placed one point is told about a
    /// river rather than about a chain.
    /// </summary>
    private static FlattenedChain FlattenChain(IReadOnlyList<WaterCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A water curve needs at least two points to have a centerline.");
        }
        return BezierChain.FlattenOpen(ToChain(points));
    }

    private static WaterProfileSample Sample(WaterCurvePointDocument point) => new(
        point.ElevationMeters,
        point.ChannelDepthMeters,
        point.ClearanceAboveMeters);

    private static WaterProfileSample Mix(
        WaterCurvePointDocument from,
        WaterCurvePointDocument to,
        double fraction) => new(
        Between(from.ElevationMeters, to.ElevationMeters, fraction),
        Between(from.ChannelDepthMeters, to.ChannelDepthMeters, fraction),
        Between(from.ClearanceAboveMeters, to.ClearanceAboveMeters, fraction));

    private static decimal Between(decimal from, decimal to, double fraction)
    {
        if (from == to) return from;
        var value = (double)from + ((double)to - (double)from) * fraction;
        return decimal.Round((decimal)value, HeightDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Prepares the shared horizontal corridor from a river document. Widths
    /// stay in metres until after interpolation so the non-rounding boundary
    /// rule is unchanged.
    /// </summary>
    private static OpenChainCorridor CorridorShape(
        WorkspaceMetrics metrics,
        IReadOnlyList<WaterCurvePointDocument> points)
    {
        var centerline = FlattenChain(points);
        var widths = new double[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            widths[index] = (double)points[index].WidthMeters;
        }
        return OpenChainCorridor.For(
            centerline,
            widths,
            (double)metrics.AuthoringPixelsPerMeter);
    }
}
