using System.Globalization;

namespace SceneMaker.Core;

/// <summary>Whether a bridge may be authored as it was asked for.</summary>
public readonly record struct BridgeValidationResult(bool IsValid, string? Reason)
{
    public static BridgeValidationResult Valid { get; } = new(true, null);
}

/// <summary>
/// Pure document operations for straight level spans. A bridge is authored as
/// two ends, a width and one height; its deck is the same band a Path is, and
/// its four posts are derived from those numbers rather than stored, so
/// nothing can drift away from the bridge that produced it.
/// </summary>
public static class BridgeEditing
{
    /// <summary>
    /// A useful first width. Wide enough for the first target Actor to pass
    /// another, and no promise beyond that: traversal is the simulation's
    /// question, here as everywhere else.
    /// </summary>
    public const decimal DefaultWidthMeters = 4.0m;

    public static SceneDocument Place(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        PropDisplayCatalog propAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string assetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(propAssets);
        var candidate = Candidate(
            scene,
            NextBridgeId(scene),
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY,
            assetKey,
            anchorAssetKey,
            widthMeters,
            elevationMeters);
        var validation = Validate(scene, terrainAssets, propAssets, candidate);
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
        TerrainDisplayCatalog terrainAssets,
        PropDisplayCatalog propAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string assetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(propAssets);
        try
        {
            return Validate(
                scene,
                terrainAssets,
                propAssets,
                Candidate(
                    scene,
                    NextBridgeId(scene),
                    startAnchorX,
                    startAnchorY,
                    endAnchorX,
                    endAnchorY,
                    assetKey,
                    anchorAssetKey,
                    widthMeters,
                    elevationMeters));
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
        return
        [
            .. BridgeGeometry.Corners(metrics, bridge)
                .Select(corner => CornerAnchor(metrics, corner))
                .Select(anchor => PropEditing.CollisionBoundsFor(post, anchor.X, anchor.Y)),
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
        SceneDocument scene,
        string bridgeId,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string assetKey,
        string anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters) => new()
    {
        BridgeId = bridgeId,
        AssetKey = assetKey,
        AnchorAssetKey = anchorAssetKey,
        StartAuthoringPx = new AuthoringPixelPosition { X = startAnchorX, Y = startAnchorY },
        EndAuthoringPx = new AuthoringPixelPosition { X = endAnchorX, Y = endAnchorY },
        WidthMeters = widthMeters,
        ElevationMeters = elevationMeters,
    };

    /// <summary>
    /// One answer for the draft and for the commit. A bridge is placeable when
    /// its own numbers are representable, its deck lies inside the Scene, and
    /// all four of its posts can stand where they would stand: a bridge is
    /// authored whole, so it is refused whole.
    /// </summary>
    private static BridgeValidationResult Validate(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        PropDisplayCatalog propAssets,
        BridgeDocument bridge)
    {
        _ = terrainAssets.Resolve(bridge.AssetKey);
        var metrics = propAssets.Metrics;
        if (bridge.WidthMeters <= 0m)
            return new BridgeValidationResult(false, "A bridge needs a positive width.");
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
            occupied.Add(PropEditing.CollisionBoundsFor(
                propAssets.Resolve(prop.AssetKey),
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y));
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
