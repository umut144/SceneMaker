using System.Globalization;

namespace SceneMaker.Core;

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
