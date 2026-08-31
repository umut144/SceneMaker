namespace SceneMaker.Core;

public static class SceneMakerSchemas
{
    public const string Scene = "srt.scene_maker_scene";
    public const int SceneVersion = 5;
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

public sealed record PropDocument
{
    public required string InstanceId { get; init; }
    public required string AssetKey { get; init; }
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
}

public sealed record TransitionDocument
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
    public required List<PropDocument> Placements { get; init; }
    public required List<TransitionDocument> Transitions { get; init; }
    public required TemplateDefinitionDocument? TemplateDefinition { get; init; }
    public required List<TemplateAnchorDocument> TemplateAnchors { get; init; }

    public static SceneDocument Create(
        string sceneId,
        int widthCells,
        int heightCells,
        SceneKind sceneKind = SceneKind.Instance,
        int templateGroupNumber = 1,
        int insertionAnchorX = 0,
        int insertionAnchorY = 0) => new()
    {
        Schema = SceneMakerSchemas.Scene,
        Version = SceneMakerSchemas.SceneVersion,
        SceneId = sceneId,
        SceneKind = sceneKind,
        CoordinateSpace = SceneMakerSchemas.CoordinateSpace,
        SizeCells = new SceneSizeCells { Width = widthCells, Height = heightCells },
        TerrainCells = [],
        Placements = [],
        Transitions = [],
        TemplateDefinition = sceneKind == SceneKind.Template
            ? new TemplateDefinitionDocument
            {
                GroupNumber = templateGroupNumber,
                InsertionAnchorAuthoringPx = new AuthoringPixelPosition
                {
                    X = insertionAnchorX,
                    Y = insertionAnchorY,
                },
            }
            : null,
        TemplateAnchors = [],
    };
}

public sealed record LoadedWorkspace(string DirectoryPath, string WorkspaceKey)
{
    public string ScenesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.ScenesDirectoryName);
    public string TemplatesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.TemplatesDirectoryName);
}

public sealed record LoadedScene(string FilePath, SceneDocument Document);
