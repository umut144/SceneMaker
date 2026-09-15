namespace SceneMaker.Core;

public static class SceneMakerSchemas
{
    public const string Scene = "srt.scene_maker_scene";
    public const int SceneVersion = 22;
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
/// The number this Scene will hand the next Placement of one Asset. It is
/// the whole of how <see cref="PropEditing.Place"/> names a new Placement:
/// read the entry for the Asset, name the Placement with it, write the entry
/// back one higher. There is one entry per Asset that has ever been placed in
/// this Scene, and none for an Asset that never has.
///
/// <para>It only grows. Deleting a Placement does not move its Asset's entry
/// back down - see <see cref="SceneDocument.PropInstanceCounters"/> for why
/// that is the point rather than an oversight.</para>
/// </summary>
public sealed record PropInstanceCounterDocument
{
    public required string AssetKey { get; init; }

    /// <summary>The number the next Placement of this Asset will receive.</summary>
    public required int NextIndex { get; init; }
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

/// <summary>How one point shapes a closed hill contour.</summary>
public enum ElevationRegionPointMode
{
    Linear,
    Aligned,
}

/// <summary>
/// One point of a hill's closed outline. It has its own persisted record:
/// sharing the water record would make a hill carry river fields and tie
/// two unrelated document schemas together.
/// </summary>
public sealed record ElevationRegionPointDocument
{
    public required AuthoringPixelPosition PositionAuthoringPx { get; init; }
    public required ElevationRegionPointMode Mode { get; init; }
    public required AuthoringPixelOffset HandleInAuthoringPx { get; init; }
    public required AuthoringPixelOffset HandleOutAuthoringPx { get; init; }
}

/// <summary>
/// One level-topped region of raised Terrain. The contour is authored truth and
/// its covered cells are derived; keeping it lets the outline be edited and a
/// later route generator follow the actual hill instead of a baked raster.
///
/// <para>It holds a shape and a height and no material. What the raised surface
/// is made of is the painted Terrain cell's own Asset, which is why a contour
/// over unpainted ground raises nothing and why repainting underneath changes
/// what the hill shows.</para>
/// </summary>
public sealed record ElevationRegionDocument
{
    public required string ElevationRegionId { get; init; }

    /// <summary>The absolute top of the solid column inside the contour.</summary>
    public required decimal ElevationMeters { get; init; }

    public required List<ElevationRegionPointDocument> Points { get; init; }
}

/// <summary>How one point shapes an open route centerline.</summary>
public enum RoutePointMode
{
    Linear,
    Aligned,
}

/// <summary>
/// Whether a Path segment is presented independently above its surroundings or
/// excavates the Terrain above its own materialized floor.
/// </summary>
public enum RouteSegmentOperation
{
    Additive,
    Subtractive,
}

/// <summary>
/// One authored support point of a continuously inclined route surface.
/// Position and handles describe its open Bezier centerline; absolute elevation
/// and full width interpolate over that centerline's arc length.
///
/// <para>The point position is deliberately not tied to either Scene grid. A
/// route is meshed as a continuous band rather than rasterized into Terrain or
/// water cells. The first elevation is directly authored on the Workspace's
/// vertical quantum; later absolute elevations may be grade-derived.</para>
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
/// The authored meaning of one interval between neighbouring Path points.
/// Its grade is stored rather than recovered from rounded elevations and
/// flattened arc lengths. A subtractive interval also carries the headroom it
/// asks to remove above the Path floor. The stable ID survives later point
/// editing and lets runtime data refer to this exact interval.
/// </summary>
public sealed record RouteSurfaceSegmentDocument
{
    public required string SegmentId { get; init; }
    public required int GradePercent { get; init; }
    public required RouteSegmentOperation Operation { get; init; }
    public decimal? ClearanceAboveMeters { get; init; }
}

/// <summary>
/// A straight, level span between two ends. Its deck is geometrically a Path
/// with two linear points at one height and is built from the same geometry,
/// but it keeps its own record: a Path carries per-segment grade and an
/// additive/subtractive operation that a bridge has no meaning for, and a
/// bridge carries an anchor Asset and a two-point rule that a Path must not be
/// held to. One shared record would give each kind the other's fields and two
/// mutually exclusive sets of rules.
///
/// <para>Everything derivable is derived. The length follows from the two
/// ends, and the four corner posts follow from the ends together with the
/// width - so changing the width moves them, deleting the bridge deletes them,
/// and moving the bridge takes them along, none of which needs a second thing
/// to be kept in step.</para>
/// </summary>
public sealed record BridgeDocument
{
    public required string BridgeId { get; init; }

    /// <summary>
    /// The Placement Asset a deck is planked with. A deck is not a material
    /// laid over the span: it is a row of planks, so what the bridge names is
    /// the thing that gets repeated - one plank - and how many times is
    /// authored below. The deck is the row; the Asset is the part.
    /// </summary>
    public required string PlankAssetKey { get; init; }

    /// <summary>
    /// The Placement Asset whose named Component stands at each corner. The
    /// bridge points at the Asset rather than at the part, because
    /// <c>anchor_component</c> is what says which part an Asset offers, and
    /// naming a Component here would make a PolyTools composition detail into
    /// authored map identity.
    /// </summary>
    public required string AnchorAssetKey { get; init; }

    public required AuthoringPixelPosition StartAuthoringPx { get; init; }
    public required AuthoringPixelPosition EndAuthoringPx { get; init; }

    /// <summary>The full deck width, perpendicular to start-to-end.</summary>
    public required decimal WidthMeters { get; init; }

    /// <summary>
    /// The deck surface, absolute and on the Workspace elevation quantum. One
    /// number for the whole span: a bridge is level until a case needs a
    /// climbing one, and a Path already authors that case.
    /// </summary>
    public required decimal ElevationMeters { get; init; }

    /// <summary>
    /// How many planks fill the span. Count is authored and depth is derived,
    /// not the other way round: a plank is a repeated part, and a bridge with
    /// a leftover sliver at one end is not a bridge anyone wants to author
    /// around.
    /// </summary>
    public required int PlankCount { get; init; }

    /// <summary>
    /// The empty run between two neighbouring planks. Gaps sit between planks
    /// and never at the ends, so a deck starts and finishes on wood however
    /// wide they are.
    /// </summary>
    public required decimal PlankGapMeters { get; init; }
}

/// <summary>
/// An independently materialized surface along an open route. Unlike a
/// hill it can cross an unpainted position, so it carries the Terrain Asset
/// whose surface it presents instead of inheriting material from cells below.
/// </summary>
public sealed record RouteSurfaceDocument
{
    public required string RouteSurfaceId { get; init; }
    public required string AssetKey { get; init; }
    public required List<RouteSurfacePointDocument> Points { get; init; }
    public required List<RouteSurfaceSegmentDocument> Segments { get; init; }
}

/// <summary>
/// One end of an open water body. A River runs from its source to its mouth and
/// that is the whole of its flow direction, so an end is the only place where
/// one body can be said to meet another.
/// </summary>
public enum WaterEnd
{
    Source,
    Mouth,
}

/// <summary>
/// One named switch a Scene can stand on or off, and where it starts.
///
/// <para>The switch lives on the Scene rather than on a body because it relates
/// several bodies to each other: a fork and the branches leaving it, agreeing
/// about when each of them exists. Where the Scene starts belongs to the switch
/// for the same reason - two bodies on one switch must not be able to disagree
/// about it.</para>
///
/// <para>What makes a switch flip is not authored here and never will be.
/// SceneMaker says that something is switchable and what it is called; when it
/// flips is the consumer's decision. Neither is whether a body that exists is
/// carrying water: a dry bed is weather, the switch is existence, and only the
/// second one is a fact about the map.</para>
/// </summary>
public sealed record SwitchDocument
{
    /// <summary>Author-given and unique in the Scene: a consumer binds triggers to it.</summary>
    public required string Switch { get; init; }

    /// <summary>Where the Scene stands before anything has flipped it.</summary>
    public required bool InitiallyOn { get; init; }
}

/// <summary>
/// An authored claim that one of this body's ends sits on another body: the
/// fork a branch leaves at, or the place a side channel rejoins.
///
/// <para>Only the partner is authored. Both stations are derived at export
/// time, because storing one would make an edit far upstream - which changes
/// arc length - move a fork nobody touched. Authoring the partner is what turns
/// a fork pulled apart into a refused export instead of a junction that quietly
/// stops being reported.</para>
/// </summary>
public sealed record WaterJunctionDocument
{
    public required WaterEnd End { get; init; }
    public required string WaterBodyId { get; init; }
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
/// river, a cut channel and a tunnel through a hill without a second rule.
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
/// a hill along its length.</para>
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
    /// The switch that decides whether this body exists, or null for a body
    /// that is always there. A stretch that is wide while a branch is shut and
    /// narrow while it flows is two bodies on two switches the consumer holds
    /// opposite: alternatives are bodies, because the band is per body and a
    /// second width is therefore a second body.
    /// </summary>
    public required string? Switch { get; init; }

    /// <summary>
    /// The bodies this one's ends sit on, ordered by end and then by id. Empty
    /// for a river that begins and ends on its own.
    /// </summary>
    public required List<WaterJunctionDocument> Junctions { get; init; }

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
    /// The next number this Scene will hand out per Placement Asset, ordered by
    /// asset_key. See <see cref="PropInstanceCounterDocument"/>.
    ///
    /// <para>This is what keeps a Placement's <c>instance_id</c> from being
    /// reused across the Scene's whole life, not merely unique among the
    /// Placements alive right now - the property a consumer that keeps its own
    /// long-lived references by id, such as <c>world01</c>, depends on. Once a
    /// number has named a Placement, deleting that Placement does not let a
    /// later one carry the same number: the counter that produced it never
    /// moves back down.</para>
    /// </summary>
    public required List<PropInstanceCounterDocument> PropInstanceCounters { get; init; }

    /// <summary>
    /// Closed, editable Terrain regions whose effective cells are derived.
    /// Their absolute tops fold with painted Terrain; they are not voxel data.
    /// </summary>
    public required List<ElevationRegionDocument> ElevationRegions { get; init; }

    /// <summary>
    /// Independently materialized, continuously inclined bands. They are
    /// Layered-3D surfaces and never fold into the Terrain height field.
    /// </summary>
    public required List<RouteSurfaceDocument> RouteSurfaces { get; init; }

    /// <summary>
    /// The switches this Scene has, ordered by name. Empty for a Scene nothing
    /// switches.
    /// </summary>
    public required List<SwitchDocument> Switches { get; init; }

    /// <summary>
    /// The authored water of this Scene. Its raster is derived at export time,
    /// which is why nothing here counts cells.
    /// </summary>
    public required List<WaterBodyDocument> WaterBodies { get; init; }

    /// <summary>
    /// Straight level spans. Their decks are independent surfaces like a
    /// Path's, and their corner posts are derived rather than stored.
    /// </summary>
    public required List<BridgeDocument> Bridges { get; init; }

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
        PropInstanceCounters = [],
        ElevationRegions = [],
        RouteSurfaces = [],
        Switches = [],
        WaterBodies = [],
        Bridges = [],
        TemplateDefinition = templateDefinition,
        TemplateAnchors = [],
        DefaultElevationMeters = defaultElevationMeters,
    };
}

public sealed record LoadedWorkspace(string DirectoryPath, string WorkspaceKey);

/// <summary>
/// One named map set inside a Workspace - the World's `config.json` and
/// `imports/polytools/` are shared by every Game built from it, but each
/// Game keeps its own `scenes/`, `templates/` and `exports/`. A Game is a
/// plain directory and nothing else: no file of its own, no schema, no
/// version. A Scene finds out which Game it belongs to from the path it was
/// opened at, the same way it finds out which Workspace it belongs to today -
/// never from a field stored inside the document, which would be a second
/// place that answer could live and disagree with the first.
/// </summary>
public sealed record LoadedGame(LoadedWorkspace Workspace, string GameKey)
{
    public string DirectoryPath => Path.Combine(Workspace.DirectoryPath, GameKey);
    public string ScenesDirectoryPath => Path.Combine(DirectoryPath, GameStore.ScenesDirectoryName);
    public string TemplatesDirectoryPath => Path.Combine(DirectoryPath, GameStore.TemplatesDirectoryName);
}

public sealed record LoadedScene(string FilePath, SceneDocument Document);
