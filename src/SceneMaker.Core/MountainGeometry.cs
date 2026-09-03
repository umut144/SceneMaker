namespace SceneMaker.Core;

/// <summary>
/// Derives solid Terrain from editable mountain contours. A mountain is not a
/// separate fill and carries no material of its own: it raises the top of
/// painted Terrain's solid column, so the existing cut rule can carve a river or
/// tunnel through it and the Asset underneath keeps saying what the surface is
/// made of.
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
    /// Painted Terrain and every mountain folded into one height field.
    ///
    /// <para>A mountain contributes height and nothing else. It raises the top
    /// of a painted cell's solid column and leaves its Asset alone, so a contour
    /// drawn around a stretch of sand and grass lifts that pattern unchanged
    /// rather than replacing it with one material. Which is why a contour over a
    /// coordinate nobody painted produces no cell at all: there is no column to
    /// raise, and a mountain has no material of its own to make one from. The
    /// body stays authored and starts working the moment Terrain is painted
    /// under it.</para>
    ///
    /// <para>Every contour is flattened and checked here, before any of that is
    /// used, and unconditionally - a Scene with no Terrain cells at all must
    /// still refuse an unusable contour. Skipping the walk when there is nothing
    /// to raise would hide exactly that.</para>
    /// </summary>
    public static IReadOnlyList<TerrainCellDocument> EffectiveTerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        var raised = RaisedTops(scene, metrics);
        // The painted cells are already unique and canonically ordered by Y then
        // X, so the fold is a mapping and needs no ordering of its own.
        List<TerrainCellDocument> terrain = new(scene.TerrainCells.Count);
        foreach (var cell in scene.TerrainCells)
        {
            terrain.Add(
                raised.TryGetValue(new TerrainCellCoordinate(cell.X, cell.Y), out var top)
                && top > cell.ElevationMeters
                    ? cell with { ElevationMeters = top }
                    : cell);
        }
        return terrain;
    }

    /// <summary>
    /// The painted cells this one body is currently responsible for lifting:
    /// those where its top stands strictly above the painted height and above
    /// every other body over them. Each is returned as the cell would look with
    /// the body in place, Asset included, so a caller never has to look the
    /// material up a second time.
    ///
    /// <para>One definition, read in both directions. For a body that is not in
    /// the Scene yet it answers what placing it would raise; for one that is, it
    /// answers what removing it would lower, because bodies are told apart by
    /// ID and a body is never compared against itself. Ties fall out correctly
    /// on their own: two bodies at one height each raise nothing over the other,
    /// and removing either leaves the height where it is.</para>
    /// </summary>
    public static IReadOnlyList<TerrainCellDocument> CellsRaisedBy(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        MountainBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(body);

        Dictionary<TerrainCellCoordinate, TerrainCellDocument> painted = [];
        foreach (var cell in scene.TerrainCells)
            painted[new TerrainCellCoordinate(cell.X, cell.Y)] = cell;

        var others = RaisedTops(scene, metrics, exceptBodyId: body.MountainBodyId);

        List<TerrainCellDocument> raised = [];
        foreach (var coordinate in TerrainCells(scene, metrics, body))
        {
            if (!painted.TryGetValue(coordinate, out var cell)) continue;
            var beneath = others.TryGetValue(coordinate, out var other) && other > cell.ElevationMeters
                ? other
                : cell.ElevationMeters;
            if (body.ElevationMeters <= beneath) continue;
            raised.Add(cell with { ElevationMeters = body.ElevationMeters });
        }
        return raised;
    }

    /// <summary>
    /// The highest mountain top over each covered coordinate. Walks every body,
    /// which is where each contour is flattened and checked.
    /// </summary>
    private static Dictionary<TerrainCellCoordinate, decimal> RaisedTops(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string? exceptBodyId = null)
    {
        Dictionary<TerrainCellCoordinate, decimal> raised = [];
        foreach (var body in scene.MountainBodies)
        {
            if (exceptBodyId is not null
                && string.Equals(body.MountainBodyId, exceptBodyId, StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var coordinate in TerrainCells(scene, metrics, body))
            {
                if (!raised.TryGetValue(coordinate, out var top) || body.ElevationMeters > top)
                    raised[coordinate] = body.ElevationMeters;
            }
        }
        return raised;
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
}
