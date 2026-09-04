namespace SceneMaker.Core;

/// <summary>
/// One authored support point of an inclined route surface.
///
/// <para>The plan is a point of an open Bezier centerline. Width and elevation
/// belong to that same point and are interpolated over centerline arc length;
/// neither is sampled into Terrain cells.</para>
/// </summary>
public readonly record struct RouteSurfacePoint(
    BezierChainPoint Plan,
    decimal WidthMeters,
    decimal ElevationMeters);

/// <summary>
/// The signed vertical change between two neighbouring authored route points
/// and the horizontal arc length available for it.
///
/// <para><see cref="RisePerMeter"/> is geometry, not a verdict. An Actor or a
/// generator may compare it with its own capability, but that capability does
/// not become a property of the route.</para>
/// </summary>
public readonly record struct RouteGradeSegment(
    int StartPointIndex,
    int EndPointIndex,
    double RunMeters,
    double RiseMeters,
    double RisePerMeter)
{
    public double AbsoluteRisePerMeter => Math.Abs(RisePerMeter);
}

/// <summary>
/// A prepared route surface: an open, variable-width horizontal band and its
/// continuous elevation profile.
///
/// <para>The authored elevations remain on the points. Values between them are
/// doubles derived linearly over arc length and are deliberately not rounded
/// to the Workspace elevation quantum. The quantum constrains authored intent;
/// it is not a vertical voxel size and must not turn this inclined surface into
/// terraces.</para>
/// </summary>
public sealed record PreparedRouteSurface
{
    public required IReadOnlyList<RouteSurfacePoint> Points { get; init; }
    public required OpenChainCorridor Corridor { get; init; }
    public required IReadOnlyList<RouteGradeSegment> GradeSegments { get; init; }
    public required double TotalLengthMeters { get; init; }
    public required double MaximumAbsoluteRisePerMeter { get; init; }

    public FlattenedChain Centerline => Corridor.Centerline;

    /// <summary>
    /// The absolute surface height at one centerline station, clamped to the
    /// two ends and linear over arc length between authored points.
    /// </summary>
    public double ElevationAt(double station)
    {
        var anchorStations = Centerline.AnchorStations;
        if (station <= anchorStations[0]) return (double)Points[0].ElevationMeters;
        if (station >= anchorStations[^1]) return (double)Points[^1].ElevationMeters;

        for (var index = 0; index + 1 < Points.Count; index++)
        {
            var from = anchorStations[index];
            var to = anchorStations[index + 1];
            if (station > to) continue;
            var span = to - from;
            var fraction = span <= 0.0 ? 1.0 : (station - from) / span;
            var start = (double)Points[index].ElevationMeters;
            var end = (double)Points[index + 1].ElevationMeters;
            return start + (end - start) * fraction;
        }
        return (double)Points[^1].ElevationMeters;
    }
}

/// <summary>
/// Builds the derived geometry of a free ramp or another inclined route.
///
/// <para>This layer answers shape, height and grade only. It does not decide
/// whether an Actor can traverse the result, interpret the Asset carried by a
/// persisted route, or choose the points of a generated helix.</para>
/// </summary>
public static class RouteSurfaceGeometry
{
    /// <summary>Prepares the derived geometry of a persisted route surface.</summary>
    public static PreparedRouteSurface Prepare(
        WorkspaceMetrics metrics,
        RouteSurfaceDocument route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return Prepare(metrics, route.Points.Select(ToGeometryPoint).ToArray());
    }

    public static PreparedRouteSurface Prepare(
        WorkspaceMetrics metrics,
        IReadOnlyList<RouteSurfacePoint> points)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A route surface needs at least two points to have a centerline.");
        }
        if (points.Any(static point => point.WidthMeters <= 0m))
            throw new SceneMakerDocumentException("A route surface needs positive widths.");

        var authored = points.ToArray();
        var chain = BezierChain.FlattenOpen(authored.Select(static point => point.Plan).ToArray());
        var widths = authored.Select(static point => (double)point.WidthMeters).ToArray();
        var corridor = OpenChainCorridor.For(
            chain,
            widths,
            (double)metrics.AuthoringPixelsPerMeter);

        var authoringPixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var grades = new RouteGradeSegment[authored.Length - 1];
        var maximumAbsoluteRisePerMeter = 0.0;
        for (var index = 0; index + 1 < authored.Length; index++)
        {
            var runMeters = (chain.AnchorStations[index + 1] - chain.AnchorStations[index])
                / authoringPixelsPerMeter;
            var riseMeters = (double)(
                authored[index + 1].ElevationMeters - authored[index].ElevationMeters);
            var risePerMeter = runMeters > 0.0
                ? riseMeters / runMeters
                : riseMeters == 0.0
                    ? 0.0
                    : Math.CopySign(double.PositiveInfinity, riseMeters);
            grades[index] = new RouteGradeSegment(
                index,
                index + 1,
                runMeters,
                riseMeters,
                risePerMeter);
            maximumAbsoluteRisePerMeter = Math.Max(
                maximumAbsoluteRisePerMeter,
                Math.Abs(risePerMeter));
        }

        return new PreparedRouteSurface
        {
            Points = authored,
            Corridor = corridor,
            GradeSegments = grades,
            TotalLengthMeters = chain.Stations[^1] / authoringPixelsPerMeter,
            MaximumAbsoluteRisePerMeter = maximumAbsoluteRisePerMeter,
        };
    }

    /// <summary>
    /// Whether this authored interval has horizontal arc length. Equal endpoint
    /// positions may still return true when their handles make a loop; only a
    /// segment whose flattened geometry really collapses returns false.
    /// </summary>
    internal static bool HasPositiveRun(
        RouteSurfacePointDocument start,
        RouteSurfacePointDocument end)
    {
        try
        {
            _ = BezierChain.FlattenOpen(
                [ToGeometryPoint(start).Plan, ToGeometryPoint(end).Plan]);
            return true;
        }
        catch (SceneMakerDocumentException)
        {
            return false;
        }
    }

    private static RouteSurfacePoint ToGeometryPoint(RouteSurfacePointDocument point) => new(
        new BezierChainPoint(
            point.PositionAuthoringPx.X,
            point.PositionAuthoringPx.Y,
            point.HandleInAuthoringPx.X,
            point.HandleInAuthoringPx.Y,
            point.HandleOutAuthoringPx.X,
            point.HandleOutAuthoringPx.Y),
        point.WidthMeters,
        point.ElevationMeters);
}
