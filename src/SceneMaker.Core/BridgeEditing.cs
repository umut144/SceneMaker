using System.Globalization;

namespace SceneMaker.Core;

/// <summary>
/// Which of a span's two ends. They are told apart by the order they were
/// authored in, which is also what gives left and right a meaning.
/// </summary>
public enum BridgeEnd
{
    Start,
    End,
}

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
        string plankAssetKey,
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
            plankAssetKey,
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
        string plankAssetKey,
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
                    plankAssetKey,
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

    /// <summary>
    /// Moves an existing bridge's ends and numbers, keeping its id and the two
    /// Assets it is built from. Reshaping rather than replacing is what lets
    /// an author drag an end without the bridge becoming a different one - and
    /// the posts follow, because they were never stored.
    /// </summary>
    public static SceneDocument Reshape(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var candidate = Reshaped(
            scene,
            bridgeId,
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY,
            widthMeters,
            elevationMeters,
            plankCount,
            plankGapMeters);
        var validation = Validate(scene, propAssets, candidate);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        // Ordered by id already, and the id has not changed, so the entry is
        // replaced where it stands rather than sorted again.
        var bridges = scene.Bridges
            .Select(bridge => string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal)
                ? candidate
                : bridge)
            .ToList();
        return scene with { Bridges = bridges };
    }

    /// <summary>
    /// Whether that reshape would be taken, answered by the same call the
    /// commit makes so a drag that looks Ready cannot then be refused.
    /// </summary>
    public static BridgeValidationResult ValidateReshape(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
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
                Reshaped(
                    scene,
                    bridgeId,
                    startAnchorX,
                    startAnchorY,
                    endAnchorX,
                    endAnchorY,
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

    /// <summary>The bridge with this id, or an exception naming it.</summary>
    public static BridgeDocument Require(SceneDocument scene, string bridgeId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Bridges.FirstOrDefault(bridge =>
                string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal))
            ?? throw new SceneMakerDocumentException($"Scene has no bridge '{bridgeId}'.");
    }

    /// <summary>
    /// Which end of which bridge sits under an authoring position, within
    /// <paramref name="radiusAuthoringPixels"/>, or none. The nearer end of the
    /// nearer bridge wins, so two ends meeting at a pier stay separable.
    /// </summary>
    public static (BridgeDocument Bridge, BridgeEnd End)? FindEndAt(
        SceneDocument scene,
        double authoringX,
        double authoringY,
        double radiusAuthoringPixels)
    {
        ArgumentNullException.ThrowIfNull(scene);
        (BridgeDocument Bridge, BridgeEnd End)? best = null;
        var bestDistance = radiusAuthoringPixels;
        foreach (var bridge in scene.Bridges)
        {
            foreach (var end in new[] { BridgeEnd.Start, BridgeEnd.End })
            {
                var position = end == BridgeEnd.Start
                    ? bridge.StartAuthoringPx
                    : bridge.EndAuthoringPx;
                var deltaX = position.X - authoringX;
                var deltaY = position.Y - authoringY;
                var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = (bridge, end);
            }
        }
        return best;
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

    private static BridgeDocument Reshaped(
        SceneDocument scene,
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters) => Require(scene, bridgeId) with
    {
        StartAuthoringPx = new AuthoringPixelPosition { X = startAnchorX, Y = startAnchorY },
        EndAuthoringPx = new AuthoringPixelPosition { X = endAnchorX, Y = endAnchorY },
        WidthMeters = widthMeters,
        ElevationMeters = elevationMeters,
        PlankCount = plankCount,
        PlankGapMeters = plankGapMeters,
    };

    private static BridgeDocument Candidate(
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string plankAssetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters) => new()
    {
        BridgeId = bridgeId,
        PlankAssetKey = plankAssetKey,
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
        // Asked rather than resolved, because this answer is a refusal and not
        // a fault: the kit names what the PolyTools Set names, and a Workspace
        // may have stopped enabling one of the two.
        if (!propAssets.Enables(bridge.PlankAssetKey))
        {
            return new BridgeValidationResult(
                false,
                $"This Workspace does not enable the plank Asset '{bridge.PlankAssetKey}'.");
        }
        if (!propAssets.Enables(bridge.AnchorAssetKey))
        {
            return new BridgeValidationResult(
                false,
                $"This Workspace does not enable the post Asset '{bridge.AnchorAssetKey}'.");
        }
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
        {
            // Not against itself. A bridge being reshaped carries the id it
            // already has, so its current posts are about to stop existing and
            // must not block where its new ones go - otherwise nudging an end
            // by a pixel would be refused by the bridge's own shadow. A newly
            // placed bridge takes an unused id, so this skips nothing for it.
            if (string.Equals(existing.BridgeId, bridge.BridgeId, StringComparison.Ordinal))
                continue;
            occupied.AddRange(PostBounds(metrics, propAssets, existing));
        }

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
