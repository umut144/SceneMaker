namespace SceneMaker.Core;

public readonly record struct PlacementBoundsAuthoringPixels(
    int Left,
    int Bottom,
    int Width,
    int Height)
{
    public int Right => checked(Left + Width);
    public int Top => checked(Bottom + Height);

    public bool Overlaps(PlacementBoundsAuthoringPixels other) =>
        Left < other.Right && Right > other.Left
        && Bottom < other.Top && Top > other.Bottom;
}

public readonly record struct PlacementValidationResult(
    bool IsValid,
    string? Reason,
    bool HasCompleteTerrain = true,
    string? Warning = null)
{
    public static PlacementValidationResult Valid { get; } = new(true, null);
}

public static class PlacementEditing
{
    public static SceneDocument Place(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int anchorX,
        int anchorY,
        uint assetId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(placementAssets);
        DocumentValidation.Validate(scene);
        var asset = placementAssets.Resolve(assetId);
        var validation = ValidateCandidate(
            scene,
            placementAssets,
            transitionAssets,
            anchorX,
            anchorY,
            assetId);
        if (!validation.IsValid)
            throw new SceneMakerDocumentException(validation.Reason!);

        var placement = new PlacementDocument
        {
            InstanceId = NextInstanceId(scene, asset.Key),
            AssetId = assetId,
            PositionAuthoringPx = new AuthoringPixelPosition { X = anchorX, Y = anchorY },
        };
        var placed = scene with
        {
            Placements = scene.Placements
                .Append(placement)
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
        };
        DocumentValidation.Validate(placed);
        return placed;
    }

    public static PlacementValidationResult ValidateCandidate(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int anchorX,
        int anchorY,
        uint assetId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(placementAssets);
        var asset = placementAssets.Resolve(assetId);
        PlacementBoundsAuthoringPixels candidate;
        try
        {
            candidate = BoundsFor(asset, anchorX, anchorY);
        }
        catch (OverflowException)
        {
            return new PlacementValidationResult(false, "Placement coordinates exceed the supported range.");
        }

        if (!IsInsideScene(scene, candidate))
            return new PlacementValidationResult(false, "Placement footprint lies outside the Scene bounds.");

        foreach (var existing in scene.Placements)
        {
            var existingAsset = placementAssets.Resolve(existing.AssetId);
            var existingBounds = BoundsFor(
                existingAsset,
                existing.PositionAuthoringPx.X,
                existing.PositionAuthoringPx.Y);
            if (candidate.Overlaps(existingBounds))
                return new PlacementValidationResult(
                    false,
                    $"Placement footprint overlaps '{existing.InstanceId}'.");
        }
        foreach (var transition in scene.Transitions)
        {
            var transitionAsset = transitionAssets.Resolve(transition.AssetId);
            var transitionBounds = TransitionEditing.BoundsFor(
                transitionAsset,
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            if (candidate.Overlaps(transitionBounds))
                return new PlacementValidationResult(
                    false,
                    $"Placement footprint overlaps Transition '{transition.InstanceId}'.");
        }
        var missingTerrain = TerrainCoverage.MissingCells(scene, candidate);
        return missingTerrain.Count == 0
            ? PlacementValidationResult.Valid
            : new PlacementValidationResult(
                IsValid: true,
                Reason: null,
                HasCompleteTerrain: false,
                Warning: $"Placement footprint lacks Terrain at {TerrainCoverage.FormatMissingCells(missingTerrain)}.");
    }

    public static SceneDocument PlaceLine(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY,
        uint assetId)
    {
        var asset = placementAssets.Resolve(assetId);
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
                assetId);
        }
        return result;
    }

    public static IReadOnlyList<(int X, int Y)> LineAnchors(
        PlacementDisplayAsset asset,
        int startAnchorX,
        int startAnchorY,
        int endAnchorX,
        int endAnchorY)
    {
        return SpatialLineAnchors.Build(
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels,
            startAnchorX,
            startAnchorY,
            endAnchorX,
            endAnchorY);
    }

    public static PlacementDocument? FindAt(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(placementAssets);
        for (var index = scene.Placements.Count - 1; index >= 0; index--)
        {
            var placement = scene.Placements[index];
            var asset = placementAssets.Resolve(placement.AssetId);
            var bounds = BoundsFor(
                asset,
                placement.PositionAuthoringPx.X,
                placement.PositionAuthoringPx.Y);
            if (authoringX >= bounds.Left && authoringX < bounds.Right
                && authoringY >= bounds.Bottom && authoringY < bounds.Top)
                return placement;
        }
        return null;
    }

    public static SceneDocument EraseAt(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        int authoringX,
        int authoringY)
    {
        var found = FindAt(scene, placementAssets, authoringX, authoringY);
        if (found is null) return scene;
        var erased = scene with
        {
            Placements = scene.Placements
                .Where(placement => placement.InstanceId != found.InstanceId)
                .ToList(),
        };
        DocumentValidation.Validate(erased);
        return erased;
    }

    public static void ValidateAssetReferences(
        SceneDocument scene,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        DocumentValidation.Validate(scene);
        List<PlacementBoundsAuthoringPixels> accepted = [];
        foreach (var placement in scene.Placements)
        {
            var asset = placementAssets.Resolve(placement.AssetId);
            var bounds = BoundsFor(
                asset,
                placement.PositionAuthoringPx.X,
                placement.PositionAuthoringPx.Y);
            if (!IsInsideScene(scene, bounds))
                throw new SceneMakerDocumentException(
                    "Placement footprint lies outside the Scene bounds.");
            if (accepted.Any(bounds.Overlaps))
                throw new SceneMakerDocumentException(
                    $"Placement '{placement.InstanceId}' overlaps another Placement footprint.");
            accepted.Add(bounds);
        }
        foreach (var transition in scene.Transitions)
        {
            var transitionAsset = transitionAssets.Resolve(transition.AssetId);
            var transitionBounds = TransitionEditing.BoundsFor(
                transitionAsset,
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            foreach (var placement in scene.Placements)
            {
                var placementAsset = placementAssets.Resolve(placement.AssetId);
                if (transitionBounds.Overlaps(BoundsFor(
                        placementAsset,
                        placement.PositionAuthoringPx.X,
                        placement.PositionAuthoringPx.Y)))
                {
                    throw new SceneMakerDocumentException(
                        $"Placement '{placement.InstanceId}' overlaps Transition '{transition.InstanceId}'.");
                }
            }
        }
    }

    public static PlacementBoundsAuthoringPixels BoundsFor(
        PlacementDisplayAsset asset,
        int anchorX,
        int anchorY) => new(
            checked(anchorX - asset.AnchorXAuthoringPixels),
            checked(anchorY - asset.AnchorYAuthoringPixels),
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels);

    public static decimal PositionMeters(int authoringPixels) =>
        (decimal)authoringPixels / AuthoringMetrics.AuthoringPixelsPerMeter;

    public static int PositionGamePixels(int authoringPixels) => checked(authoringPixels * 4);

    private static bool IsInsideScene(
        SceneDocument scene,
        PlacementBoundsAuthoringPixels bounds)
    {
        var width = AuthoringMetrics.SceneWidthAuthoringPixels(scene);
        var height = AuthoringMetrics.SceneHeightAuthoringPixels(scene);
        return bounds.Left >= 0 && bounds.Bottom >= 0
            && bounds.Right <= width && bounds.Top <= height;
    }

    private static string NextInstanceId(SceneDocument scene, string assetKey)
    {
        var existing = scene.Placements
            .Select(static placement => placement.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = $"{assetKey}_{index:0000}";
            if (!existing.Contains(candidate)) return candidate;
        }
        throw new SceneMakerDocumentException(
            $"Scene has exhausted stable instance IDs for '{assetKey}'.");
    }
}
