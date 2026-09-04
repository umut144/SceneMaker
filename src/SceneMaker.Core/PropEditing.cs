using System.Globalization;

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
}

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

        var prop = new PropDocument
        {
            InstanceId = NextInstanceId(scene, asset.AssetKey),
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
        };
        return placed;
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
        string assetKey)
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

        foreach (var existing in scene.Props)
        {
            var existingAsset = propAssets.Resolve(existing.AssetKey);
            var existingBounds = BoundsFor(
                existingAsset,
                existing.PositionAuthoringPx.X,
                existing.PositionAuthoringPx.Y);
            if (candidate.Overlaps(existingBounds))
                return new PropValidationResult(
                    false,
                    $"Placement footprint overlaps '{existing.InstanceId}'.");
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
        List<PropBoundsAuthoringPixels> accepted = [];
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
            if (accepted.Any(bounds.Overlaps))
                throw new SceneMakerDocumentException(
                    $"Placement '{prop.InstanceId}' overlaps another Placement footprint.");
            accepted.Add(bounds);
        }
    }

    public static PropBoundsAuthoringPixels BoundsFor(
        PropDisplayAsset asset,
        int anchorX,
        int anchorY) => new(
            checked(anchorX - asset.AnchorXAuthoringPixels),
            checked(anchorY - asset.AnchorYAuthoringPixels),
            asset.FootprintWidthAuthoringPixels,
            asset.FootprintHeightAuthoringPixels);

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
    /// The lowest index this Asset has free, so that erasing a Prop and placing
    /// a new one reuses the gap rather than counting on. Only this Asset's own
    /// IDs are read, and the probe compares numbers: formatting a candidate
    /// string per attempt made placing the n-th Prop allocate n strings.
    /// </summary>
    private static string NextInstanceId(SceneDocument scene, string assetKey)
    {
        var prefix = assetKey + "_";
        HashSet<int> used = [];
        foreach (var prop in scene.Props)
        {
            if (!prop.InstanceId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            // A different Asset whose key starts with this one ('stone' and
            // 'stone_big') fails to parse here and is correctly ignored.
            if (int.TryParse(
                    prop.InstanceId.AsSpan(prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                used.Add(index);
            }
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            if (used.Add(index)) return $"{assetKey}_{index:0000}";
        }
        throw new SceneMakerDocumentException(
            $"Scene has exhausted stable instance IDs for '{assetKey}'.");
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
