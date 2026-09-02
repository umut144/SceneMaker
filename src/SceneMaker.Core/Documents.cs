namespace SceneMaker.Core;

public static class SceneMakerSchemas
{
    public const string Scene = "srt.scene_maker_scene";
    public const int SceneVersion = 8;
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

    /// <summary>
    /// The height of this cell's walking surface, in metres, in the same unit
    /// as the rest of the export. What a surface means is the Asset's business
    /// (see <see cref="WorkspaceAssetProfile.Surface"/>); how high it sits is
    /// authored per cell, because one grass Asset covers valley floor and hill
    /// alike.
    /// </summary>
    public required decimal ElevationMeters { get; init; }
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

    /// <summary>
    /// The height this Prop stands at, in metres. Usually the height of the
    /// Terrain under it; a bridge deck is what makes it its own field, because
    /// there the Prop sits above the water it crosses.
    /// </summary>
    public required decimal ElevationMeters { get; init; }
}

/// <summary>
/// An offset in authoring pixels. Distinct from
/// <see cref="AuthoringPixelPosition"/> on purpose: a Bezier handle is a
/// direction and a length away from its point, never a place on the map, and it
/// is the one quantity here that may be negative.
/// </summary>
public sealed record AuthoringPixelOffset
{
    public required int X { get; init; }
    public required int Y { get; init; }

    public static AuthoringPixelOffset Zero { get; } = new() { X = 0, Y = 0 };

    // A method rather than a property: a computed property would be serialized
    // into every document as a field nobody authored.
    public bool IsZero() => X == 0 && Y == 0;
}

/// <summary>
/// What kind of water a body is. A River is an open curve carrying a corridor
/// of a fixed width around it. A Lake - a closed curve, filled - is the kind
/// this will grow, which is why the document says which kind it holds instead
/// of assuming the only one there is today.
/// </summary>
public enum WaterKind
{
    River,
}

/// <summary>
/// How a curve point's two handles relate. <c>Linear</c> forces both to zero,
/// which makes the neighbouring segments straight; <c>Aligned</c> keeps them
/// collinear with independent lengths, so the curve passes through the point
/// without a kink. This mirrors the point modes of the PolyTools Bezier tool,
/// deliberately reduced to the two a river needs.
/// </summary>
public enum WaterPointMode
{
    Linear,
    Aligned,
}

/// <summary>
/// One authored point of a water body's centerline. The point itself sits on
/// the water grid; its handles do not, because a handle is a curve control
/// rather than a place, and snapping it would quantize the curve's shape.
/// </summary>
public sealed record WaterCurvePointDocument
{
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
    public required WaterPointMode Mode { get; init; }
    public required AuthoringPixelOffset HandleInAuthoringPx { get; init; }
    public required AuthoringPixelOffset HandleOutAuthoringPx { get; init; }
}

/// <summary>
/// One authored body of water. The curve is the authored truth: the cells a
/// simulation reads are derived from it by <see cref="WaterGeometry"/> and
/// never stored here, so a river stays reshapeable for as long as it exists.
///
/// <para>For a River the first point is the source and the last is the mouth.
/// That is the whole of its flow direction; nothing else in the document says
/// which way the water runs.</para>
/// </summary>
public sealed record WaterBodyDocument
{
    public required string WaterBodyId { get; init; }
    public required WaterKind WaterKind { get; init; }

    /// <summary>
    /// The Terrain Asset this body is made of. Its surface - "water" - lives on
    /// the Asset like every other surface, so a body cannot contradict the
    /// Asset it names.
    /// </summary>
    public required string AssetKey { get; init; }

    /// <summary>
    /// The full width of the corridor around the centerline, in metres. It
    /// belongs to the body rather than to its points: a river that widens is
    /// authored as a second river starting where the first one ends.
    /// </summary>
    public required decimal WidthMeters { get; init; }

    /// <summary>The height of the water surface, in metres. Flat per body.</summary>
    public required decimal ElevationMeters { get; init; }

    public required List<WaterCurvePointDocument> Points { get; init; }
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

    /// <summary>
    /// The authored water of this Scene. Its raster is derived at export time,
    /// which is why nothing here counts cells.
    /// </summary>
    public required List<WaterBodyDocument> WaterBodies { get; init; }

    public required TemplateDefinitionDocument? TemplateDefinition { get; init; }
    public required List<TemplateAnchorDocument> TemplateAnchors { get; init; }

    /// <summary>
    /// The height a newly authored cell or Prop takes unless the author says
    /// otherwise. Authored intent about this Scene, like its size - a marsh map
    /// starts at 0, a plateau at 12 - not an editor preference, which is why it
    /// survives closing the Scene.
    /// </summary>
    public required decimal DefaultElevationMeters { get; init; }

    /// <summary>The height a Scene starts at when the author names none.</summary>
    public const decimal GroundElevationMeters = 1.0m;

    /// <summary>An empty Scene Instance.</summary>
    public static SceneDocument CreateInstance(
        string sceneId,
        int widthCells,
        int heightCells,
        decimal defaultElevationMeters = GroundElevationMeters) =>
        Empty(
            sceneId,
            SceneKind.Instance,
            widthCells,
            heightCells,
            defaultElevationMeters,
            templateDefinition: null);

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
        int insertionAnchorY,
        decimal defaultElevationMeters = GroundElevationMeters) =>
        Empty(
            sceneId,
            SceneKind.Template,
            widthCells,
            heightCells,
            defaultElevationMeters,
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
        decimal defaultElevationMeters,
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
        WaterBodies = [],
        TemplateDefinition = templateDefinition,
        TemplateAnchors = [],
        DefaultElevationMeters = defaultElevationMeters,
    };
}

public sealed record LoadedWorkspace(string DirectoryPath, string WorkspaceKey)
{
    public string ScenesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.ScenesDirectoryName);
    public string TemplatesDirectoryPath => Path.Combine(DirectoryPath, WorkspaceStore.TemplatesDirectoryName);
}

public sealed record LoadedScene(string FilePath, SceneDocument Document);
