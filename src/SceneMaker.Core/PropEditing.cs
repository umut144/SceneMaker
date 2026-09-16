namespace SceneMaker.Core;

public readonly record struct PropBoundsAuthoringPixels(
    int Left,
    int Bottom,
    int Width,
    int Height)
{
    public int Right => checked(Left + Width);
    public int Top => checked(Bottom + Height);

    public bool Overlaps(PropBoundsAuthoringPixels other) =>
        Left < other.Right && Right > other.Left
        && Bottom < other.Top && Top > other.Bottom;

    /// <summary>
    /// The rectangle the two bounds share, or null when they do not overlap -
    /// touching edges or corners included, matching <see cref="Overlaps"/>.
    /// </summary>
    public PropBoundsAuthoringPixels? Intersect(PropBoundsAuthoringPixels other)
    {
        if (!Overlaps(other)) return null;
        var left = Math.Max(Left, other.Left);
        var bottom = Math.Max(Bottom, other.Bottom);
        var right = Math.Min(Right, other.Right);
        var top = Math.Min(Top, other.Top);
        return new PropBoundsAuthoringPixels(left, bottom, checked(right - left), checked(top - bottom));
    }
}

/// <summary>
/// Two Props on the same Scene whose placement footprints overlap, and the
/// rectangle they share. world01 refuses to load a Scene where this holds for
/// any pair - see <see cref="PropEditing.FindOverlaps(SceneDocument, PropDisplayCatalog)"/>.
/// </summary>
public readonly record struct PropFootprintOverlap(
    string FirstInstanceId,
    string SecondInstanceId,
    PropBoundsAuthoringPixels Intersection);

/// <summary>
/// Whether a Prop may be authored where it was asked for. Terrain is not part
/// of the question: a Prop carries its own absolute height and is allowed to
/// stand free, so nothing here looks at what is or is not under its footprint.
/// </summary>
public readonly record struct PropValidationResult(
    bool IsValid,
    string? Reason)
{
    public static PropValidationResult Valid { get; } = new(true, null);
}

public static class PropEditing
{
    public static SceneDocument Place(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int anchorX,
        int anchorY,
        string assetKey,
        decimal? elevationMeters = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var asset = propAssets.Resolve(assetKey);
        var validation = ValidateCandidate(scene, propAssets, anchorX, anchorY, assetKey);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        var (instanceId, counters) = AllocateInstanceId(scene, asset.AssetKey);
        var prop = new PropDocument
        {
            InstanceId = instanceId,
            AssetKey = assetKey,
            PositionAuthoringPx = new AuthoringPixelPosition { X = anchorX, Y = anchorY },
            ElevationMeters = elevationMeters ?? scene.DefaultElevationMeters,
        };
        var placed = scene with
        {
            Props = scene.Props
                .Append(prop)
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
            PropInstanceCounters = counters,
        };
        return placed;
    }

    /// <summary>
    /// Whether the given Placement may be carried to a new anchor. The same
    /// question <see cref="ValidateCandidate"/> asks for a fresh Placement,
    /// asked with the Placement's own current footprint left out of the
    /// collision check - it is the thing being moved, not an obstacle to it.
    /// </summary>
    public static PropValidationResult ValidateMove(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        string instanceId,
        int anchorX,
        int anchorY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var existing = scene.Props.FirstOrDefault(prop =>
            string.Equals(prop.InstanceId, instanceId, StringComparison.Ordinal));
        if (existing is null)
            return new PropValidationResult(false, $"Unknown Placement '{instanceId}'.");
        return ValidateCandidate(
            scene, propAssets, anchorX, anchorY, existing.AssetKey, excludeInstanceId: instanceId);
    }

    /// <summary>
    /// Carries an already-placed Placement to a new anchor. It keeps every
    /// other field - its Asset, its elevation, its instance id - exactly as
    /// they were, because a move is not a re-placement: nothing about what
    /// the Placement is changes, only where it stands.
    /// </summary>
    public static SceneDocument Move(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        string instanceId,
        int anchorX,
        int anchorY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var validation = ValidateMove(scene, propAssets, instanceId, anchorX, anchorY);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        return scene with
        {
            Props = scene.Props
                .Select(prop => string.Equals(prop.InstanceId, instanceId, StringComparison.Ordinal)
                    ? prop with
                    {
                        PositionAuthoringPx = new AuthoringPixelPosition { X = anchorX, Y = anchorY },
                    }
                    : prop)
                .ToList(),
        };
    }

    /// <summary>
    /// The whole of what makes a Prop placeable: a representable footprint,
    /// inside the Scene, meeting no other Prop. Terrain underneath is
    /// deliberately not asked about — Props hold an absolute height and may
    /// stand over a hole.
    /// </summary>
    public static PropValidationResult ValidateCandidate(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int anchorX,
        int anchorY,
        string assetKey,
        string? excludeInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        var asset = propAssets.Resolve(assetKey);
        PropBoundsAuthoringPixels candidate;
        try
        {
            candidate = BoundsFor(asset, anchorX, anchorY);
        }
        catch (OverflowException)
        {
            return new PropValidationResult(false, "Placement coordinates exceed the supported range.");
        }

        if (!IsInsideScene(scene, candidate, propAssets.Metrics))
            return new PropValidationResult(false, "Placement footprint lies outside the Scene bounds.");

        // Whether two Placements fit is asked of what they occupy, not of
        // what they are drawn as. A tree's crown may reach over a lamp; its
        // trunk may not stand in the same place.
        // An Asset that occupies nothing cannot collide with anything, in
        // either direction, so both ends of the question skip it.
        if (CollisionBoundsFor(asset, anchorX, anchorY) is not { } candidateCollision)
            return PropValidationResult.Valid;
        foreach (var existing in scene.Props)
        {
            // A Placement being moved is not compared against its own,
            // about-to-be-replaced footprint - that would refuse every move
            // that overlaps where the Placement already stands.
            if (excludeInstanceId is not null
                && string.Equals(existing.InstanceId, excludeInstanceId, StringComparison.Ordinal))
            {
                continue;
            }
            var existingAsset = propAssets.Resolve(existing.AssetKey);
            if (CollisionBoundsFor(
                    existingAsset,
                    existing.PositionAuthoringPx.X,
                    existing.PositionAuthoringPx.Y) is not { } existingCollision)
            {
                continue;
            }
            if (candidateCollision.Overlaps(existingCollision))
                return new PropValidationResult(
                    false,
                    $"Placement collides with '{existing.InstanceId}'.");
        }

        // A bridge post is not a Prop in the document, but it stands in the
        // same map and takes the same space. "Occupied" has to mean one thing
        // whoever asks, or a Placement could be set into a post that a bridge
        // would refuse to set into the Placement.
        foreach (var bridge in scene.Bridges)
        {
            if (BridgeEditing
                .PostBounds(propAssets.Metrics, propAssets, bridge)
                .Any(candidateCollision.Overlaps))
            {
                return new PropValidationResult(
                    false,
                    $"Placement collides with a post of bridge '{bridge.BridgeId}'.");
            }
        }
        return PropValidationResult.Valid;
    }

    public static SceneDocument PlaceLine(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string assetKey,
        decimal? elevationMeters = null,
        int offsetAuthoringPixels = 0)
    {
        var asset = propAssets.Resolve(assetKey);
        var result = scene;
        foreach (var (anchorX, anchorY) in LineAnchors(
                     asset,
                     startAnchorX,
                     startAnchorY,
                     endAnchorX,
                     endAnchorY,
                     offsetAuthoringPixels))
        {
            result = Place(result, propAssets, anchorX, anchorY, assetKey, elevationMeters);
        }
        return result;
    }

    public static IReadOnlyList<(int X, int Y)> LineAnchors(
        PropDisplayAsset asset,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        int offsetAuthoringPixels = 0) => SpatialLineAnchors.Build(
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels,
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY,
            offsetAuthoringPixels);

    public static PropDocument? FindAt(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        for (var index = scene.Props.Count - 1; index >= 0; index--)
        {
            var prop = scene.Props[index];
            var asset = propAssets.Resolve(prop.AssetKey);
            var bounds = BoundsFor(
                asset,
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y);
            if (authoringX >= bounds.Left && authoringX < bounds.Right
                && authoringY >= bounds.Bottom && authoringY < bounds.Top)
                return prop;
        }
        return null;
    }

    public static SceneDocument EraseAt(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        int authoringX,
        int authoringY)
    {
        var found = FindAt(scene, propAssets, authoringX, authoringY);
        if (found is null) return scene;
        return scene with
        {
            Props = scene.Props
                .Where(prop => prop.InstanceId != found.InstanceId)
                .ToList(),
        };
    }

    public static void ValidateAssetReferences(
        SceneDocument scene,
        PropDisplayCatalog propAssets)
    {
        DocumentValidation.ValidateGrid(scene, propAssets.Metrics);
        // The two questions differ and are asked of different boxes: what is
        // drawn has to be inside the Scene, and what is occupied has to be
        // free. Asking both of the footprint would refuse documents that
        // placing them was allowed to produce.
        List<PropBoundsAuthoringPixels> accepted = [];
        foreach (var bridge in scene.Bridges)
            accepted.AddRange(BridgeEditing.PostBounds(propAssets.Metrics, propAssets, bridge));
        foreach (var prop in scene.Props)
        {
            var asset = propAssets.Resolve(prop.AssetKey);
            var bounds = BoundsFor(
                asset,
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y);
            if (!IsInsideScene(scene, bounds, propAssets.Metrics))
                throw new SceneMakerDocumentException(
                    "Placement footprint lies outside the Scene bounds.");
            if (CollisionBoundsFor(
                    asset,
                    prop.PositionAuthoringPx.X,
                    prop.PositionAuthoringPx.Y) is not { } collision)
            {
                continue;
            }
            if (accepted.Any(collision.Overlaps))
                throw new SceneMakerDocumentException(
                    $"Placement '{prop.InstanceId}' collides with something already in the Scene.");
            accepted.Add(collision);
        }
    }

    /// <summary>What an Asset is drawn as, where it stands.</summary>
    public static PropBoundsAuthoringPixels BoundsFor(
        PropDisplayAsset asset,
        int anchorX,
        int anchorY) => new(
            checked(anchorX - asset.AnchorXAuthoringPixels),
            checked(anchorY - asset.AnchorYAuthoringPixels),
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels);

    /// <summary>
    /// What an Asset occupies, where it stands, or null when it occupies
    /// nothing. It sits inside the footprint and is offset from the anchor in
    /// its own right, because a model's collision need not be centred on what
    /// it is drawn as.
    /// </summary>
    public static PropBoundsAuthoringPixels? CollisionBoundsFor(
        PropDisplayAsset asset,
        int anchorX,
        int anchorY)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Collision is not { } collision) return null;
        return new PropBoundsAuthoringPixels(
            checked(anchorX + collision.OffsetXAuthoringPixels),
            checked(anchorY + collision.OffsetYAuthoringPixels),
            collision.WidthAuthoringPixels,
            collision.HeightAuthoringPixels);
    }

    /// <summary>
    /// Every pair among <paramref name="placements"/> whose footprint bounds
    /// overlap, paired with the intersection of just those two footprints -
    /// the exact region a caller should point at, not the whole of either
    /// Prop. Takes bounds directly rather than a Scene so a live drag in
    /// progress can be checked against its current position before the
    /// document catches up. O(n^2): a Scene's Prop count never approaches
    /// where that would matter.
    /// </summary>
    public static IReadOnlyList<PropFootprintOverlap> FindOverlaps(
        IReadOnlyList<(string InstanceId, PropBoundsAuthoringPixels Bounds)> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        List<PropFootprintOverlap> overlaps = [];
        for (var i = 0; i < placements.Count; i++)
        {
            for (var j = i + 1; j < placements.Count; j++)
            {
                if (placements[i].Bounds.Intersect(placements[j].Bounds) is { } intersection)
                {
                    overlaps.Add(new PropFootprintOverlap(
                        placements[i].InstanceId, placements[j].InstanceId, intersection));
                }
            }
        }
        return overlaps;
    }

    /// <summary>
    /// Every overlapping Prop pair on the Scene as authored, by placement
    /// footprint. SceneMaker itself allows a footprint to overlap while
    /// authoring - <see cref="ValidateCandidate"/> only refuses a Placement
    /// whose narrower collision box overlaps another's, a tree's crown may
    /// still reach over a lamp - but world01 refuses to load a Scene where any
    /// two footprints do, so this is checked again before export.
    /// </summary>
    public static IReadOnlyList<PropFootprintOverlap> FindOverlaps(
        SceneDocument scene,
        PropDisplayCatalog propAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        return FindOverlaps(scene.Props
            .Select(prop => (
                prop.InstanceId,
                Bounds: BoundsFor(
                    propAssets.Resolve(prop.AssetKey),
                    prop.PositionAuthoringPx.X,
                    prop.PositionAuthoringPx.Y)))
            .ToList());
    }

    public static decimal PositionMeters(int authoringPixels, WorkspaceMetrics metrics) =>
        (decimal)authoringPixels / metrics.AuthoringPixelsPerMeter;

    public static int PositionGamePixels(int authoringPixels, WorkspaceMetrics metrics) =>
        checked((int)(authoringPixels * metrics.GamePixelsPerMeter / metrics.AuthoringPixelsPerMeter));

    private static bool IsInsideScene(
        SceneDocument scene,
        PropBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics)
    {
        var width = metrics.SceneWidthAuthoringPixels(scene);
        var height = metrics.SceneHeightAuthoringPixels(scene);
        return bounds.Left >= 0 && bounds.Bottom >= 0
            && bounds.Right <= width && bounds.Top <= height;
    }

    /// <summary>
    /// Names the next Placement of one Asset and hands back the counter this
    /// Scene must keep going forward. Deliberately not a scan for the lowest
    /// free number, and never an index into the Props array: either of those
    /// would let erasing a Placement and placing a new one reuse the erased
    /// one's number, which is exactly what a consumer holding that number in
    /// a reference outside this Scene - such as <c>world01</c>'s per-map design
    /// file - cannot survive. A number this Scene has already spent on one
    /// Placement is never spent on a second one, even after the first is gone.
    /// </summary>
    private static (string InstanceId, List<PropInstanceCounterDocument> Counters) AllocateInstanceId(
        SceneDocument scene, string assetKey)
    {
        var nextIndex = 1;
        foreach (var counter in scene.PropInstanceCounters)
        {
            if (string.Equals(counter.AssetKey, assetKey, StringComparison.Ordinal))
            {
                nextIndex = counter.NextIndex;
                break;
            }
        }

        if (nextIndex == int.MaxValue)
        {
            throw new SceneMakerDocumentException(
                $"Scene has exhausted stable instance IDs for '{assetKey}'.");
        }

        var instanceId = $"{assetKey}_{nextIndex:0000}";
        var counters = scene.PropInstanceCounters
            .Where(counter => !string.Equals(counter.AssetKey, assetKey, StringComparison.Ordinal))
            .Append(new PropInstanceCounterDocument { AssetKey = assetKey, NextIndex = nextIndex + 1 })
            .OrderBy(static counter => counter.AssetKey, StringComparer.Ordinal)
            .ToList();
        return (instanceId, counters);
    }
}

internal static class SpatialLineAnchors
{
    public static IReadOnlyList<(int X, int Y)> Build(
        int footprintWidth,
        int footprintHeight,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        int offsetAuthoringPixels)
    {
        if (offsetAuthoringPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetAuthoringPixels));
        var deltaX = (double)endAnchorX - startAnchorX;
        var deltaY = (double)endAnchorY - startAnchorY;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length == 0d) return [(startAnchorX, startAnchorY)];

        var unitX = deltaX / length;
        var unitY = deltaY / length;
        var horizontalStep = Math.Abs(unitX) < double.Epsilon
            ? double.PositiveInfinity
            : footprintWidth / Math.Abs(unitX);
        var verticalStep = Math.Abs(unitY) < double.Epsilon
            ? double.PositiveInfinity
            : footprintHeight / Math.Abs(unitY);
        var stepLength = Math.Min(horizontalStep, verticalStep)
            + offsetAuthoringPixels;
        var count = checked((int)Math.Floor(length / stepLength));

        List<(int X, int Y)> anchors = [(startAnchorX, startAnchorY)];
        var previousBounds = new PropBoundsAuthoringPixels(
            startAnchorX,
            startAnchorY,
            footprintWidth,
            footprintHeight);
        for (var index = 1; index <= count; index++)
        {
            var anchorX = checked((int)Math.Round(
                startAnchorX + (unitX * stepLength * index),
                MidpointRounding.AwayFromZero));
            var anchorY = checked((int)Math.Round(
                startAnchorY + (unitY * stepLength * index),
                MidpointRounding.AwayFromZero));
            var bounds = new PropBoundsAuthoringPixels(
                anchorX,
                anchorY,
                footprintWidth,
                footprintHeight);
            if (bounds.Overlaps(previousBounds)) continue;
            anchors.Add((anchorX, anchorY));
            previousBounds = bounds;
        }
        return anchors;
    }
}
