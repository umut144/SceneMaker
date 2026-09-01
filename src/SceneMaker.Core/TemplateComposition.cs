namespace SceneMaker.Core;

public sealed record TemplateSelection(
    string AnchorId,
    int GroupNumber,
    string TemplateSceneId);

/// <summary>
/// The terrain cells that remain visibly effective for one selected Template
/// after later stable Anchor selections have overwritten shared cells.
/// </summary>
public sealed record TemplateTerrainMask(
    string AnchorId,
    string TemplateSceneId,
    IReadOnlyList<TemplateTerrainMaskCell> Cells);

public readonly record struct TemplateTerrainMaskCell(int X, int Y);

public sealed record TemplateCompositionResult(
    SceneDocument ComposedScene,
    IReadOnlyList<TemplateSelection> Selections,
    IReadOnlyList<TemplateTerrainMask> EffectiveTerrainMasks);

/// <summary>
/// Produces an ephemeral Scene Instance from an unchanged authored base Scene
/// and a deterministic selection of Scene Templates. It never persists or
/// mutates either source document.
///
/// A Template replaces everything inside its Terrain mask: Props of the base
/// Scene whose footprint intersects the mask are dropped from the composition.
/// </summary>
public static class TemplateComposition
{
    public static TemplateCompositionResult Compose(
        SceneDocument baseScene,
        IEnumerable<SceneDocument> workspaceScenes,
        PropDisplayCatalog propAssets,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(baseScene);
        ArgumentNullException.ThrowIfNull(workspaceScenes);
        ArgumentNullException.ThrowIfNull(propAssets);
        DocumentValidation.ValidateGrid(baseScene, propAssets.Metrics);
        if (baseScene.SceneKind != SceneKind.Instance)
            throw new SceneMakerDocumentException(
                "Only a Scene Instance can receive Scene Templates.");

        var templates = workspaceScenes
            .Where(static scene => scene is not null)
            .Select(scene =>
            {
                DocumentValidation.ValidateGrid(scene, propAssets.Metrics);
                return scene;
            })
            .Where(static scene => scene.SceneKind == SceneKind.Template)
            .OrderBy(static scene => scene.SceneId, StringComparer.Ordinal)
            .ToList();
        var selected = Select(baseScene.TemplateAnchors, templates, seed);

        var terrain = baseScene.TerrainCells.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell);
        var props = baseScene.Props.ToList();
        List<(SelectedTemplate Candidate, HashSet<TerrainCellCoordinate> Mask)> masks = [];
        foreach (var candidate in selected)
        {
            var translation = Translation(candidate.Anchor, candidate.Template, propAssets.Metrics);
            var mask = TranslateTerrainMask(baseScene, candidate.Template, translation);
            masks.Add((candidate, mask));

            foreach (var cell in candidate.Template.TerrainCells)
            {
                var coordinate = new TerrainCellCoordinate(
                    checked(cell.X + translation.CellX),
                    checked(cell.Y + translation.CellY));
                // The Template brings its own heights: an Anchor places it in
                // x and y only, it never lifts or lowers what it places.
                terrain[coordinate] = cell with { X = coordinate.X, Y = coordinate.Y };
            }

            props = props
                .Where(prop => !FootprintIntersectsMask(
                    PropEditing.BoundsFor(
                        propAssets.Resolve(prop.AssetKey),
                        prop.PositionAuthoringPx.X,
                        prop.PositionAuthoringPx.Y),
                    mask,
                    propAssets.Metrics))
                .ToList();
            props.AddRange(candidate.Template.Props.Select(prop =>
                TranslateProp(candidate.Anchor, candidate.Template, prop, translation)));
        }

        var composed = baseScene with
        {
            TerrainCells = terrain
                .OrderBy(static value => value.Key.Y)
                .ThenBy(static value => value.Key.X)
                .Select(static value => value.Value)
                .ToList(),
            Props = props
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
        };
        DocumentValidation.ValidateGrid(composed, propAssets.Metrics);
        PropEditing.ValidateAssetReferences(composed, propAssets);
        ValidateTerrainCoverage(composed, propAssets);
        return new TemplateCompositionResult(
            composed,
            selected.Select(static value => new TemplateSelection(
                value.Anchor.AnchorId,
                value.Anchor.GroupNumber,
                value.Template.SceneId)).ToList(),
            EffectiveTerrainMasks(masks));
    }

    private static IReadOnlyList<TemplateTerrainMask> EffectiveTerrainMasks(
        IReadOnlyList<(SelectedTemplate Candidate, HashSet<TerrainCellCoordinate> Mask)> masks)
    {
        HashSet<TerrainCellCoordinate> claimedByLaterTemplate = [];
        List<TemplateTerrainMask> result = [];
        for (var index = masks.Count - 1; index >= 0; index--)
        {
            var (candidate, mask) = masks[index];
            var effective = mask
                .Where(cell => !claimedByLaterTemplate.Contains(cell))
                .OrderBy(static cell => cell.Y)
                .ThenBy(static cell => cell.X)
                .Select(static cell => new TemplateTerrainMaskCell(cell.X, cell.Y))
                .ToList();
            claimedByLaterTemplate.UnionWith(mask);
            result.Add(new TemplateTerrainMask(
                candidate.Anchor.AnchorId,
                candidate.Template.SceneId,
                effective));
        }
        result.Reverse();
        return result;
    }

    private static IReadOnlyList<SelectedTemplate> Select(
        IReadOnlyList<TemplateAnchorDocument> anchors,
        IReadOnlyList<SceneDocument> templates,
        ulong seed)
    {
        List<SelectedTemplate> result = [];
        foreach (var group in anchors.GroupBy(static anchor => anchor.GroupNumber)
                     .OrderBy(static group => group.Key))
        {
            var pool = templates
                .Where(template => template.TemplateDefinition!.GroupNumber == group.Key)
                .OrderBy(static template => template.SceneId, StringComparer.Ordinal)
                .ToList();
            var orderedAnchors = group.OrderBy(static anchor => anchor.AnchorId, StringComparer.Ordinal)
                .ToList();
            if (pool.Count < orderedAnchors.Count)
            {
                throw new SceneMakerDocumentException(
                    $"Template group {group.Key} has {pool.Count} Template{(pool.Count == 1 ? string.Empty : "s")} for {orderedAnchors.Count} Anchors; selection without replacement requires at least one Template per Anchor.");
            }
            Shuffle(pool, MixSeed(seed, group.Key));
            for (var index = 0; index < orderedAnchors.Count; index++)
                result.Add(new SelectedTemplate(orderedAnchors[index], pool[index]));
        }
        return result.OrderBy(static value => value.Anchor.AnchorId, StringComparer.Ordinal).ToList();
    }

    private static void Shuffle(List<SceneDocument> values, ulong seed)
    {
        var random = new DeterministicRandom(seed);
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swap = (int)(random.Next() % (ulong)(index + 1));
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    private static ulong MixSeed(ulong seed, int groupNumber) =>
        seed ^ (unchecked((ulong)(uint)groupNumber) * 0x9E3779B97F4A7C15UL);

    /// <summary>
    /// Anchor positions and Template insertion anchors are both validated to sit
    /// on the WorldGrid, so their difference is always a whole number of cells.
    /// </summary>
    private static TemplateTranslation Translation(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        WorkspaceMetrics metrics)
    {
        var insertion = template.TemplateDefinition!.InsertionAnchorAuthoringPx;
        var authoringX = checked(anchor.PositionAuthoringPx.X - insertion.X);
        var authoringY = checked(anchor.PositionAuthoringPx.Y - insertion.Y);
        var step = metrics.AuthoringPixelsPerTerrainCell;
        return new TemplateTranslation(authoringX, authoringY, authoringX / step, authoringY / step);
    }

    private static HashSet<TerrainCellCoordinate> TranslateTerrainMask(
        SceneDocument baseScene,
        SceneDocument template,
        TemplateTranslation translation)
    {
        HashSet<TerrainCellCoordinate> mask = [];
        foreach (var cell in template.TerrainCells)
        {
            var x = checked(cell.X + translation.CellX);
            var y = checked(cell.Y + translation.CellY);
            if (x < 0 || x >= baseScene.SizeCells.Width
                || y < 0 || y >= baseScene.SizeCells.Height)
            {
                throw new SceneMakerDocumentException(
                    $"Template '{template.SceneId}' Terrain at ({cell.X}, {cell.Y}) lies outside Scene Instance '{baseScene.SceneId}' after placement.");
            }
            mask.Add(new TerrainCellCoordinate(x, y));
        }
        return mask;
    }

    private static PropDocument TranslateProp(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        PropDocument prop,
        TemplateTranslation translation) => new()
    {
        InstanceId = DerivedInstanceId(anchor, template, prop.InstanceId),
        AssetKey = prop.AssetKey,
        PositionAuthoringPx = new AuthoringPixelPosition
        {
            X = checked(prop.PositionAuthoringPx.X + translation.AuthoringX),
            Y = checked(prop.PositionAuthoringPx.Y + translation.AuthoringY),
        },
        ElevationMeters = prop.ElevationMeters,
    };

    private static string DerivedInstanceId(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        string sourceInstanceId) =>
        $"{anchor.AnchorId}.{template.SceneId}.{sourceInstanceId}";

    private static bool FootprintIntersectsMask(
        PropBoundsAuthoringPixels bounds,
        IReadOnlySet<TerrainCellCoordinate> mask,
        WorkspaceMetrics metrics) =>
        TerrainCoverage.IntersectedCells(bounds, metrics).Any(mask.Contains);

    private static void ValidateTerrainCoverage(
        SceneDocument scene,
        PropDisplayCatalog propAssets)
    {
        var authored = TerrainCoverage.AuthoredCells(scene);
        foreach (var prop in scene.Props)
        {
            var bounds = PropEditing.BoundsFor(
                propAssets.Resolve(prop.AssetKey),
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y);
            var missing = TerrainCoverage.MissingCells(authored, bounds, propAssets.Metrics);
            if (missing.Count == 0) continue;
            throw new SceneMakerDocumentException(
                $"Composed Prop '{prop.InstanceId}' lacks Terrain at {TerrainCoverage.FormatMissingCells(missing)}.");
        }
    }

    private sealed record SelectedTemplate(
        TemplateAnchorDocument Anchor,
        SceneDocument Template);

    private readonly record struct TemplateTranslation(
        int AuthoringX,
        int AuthoringY,
        int CellX,
        int CellY);

    private sealed class DeterministicRandom(ulong state)
    {
        private ulong _state = state;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
