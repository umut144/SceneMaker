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
/// </summary>
public static class TemplateComposition
{
    public static TemplateCompositionResult Compose(
        SceneDocument baseScene,
        IEnumerable<SceneDocument> workspaceScenes,
        PropDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(baseScene);
        ArgumentNullException.ThrowIfNull(workspaceScenes);
        ArgumentNullException.ThrowIfNull(placementAssets);
        ArgumentNullException.ThrowIfNull(transitionAssets);
        DocumentValidation.ValidateGrid(baseScene, placementAssets.Metrics);
        if (baseScene.SceneKind != SceneKind.Instance)
            throw new SceneMakerDocumentException(
                "Only a Scene Instance can receive Scene Templates.");

        var templates = workspaceScenes
            .Where(static scene => scene is not null)
            .Select(scene =>
            {
                DocumentValidation.ValidateGrid(scene, placementAssets.Metrics);
                return scene;
            })
            .Where(static scene => scene.SceneKind == SceneKind.Template)
            .OrderBy(static scene => scene.SceneId, StringComparer.Ordinal)
            .ToList();
        var selected = Select(baseScene.TemplateAnchors, templates, seed);

        var terrain = baseScene.TerrainCells.ToDictionary(
            static cell => (cell.X, cell.Y),
            static cell => cell.AssetKey);
        var placements = baseScene.Placements.ToList();
        var transitions = baseScene.Transitions.ToList();
        List<(SelectedTemplate Candidate, HashSet<(int X, int Y)> Mask)> masks = [];
        foreach (var candidate in selected)
        {
            var translation = Translation(candidate.Anchor, candidate.Template, placementAssets.Metrics);
            var mask = TranslateTerrainMask(baseScene, candidate.Template, translation);
            masks.Add((candidate, mask));
            RejectTransitionMaskIntersection(
                transitions,
                transitionAssets,
                mask,
                candidate.Anchor.AnchorId);

            foreach (var cell in candidate.Template.TerrainCells)
            {
                terrain[(checked(cell.X + translation.CellX), checked(cell.Y + translation.CellY))] =
                    cell.AssetKey;
            }

            placements = placements
                .Where(placement => !FootprintIntersectsMask(
                    PropEditing.BoundsFor(
                        placementAssets.Resolve(placement.AssetKey),
                        placement.PositionAuthoringPx.X,
                        placement.PositionAuthoringPx.Y),
                    mask,
                    placementAssets.Metrics))
                .ToList();
            transitions = transitions
                .Where(transition => !FootprintIntersectsMask(
                    TransitionEditing.BoundsFor(
                        transitionAssets.Resolve(transition.AssetKey),
                        transition.PositionAuthoringPx.X,
                        transition.PositionAuthoringPx.Y),
                    mask,
                    transitionAssets.Metrics))
                .ToList();

            placements.AddRange(candidate.Template.Placements.Select(placement =>
                TranslatePlacement(candidate.Anchor, candidate.Template, placement, translation)));
            transitions.AddRange(candidate.Template.Transitions.Select(transition =>
                TranslateTransition(candidate.Anchor, candidate.Template, transition, translation)));
        }

        var composed = baseScene with
        {
            TerrainCells = terrain
                .OrderBy(static value => value.Key.Y)
                .ThenBy(static value => value.Key.X)
                .Select(static value => new TerrainCellDocument
                {
                    X = value.Key.X,
                    Y = value.Key.Y,
                    AssetKey = value.Value,
                })
                .ToList(),
            Placements = placements
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
            Transitions = transitions
                .OrderBy(static value => value.InstanceId, StringComparer.Ordinal)
                .ToList(),
        };
        DocumentValidation.ValidateGrid(composed, placementAssets.Metrics);
        PropEditing.ValidateAssetReferences(composed, placementAssets, transitionAssets);
        TransitionEditing.ValidateAssetReferences(composed, placementAssets, transitionAssets);
        ValidateTerrainCoverage(composed, placementAssets, transitionAssets);
        return new TemplateCompositionResult(
            composed,
            selected.Select(static value => new TemplateSelection(
                value.Anchor.AnchorId,
                value.Anchor.GroupNumber,
                value.Template.SceneId)).ToList(),
            EffectiveTerrainMasks(masks));
    }

    private static IReadOnlyList<TemplateTerrainMask> EffectiveTerrainMasks(
        IReadOnlyList<(SelectedTemplate Candidate, HashSet<(int X, int Y)> Mask)> masks)
    {
        HashSet<(int X, int Y)> claimedByLaterTemplate = [];
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

    private static TemplateTranslation Translation(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        WorkspaceMetrics metrics)
    {
        var insertion = template.TemplateDefinition!.InsertionAnchorAuthoringPx;
        var authoringX = checked(anchor.PositionAuthoringPx.X - insertion.X);
        var authoringY = checked(anchor.PositionAuthoringPx.Y - insertion.Y);
        var step = metrics.AuthoringPixelsPerTerrainCell;
        if (authoringX % step != 0 || authoringY % step != 0)
        {
            throw new SceneMakerDocumentException(
                $"Template Anchor '{anchor.AnchorId}' and Template '{template.SceneId}' must produce a WorldGrid-aligned Terrain translation.");
        }
        return new TemplateTranslation(authoringX, authoringY, authoringX / step, authoringY / step);
    }

    private static HashSet<(int X, int Y)> TranslateTerrainMask(
        SceneDocument baseScene,
        SceneDocument template,
        TemplateTranslation translation)
    {
        HashSet<(int X, int Y)> mask = [];
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
            mask.Add((x, y));
        }
        return mask;
    }

    private static PropDocument TranslatePlacement(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        PropDocument placement,
        TemplateTranslation translation) => new()
    {
        InstanceId = DerivedInstanceId(anchor, template, placement.InstanceId),
        AssetKey = placement.AssetKey,
        PositionAuthoringPx = new AuthoringPixelPosition
        {
            X = checked(placement.PositionAuthoringPx.X + translation.AuthoringX),
            Y = checked(placement.PositionAuthoringPx.Y + translation.AuthoringY),
        },
    };

    private static TransitionDocument TranslateTransition(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        TransitionDocument transition,
        TemplateTranslation translation) => new()
    {
        InstanceId = DerivedInstanceId(anchor, template, transition.InstanceId),
        AssetKey = transition.AssetKey,
        PositionAuthoringPx = new AuthoringPixelPosition
        {
            X = checked(transition.PositionAuthoringPx.X + translation.AuthoringX),
            Y = checked(transition.PositionAuthoringPx.Y + translation.AuthoringY),
        },
    };

    private static string DerivedInstanceId(
        TemplateAnchorDocument anchor,
        SceneDocument template,
        string sourceInstanceId) =>
        $"{anchor.AnchorId}.{template.SceneId}.{sourceInstanceId}";

    private static bool FootprintIntersectsMask(
        PropBoundsAuthoringPixels bounds,
        IReadOnlySet<(int X, int Y)> mask,
        WorkspaceMetrics metrics)
    {
        var step = metrics.AuthoringPixelsPerTerrainCell;
        var firstX = bounds.Left / step;
        var lastX = checked(bounds.Right - 1) / step;
        var firstY = bounds.Bottom / step;
        var lastY = checked(bounds.Top - 1) / step;
        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++)
            {
                if (mask.Contains((x, y))) return true;
            }
        }
        return false;
    }

    private static void RejectTransitionMaskIntersection(
        IEnumerable<TransitionDocument> transitions,
        TransitionDisplayCatalog assets,
        IReadOnlySet<(int X, int Y)> mask,
        string anchorId)
    {
        foreach (var transition in transitions)
        {
            var asset = assets.Resolve(transition.AssetKey);
            var bounds = TransitionEditing.BoundsFor(
                asset,
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            if (FootprintIntersectsMask(bounds, mask, assets.Metrics))
            {
                throw new SceneMakerDocumentException(
                    $"Template Anchor '{anchorId}' would overwrite Transition '{transition.InstanceId}'. Transitions remain authored only on the Scene Instance.");
            }
        }
    }

    private static void ValidateTerrainCoverage(
        SceneDocument scene,
        PropDisplayCatalog placements,
        TransitionDisplayCatalog transitions)
    {
        foreach (var placement in scene.Placements)
        {
            var bounds = PropEditing.BoundsFor(
                placements.Resolve(placement.AssetKey),
                placement.PositionAuthoringPx.X,
                placement.PositionAuthoringPx.Y);
            ThrowIfMissingTerrain(scene, bounds, "Placement", placement.InstanceId, placements.Metrics);
        }
        foreach (var transition in scene.Transitions)
        {
            var bounds = TransitionEditing.BoundsFor(
                transitions.Resolve(transition.AssetKey),
                transition.PositionAuthoringPx.X,
                transition.PositionAuthoringPx.Y);
            ThrowIfMissingTerrain(scene, bounds, "Transition", transition.InstanceId, transitions.Metrics);
        }
    }

    private static void ThrowIfMissingTerrain(
        SceneDocument scene,
        PropBoundsAuthoringPixels bounds,
        string label,
        string instanceId,
        WorkspaceMetrics metrics)
    {
        var missing = TerrainCoverage.MissingCells(scene, bounds, metrics);
        if (missing.Count == 0) return;
        throw new SceneMakerDocumentException(
            $"Composed {label} '{instanceId}' lacks Terrain at {TerrainCoverage.FormatMissingCells(missing)}.");
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
