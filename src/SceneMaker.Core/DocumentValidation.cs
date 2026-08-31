using System.Text.RegularExpressions;

namespace SceneMaker.Core;

public sealed class SceneMakerDocumentException : Exception
{
    public SceneMakerDocumentException(string message) : base(message) { }
    public SceneMakerDocumentException(string message, Exception innerException) : base(message, innerException) { }
}

public static partial class DocumentValidation
{
    public static void Validate(SceneDocument? document)
    {
        if (document is null)
            throw new SceneMakerDocumentException("Scene document must not be null.");
        if (document.Schema != SceneMakerSchemas.Scene
            || document.Version != SceneMakerSchemas.SceneVersion)
        {
            throw new SceneMakerDocumentException(
                $"Scene must use {SceneMakerSchemas.Scene} version {SceneMakerSchemas.SceneVersion}.");
        }
        if (document.CoordinateSpace != SceneMakerSchemas.CoordinateSpace)
        {
            throw new SceneMakerDocumentException(
                $"Scene coordinate_space must be '{SceneMakerSchemas.CoordinateSpace}'.");
        }

        ValidateStableId("scene_id", document.SceneId);
        if (!Enum.IsDefined(document.SceneKind))
            throw new SceneMakerDocumentException("Scene requires a supported scene_kind.");
        if (document.SizeCells is null)
            throw new SceneMakerDocumentException("Scene requires size_cells.");
        if (document.SizeCells.Width <= 0 || document.SizeCells.Height <= 0)
            throw new SceneMakerDocumentException("Scene size_cells must be positive on both axes.");

        if (document.TerrainCells is null)
            throw new SceneMakerDocumentException("Scene requires terrain_cells.");

        TerrainCellDocument? previous = null;
        foreach (var cell in document.TerrainCells)
        {
            if (cell.X < 0 || cell.X >= document.SizeCells.Width
                || cell.Y < 0 || cell.Y >= document.SizeCells.Height)
            {
                throw new SceneMakerDocumentException(
                    $"Terrain cell ({cell.X}, {cell.Y}) lies outside size_cells.");
            }
            if (previous is not null
                && (cell.Y < previous.Y || cell.Y == previous.Y && cell.X <= previous.X))
            {
                throw new SceneMakerDocumentException(
                    "Terrain cells must be unique and canonically ordered by Y, then X.");
            }
            previous = cell;
        }
        if (document.Props is null)
            throw new SceneMakerDocumentException("Scene requires props.");

        string? previousInstanceId = null;
        foreach (var prop in document.Props)
        {
            ValidateStableId("prop instance_id", prop.InstanceId);
            if (previousInstanceId is not null
                && string.CompareOrdinal(prop.InstanceId, previousInstanceId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Props must have unique instance IDs in canonical ordinal order.");
            }
            if (prop.PositionAuthoringPx is null)
                throw new SceneMakerDocumentException(
                    $"Prop '{prop.InstanceId}' requires position_authoring_px.");
            previousInstanceId = prop.InstanceId;
        }

        if (document.TemplateAnchors is null)
            throw new SceneMakerDocumentException("Scene requires template_anchors.");

        if (document.SceneKind == SceneKind.Instance)
        {
            if (document.TemplateDefinition is not null)
            {
                throw new SceneMakerDocumentException(
                    "Scene Instance requires template_definition to be null.");
            }
        }
        else
        {
            if (document.TemplateDefinition is null)
                throw new SceneMakerDocumentException("Scene Template requires template_definition.");
            if (document.TemplateAnchors.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own Template Anchors.");
            ValidateGroupNumber("Scene Template", document.TemplateDefinition.GroupNumber);
        }

        string? previousAnchorId = null;
        foreach (var anchor in document.TemplateAnchors)
        {
            ValidateStableId("template anchor_id", anchor.AnchorId);
            if (previousAnchorId is not null
                && string.CompareOrdinal(anchor.AnchorId, previousAnchorId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Template Anchors must have unique IDs in canonical ordinal order.");
            }
            ValidateGroupNumber($"Template Anchor '{anchor.AnchorId}'", anchor.GroupNumber);
            previousAnchorId = anchor.AnchorId;
        }
    }

    public static void ValidateGrid(SceneDocument? document, WorkspaceMetrics metrics)
    {
        Validate(document);
        ArgumentNullException.ThrowIfNull(metrics);
        try
        {
            _ = metrics.SceneWidthAuthoringPixels(document!);
            _ = metrics.SceneHeightAuthoringPixels(document!);
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Scene size_cells exceeds the Workspace authoring coordinate range.", exception);
        }

        if (document!.SceneKind == SceneKind.Template)
        {
            ValidateGridAnchor(
                "Scene Template insertion anchor",
                document.TemplateDefinition!.InsertionAnchorAuthoringPx,
                document.SizeCells,
                metrics);
        }
        foreach (var anchor in document.TemplateAnchors)
        {
            ValidateGridAnchor(
                $"Template Anchor '{anchor.AnchorId}'",
                anchor.PositionAuthoringPx,
                document.SizeCells,
                metrics);
        }
    }

    private static void ValidateGroupNumber(string label, int groupNumber)
    {
        if (groupNumber <= 0)
            throw new SceneMakerDocumentException($"{label} group_number must be positive.");
    }

    private static void ValidateGridAnchor(
        string label,
        AuthoringPixelPosition? position,
        SceneSizeCells size,
        WorkspaceMetrics metrics)
    {
        if (position is null)
            throw new SceneMakerDocumentException($"{label} requires an authoring-pixel position.");
        var step = metrics.AuthoringPixelsPerTerrainCell;
        var width = checked(size.Width * step);
        var height = checked(size.Height * step);
        if (position.X < 0 || position.X > width
            || position.Y < 0 || position.Y > height)
        {
            throw new SceneMakerDocumentException($"{label} lies outside Scene bounds.");
        }
        if (position.X % step != 0 || position.Y % step != 0)
        {
            throw new SceneMakerDocumentException(
                $"{label} must align to the {step}-authoring-pixel WorldGrid.");
        }
    }

    public static void ValidateStableId(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !StableIdRegex().IsMatch(value))
        {
            throw new SceneMakerDocumentException(
                $"{label} must use lowercase snake_case segments separated by dots.");
        }
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
