namespace SceneMaker.Core;

public static class SceneMakerSchemas
{
    public const string Scene = "srt.scene_maker_scene";
    public const int SceneVersion = 6;
    public const string CoordinateSpace = "scene_local_bottom_left_y_up";
}

public enum SceneKind
{
    Instance,
    Template,
}

public sealed record SceneSizeCells
{
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public sealed record TerrainCellDocument
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required string AssetKey { get; init; }
}

public sealed record AuthoringPixelPosition
{
    public required int X { get; init; }
    public required int Y { get; init; }
}

/// <summary>
/// One authored instance of a PolyTools Prop. SceneMaker knows exactly one
/// spatial instance kind; the synchronized PolyTools catalog decides which
/// Assets are Props.
/// </summary>
public sealed record PropDocument
{
    public required string InstanceId { get; init; }
    public required string AssetKey { get; init; }
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
}

public sealed record TemplateDefinitionDocument
{
    public required int GroupNumber { get; init; }
    public required AuthoringPixelPosition InsertionAnchorAuthoringPx { get; init; }
}

public sealed record TemplateAnchorDocument
{
    public required string AnchorId { get; init; }
    public required int GroupNumber { get; init; }
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
}

public sealed record SceneDocument
{
    public required string Schema { get; init; }
    public required int Version { get; init; }
    public required string SceneId { get; init; }
    public required SceneKind SceneKind { get; init; }
    public required string CoordinateSpace { get; init; }
    public required SceneSizeCells SizeCells { get; init; }
    public required List<TerrainCellDocument> TerrainCells { get; init; }
    public required List<PropDocument> Props { get; init; }
    public required TemplateDefinitionDocument? TemplateDefinition { get; init; }
    public required List<TemplateAnchorDocument> TemplateAnchors { get; init; }

    /// <summary>An empty Scene Instance.</summary>
    public static SceneDocument CreateInstance(string sceneId, int widthCells, int heightCells) =>
        Empty(sceneId, SceneKind.Instance, widthCells, heightCells, templateDefinition: null);

    /// <summary>
    /// An empty Scene Template. Only a Template carries a group number and an
    /// insertion anchor, which is why the Instance factory has no room for
    /// them: passing them there used to be accepted and silently dropped.
    /// </summary>
    public static SceneDocument CreateTemplate(
        string sceneId,
        int widthCells,
        int heightCells,
        int groupNumber,
        int insertionAnchorX,
        int insertionAnchorY) =>
        Empty(
            sceneId,
            SceneKind.Template,
            widthCells,
            heightCells,
            new TemplateDefinitionDocument
            {
                GroupNumber = groupNumber,
                InsertionAnchorAuthoringPx = new AuthoringPixelPosition
                {
                    X = insertionAnchorX,
                    Y = insertionAnchorY,
                },
            });

    private static SceneDocument Empty(
        string sceneId,
        SceneKind sceneKind,
        int widthCells,
        int heightCells,
        TemplateDefinitionDocument? templateDefinition) => new()
    {
        Schema = SceneMakerSchemas.Scene,
        Version = SceneMakerSchemas.SceneVersion,
        SceneId = sceneId,
        SceneKind = sceneKind,
        CoordinateSpace = SceneMakerSchemas.CoordinateSpace,
        SizeCells = new SceneSizeCells { Width = widthCells, Height = heightCells },
        TerrainCells = [],
        Props = [],
        TemplateDefinition = templateDefinition,
        TemplateAnchors = [],
    };
}

public sealed record LoadedWorkspace(string DirectoryPath, string WorkspaceKey)
{
    public string ScenesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.ScenesDirectoryName);
    public string TemplatesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.TemplatesDirectoryName);
}

public sealed record LoadedScene(string FilePath, SceneDocument Document);
