namespace SceneMaker.Core;

public static class TransitionEditing
{
    public static SceneDocument Place(
        SceneDocument scene,
        PropDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int anchorX,
        int anchorY,
        string assetKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(placementAssets);
        ArgumentNullException.ThrowIfNull(transitionAssets);
        DocumentValidation.ValidateGrid(scene, transitionAssets.Metrics);
        var asset = transitionAssets.Resolve(assetKey);
        var validation = ValidateCandidate(
            scene,
            placementAssets,
            transitionAssets,
            anchorX,
            anchorY,
            assetKey);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        var transition = new TransitionDocument
        {
            InstanceId = NextInstanceId(scene, asset.AssetKey),
            AssetKey = assetKey,
            PositionAuthoringPx = new AuthoringPixelPosition { X = anchorX, Y = anchorY },
        };
        var placed = scene with
        {
            Transitions = scene.Transitions
                .Append(transition)
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
        };
        DocumentValidation.ValidateGrid(placed, transitionAssets.Metrics);
        return placed;
    }

    public static PropValidationResult ValidateCandidate(
        SceneDocument scene,
        PropDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int anchorX,
        int anchorY,
        string assetKey)
    {
        var asset = transitionAssets.Resolve(assetKey);
        PropBoundsAuthoringPixels candidate;
        try
        {
            candidate = BoundsFor(asset, anchorX, anchorY);
        }
        catch (OverflowException)
        {
            return new PropValidationResult(false, "Transition coordinates exceed the supported range.");
        }

        if (!IsInsideScene(scene, candidate, transitionAssets.Metrics))
            return new PropValidationResult(false, "Transition footprint lies outside the Scene bounds.");

        foreach (var placement in scene.Placements)
        {
            var placementAsset = placementAssets.Resolve(placement.AssetKey);
            var bounds = PropEditing.BoundsFor(
                placementAsset,
                placement.PositionAuthoringPx.X,
                placement.PositionAuthoringPx.Y);
            if (candidate.Overlaps(bounds))
                return new PropValidationResult(
                    false,
                    $"Transition footprint overlaps Placement '{placement.InstanceId}'.");
        }

        foreach (var existing in scene.Transitions)
        {
            var existingAsset = transitionAssets.Resolve(existing.AssetKey);
            var bounds = BoundsFor(
                existingAsset,
                existing.PositionAuthoringPx.X,
                existing.PositionAuthoringPx.Y);
            if (candidate.Overlaps(bounds))
                return new PropValidationResult(
                    false,
                    $"Transition footprint overlaps '{existing.InstanceId}'.");
        }
        var missingTerrain = TerrainCoverage.MissingCells(scene, candidate, transitionAssets.Metrics);
        return missingTerrain.Count == 0
            ? PropValidationResult.Valid
            : new PropValidationResult(
                IsValid: true,
                Reason: null,
                HasCompleteTerrain: false,
                Warning: $"Transition footprint lacks Terrain at {TerrainCoverage.FormatMissingCells(missingTerrain)}.");
    }

    public static SceneDocument PlaceLine(
        SceneDocument scene,
        PropDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        string assetKey)
    {
        var asset = transitionAssets.Resolve(assetKey);
        var result = scene;
        foreach (var (anchorX, anchorY) in LineAnchors(
                     asset,
                     startAnchorX,
                     startAnchorY,
                     endAnchorX,
                     endAnchorY))
        {
            result = Place(
                result,
                placementAssets,
                transitionAssets,
                anchorX,
                anchorY,
                assetKey);
        }
        return result;
    }

    public static IReadOnlyList<(int X, int Y)> LineAnchors(
        TransitionDisplayAsset asset,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY) => SpatialLineAnchors.Build(
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels,
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY,
            offsetAuthoringPixels: 0);

    public static TransitionDocument? FindAt(
        SceneDocument scene,
        TransitionDisplayCatalog transitionAssets,
        int authoringX,
        int authoringY)
    {
        for (var index = scene.Transitions.Count - 1; index >= 0; index--)
        {
            var transition = scene.Transitions[index];
            var asset = transitionAssets.Resolve(transition.AssetKey);
            var bounds = BoundsFor(
                asset,
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            if (authoringX >= bounds.Left && authoringX < bounds.Right
                && authoringY >= bounds.Bottom && authoringY < bounds.Top)
                return transition;
        }
        return null;
    }

    public static SceneDocument EraseAt(
        SceneDocument scene,
        TransitionDisplayCatalog transitionAssets,
        int authoringX,
        int authoringY)
    {
        var found = FindAt(scene, transitionAssets, authoringX, authoringY);
        if (found is null) return scene;
        var erased = scene with
        {
            Transitions = scene.Transitions
                .Where(transition => transition.InstanceId != found.InstanceId)
                .ToList(),
        };
        DocumentValidation.ValidateGrid(erased, transitionAssets.Metrics);
        return erased;
    }

    public static void ValidateAssetReferences(
        SceneDocument scene,
        PropDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        DocumentValidation.ValidateGrid(scene, transitionAssets.Metrics);
        List<PropBoundsAuthoringPixels> accepted = [];
        foreach (var transition in scene.Transitions)
        {
            var asset = transitionAssets.Resolve(transition.AssetKey);
            var bounds = BoundsFor(
                asset,
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            if (!IsInsideScene(scene, bounds, transitionAssets.Metrics))
                throw new SceneMakerDocumentException(
                    "Transition footprint lies outside the Scene bounds.");
            if (accepted.Any(bounds.Overlaps))
                throw new SceneMakerDocumentException(
                    $"Transition '{transition.InstanceId}' overlaps another Transition footprint.");
            foreach (var placement in scene.Placements)
            {
                var placementAsset = placementAssets.Resolve(placement.AssetKey);
                if (bounds.Overlaps(PropEditing.BoundsFor(
                        placementAsset,
                        placement.PositionAuthoringPx.X,
                        placement.PositionAuthoringPx.Y)))
                {
                    throw new SceneMakerDocumentException(
                        $"Transition '{transition.InstanceId}' overlaps Placement '{placement.InstanceId}'.");
                }
            }
            accepted.Add(bounds);
        }
    }

    public static PropBoundsAuthoringPixels BoundsFor(
        TransitionDisplayAsset asset,
        int anchorX,
        int anchorY) => new(
            checked(anchorX - asset.AnchorXAuthoringPixels),
            checked(anchorY - asset.AnchorYAuthoringPixels),
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels);

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

    private static string NextInstanceId(SceneDocument scene, string assetKey)
    {
        var existing = scene.Transitions
            .Select(static transition => transition.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = $"{assetKey}_{index:0000}";
            if (!existing.Contains(candidate)) return candidate;
        }
        throw new SceneMakerDocumentException(
            $"Scene has exhausted stable Transition instance IDs for '{assetKey}'.");
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
