using System.Globalization;

namespace SceneMaker.Core;

/// <summary>
/// One point of a curve while it is being drawn: where it sits, how its handles
/// are meant to behave, and the handle the author actually pulled out of it, if
/// any. A point placed with a plain click has none, which is what lets
/// <see cref="WaterEditing.ResolveCurve"/> tell "no handle wanted" apart from
/// "a handle of zero length".
/// </summary>
/// <summary>
/// A fork the document states and the geometry no longer supports, together
/// with the body that states it. Which body it is matters: the same list read
/// as a colour has to paint one river red, not every river in the Scene.
/// </summary>
public sealed record BrokenJunction(
    string WaterBodyId,
    string PartnerWaterBodyId,
    WaterEnd End,
    string Message);

public readonly record struct WaterDraftPoint(
    int X,
    int Y,
    WaterPointMode Mode,
    decimal ElevationMeters,
    decimal ChannelDepthMeters,
    decimal ClearanceAboveMeters,
    decimal WidthMeters,
    AuthoringPixelOffset? DraggedHandleOut = null);

/// <summary>
/// Water editing over the canonically ordered body list. Like the other editing
/// operations these are pure <c>SceneDocument -&gt; SceneDocument</c> functions
/// that assume a canonical document and produce one; validation runs at the IO
/// boundaries.
///
/// <para>What is authored is the curve. No operation here writes a cell, and
/// none reads one: the raster is derived by <see cref="WaterGeometry"/>
/// whenever somebody needs it, which is what keeps a river reshapeable after it
/// has been drawn.</para>
/// </summary>
public static class WaterEditing
{
    /// <summary>
    /// Adds one authored river. The points arrive from the tool already snapped
    /// to the water grid; that they are is checked at the IO boundary, so a
    /// hand-edited document cannot smuggle an off-grid curve past it.
    /// </summary>
    public static SceneDocument PlaceRiver(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        IReadOnlyList<WaterCurvePointDocument> points,
        string assetKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(points);
        _ = RequireDrawable(terrainAssets, assetKey);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A river needs at least two points; the first is its source and the last its mouth.");
        }
        if (points.Any(static point => point.WidthMeters <= 0m))
            throw new SceneMakerDocumentException("A river needs a positive width at every point.");

        var body = new WaterBodyDocument
        {
            WaterBodyId = NextWaterBodyId(scene, WaterKind.River),
            WaterKind = WaterKind.River,
            AssetKey = assetKey,
            // A drawn river is always there and meets nothing. Both are changed
            // by their own operations rather than by drawing, so that the tool
            // that authors geometry authors only geometry.
            Switch = null,
            Junctions = [],
            Points = [.. points],
        };
        return scene with
        {
            WaterBodies = scene.WaterBodies
                .Append(body)
                .OrderBy(static value => value.WaterBodyId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>
    /// Replaces a body's whole point list, keeping its id and everything else.
    ///
    /// <para>One operation rather than move / insert / delete / retype, because
    /// from the document's side those are all the same thing: a curve is its
    /// points, and what a tool did to arrive at a new list is the tool's
    /// business. The id does not change, so the entry is replaced where it
    /// stands and the canonical order is untouched.</para>
    /// </summary>
    public static SceneDocument Reshape(
        SceneDocument scene,
        string waterBodyId,
        IReadOnlyList<WaterCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(waterBodyId);
        ArgumentNullException.ThrowIfNull(points);
        var body = Require(scene, waterBodyId);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A river needs at least two points; the first is its source and the last its mouth.");
        }
        if (points.Any(static point => point.WidthMeters <= 0m))
            throw new SceneMakerDocumentException("A river needs a positive width at every point.");
        return Replace(scene, body with { Points = [.. points] });
    }

    /// <summary>
    /// Puts a body on a switch, or takes it off every switch with null. When
    /// the switch flips is not decided here and never will be; this only
    /// records which switch decides whether the body exists.
    /// </summary>
    public static SceneDocument SetSwitch(
        SceneDocument scene,
        string waterBodyId,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var body = Require(scene, waterBodyId);
        if (name is not null
            && !scene.Switches.Any(candidate =>
                string.Equals(candidate.Switch, name, StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException($"The Scene declares no switch '{name}'.");
        }

        return Replace(scene, body with { Switch = name });
    }

    /// <summary>
    /// Records which bodies this one's ends sit on, in the canonical order the
    /// document keeps them in. Only the partner is stated here; where exactly
    /// the two meet is worked out from the curves whenever anybody asks.
    /// </summary>
    public static SceneDocument SetJunctions(
        SceneDocument scene,
        string waterBodyId,
        IReadOnlyList<WaterJunctionDocument> junctions)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(junctions);
        var body = Require(scene, waterBodyId);
        var ordered = junctions
            .DistinctBy(static claim => (claim.End, claim.WaterBodyId))
            .OrderBy(static claim => claim.End)
            .ThenBy(static claim => claim.WaterBodyId, StringComparer.Ordinal)
            .ToList();
        foreach (var claim in ordered)
        {
            if (string.Equals(claim.WaterBodyId, waterBodyId, StringComparison.Ordinal))
                throw new SceneMakerDocumentException($"Water body '{waterBodyId}' cannot meet itself.");
            _ = Require(scene, claim.WaterBodyId);
        }
        return Replace(scene, body with { Junctions = ordered });
    }

    /// <summary>
    /// Inserts an authored point into a body where a position falls on it.
    ///
    /// <para>The segment is split by de Casteljau, so the curve is the curve it
    /// was: a point put into a bend leaves the bend where it was, and the two
    /// neighbours give up exactly the part of their handles the split takes.
    /// Zeroing the new point's handles instead would straighten whatever the
    /// author had drawn, which is the opposite of what inserting a point is
    /// for.</para>
    ///
    /// <para>The section and the width come from the curve at that station, not
    /// from a tool's defaults, so inserting changes nothing about the river
    /// except that there is now somewhere to take hold of it. What does move is
    /// the position: a curve point belongs on the water grid, so it lands at the
    /// nearest grid position to the split and carries the curve with it, by at
    /// most half a cell.</para>
    /// </summary>
    public static SceneDocument InsertPoint(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string waterBodyId,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        var body = Require(scene, waterBodyId);

        if (WaterGeometry.NearestCenterlineAnchor(
                scene, metrics, authoringX, authoringY, metrics.AuthoringPixelsPerWaterCell * 2.0)
            is not { } anchor
            || !string.Equals(anchor.WaterBodyId, waterBodyId, StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Water body '{waterBodyId}' does not run past that position.");
        }

        var centerline = WaterGeometry.Flatten(body.Points);
        var anchors = centerline.AnchorStations;
        var station = (double)anchor.StationMeters * (double)metrics.AuthoringPixelsPerMeter;

        // Which authored stretch it falls in, and how far along that stretch -
        // by arc length, which is what every value along a river interpolates
        // over, and what the author sees.
        var index = 0;
        while (index + 2 < anchors.Count && station >= anchors[index + 1]) index++;
        var span = anchors[index + 1] - anchors[index];
        if (span <= 0.0)
        {
            throw new SceneMakerDocumentException(
                $"Water body '{waterBodyId}' has no length there to put a point into.");
        }
        var t = Math.Clamp((station - anchors[index]) / span, 0.0, 1.0);
        if (t <= 0.0 || t >= 1.0)
        {
            throw new SceneMakerDocumentException(
                $"Water body '{waterBodyId}' already has a point there.");
        }

        var sample = WaterGeometry.SampleAt(body.Points, anchors, station);
        var points = body.Points.ToList();
        var inserted = new WaterCurvePointDocument
        {
            PositionAuthoringPx = anchor.PositionAuthoringPx,
            Mode = WaterPointMode.Linear,
            HandleInAuthoringPx = AuthoringPixelOffset.Zero,
            HandleOutAuthoringPx = AuthoringPixelOffset.Zero,
            ElevationMeters = sample.ElevationMeters,
            ChannelDepthMeters = sample.ChannelDepthMeters,
            ClearanceAboveMeters = sample.ClearanceAboveMeters,
            WidthMeters = WaterGeometry.WidthAt(body.Points, anchors, station),
        };

        // A stretch between two Linear points is a straight line, and splitting
        // a line at a point on it gives two lines - exactly, with no handles and
        // no tolerance. Running de Casteljau over it would be correct too, but
        // it would hand the author two Aligned points and a pair of handles
        // where they had drawn none.
        var straight = points[index].Mode == WaterPointMode.Linear
            && points[index + 1].Mode == WaterPointMode.Linear;
        if (!straight)
        {
            var split = BezierChain.SplitSegment(
                ToChainPoint(points[index]), ToChainPoint(points[index + 1]), t);
            points[index] = WithHandles(
                points[index],
                points[index].HandleInAuthoringPx,
                SplitHandle(split.From.HandleOutX, split.From.HandleOutY));
            points[index + 1] = WithHandles(
                points[index + 1],
                SplitHandle(split.To.HandleInX, split.To.HandleInY),
                points[index + 1].HandleOutAuthoringPx);
            inserted = WithHandles(
                inserted,
                SplitHandle(split.Inserted.HandleInX, split.Inserted.HandleInY),
                SplitHandle(split.Inserted.HandleOutX, split.Inserted.HandleOutY));
        }

        points.Insert(index + 1, inserted);
        return Reshape(scene, waterBodyId, points);
    }

    /// <summary>A split handle, rounded: a handle is stored in whole authoring pixels.</summary>
    private static AuthoringPixelOffset SplitHandle(double x, double y) => new()
    {
        X = (int)Math.Round(x, MidpointRounding.AwayFromZero),
        Y = (int)Math.Round(y, MidpointRounding.AwayFromZero),
    };

    private static BezierChainPoint ToChainPoint(WaterCurvePointDocument point) => new(
        point.PositionAuthoringPx.X,
        point.PositionAuthoringPx.Y,
        point.HandleInAuthoringPx.X,
        point.HandleInAuthoringPx.Y,
        point.HandleOutAuthoringPx.X,
        point.HandleOutAuthoringPx.Y);

    /// <summary>
    /// A point with new handles, and <c>Aligned</c> so they are kept: a
    /// <c>Linear</c> point is exactly the absence of handles, so a split that
    /// gave one a handle would have it dropped again at the IO boundary.
    /// </summary>
    private static WaterCurvePointDocument WithHandles(
        WaterCurvePointDocument point,
        AuthoringPixelOffset handleIn,
        AuthoringPixelOffset handleOut) =>
        handleIn.IsZero() && handleOut.IsZero()
            ? point with
            {
                Mode = WaterPointMode.Linear,
                HandleInAuthoringPx = handleIn,
                HandleOutAuthoringPx = handleOut,
            }
            : point with
            {
                Mode = WaterPointMode.Aligned,
                HandleInAuthoringPx = handleIn,
                HandleOutAuthoringPx = handleOut,
            };

    /// <summary>The body under this id, or a refusal naming it.</summary>
    public static WaterBodyDocument Require(SceneDocument scene, string waterBodyId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.WaterBodies.FirstOrDefault(body =>
            string.Equals(body.WaterBodyId, waterBodyId, StringComparison.Ordinal))
            ?? throw new SceneMakerDocumentException($"Water body '{waterBodyId}' does not exist.");
    }

    /// <summary>
    /// The curve point of this body within reach of a position, nearest first,
    /// or null. A point is grabbed by its position and not by its corridor, so
    /// two points close together are told apart by which one is nearer.
    /// </summary>
    public static int? FindPointAt(
        WaterBodyDocument body,
        int authoringX,
        int authoringY,
        double hitRadiusAuthoringPixels)
    {
        ArgumentNullException.ThrowIfNull(body);
        int? nearest = null;
        var best = hitRadiusAuthoringPixels * hitRadiusAuthoringPixels;
        for (var index = 0; index < body.Points.Count; index++)
        {
            var position = body.Points[index].PositionAuthoringPx;
            double dx = position.X - authoringX;
            double dy = position.Y - authoringY;
            var distance = dx * dx + dy * dy;
            if (distance > best) continue;
            best = distance;
            nearest = index;
        }
        return nearest;
    }

    /// <summary>
    /// Every junction a Scene states that its curves no longer support, as
    /// sentences for the author.
    ///
    /// <para>Moving a point is allowed to break a fork - the editor validates
    /// at its IO boundaries like everything else here, so a document may be
    /// invalid between two edits. What must not happen is that the author
    /// learns about it from an export hours later, so a tool asks this after
    /// every change and says the answer out loud.</para>
    /// </summary>
    public static IReadOnlyList<BrokenJunction> BrokenJunctions(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        List<BrokenJunction> broken = [];
        foreach (var body in scene.WaterBodies)
        {
            foreach (var claim in body.Junctions)
            {
                var partner = scene.WaterBodies.FirstOrDefault(candidate =>
                    string.Equals(candidate.WaterBodyId, claim.WaterBodyId, StringComparison.Ordinal));
                var end = claim.End.ToString().ToLowerInvariant();
                if (partner is null)
                {
                    broken.Add(new BrokenJunction(
                        body.WaterBodyId,
                        claim.WaterBodyId,
                        claim.End,
                        $"'{body.WaterBodyId}' meets '{claim.WaterBodyId}' at its {end}, which the Scene no longer has."));
                    continue;
                }
                if (WaterGeometry.SurfaceAtJunction(metrics, body, claim.End, partner) is not { } surface)
                {
                    broken.Add(new BrokenJunction(
                        body.WaterBodyId,
                        partner.WaterBodyId,
                        claim.End,
                        $"'{body.WaterBodyId}' no longer touches '{partner.WaterBodyId}' at its {end}."));
                    continue;
                }
                var own = claim.End == WaterEnd.Source
                    ? body.Points[0].ElevationMeters
                    : body.Points[^1].ElevationMeters;
                if (Math.Abs(own - surface) > metrics.ElevationQuantumMeters)
                {
                    broken.Add(new BrokenJunction(
                        body.WaterBodyId,
                        partner.WaterBodyId,
                        claim.End,
                        FormattableString.Invariant(
                            $"'{body.WaterBodyId}' meets '{partner.WaterBodyId}' at {own:0.###} m while its surface there is {surface:0.###} m.")));
                }
            }
        }
        return broken;
    }

    private static SceneDocument Replace(SceneDocument scene, WaterBodyDocument body) => scene with
    {
        WaterBodies = scene.WaterBodies
            .Select(candidate => string.Equals(
                candidate.WaterBodyId, body.WaterBodyId, StringComparison.Ordinal)
                    ? body
                    : candidate)
            .ToList(),
    };

    public static SceneDocument Remove(SceneDocument scene, string waterBodyId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.WaterBodies
            .Where(body => !string.Equals(body.WaterBodyId, waterBodyId, StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.WaterBodies.Count
            ? scene
            : scene with { WaterBodies = remaining };
    }

    /// <summary>
    /// The body whose corridor covers this authoring-pixel position, or null.
    /// Where two overlap the last one wins, so what is picked is what is drawn
    /// on top.
    /// </summary>
    public static WaterBodyDocument? FindAt(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        WaterBodyDocument? found = null;
        foreach (var body in scene.WaterBodies)
        {
            if (WaterGeometry.Contains(metrics, body, authoringX, authoringY)) found = body;
        }
        return found;
    }

    /// <summary>Runs at the IO boundary, so it validates the whole document.</summary>
    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var body in scene.WaterBodies)
            _ = RequireDrawable(terrainAssets, body.AssetKey);
    }

    /// <summary>
    /// A body of water is made of an Asset that is authored as a curve. The
    /// other way round - a river made of grass - was possible and produced a
    /// green river nobody asked for.
    /// </summary>
    private static TerrainDisplayAsset RequireDrawable(
        TerrainDisplayCatalog terrainAssets,
        string assetKey)
    {
        var asset = terrainAssets.Resolve(assetKey);
        if (asset.Authoring != TerrainAuthoring.Curve)
        {
            throw new SceneMakerDocumentException(
                $"Terrain Asset '{assetKey}' is authored as cells and cannot be drawn as a water body.");
        }
        return asset;
    }

    /// <summary>
    /// A curve point ready to be stored. <c>Linear</c> drops whatever handles
    /// it was given rather than storing a mode and a shape that disagree.
    /// </summary>
    public static WaterCurvePointDocument Point(
        int authoringX,
        int authoringY,
        WaterPointMode mode,
        decimal elevationMeters = 0m,
        decimal channelDepthMeters = DefaultChannelDepthMeters,
        decimal clearanceAboveMeters = DefaultClearanceAboveMeters,
        decimal widthMeters = DefaultWidthMeters,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = authoringX, Y = authoringY },
        Mode = mode,
        HandleInAuthoringPx = mode == WaterPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = mode == WaterPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleOut ?? AuthoringPixelOffset.Zero,
        ElevationMeters = elevationMeters,
        ChannelDepthMeters = channelDepthMeters,
        ClearanceAboveMeters = clearanceAboveMeters,
        WidthMeters = widthMeters,
    };

    /// <summary>A useful first width without imposing a world's closed list.</summary>
    public const decimal DefaultWidthMeters = 4.0m;

    /// <summary>A river deep enough to be water, as a starting value.</summary>
    public const decimal DefaultChannelDepthMeters = 0.5m;

    /// <summary>
    /// Headroom a river asks for by default. Generous on purpose: where the
    /// ground never reaches it the river is simply open, and it only starts to
    /// matter where a hill does.
    /// </summary>
    public const decimal DefaultClearanceAboveMeters = 5.0m;

    /// <summary>
    /// Turns a drawn curve into stored points. Two things happen here, both of
    /// them copied from the PolyTools Bezier tool so that a curve drawn in
    /// SceneMaker behaves the way the same gestures behave there.
    ///
    /// <para>A handle the author pulled out is kept, but oriented along the
    /// direction the curve is travelling: dragging backwards past the previous
    /// point would otherwise put a cusp where the author drew a bend. Its
    /// mirror becomes the incoming handle, so the curve passes through the
    /// point without a kink.</para>
    ///
    /// <para>An aligned point the author only clicked has no handle of its own,
    /// and gets one from its neighbours: a tangent along the line between them,
    /// a third of the distance to each. That is what makes clicking through a
    /// river produce a curve rather than a polygon. A linear point keeps none of
    /// this - it is exactly the absence of handles.</para>
    /// </summary>
    public static IReadOnlyList<WaterCurvePointDocument> ResolveCurve(
        IReadOnlyList<WaterDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<WaterCurvePointDocument> points = new(draft.Count);
        for (var index = 0; index < draft.Count; index++)
        {
            var current = draft[index];
            if (current.Mode == WaterPointMode.Linear)
            {
                points.Add(Vertical(current, Point(current.X, current.Y, WaterPointMode.Linear)));
                continue;
            }

            WaterDraftPoint? previous = index > 0 ? draft[index - 1] : null;
            WaterDraftPoint? next = index + 1 < draft.Count ? draft[index + 1] : null;
            var (handleIn, handleOut) = current.DraggedHandleOut is { } dragged
                ? Mirrored(Orient(dragged, current, previous))
                : Automatic(current, previous, next);
            points.Add(Vertical(current, Point(
                current.X,
                current.Y,
                WaterPointMode.Aligned,
                handleIn: handleIn,
                handleOut: handleOut)));
        }
        return points;
    }

    /// <summary>
    /// Carries a draft point's vertical values onto the stored point. They
    /// travel untouched: what the author set for the source is what the source
    /// gets, and everything between two points is interpolated later rather
    /// than baked in here.
    /// </summary>
    private static WaterCurvePointDocument Vertical(
        WaterDraftPoint draft,
        WaterCurvePointDocument point) => point with
    {
        ElevationMeters = draft.ElevationMeters,
        ChannelDepthMeters = draft.ChannelDepthMeters,
        ClearanceAboveMeters = draft.ClearanceAboveMeters,
        WidthMeters = draft.WidthMeters,
    };

    /// <summary>
    /// The drawn handle, flipped if it points back the way the curve came from.
    /// </summary>
    private static AuthoringPixelOffset Orient(
        AuthoringPixelOffset handleOut,
        WaterDraftPoint current,
        WaterDraftPoint? previous)
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
        WaterDraftPoint current,
        WaterDraftPoint? previous,
        WaterDraftPoint? next)
    {
        // An end of the curve has one neighbour and therefore one handle: a
        // third of the way towards it, which is the cubic that draws a straight
        // line and bends only once the other end says so.
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

    private static AuthoringPixelOffset Third(WaterDraftPoint from, WaterDraftPoint to) =>
        Offset((to.X - from.X) / 3.0, (to.Y - from.Y) / 3.0);

    private static double Distance(WaterDraftPoint from, WaterDraftPoint to)
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

    private static string NextWaterBodyId(SceneDocument scene, WaterKind kind)
    {
        var prefix = KindToken(kind) + "_";
        HashSet<int> used = [];
        foreach (var body in scene.WaterBodies)
        {
            if (!body.WaterBodyId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    body.WaterBodyId.AsSpan(prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                used.Add(index);
            }
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            if (used.Add(index)) return $"{prefix}{index:0000}";
        }
        throw new SceneMakerDocumentException(
            $"Scene has exhausted stable water body IDs for '{prefix}'.");
    }

    private static string KindToken(WaterKind kind) => kind switch
    {
        WaterKind.River => "river",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
