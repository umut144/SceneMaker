using System.Globalization;

namespace SceneMaker.Core;

/// <summary>One point of an open route while it is being drawn.</summary>
public readonly record struct RouteDraftPoint(
    int X,
    int Y,
    RoutePointMode Mode,
    decimal ElevationMeters,
    decimal WidthMeters,
    AuthoringPixelOffset? DraggedHandleOut = null);

/// <summary>
/// One of the five authoring grades a Path segment may use. The value belongs
/// to the segment arriving at a draft point; the first point's value is ignored.
/// </summary>
public enum RouteGradePreset
{
    DownFiftyPercent,
    DownTwentyFivePercent,
    Level,
    UpTwentyFivePercent,
    UpFiftyPercent,
}

/// <summary>
/// One point of a grade-authored Path draft. Only the Path's separate starting
/// elevation is absolute; every later height is derived from horizontal arc
/// length and <see cref="GradeFromPrevious"/>.
/// </summary>
public readonly record struct GradedRouteDraftPoint(
    int X,
    int Y,
    RoutePointMode Mode,
    decimal WidthMeters,
    RouteGradePreset GradeFromPrevious,
    AuthoringPixelOffset? DraggedHandleOut = null);

/// <summary>
/// Pure document operations for independently materialized route surfaces.
/// A route presents its own Terrain Asset and does not alter the Terrain cells
/// or elevation regions below it.
/// </summary>
public static class RouteSurfaceEditing
{
    /// <summary>
    /// A useful initial width for an Actor with a one-metre collision radius.
    /// It is an editor starting value, not a traversal guarantee: a later Actor
    /// profile may require more width, especially on a tight inner curve.
    /// </summary>
    public const decimal DefaultWidthMeters = 2.0m;

    public static SceneDocument Place(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        WorkspaceMetrics metrics,
        IReadOnlyList<RouteSurfacePointDocument> points,
        string assetKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);
        _ = terrainAssets.Resolve(assetKey);
        if (points.Count < 2)
            throw new SceneMakerDocumentException("A Path needs at least two points.");
        if (points.Any(static point => point.WidthMeters <= 0m))
            throw new SceneMakerDocumentException("A Path needs a positive width at every point.");
        if (!metrics.IsElevationAligned(points[0].ElevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"A Path's starting height must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var route = new RouteSurfaceDocument
        {
            RouteSurfaceId = NextRouteSurfaceId(scene),
            AssetKey = assetKey,
            Points = [.. points],
        };
        _ = RouteSurfaceGeometry.Prepare(metrics, route);
        return scene with
        {
            RouteSurfaces = scene.RouteSurfaces
                .Append(route)
                .OrderBy(static value => value.RouteSurfaceId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static SceneDocument Remove(SceneDocument scene, string routeSurfaceId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.RouteSurfaces
            .Where(route => !string.Equals(
                route.RouteSurfaceId,
                routeSurfaceId,
                StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.RouteSurfaces.Count
            ? scene
            : scene with { RouteSurfaces = remaining };
    }

    /// <summary>The last route whose horizontal band contains this position.</summary>
    public static RouteSurfaceDocument? FindAt(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        RouteSurfaceDocument? found = null;
        foreach (var route in scene.RouteSurfaces)
        {
            if (RouteSurfaceGeometry.Prepare(metrics, route).Corridor
                .NearestStation(authoringX, authoringY) is not null)
            {
                found = route;
            }
        }
        return found;
    }

    /// <summary>A stored point with mode-consistent handles.</summary>
    public static RouteSurfacePointDocument Point(
        int authoringX,
        int authoringY,
        RoutePointMode mode,
        decimal elevationMeters,
        decimal widthMeters = DefaultWidthMeters,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = authoringX, Y = authoringY },
        Mode = mode,
        HandleInAuthoringPx = mode == RoutePointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = mode == RoutePointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleOut ?? AuthoringPixelOffset.Zero,
        ElevationMeters = elevationMeters,
        WidthMeters = widthMeters,
    };

    /// <summary>
    /// Resolves clicked and dragged draft points into an open Bezier chain.
    /// Automatic endpoint and interior handles follow the same gesture rule as
    /// the river tool, while the route keeps its own document record.
    /// </summary>
    public static IReadOnlyList<RouteSurfacePointDocument> ResolveCurve(
        IReadOnlyList<RouteDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<RouteSurfacePointDocument> points = new(draft.Count);
        for (var index = 0; index < draft.Count; index++)
        {
            var current = draft[index];
            if (current.Mode == RoutePointMode.Linear)
            {
                points.Add(Point(
                    current.X,
                    current.Y,
                    RoutePointMode.Linear,
                    current.ElevationMeters,
                    current.WidthMeters));
                continue;
            }

            RouteDraftPoint? previous = index > 0 ? draft[index - 1] : null;
            RouteDraftPoint? next = index + 1 < draft.Count ? draft[index + 1] : null;
            var (handleIn, handleOut) = current.DraggedHandleOut is { } dragged
                ? Mirrored(Orient(dragged, current, previous))
                : Automatic(current, previous, next);
            points.Add(Point(
                current.X,
                current.Y,
                RoutePointMode.Aligned,
                current.ElevationMeters,
                current.WidthMeters,
                handleIn,
                handleOut));
        }
        return points;
    }

    /// <summary>
    /// Resolves a grade-authored draft and derives every absolute point height
    /// from the starting height and the preceding segment's horizontal Bezier
    /// arc length. Intermediate results stay unrounded; only stored anchors are
    /// rounded to six decimal places for deterministic JSON.
    /// </summary>
    public static IReadOnlyList<RouteSurfacePointDocument> ResolveGradedCurve(
        WorkspaceMetrics metrics,
        decimal startElevationMeters,
        IReadOnlyList<GradedRouteDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Any(static point => !Enum.IsDefined(point.GradeFromPrevious)))
            throw new SceneMakerDocumentException("A Path draft contains an unsupported grade.");
        if (!metrics.IsElevationAligned(startElevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"A Path's starting height must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var resolved = ResolveCurve(draft.Select(point => new RouteDraftPoint(
            point.X,
            point.Y,
            point.Mode,
            startElevationMeters,
            point.WidthMeters,
            point.DraggedHandleOut)).ToArray()).ToArray();
        if (resolved.Length < 2)
            return resolved;

        var centerline = RouteSurfaceGeometry.Flatten(resolved);
        var authoringPixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var elevation = (double)startElevationMeters;
        resolved[0] = resolved[0] with { ElevationMeters = startElevationMeters };
        for (var index = 1; index < resolved.Length; index++)
        {
            var runMeters = (centerline.AnchorStations[index]
                    - centerline.AnchorStations[index - 1])
                / authoringPixelsPerMeter;
            elevation += runMeters * GradeRatio(draft[index].GradeFromPrevious);
            resolved[index] = resolved[index] with
            {
                ElevationMeters = Math.Round(
                    (decimal)elevation,
                    6,
                    MidpointRounding.AwayFromZero),
            };
        }
        return resolved;
    }

    /// <summary>The signed rise per metre represented by one preset.</summary>
    public static double GradeRatio(RouteGradePreset grade) => grade switch
    {
        RouteGradePreset.DownFiftyPercent => -0.5,
        RouteGradePreset.DownTwentyFivePercent => -0.25,
        RouteGradePreset.Level => 0.0,
        RouteGradePreset.UpTwentyFivePercent => 0.25,
        RouteGradePreset.UpFiftyPercent => 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    /// <summary>
    /// Requires every route to name an enabled Asset whose SceneMaker role is
    /// Terrain. The catalog embodies that role filter; the Asset's cell/curve
    /// authoring style is deliberately irrelevant to a route.
    /// </summary>
    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var route in scene.RouteSurfaces)
            _ = terrainAssets.Resolve(route.AssetKey);
    }

    private static AuthoringPixelOffset Orient(
        AuthoringPixelOffset handleOut,
        RouteDraftPoint current,
        RouteDraftPoint? previous)
    {
        if (previous is not { } from) return handleOut;
        var travelX = current.X - from.X;
        var travelY = current.Y - from.Y;
        return handleOut.X * travelX + handleOut.Y * travelY < 0
            ? new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }
            : handleOut;
    }

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) Mirrored(
        AuthoringPixelOffset handleOut) =>
        (new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }, handleOut);

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) Automatic(
        RouteDraftPoint current,
        RouteDraftPoint? previous,
        RouteDraftPoint? next)
    {
        if (previous is not { } from)
        {
            return next is { } onlyNext
                ? (AuthoringPixelOffset.Zero, Third(current, onlyNext))
                : (AuthoringPixelOffset.Zero, AuthoringPixelOffset.Zero);
        }
        if (next is not { } to) return (Third(current, from), AuthoringPixelOffset.Zero);

        var tangentX = (double)(to.X - from.X);
        var tangentY = (double)(to.Y - from.Y);
        var tangentLength = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (tangentLength <= 0.0) return (Third(current, from), Third(current, to));

        var directionX = tangentX / tangentLength;
        var directionY = tangentY / tangentLength;
        var incoming = Distance(current, from) / 3.0;
        var outgoing = Distance(current, to) / 3.0;
        return (
            Offset(-directionX * incoming, -directionY * incoming),
            Offset(directionX * outgoing, directionY * outgoing));
    }

    private static AuthoringPixelOffset Third(RouteDraftPoint from, RouteDraftPoint to) =>
        Offset((to.X - from.X) / 3.0, (to.Y - from.Y) / 3.0);

    private static double Distance(RouteDraftPoint from, RouteDraftPoint to)
    {
        var deltaX = (double)(to.X - from.X);
        var deltaY = (double)(to.Y - from.Y);
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static AuthoringPixelOffset Offset(double x, double y) => new()
    {
        X = (int)Math.Round(x, MidpointRounding.AwayFromZero),
        Y = (int)Math.Round(y, MidpointRounding.AwayFromZero),
    };

    private static string NextRouteSurfaceId(SceneDocument scene)
    {
        const string Prefix = "route_";
        HashSet<int> used = [];
        foreach (var route in scene.RouteSurfaces)
        {
            if (!route.RouteSurfaceId.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    route.RouteSurfaceId.AsSpan(Prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                used.Add(index);
            }
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            if (used.Add(index)) return $"{Prefix}{index:0000}";
        }
        throw new SceneMakerDocumentException("Scene has exhausted stable route surface IDs.");
    }
}
