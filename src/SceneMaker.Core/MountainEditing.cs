using System.Globalization;

namespace SceneMaker.Core;

/// <summary>One point of a closed mountain contour while it is being drawn.</summary>
public readonly record struct MountainDraftPoint(
    int X,
    int Y,
    MountainPointMode Mode,
    AuthoringPixelOffset? DraggedHandleOut = null);

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

    /// <summary>
    /// The topmost mountain contour containing an authoring position. Picking
    /// follows the same containment rule as rasterization, so the body the
    /// eraser sees is the one whose cells the author sees.
    /// </summary>
    public static MountainBodyDocument? FindAt(
        SceneDocument scene,
        int authoringX,
        int authoringY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.MountainBodies
            .Where(body => ContourRaster.Contains(
                MountainGeometry.RequireContour(body), authoringX, authoringY))
            .OrderBy(static body => body.ElevationMeters)
            .ThenBy(static body => body.MountainBodyId, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// Resolves a closed draft into stored Bezier points. Unlike an open river,
    /// every aligned point has two neighbours: the last and first points are
    /// neighbours across the closing edge too.
    /// </summary>
    public static IReadOnlyList<MountainCurvePointDocument> ResolveContour(
        IReadOnlyList<MountainDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<MountainCurvePointDocument> points = new(draft.Count);
        for (var index = 0; index < draft.Count; index++)
        {
            var current = draft[index];
            if (current.Mode == MountainPointMode.Linear)
            {
                points.Add(Point(current.X, current.Y));
                continue;
            }

            var previous = draft[(index + draft.Count - 1) % draft.Count];
            var next = draft[(index + 1) % draft.Count];
            var (handleIn, handleOut) = current.DraggedHandleOut is { } dragged
                ? Mirrored(Orient(dragged, current, previous))
                : AutomaticHandles(current, previous, next);
            points.Add(Point(
                current.X,
                current.Y,
                MountainPointMode.Aligned,
                handleIn: handleIn,
                handleOut: handleOut));
        }
        return points;
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

    private static AuthoringPixelOffset Orient(
        AuthoringPixelOffset handleOut,
        MountainDraftPoint current,
        MountainDraftPoint previous)
    {
        var travelX = current.X - previous.X;
        var travelY = current.Y - previous.Y;
        return handleOut.X * travelX + handleOut.Y * travelY < 0
            ? new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }
            : handleOut;
    }

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) Mirrored(
        AuthoringPixelOffset handleOut) =>
        (new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }, handleOut);

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) AutomaticHandles(
        MountainDraftPoint current,
        MountainDraftPoint previous,
        MountainDraftPoint next)
    {
        var tangentX = (double)(next.X - previous.X);
        var tangentY = (double)(next.Y - previous.Y);
        var tangentLength = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (tangentLength <= 0.0)
            return (Third(current, previous), Third(current, next));

        var incoming = Distance(current, previous) / 3.0;
        var outgoing = Distance(current, next) / 3.0;
        return (
            Offset(-tangentX / tangentLength * incoming, -tangentY / tangentLength * incoming),
            Offset(tangentX / tangentLength * outgoing, tangentY / tangentLength * outgoing));
    }

    private static AuthoringPixelOffset Third(MountainDraftPoint from, MountainDraftPoint to) =>
        Offset((to.X - from.X) / 3.0, (to.Y - from.Y) / 3.0);

    private static double Distance(MountainDraftPoint from, MountainDraftPoint to)
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
