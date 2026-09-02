using System.Globalization;

namespace SceneMaker.Core;

/// <summary>
/// One point of a curve while it is being drawn: where it sits, how its handles
/// are meant to behave, and the handle the author actually pulled out of it, if
/// any. A point placed with a plain click has none, which is what lets
/// <see cref="WaterEditing.ResolveCurve"/> tell "no handle wanted" apart from
/// "a handle of zero length".
/// </summary>
public readonly record struct WaterDraftPoint(
    int X,
    int Y,
    WaterPointMode Mode,
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
        string assetKey,
        decimal widthMeters,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(points);
        _ = terrainAssets.Resolve(assetKey);
        if (points.Count < 2)
        {
            throw new SceneMakerDocumentException(
                "A river needs at least two points; the first is its source and the last its mouth.");
        }
        if (widthMeters <= 0m)
            throw new SceneMakerDocumentException("A river needs a positive width in metres.");

        var body = new WaterBodyDocument
        {
            WaterBodyId = NextWaterBodyId(scene, WaterKind.River),
            WaterKind = WaterKind.River,
            AssetKey = assetKey,
            WidthMeters = widthMeters,
            ElevationMeters = elevationMeters,
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
            _ = terrainAssets.Resolve(body.AssetKey);
    }

    /// <summary>
    /// A curve point ready to be stored. <c>Linear</c> drops whatever handles
    /// it was given rather than storing a mode and a shape that disagree.
    /// </summary>
    public static WaterCurvePointDocument Point(
        int authoringX,
        int authoringY,
        WaterPointMode mode,
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
    };

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
                points.Add(Point(current.X, current.Y, WaterPointMode.Linear));
                continue;
            }

            WaterDraftPoint? previous = index > 0 ? draft[index - 1] : null;
            WaterDraftPoint? next = index + 1 < draft.Count ? draft[index + 1] : null;
            var (handleIn, handleOut) = current.DraggedHandleOut is { } dragged
                ? Mirrored(Orient(dragged, current, previous))
                : Automatic(current, previous, next);
            points.Add(Point(current.X, current.Y, WaterPointMode.Aligned, handleIn, handleOut));
        }
        return points;
    }

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
