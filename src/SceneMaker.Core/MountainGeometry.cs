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
    /// field. The highest top wins, and a mountain at the same height as painted
    /// Terrain deliberately owns the surface there.
    ///
    /// <para>Two mountain bodies may overlap at one elevation only when they
    /// name the same Asset, because no geometric fact could otherwise choose
    /// which surface that place presents. That is a question about the contours
    /// and not about which of them happens to be visible, so it is asked of
    /// every contribution a cell receives - including one buried under a third,
    /// higher body that would hide the tie. Folding the bodies one after another
    /// and comparing each against the winner so far cannot ask it: whether the
    /// tie was ever compared would depend on the order the bodies were folded
    /// in, which is their ID order and therefore the order they were drawn in.
    /// A cell's contributions are collected first, checked per elevation, and
    /// only then reduced to the one that shows.</para>
    /// </summary>
    public static IReadOnlyList<TerrainCellDocument> EffectiveTerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        Dictionary<TerrainCellCoordinate, TerrainCellDocument> terrain = [];
        foreach (var cell in scene.TerrainCells)
            terrain.Add(new TerrainCellCoordinate(cell.X, cell.Y), cell);

        foreach (var (coordinate, contributions) in MountainContributions(scene, metrics))
        {
            RequireOneAssetPerElevation(coordinate, contributions);

            var top = contributions[^1].ElevationMeters;
            if (terrain.TryGetValue(coordinate, out var painted)
                && painted.ElevationMeters > top)
            {
                continue;
            }
            terrain[coordinate] = new TerrainCellDocument
            {
                X = coordinate.X,
                Y = coordinate.Y,
                AssetKey = contributions
                    .First(value => value.ElevationMeters == top)
                    .AssetKey,
                ElevationMeters = top,
            };
        }

        return terrain
            .OrderBy(static value => value.Key.Y)
            .ThenBy(static value => value.Key.X)
            .Select(static value => value.Value)
            .ToList();
    }

    /// <summary>
    /// Every mountain contribution each covered cell receives, cells in
    /// canonical order and each cell's contributions ordered by elevation and
    /// then by body ID. Both orders are derived from the contributions
    /// themselves rather than from how the bodies were reached, which is what
    /// makes the checks below answer the same way every time.
    /// </summary>
    private static List<(TerrainCellCoordinate Coordinate, List<MountainContribution> Contributions)>
        MountainContributions(SceneDocument scene, WorkspaceMetrics metrics)
    {
        Dictionary<TerrainCellCoordinate, List<MountainContribution>> cells = [];
        foreach (var body in scene.MountainBodies)
        {
            var contribution = new MountainContribution(
                body.MountainBodyId,
                body.AssetKey,
                body.ElevationMeters);
            foreach (var coordinate in TerrainCells(scene, metrics, body))
            {
                if (!cells.TryGetValue(coordinate, out var found))
                    cells[coordinate] = found = [];
                found.Add(contribution);
            }
        }

        return cells
            .OrderBy(static entry => entry.Key.Y)
            .ThenBy(static entry => entry.Key.X)
            .Select(static entry => (
                Coordinate: entry.Key,
                Contributions: entry.Value
                    .OrderBy(static value => value.ElevationMeters)
                    .ThenBy(static value => value.MountainBodyId, StringComparer.Ordinal)
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// One elevation over one cell presents one surface. Two bodies tying there
    /// with different Assets leave geometry no winner, so the Scene is refused
    /// rather than resolved by an arbitrary rule. The contributions arrive
    /// sorted, so which pair the message names is a fact about the Scene and not
    /// about the order its bodies were authored in.
    /// </summary>
    private static void RequireOneAssetPerElevation(
        TerrainCellCoordinate coordinate,
        IReadOnlyList<MountainContribution> contributions)
    {
        var first = 0;
        for (var index = 1; index < contributions.Count; index++)
        {
            var current = contributions[index];
            var reference = contributions[first];
            if (current.ElevationMeters != reference.ElevationMeters)
            {
                first = index;
                continue;
            }
            if (string.Equals(current.AssetKey, reference.AssetKey, StringComparison.Ordinal))
                continue;

            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Mountain bodies '{reference.MountainBodyId}' and '{current.MountainBodyId}' overlap at Terrain cell ({coordinate.X}, {coordinate.Y}) on the same elevation {current.ElevationMeters:0.###} m with different Assets."));
        }
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

    /// <summary>What one mountain body offers one cell.</summary>
    private readonly record struct MountainContribution(
        string MountainBodyId,
        string AssetKey,
        decimal ElevationMeters);
}
