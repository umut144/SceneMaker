using System.Globalization;

namespace SceneMaker.Core;

/// <summary>Whether a bridge may be authored as it was asked for.</summary>
public readonly record struct BridgeValidationResult(bool IsValid, string? Reason)
{
    public static BridgeValidationResult Valid { get; } = new(true, null);
}

/// <summary>
/// Pure document operations for straight level spans. A bridge is authored as
/// two ends, a width, one height and how the deck is planked; the quad it is
/// walked on is the same band a Path is, and its planks and its four posts are
/// derived from those numbers rather than stored, so nothing can drift away
/// from the bridge that produced it.
/// </summary>
public static class BridgeEditing
{
    /// <summary>
    /// A useful first width. Wide enough for the first target Actor to pass
    /// another, and no promise beyond that: traversal is the simulation's
    /// question, here as everywhere else.
    /// </summary>
    public const decimal DefaultWidthMeters = 4.0m;

    /// <summary>
    /// A useful first plank count and gap. Twelve planks over a short span
    /// read as a deck rather than as a fence, and a ten centimetre gap is
    /// wide enough to see through and narrow enough to walk over.
    /// </summary>
    public const int DefaultPlankCount = 12;

    public const decimal DefaultPlankGapMeters = 0.1m;

    public static SceneDocument Place(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string deckAssetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var candidate = Candidate(
            NextBridgeId(scene),
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY,
            deckAssetKey,
            anchorAssetKey,
            widthMeters,
            elevationMeters,
            plankCount,
            plankGapMeters);
        var validation = Validate(scene, propAssets, candidate);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        List<BridgeDocument> bridges = [.. scene.Bridges, candidate];
        bridges.Sort(static (left, right) =>
            string.CompareOrdinal(left.BridgeId, right.BridgeId));
        return scene with { Bridges = bridges };
    }

    /// <summary>
    /// Whether the bridge under construction could be authored, answered by
    /// the same call the commit makes so a draft that reads as ready cannot
    /// then be refused.
    /// </summary>
    public static BridgeValidationResult ValidateCandidate(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string deckAssetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        try
        {
            return Validate(
                scene,
                propAssets,
                Candidate(
                    NextBridgeId(scene),
                    startAnchorX,
                    startAnchorY,
                    endAnchorX,
                    endAnchorY,
                    deckAssetKey,
                    anchorAssetKey,
                    widthMeters,
                    elevationMeters,
                    plankCount,
                    plankGapMeters));
        }
        catch (OverflowException)
        {
            return new BridgeValidationResult(false, "Bridge coordinates exceed the supported range.");
        }
    }

    /// <summary>The whole bridge under an authoring position, or none.</summary>
    public static BridgeDocument? FindAt(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        double authoringX,
        double authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        // Last one wins, so the bridge drawn most recently is the one a click
        // over two stacked decks takes - the same answer the Canvas draws on
        // top.
        BridgeDocument? found = null;
        foreach (var bridge in scene.Bridges)
        {
            var corridor = BridgeGeometry.Deck(metrics, bridge).Corridor;
            if (corridor.NearestStation(authoringX, authoringY) is not null) found = bridge;
        }
        return found;
    }

    public static SceneDocument Remove(SceneDocument scene, string bridgeId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.Bridges
            .Where(bridge => !string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.Bridges.Count
            ? scene
            : scene with { Bridges = remaining };
    }

    /// <summary>
    /// Where this bridge's posts stand, as Placement boxes. Derived on every
    /// question rather than stored, which is what makes a post unable to be
    /// left behind by the bridge that set it.
    /// </summary>
    public static IReadOnlyList<PropBoundsAuthoringPixels> PostBounds(
        WorkspaceMetrics metrics,
        PropDisplayCatalog propAssets,
        BridgeDocument bridge)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(propAssets);
        ArgumentNullException.ThrowIfNull(bridge);
        var post = propAssets.Resolve(bridge.AnchorAssetKey);

        // A post Asset that carries no collision region occupies nothing, and
        // four of them occupy nothing four times over. The bridge still sets
        // its posts; they just take no space, which is the same answer a
        // Placement of that Asset gives anywhere else on the map.
        return
        [
            .. BridgeGeometry.Corners(metrics, bridge)
                .Select(corner => CornerAnchor(metrics, corner))
                .Select(anchor => PropEditing.CollisionBoundsFor(post, anchor.X, anchor.Y))
                .OfType<PropBoundsAuthoringPixels>(),
        ];
    }

    /// <summary>
    /// Where a corner sits, as the authoring-pixel anchor a Placement is set
    /// by. Rounded once, here, so the draft check and the export cannot land a
    /// post on two different pixels.
    /// </summary>
    public static (int X, int Y) CornerAnchor(WorkspaceMetrics metrics, BridgeCorner corner)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return (
            checked((int)Math.Round(
                corner.XMeters * metrics.AuthoringPixelsPerMeter,
                MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(
                corner.YMeters * metrics.AuthoringPixelsPerMeter,
                MidpointRounding.AwayFromZero)));
    }

    private static BridgeDocument Candidate(
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string deckAssetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters) => new()
    {
        BridgeId = bridgeId,
        DeckAssetKey = deckAssetKey,
        AnchorAssetKey = anchorAssetKey,
        StartAuthoringPx = new AuthoringPixelPosition { X = startAnchorX, Y = startAnchorY },
        EndAuthoringPx = new AuthoringPixelPosition { X = endAnchorX, Y = endAnchorY },
        WidthMeters = widthMeters,
        ElevationMeters = elevationMeters,
        PlankCount = plankCount,
        PlankGapMeters = plankGapMeters,
    };

    /// <summary>
    /// One answer for the draft and for the commit. A bridge is placeable when
    /// its own numbers are representable, its deck lies inside the Scene, and
    /// all four of its posts can stand where they would stand: a bridge is
    /// authored whole, so it is refused whole.
    /// </summary>
    private static BridgeValidationResult Validate(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        BridgeDocument bridge)
    {
        _ = propAssets.Resolve(bridge.DeckAssetKey);
        var metrics = propAssets.Metrics;
        if (bridge.WidthMeters <= 0m)
            return new BridgeValidationResult(false, "A bridge needs a positive width.");
        if (bridge.PlankCount <= 0)
            return new BridgeValidationResult(false, "A deck needs at least one plank.");
        if (bridge.PlankGapMeters < 0m)
            return new BridgeValidationResult(false, "A gap between planks cannot be negative.");
        if (!metrics.IsElevationAligned(bridge.ElevationMeters))
        {
            return new BridgeValidationResult(
                false,
                FormattableString.Invariant(
                    $"A bridge's height must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }
        if (bridge.StartAuthoringPx == bridge.EndAuthoringPx)
            return new BridgeValidationResult(false, "A bridge needs two different ends.");

        _ = propAssets.Resolve(bridge.AnchorAssetKey);

        // Depth is what the gaps leave over, so too many of them - or too wide
        // - is a deck of nothing. Said here rather than let the layout throw,
        // because the draft has to be able to explain itself while the author
        // is still turning the number.
        var depth = BridgeGeometry.PlankDepthMeters(
            BridgeGeometry.LengthMeters(metrics, bridge),
            bridge.PlankCount,
            bridge.PlankGapMeters);
        if (depth <= 0m)
        {
            return new BridgeValidationResult(
                false,
                "The gaps leave no room for a plank; use fewer planks or a smaller gap.");
        }

        var width = metrics.SceneWidthAuthoringPixels(scene);
        var height = metrics.SceneHeightAuthoringPixels(scene);
        foreach (var corner in BridgeGeometry.Corners(metrics, bridge))
        {
            var x = corner.XMeters * metrics.AuthoringPixelsPerMeter;
            var y = corner.YMeters * metrics.AuthoringPixelsPerMeter;
            if (x < 0m || y < 0m || x > width || y > height)
                return new BridgeValidationResult(false, "A bridge reaches outside the Scene bounds.");
        }

        // The posts are ordinary occupants of the map, so they answer the
        // ordinary question - against Placements and against the posts of
        // every other bridge, including the ones this bridge is setting.
        List<PropBoundsAuthoringPixels> occupied = [];
        foreach (var prop in scene.Props)
        {
            if (PropEditing.CollisionBoundsFor(
                    propAssets.Resolve(prop.AssetKey),
                    prop.PositionAuthoringPx.X,
                    prop.PositionAuthoringPx.Y) is { } bounds)
            {
                occupied.Add(bounds);
            }
        }
        foreach (var existing in scene.Bridges)
            occupied.AddRange(PostBounds(metrics, propAssets, existing));

        foreach (var postBounds in PostBounds(metrics, propAssets, bridge))
        {
            if (occupied.Any(postBounds.Overlaps))
            {
                return new BridgeValidationResult(
                    false,
                    $"A post of this bridge collides with something already there.");
            }
            occupied.Add(postBounds);
        }
        return BridgeValidationResult.Valid;
    }

    private static string NextBridgeId(SceneDocument scene)
    {
        const string Prefix = "bridge_";
        HashSet<int> used = [];
        foreach (var bridge in scene.Bridges)
        {
            if (!bridge.BridgeId.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    bridge.BridgeId.AsSpan(Prefix.Length),
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
        throw new SceneMakerDocumentException("Scene has exhausted stable bridge IDs.");
    }
}
