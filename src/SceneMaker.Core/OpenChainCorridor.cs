namespace SceneMaker.Core;

/// <summary>
/// One flattened piece of an open centerline, with the width its corridor has
/// along it and the square end caps it carries.
///
/// <para>The caps belong to the two outermost segments rather than to the
/// corridor as a whole. A cap trims the disc around its own segment and cannot
/// cut another part of a chain that bends back across the same line.</para>
/// </summary>
public readonly record struct OpenChainCorridorSegment(
    ChainSegment Chain,
    double StartHalfWidth,
    double EndHalfWidth,
    bool CapsAtStart,
    bool CapsAtEnd)
{
    public double MinY => Chain.MinY;
    public double MaxY => Chain.MaxY;
    public double MaximumHalfWidth => Math.Max(StartHalfWidth, EndHalfWidth);

    /// <summary>Behind the start, on the far side of the line across it.</summary>
    public bool IsBeforeStart(double x, double y) =>
        CapsAtStart
        && (x - Chain.StartX) * Chain.DeltaX + (y - Chain.StartY) * Chain.DeltaY < 0.0;

    /// <summary>Past the end, on the far side of the line across it.</summary>
    public bool IsBeyondEnd(double x, double y) =>
        CapsAtEnd
        && (x - (Chain.StartX + Chain.DeltaX)) * Chain.DeltaX
            + (y - (Chain.StartY + Chain.DeltaY)) * Chain.DeltaY > 0.0;

    /// <summary>
    /// How far along this segment the nearest point to (x, y) lies, how far
    /// away it is, and how wide the corridor is there.
    ///
    /// <para>The half width is interpolated over the same fraction the
    /// projection produced, so the width and station cannot describe different
    /// places on the centerline.</para>
    /// </summary>
    public (double DistanceSquared, double Station, double HalfWidth) NearestTo(
        double x,
        double y)
    {
        var (distanceSquared, t, station) = Chain.ProjectTo(x, y);
        return (distanceSquared, station, StartHalfWidth + t * (EndHalfWidth - StartHalfWidth));
    }
}

/// <summary>
/// The horizontal footprint of a variable-width band along an open Bezier
/// chain, prepared once so that positions can be tested with arithmetic alone.
///
/// <para>The supplied widths correspond one-for-one with the authored anchors
/// represented by <see cref="FlattenedChain.AnchorStations"/>. They are
/// interpolated linearly over arc length and never rounded. The conversion to
/// authoring pixels happens only after interpolation, preserving the exact
/// boundary decision for values expressed in metres or another source unit.</para>
///
/// <para>This type has no vertical or material meaning. A river can rasterize
/// the footprint into water cells; a route can later mesh the same footprint
/// as an inclined surface without turning that surface into Terrain steps.</para>
/// </summary>
public sealed record OpenChainCorridor
{
    public required FlattenedChain Centerline { get; init; }
    public required IReadOnlyList<OpenChainCorridorSegment> Segments { get; init; }
    public required double MaximumHalfWidth { get; init; }
    public required double MinX { get; init; }
    public required double MaxX { get; init; }
    public required double MinY { get; init; }
    public required double MaxY { get; init; }

    /// <summary>
    /// Prepares a corridor from an already flattened centerline.
    ///
    /// <paramref name="widthUnitInAuthoringPixels"/> converts each full width
    /// into authoring pixels. Keeping that conversion separate means width is
    /// interpolated in its authored unit first and is not rounded or resampled.
    /// </summary>
    public static OpenChainCorridor For(
        FlattenedChain centerline,
        IReadOnlyList<double> anchorWidths,
        double widthUnitInAuthoringPixels)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        ArgumentNullException.ThrowIfNull(anchorWidths);
        if (anchorWidths.Count != centerline.AnchorStations.Count)
        {
            throw new ArgumentException(
                "A corridor needs one width for every authored chain point.",
                nameof(anchorWidths));
        }

        var polyline = centerline.Points;
        var maximumHalfWidth = anchorWidths.Max()
            * widthUnitInAuthoringPixels / 2.0;
        var chainSegments = BezierChain.Segments(centerline);
        List<OpenChainCorridorSegment> segments = new(chainSegments.Count);
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        for (var index = 0; index < chainSegments.Count; index++)
        {
            var startHalfWidth = WidthAt(
                anchorWidths,
                centerline.AnchorStations,
                centerline.Stations[index]) * widthUnitInAuthoringPixels / 2.0;
            var endHalfWidth = WidthAt(
                anchorWidths,
                centerline.AnchorStations,
                centerline.Stations[index + 1]) * widthUnitInAuthoringPixels / 2.0;
            segments.Add(new OpenChainCorridorSegment(
                chainSegments[index],
                startHalfWidth,
                endHalfWidth,
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

        return new OpenChainCorridor
        {
            Centerline = centerline,
            Segments = segments,
            MaximumHalfWidth = maximumHalfWidth,
            MinX = minX - maximumHalfWidth,
            MaxX = maxX + maximumHalfWidth,
            MinY = minY - maximumHalfWidth,
            MaxY = maxY + maximumHalfWidth,
        };
    }

    /// <summary>
    /// The station of the nearest point on the centerline, or null when the
    /// position is not in the corridor at all.
    ///
    /// <para>Nearest rather than first: on the inside of a bend two stretches
    /// can both reach a position, and the closer one determines which place on
    /// the authored profile that position belongs to.</para>
    /// </summary>
    public double? NearestStation(
        double x,
        double y,
        IReadOnlyList<OpenChainCorridorSegment> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var best = double.MaxValue;
        double? station = null;
        foreach (var segment in candidates)
        {
            if (segment.IsBeforeStart(x, y) || segment.IsBeyondEnd(x, y)) continue;
            var (distanceSquared, candidate, halfWidth) = segment.NearestTo(x, y);
            var limit = halfWidth * halfWidth;
            if (distanceSquared > limit || distanceSquared >= best) continue;
            best = distanceSquared;
            station = candidate;
        }
        return station;
    }

    /// <summary>The same query against every segment of the corridor.</summary>
    public double? NearestStation(double x, double y) => NearestStation(x, y, Segments);

    /// <summary>
    /// The corridor's full width at one station, linear between authored
    /// points and deliberately unrounded.
    /// </summary>
    private static double WidthAt(
        IReadOnlyList<double> widths,
        IReadOnlyList<double> anchorStations,
        double station)
    {
        if (station <= anchorStations[0]) return widths[0];
        if (station >= anchorStations[^1]) return widths[^1];

        for (var index = 0; index + 1 < widths.Count; index++)
        {
            var from = anchorStations[index];
            var to = anchorStations[index + 1];
            if (station > to) continue;
            var span = to - from;
            var fraction = span <= 0.0 ? 1.0 : (station - from) / span;
            return widths[index] + (widths[index + 1] - widths[index]) * fraction;
        }
        return widths[^1];
    }
}
