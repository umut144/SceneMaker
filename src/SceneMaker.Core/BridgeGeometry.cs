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
/// One plank of a deck, as the box it occupies rather than as a mesh. Its
/// centre is in scene-local metres; <c>DepthMeters</c> runs along the span and
/// <c>WidthMeters</c> across it, which is the whole deck width - a plank
/// always reaches both edges.
/// </summary>
public readonly record struct BridgePlank(
    int Index,
    decimal CenterXMeters,
    decimal CenterYMeters,
    decimal DepthMeters,
    decimal WidthMeters);

/// <summary>
/// How a deck is laid out along its span: the run it has to fill, the depth
/// each plank came out at, the heading they all share, and the planks.
///
/// <para>Depth is derived and count is authored, so lengthening a bridge
/// thickens its planks rather than adding one. That is the trade an author
/// asked for: a deck that always ends flush, at the cost of a plank whose size
/// follows the span.</para>
/// </summary>
public sealed record BridgePlankLayout(
    decimal LengthMeters,
    decimal PlankDepthMeters,
    decimal HeadingDegrees,
    IReadOnlyList<BridgePlank> Planks);

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
    ///
    /// <para>The Asset it carries is the deck's Placement rather than a
    /// Terrain. Nothing along this path resolves it - the bake reads geometry
    /// only - and it travels so the quad can still be traced back to what it
    /// is made of.</para>
    /// </summary>
    public static RouteSurfaceDocument DeckRoute(BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        return new RouteSurfaceDocument
        {
            RouteSurfaceId = bridge.BridgeId,
            AssetKey = bridge.DeckAssetKey,
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

    /// <summary>The span, end to end, in metres.</summary>
    public static decimal LengthMeters(WorkspaceMetrics metrics, BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bridge);
        var pixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var deltaX = (bridge.EndAuthoringPx.X - bridge.StartAuthoringPx.X) / pixelsPerMeter;
        var deltaY = (bridge.EndAuthoringPx.Y - bridge.StartAuthoringPx.Y) / pixelsPerMeter;
        return Round(Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
    }

    /// <summary>
    /// What one plank measures along the span once the gaps are taken out.
    /// Arithmetic only: it answers with the number it gets, including zero or
    /// less, so the caller can say why that does not lay out rather than
    /// having an exception decide for it.
    /// </summary>
    public static decimal PlankDepthMeters(
        decimal lengthMeters,
        int plankCount,
        decimal plankGapMeters)
    {
        if (plankCount <= 0) return 0m;
        // Rounded here rather than at each caller, so the number the draft
        // refuses on, the number the Canvas draws and the number the export
        // ships are one number. A plank that rounds away to nothing is not a
        // plank, and the zero says so.
        return Math.Round(
            (lengthMeters - ((plankCount - 1) * plankGapMeters)) / plankCount,
            6,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The deck as the row of planks it is. SceneMaker lays them out rather
    /// than shipping the parameters alone, so that what an author sees on the
    /// Canvas and what a consumer builds are the same row, decided once.
    /// </summary>
    public static BridgePlankLayout Planks(WorkspaceMetrics metrics, BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bridge);
        var pixelsPerMeter = (double)metrics.AuthoringPixelsPerMeter;
        var startX = bridge.StartAuthoringPx.X / pixelsPerMeter;
        var startY = bridge.StartAuthoringPx.Y / pixelsPerMeter;
        var deltaX = (bridge.EndAuthoringPx.X / pixelsPerMeter) - startX;
        var deltaY = (bridge.EndAuthoringPx.Y / pixelsPerMeter) - startY;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!double.IsFinite(length) || length <= 0.0)
        {
            throw new SceneMakerDocumentException(
                $"Bridge '{bridge.BridgeId}' has no length; its two ends are the same place.");
        }

        var depth = PlankDepthMeters(Round(length), bridge.PlankCount, bridge.PlankGapMeters);
        if (depth <= 0m)
        {
            throw new SceneMakerDocumentException(
                $"Bridge '{bridge.BridgeId}' leaves no room for a plank between its gaps.");
        }

        var unitX = deltaX / length;
        var unitY = deltaY / length;
        var step = (double)(depth + bridge.PlankGapMeters);
        var half = (double)depth / 2.0;

        List<BridgePlank> planks = new(bridge.PlankCount);
        for (var index = 0; index < bridge.PlankCount; index++)
        {
            var along = (index * step) + half;
            planks.Add(new BridgePlank(
                index,
                Round(startX + (unitX * along)),
                Round(startY + (unitY * along)),
                depth,
                bridge.WidthMeters));
        }

        return new BridgePlankLayout(
            Round(length),
            depth,
            HeadingDegrees(unitX, unitY),
            planks);
    }

    /// <summary>
    /// The same layout, answered as "none" rather than thrown when the numbers
    /// do not lay out. The Canvas asks this way: a document that cannot be
    /// planked is refused where documents are refused, not by an exception in
    /// the middle of drawing a frame.
    /// </summary>
    public static BridgePlankLayout? TryPlanks(WorkspaceMetrics metrics, BridgeDocument bridge)
    {
        try
        {
            return Planks(metrics, bridge);
        }
        catch (SceneMakerDocumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Which way the span points, counter-clockwise from +X in the same y-up
    /// metre space the corners are given in. A plank is a rectangle, so one
    /// angle is all a consumer needs to stand the row up at any bridge angle -
    /// and arbitrary angles are the point.
    /// </summary>
    private static decimal HeadingDegrees(double unitX, double unitY)
    {
        var degrees = Math.Atan2(unitY, unitX) * 180.0 / Math.PI;
        if (degrees < 0.0) degrees += 360.0;
        return Round(degrees);
    }

    private static decimal Round(double value)
    {
        if (!double.IsFinite(value))
            throw new SceneMakerDocumentException("A bridge measurement is not a finite number.");
        return Math.Round((decimal)value, 6, MidpointRounding.AwayFromZero);
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
