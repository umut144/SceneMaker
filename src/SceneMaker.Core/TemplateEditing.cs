namespace SceneMaker.Core;

public static class TemplateEditing
{
    public const int AnchorVisualSizeAuthoringPixels = 32;

    public static int SnapToWorldGrid(int coordinate, int authoringPixelsPerTerrainCell)
    {
        if (authoringPixelsPerTerrainCell <= 0)
            throw new ArgumentOutOfRangeException(nameof(authoringPixelsPerTerrainCell));
        var step = authoringPixelsPerTerrainCell;
        return checked((int)Math.Round(
            (decimal)coordinate / step,
            MidpointRounding.AwayFromZero) * step);
    }

    public static SceneDocument PlaceAnchor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        int positionX,
        int positionY,
        int groupNumber)
    {
        RequireInstance(scene);
        var anchor = new TemplateAnchorDocument
        {
            AnchorId = NextAnchorId(scene),
            GroupNumber = groupNumber,
            PositionAuthoringPx = new AuthoringPixelPosition
            {
                X = SnapToWorldGrid(positionX, metrics.AuthoringPixelsPerTerrainCell),
                Y = SnapToWorldGrid(positionY, metrics.AuthoringPixelsPerTerrainCell),
            },
        };
        var updated = scene with
        {
            TemplateAnchors = scene.TemplateAnchors
                .Append(anchor)
                .OrderBy(static value => value.AnchorId, StringComparer.Ordinal)
                .ToList(),
        };
        DocumentValidation.ValidateGrid(updated, metrics);
        return updated;
    }

    public static SceneDocument MoveAnchor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string anchorId,
        int positionX,
        int positionY)
    {
        RequireInstance(scene);
        var found = false;
        var updated = scene with
        {
            TemplateAnchors = scene.TemplateAnchors.Select(anchor =>
            {
                if (anchor.AnchorId != anchorId) return anchor;
                found = true;
                return anchor with
                {
                    PositionAuthoringPx = new AuthoringPixelPosition
                    {
                        X = SnapToWorldGrid(positionX, metrics.AuthoringPixelsPerTerrainCell),
                        Y = SnapToWorldGrid(positionY, metrics.AuthoringPixelsPerTerrainCell),
                    },
                };
            }).ToList(),
        };
        if (!found)
            throw new SceneMakerDocumentException($"Unknown Template Anchor '{anchorId}'.");
        DocumentValidation.ValidateGrid(updated, metrics);
        return updated;
    }

    public static SceneDocument SetAnchorGroup(
        SceneDocument scene,
        string anchorId,
        int groupNumber)
    {
        RequireInstance(scene);
        var found = false;
        var updated = scene with
        {
            TemplateAnchors = scene.TemplateAnchors.Select(anchor =>
            {
                if (anchor.AnchorId != anchorId) return anchor;
                found = true;
                return anchor with { GroupNumber = groupNumber };
            }).ToList(),
        };
        if (!found)
            throw new SceneMakerDocumentException($"Unknown Template Anchor '{anchorId}'.");
        DocumentValidation.Validate(updated);
        return updated;
    }

    public static SceneDocument SetTemplateGroup(SceneDocument scene, int groupNumber)
    {
        if (scene.SceneKind != SceneKind.Template || scene.TemplateDefinition is null)
            throw new SceneMakerDocumentException("Only a Scene Template has a Template group.");
        var updated = scene with
        {
            TemplateDefinition = scene.TemplateDefinition with { GroupNumber = groupNumber },
        };
        DocumentValidation.Validate(updated);
        return updated;
    }

    public static SceneDocument MoveTemplateInsertionAnchor(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        int positionX,
        int positionY)
    {
        if (scene.SceneKind != SceneKind.Template || scene.TemplateDefinition is null)
            throw new SceneMakerDocumentException(
                "Only a Scene Template has an insertion anchor.");
        var updated = scene with
        {
            TemplateDefinition = scene.TemplateDefinition with
            {
                InsertionAnchorAuthoringPx = new AuthoringPixelPosition
                {
                    X = SnapToWorldGrid(positionX, metrics.AuthoringPixelsPerTerrainCell),
                    Y = SnapToWorldGrid(positionY, metrics.AuthoringPixelsPerTerrainCell),
                },
            },
        };
        DocumentValidation.ValidateGrid(updated, metrics);
        return updated;
    }

    public static TemplateAnchorDocument? FindAnchorAt(
        SceneDocument scene,
        int authoringX,
        int authoringY)
    {
        RequireInstance(scene);
        var half = AnchorVisualSizeAuthoringPixels / 2;
        for (var index = scene.TemplateAnchors.Count - 1; index >= 0; index--)
        {
            var anchor = scene.TemplateAnchors[index];
            var position = anchor.PositionAuthoringPx;
            if (authoringX >= position.X - half && authoringX < position.X + half
                && authoringY >= position.Y - half && authoringY < position.Y + half)
                return anchor;
        }
        return null;
    }

    private static string NextAnchorId(SceneDocument scene)
    {
        var used = scene.TemplateAnchors
            .Select(static value => value.AnchorId)
            .ToHashSet(StringComparer.Ordinal);
        for (var sequence = 1; sequence < int.MaxValue; sequence++)
        {
            var candidate = $"template_anchor_{sequence:000}";
            if (!used.Contains(candidate)) return candidate;
        }
        throw new SceneMakerDocumentException("Template Anchor ID range is exhausted.");
    }

    private static void RequireInstance(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DocumentValidation.Validate(scene);
        if (scene.SceneKind != SceneKind.Instance)
            throw new SceneMakerDocumentException("Template Anchors belong only to Scene Instances.");
    }
}
