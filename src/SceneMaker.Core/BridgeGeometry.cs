namespace SceneMaker.Core;

/// <summary>
/// One derived corner of a bridge: where a post stands and which corner it is.
/// Left and right are relative to walking from the start end to the end end,
/// which is the only thing that makes those two words mean anything.
/// </summary>
public readonly record struct BridgeCorner(
    BridgeCornerKind Kind,
    decimal XMeters,
    decimal YMeters);

public enum BridgeCornerKind
{
    StartLeft,
    StartRight,
    EndLeft,
    EndRight,
}

/// <summary>
/// The geometry of a straight level span. Nothing here is stored: the corners
/// follow from the two ends and the width, so widening a bridge moves its
/// posts and deleting it takes them with it, without a second record to keep
/// in step.
///
/// <para>The deck itself is not built here. It is an open two-point chain like
/// any other, so it goes through <see cref="RouteSurfaceGeometry"/> - a bridge
/// keeps its own authored record, not its own idea of a band.</para>
/// </summary>
public static class BridgeGeometry
{
    /// <summary>
    /// Where the four posts stand, in scene-local metres. Ordered by
    /// <see cref="BridgeCornerKind"/> so the same bridge always answers the
    /// same way.
    /// </summary>
    public static IReadOnlyList<BridgeCorner> Corners(
        WorkspaceMetrics metrics,
        BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bridge);
        var pixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var startX = bridge.StartAuthoringPx.X / pixelsPerMeter;
        var startY = bridge.StartAuthoringPx.Y / pixelsPerMeter;
        var endX = bridge.EndAuthoringPx.X / pixelsPerMeter;
        var endY = bridge.EndAuthoringPx.Y / pixelsPerMeter;
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!double.IsFinite(length) || length <= 0.0)
        {
            throw new SceneMakerDocumentException(
                $"Bridge '{bridge.BridgeId}' has no length; its two ends are the same place.");
        }

        // SceneMaker authors y-up, so turning the direction a quarter turn
        // counter-clockwise is what "left" means here. In a y-down space the
        // same arithmetic would name the other side.
        var half = (double)bridge.WidthMeters / 2.0;
        var leftX = -deltaY / length * half;
        var leftY = deltaX / length * half;

        return
        [
            Corner(BridgeCornerKind.StartLeft, startX + leftX, startY + leftY),
            Corner(BridgeCornerKind.StartRight, startX - leftX, startY - leftY),
            Corner(BridgeCornerKind.EndLeft, endX + leftX, endY + leftY),
            Corner(BridgeCornerKind.EndRight, endX - leftX, endY - leftY),
        ];
    }

    /// <summary>
    /// The deck as the open chain every other band is built from: two linear
    /// points at one height and one width, which is exactly what a level
    /// two-point Path is.
    /// </summary>
    public static PreparedRouteSurface Deck(
        WorkspaceMetrics metrics,
        BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bridge);
        return RouteSurfaceGeometry.Prepare(metrics, DeckRoute(bridge));
    }

    /// <summary>
    /// The deck expressed as a route. It exists so the bridge can reuse the
    /// route bake and raster without those learning what a bridge is; it is
    /// never persisted, and its id is the bridge's own so derived data can be
    /// traced back to what produced it.
    /// </summary>
    public static RouteSurfaceDocument DeckRoute(BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        return new RouteSurfaceDocument
        {
            RouteSurfaceId = bridge.BridgeId,
            AssetKey = bridge.AssetKey,
            Points =
            [
                DeckPoint(bridge, bridge.StartAuthoringPx),
                DeckPoint(bridge, bridge.EndAuthoringPx),
            ],
            Segments =
            [
                new RouteSurfaceSegmentDocument
                {
                    SegmentId = bridge.BridgeId + ".deck",
                    GradePercent = 0,
                    Operation = RouteSegmentOperation.Additive,
                    ClearanceAboveMeters = null,
                },
            ],
        };
    }

    private static RouteSurfacePointDocument DeckPoint(
        BridgeDocument bridge,
        AuthoringPixelPosition position) => new()
    {
        PositionAuthoringPx = position,
        Mode = RoutePointMode.Linear,
        HandleInAuthoringPx = new AuthoringPixelOffset { X = 0, Y = 0 },
        HandleOutAuthoringPx = new AuthoringPixelOffset { X = 0, Y = 0 },
        ElevationMeters = bridge.ElevationMeters,
        WidthMeters = bridge.WidthMeters,
    };

    private static BridgeCorner Corner(BridgeCornerKind kind, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new SceneMakerDocumentException("A bridge corner is not a finite position.");
        return new BridgeCorner(
            kind,
            Math.Round((decimal)x, 6, MidpointRounding.AwayFromZero),
            Math.Round((decimal)y, 6, MidpointRounding.AwayFromZero));
    }
}
