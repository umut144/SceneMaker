using System.Globalization;

namespace SceneMaker.Core;

/// <summary>Pure edits for closed, level-topped mountain bodies.</summary>
public static class MountainEditing
{
    public static SceneDocument Place(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        TerrainDisplayCatalog terrainAssets,
        IReadOnlyList<MountainCurvePointDocument> points,
        string assetKey,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(points);
        _ = RequireDrawable(terrainAssets, assetKey);
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Mountain elevation {elevationMeters:0.############################} m must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var body = new MountainBodyDocument
        {
            MountainBodyId = NextMountainBodyId(scene),
            AssetKey = assetKey,
            ElevationMeters = elevationMeters,
            Points = [.. points],
        };
        _ = MountainGeometry.RequireContour(body);
        return scene with
        {
            MountainBodies = scene.MountainBodies
                .Append(body)
                .OrderBy(static value => value.MountainBodyId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static SceneDocument Remove(SceneDocument scene, string mountainBodyId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.MountainBodies
            .Where(body => !string.Equals(
                body.MountainBodyId,
                mountainBodyId,
                StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.MountainBodies.Count
            ? scene
            : scene with { MountainBodies = remaining };
    }

    public static MountainCurvePointDocument Point(
        int authoringX,
        int authoringY,
        MountainPointMode mode = MountainPointMode.Linear,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = authoringX, Y = authoringY },
        Mode = mode,
        HandleInAuthoringPx = mode == MountainPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = mode == MountainPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleOut ?? AuthoringPixelOffset.Zero,
    };

    /// <summary>Checks every persisted mountain Asset at an IO boundary.</summary>
    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var body in scene.MountainBodies)
            _ = RequireDrawable(terrainAssets, body.AssetKey);
    }

    private static TerrainDisplayAsset RequireDrawable(
        TerrainDisplayCatalog terrainAssets,
        string assetKey)
    {
        var asset = terrainAssets.Resolve(assetKey);
        if (asset.Authoring != TerrainAuthoring.Cells)
        {
            throw new SceneMakerDocumentException(
                $"Terrain Asset '{assetKey}' is authored as a curve and cannot surface a mountain body.");
        }
        return asset;
    }

    private static string NextMountainBodyId(SceneDocument scene)
    {
        const string prefix = "mountain_";
        HashSet<int> used = [];
        foreach (var body in scene.MountainBodies)
        {
            if (!body.MountainBodyId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    body.MountainBodyId.AsSpan(prefix.Length),
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
        throw new SceneMakerDocumentException("Scene has exhausted stable mountain body IDs.");
    }
}
