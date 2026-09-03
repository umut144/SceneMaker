namespace SceneMaker.Core;

/// <summary>
/// Derives solid Terrain from editable mountain contours. A mountain is not a
/// separate fill: it raises the top of Terrain's solid column, so the existing
/// cut rule can carve a river or tunnel through it.
/// </summary>
public static class MountainGeometry
{
    public static FlattenedClosedChain Flatten(MountainBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return BezierChain.FlattenClosed(body.Points.Select(ToChainPoint).ToList());
    }

    /// <summary>The checked contour of one body, ready to query or rasterize.</summary>
    public static FlattenedClosedChain RequireContour(MountainBodyDocument body)
    {
        var ring = Flatten(body);
        if (ClosedChainGeometry.Validate(ring) is not { } defect) return ring;
        var location = defect.SegmentA is { } first && defect.SegmentB is { } second
            ? $" at flattened segments {first} and {second}"
            : string.Empty;
        throw new SceneMakerDocumentException(
            $"Mountain body '{body.MountainBodyId}' has a contour that is {Describe(defect.Kind)}{location}.");
    }

    public static IReadOnlyList<TerrainCellCoordinate> TerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        MountainBodyDocument body) =>
        ContourRaster.TerrainCells(scene, metrics, RequireContour(body));

    /// <summary>
    /// Painted Terrain and every mountain folded into one canonical height
    /// field. The highest top wins. A mountain at the same height as painted
    /// Terrain deliberately owns the surface there; two mountain bodies at the
    /// same height may overlap only when they name the same Asset, because no
    /// geometric fact could otherwise choose which surface is presented.
    /// </summary>
    public static IReadOnlyList<TerrainCellDocument> EffectiveTerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        Dictionary<TerrainCellCoordinate, Contribution> terrain = [];
        foreach (var cell in scene.TerrainCells)
        {
            terrain.Add(
                new TerrainCellCoordinate(cell.X, cell.Y),
                new Contribution(cell, MountainBodyId: null));
        }

        foreach (var body in scene.MountainBodies.OrderBy(
                     static value => value.MountainBodyId,
                     StringComparer.Ordinal))
        {
            foreach (var coordinate in TerrainCells(scene, metrics, body))
            {
                var candidate = new TerrainCellDocument
                {
                    X = coordinate.X,
                    Y = coordinate.Y,
                    AssetKey = body.AssetKey,
                    ElevationMeters = body.ElevationMeters,
                };
                if (!terrain.TryGetValue(coordinate, out var existing)
                    || candidate.ElevationMeters > existing.Cell.ElevationMeters)
                {
                    terrain[coordinate] = new Contribution(candidate, body.MountainBodyId);
                    continue;
                }
                if (candidate.ElevationMeters < existing.Cell.ElevationMeters) continue;

                if (existing.MountainBodyId is null)
                {
                    // At the same top a deliberately placed contour owns the
                    // visible surface over the underlying painted cell.
                    terrain[coordinate] = new Contribution(candidate, body.MountainBodyId);
                    continue;
                }
                if (!string.Equals(
                        candidate.AssetKey,
                        existing.Cell.AssetKey,
                        StringComparison.Ordinal))
                {
                    throw new SceneMakerDocumentException(
                        FormattableString.Invariant(
                            $"Mountain bodies '{existing.MountainBodyId}' and '{body.MountainBodyId}' overlap at Terrain cell ({coordinate.X}, {coordinate.Y}) on the same elevation {body.ElevationMeters:0.###} m with different Assets."));
                }
            }
        }

        return terrain
            .OrderBy(static value => value.Key.Y)
            .ThenBy(static value => value.Key.X)
            .Select(static value => value.Value.Cell)
            .ToList();
    }

    private static BezierChainPoint ToChainPoint(MountainCurvePointDocument point) => new(
        point.PositionAuthoringPx.X,
        point.PositionAuthoringPx.Y,
        point.HandleInAuthoringPx.X,
        point.HandleInAuthoringPx.Y,
        point.HandleOutAuthoringPx.X,
        point.HandleOutAuthoringPx.Y);

    private static string Describe(ClosedChainDefectKind kind) => kind switch
    {
        ClosedChainDefectKind.TooFewPoints => "made of fewer than three distinct points",
        ClosedChainDefectKind.ZeroArea => "flat enough to enclose nothing",
        ClosedChainDefectKind.SelfIntersecting => "in contact with itself",
        _ => throw new InvalidOperationException($"Unknown contour defect '{kind}'."),
    };

    private sealed record Contribution(
        TerrainCellDocument Cell,
        string? MountainBodyId);
}
