namespace SceneMaker.Core;

public static class SceneMakerSchemas
{
    public const string Scene = "srt.scene_maker_scene";
    public const int SceneVersion = 13;
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

/// <summary>How one point shapes a closed mountain contour.</summary>
public enum MountainPointMode
{
    Linear,
    Aligned,
}

/// <summary>
/// One point of a mountain's closed outline. It has its own persisted record:
/// sharing the water record would make a mountain carry river fields and tie
/// two unrelated document schemas together.
/// </summary>
public sealed record MountainCurvePointDocument
{
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
    public required MountainPointMode Mode { get; init; }
    public required AuthoringPixelOffset HandleInAuthoringPx { get; init; }
    public required AuthoringPixelOffset HandleOutAuthoringPx { get; init; }
}

/// <summary>
/// One level-topped region of raised Terrain. The contour is authored truth and
/// its covered cells are derived; keeping it lets the outline be edited and a
/// later route generator follow the actual mountain instead of a baked raster.
///
/// <para>It holds a shape and a height and no material. What the raised surface
/// is made of is the painted Terrain cell's own Asset, which is why a contour
/// over unpainted ground raises nothing and why repainting underneath changes
/// what the mountain shows.</para>
/// </summary>
public sealed record MountainBodyDocument
{
    public required string MountainBodyId { get; init; }

    /// <summary>The absolute top of the solid column inside the contour.</summary>
    public required decimal ElevationMeters { get; init; }

    public required List<MountainCurvePointDocument> Points { get; init; }
}

/// <summary>How one point shapes an open route centerline.</summary>
public enum RoutePointMode
{
    Linear,
    Aligned,
}

/// <summary>
/// One authored support point of a continuously inclined route surface.
/// Position and handles describe its open Bezier centerline; absolute elevation
/// and full width interpolate over that centerline's arc length.
///
/// <para>The point position is deliberately not tied to either Scene grid. A
/// route is meshed as a continuous band rather than rasterized into Terrain or
/// water cells. Its authored elevation still belongs to the Workspace's
/// vertical quantum.</para>
/// </summary>
public sealed record RouteSurfacePointDocument
{
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
    public required RoutePointMode Mode { get; init; }
    public required AuthoringPixelOffset HandleInAuthoringPx { get; init; }
    public required AuthoringPixelOffset HandleOutAuthoringPx { get; init; }
    public required decimal ElevationMeters { get; init; }
    public required decimal WidthMeters { get; init; }
}

/// <summary>
/// An independently materialized surface along an open route. Unlike a
/// mountain it can cross an unpainted position, so it carries the Terrain Asset
/// whose surface it presents instead of inheriting material from cells below.
/// </summary>
public sealed record RouteSurfaceDocument
{
    public required string RouteSurfaceId { get; init; }
    public required string AssetKey { get; init; }
    public required List<RouteSurfacePointDocument> Points { get; init; }
}

/// <summary>
/// What kind of water a body is. A River is an open curve carrying a corridor
/// whose width is interpolated between its points. A Lake - a closed curve, filled - is the kind
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
/// One authored point of a water body's centerline, in plan and in section.
///
/// <para>The point itself sits on the water grid; its handles do not, because a
/// handle is a curve control rather than a place, and snapping it would quantize
/// the curve's shape.</para>
///
/// <para>The three vertical values are what let one model describe an open
/// river, a cut channel and a tunnel through a mountain without a second rule.
/// They are absolute heights in metres, never offsets from the Terrain: a
/// stored offset would make the water move whenever the ground under it was
/// repainted, and a river's surface does not work that way. The editor can snap
/// a point to the Terrain under it while drawing, but what it writes is the
/// number, not the relationship.</para>
/// </summary>
public sealed record WaterCurvePointDocument
{
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
    public required WaterPointMode Mode { get; init; }
    public required AuthoringPixelOffset HandleInAuthoringPx { get; init; }
    public required AuthoringPixelOffset HandleOutAuthoringPx { get; init; }

    /// <summary>The height of the water surface here, absolute.</summary>
    public required decimal ElevationMeters { get; init; }

    /// <summary>
    /// How deep the water is here. The bed sits this far below
    /// <see cref="ElevationMeters"/>, and the corridor is cut from the bed
    /// upwards - so this is also where the Terrain stops being carved away and
    /// starts being the river's floor.
    /// </summary>
    public required decimal ChannelDepthMeters { get; init; }

    /// <summary>
    /// The headroom the river needs above its surface. Terrain inside the
    /// corridor is removed up to this height; Terrain that reaches higher stays
    /// and becomes a ceiling. A river is open where the ground never gets that
    /// high, and a tunnel where it does - the same number decides both.
    /// </summary>
    public required decimal ClearanceAboveMeters { get; init; }

    /// <summary>
    /// The full width of the corridor here. Width is a point value like the
    /// section above: a river can widen without being split into unrelated
    /// bodies, and the value is interpolated over the same arc length.
    /// </summary>
    public required decimal WidthMeters { get; init; }
}

/// <summary>
/// One authored body of water. The curve is the authored truth: the cells a
/// simulation reads are derived from it by <see cref="WaterGeometry"/> and
/// never stored here, so a river stays reshapeable for as long as it exists.
///
/// <para>For a River the first point is the source and the last is the mouth.
/// That is the whole of its flow direction; nothing else in the document says
/// which way the water runs.</para>
///
/// <para>Its heights are not here but on its points, and they are interpolated
/// between them: one body describes a river that falls, deepens and ducks under
/// a mountain along its length.</para>
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
    /// Closed, editable Terrain regions whose effective cells are derived.
    /// Their absolute tops fold with painted Terrain; they are not voxel data.
    /// </summary>
    public required List<MountainBodyDocument> MountainBodies { get; init; }

    /// <summary>
    /// Independently materialized, continuously inclined bands. They are
    /// Layered-3D surfaces and never fold into the Terrain height field.
    /// </summary>
    public required List<RouteSurfaceDocument> RouteSurfaces { get; init; }

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

    /// <summary>
    /// The initial height suggested when the author names none. Workspace-aware
    /// creation snaps it to that Workspace's elevation quantum before storing it.
    /// </summary>
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
        MountainBodies = [],
        RouteSurfaces = [],
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
