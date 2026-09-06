using System;
using System.Collections.Generic;
using System.Linq;
using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// The area of a Scene being authored. Terrain, River, Path and ElevationRegion
/// are separate areas because they author different geometry: painted cells,
/// a cut-and-fill curve, an independent route band, and a closed contour.
/// `Landscape` is not one of them - it is a caption the navigation draws around
/// those areas, and an area nobody can be in would have to answer what drawing
/// in it means.
/// </summary>
public enum EditorMode
{
    Terrain,
    River,
    Path,
    ElevationRegion,
    Bridge,
    Props,
    Templates,
}

public enum EditorTool
{
    Selector,
    Pencil,
    Line,
    Fill,
    DrawRiver,
    DrawPath,
    DrawElevationRegion,
    SelectElevationRegion,
    DrawBridge,
    AnchorPlace,
    AnchorMove,
}

/// <summary>
/// One tool in the tool bar. Which Assets it can author is not its business any
/// more: the area decides that, and a tool belongs to the area whose thing it
/// draws.
/// </summary>
public sealed record EditorToolDefinition(
    EditorTool Tool,
    string DisplayName,
    string IconFileName,
    IReadOnlySet<EditorMode> SupportedModes,
    bool ShowInToolBar = true);

public static class EditorToolRegistry
{
    private static readonly IReadOnlyList<EditorToolDefinition> Definitions =
    [
        Define(EditorTool.Selector, "Selector", "select.svg",
            EditorMode.Props, EditorMode.Templates),
        Define(EditorTool.Pencil, "Pencil", "pencil.svg",
            EditorMode.Terrain, EditorMode.Props),
        Define(EditorTool.Line, "Line", "line.svg",
            EditorMode.Terrain, EditorMode.Props),
        Define(EditorTool.Fill, "Fill", "fill.svg", EditorMode.Terrain),
        Define(EditorTool.DrawRiver, "Draw River", "river.svg", EditorMode.River),
        Define(EditorTool.DrawPath, "Draw Path", "line.svg", EditorMode.Path),
        Define(EditorTool.DrawElevationRegion, "Draw Hill", "mountain.svg", EditorMode.ElevationRegion),
        Define(EditorTool.SelectElevationRegion, "Select Hill", "select.svg", EditorMode.ElevationRegion),
        Define(EditorTool.DrawBridge, "Draw Bridge", "line.svg", EditorMode.Bridge),
        Define(EditorTool.AnchorMove, "Move Anchor", "move.svg", EditorMode.Templates),
        Define(EditorTool.AnchorPlace, "Place Anchor", string.Empty,
            false, EditorMode.Templates),
    ];

    private static readonly IReadOnlyDictionary<EditorTool, EditorToolDefinition> ByTool =
        Definitions.ToDictionary(static definition => definition.Tool);

    public static IReadOnlyList<EditorToolDefinition> ToolBarDefinitions { get; } =
        Definitions.Where(static definition => definition.ShowInToolBar).ToList();

    public static EditorToolDefinition Resolve(EditorTool tool) => ByTool[tool];

    public static bool Supports(EditorMode mode, EditorTool tool) =>
        Resolve(tool).SupportedModes.Contains(mode);

    public static EditorTool DefaultTool(EditorMode mode) => mode switch
    {
        EditorMode.Terrain => EditorTool.Pencil,
        EditorMode.River => EditorTool.DrawRiver,
        EditorMode.Path => EditorTool.DrawPath,
        EditorMode.ElevationRegion => EditorTool.DrawElevationRegion,
        EditorMode.Bridge => EditorTool.DrawBridge,
        EditorMode.Props => EditorTool.Pencil,
        EditorMode.Templates => EditorTool.Selector,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// How an area's Terrain Assets are authored, or null for an area that
    /// authors no Terrain at all.
    ///
    /// <para>This is where the tool bar and the Asset bar used to pull in
    /// opposite directions. The Asset used to decide the tool: choosing a river
    /// swapped the Pencil out from under the author, and choosing grass swapped
    /// it back. Now the area decides the Assets - and ElevationRegion decides that it
    /// has none, because a contour raises whatever is painted under it.</para>
    /// </summary>
    public static TerrainAuthoring? TerrainAuthoringFor(EditorMode mode) => mode switch
    {
        EditorMode.Terrain => TerrainAuthoring.Cells,
        EditorMode.River => TerrainAuthoring.Curve,
        // ElevationRegion authors a shape and a height, never a material: the painted
        // Terrain under the contour says what the raised surface is made of, so
        // there is nothing to choose here and no Asset to remember.
        _ => null,
    };

    public static string ModeDisplayName(EditorMode mode) => mode switch
    {
        EditorMode.Terrain => "Terrain",
        EditorMode.River => "River",
        EditorMode.Path => "Path",
        EditorMode.ElevationRegion => "Hill",
        EditorMode.Bridge => "Bridge",
        // The persisted model and the PolyTools adapter still call these Props,
        // but authors place map content here. Keep that implementation detail
        // out of the tool context while the document contract stays stable.
        EditorMode.Props => "Placement",
        EditorMode.Templates => "Template",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static EditorToolDefinition Define(
        EditorTool tool,
        string displayName,
        string iconFileName,
        params EditorMode[] modes) =>
        new(tool, displayName, iconFileName, modes.ToHashSet());

    private static EditorToolDefinition Define(
        EditorTool tool,
        string displayName,
        string iconFileName,
        bool showInToolBar,
        params EditorMode[] modes) =>
        new(tool, displayName, iconFileName, modes.ToHashSet(), showInToolBar);
}

/// <summary>
/// Which Terrain Assets an area offers, and which of them it should show.
///
/// <para>Both are decisions, not drawing, so they live here rather than in the
/// code that builds buttons: the rules that River offers curve-authored Assets
/// while Path may present every Terrain Asset have to be testable, and there
/// are no tests against the Godot application.</para>
/// </summary>
public static class TerrainAreaAssets
{
    /// <summary>
    /// The Assets this area can author, in the catalog's own order. Empty when
    /// the Workspace enables none of that kind - an area with nothing to draw
    /// with is a real state and is said out loud rather than filled with an
    /// Asset it cannot use.
    /// </summary>
    public static IReadOnlyList<TerrainDisplayAsset> Offered(
        EditorMode mode,
        TerrainDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        // A Path is an independent surface over the Terrain, so it may present
        // any Terrain Asset regardless of how that Asset is normally authored:
        // a track over a river is not a river.
        if (mode is EditorMode.Path) return catalog.Assets;
        if (EditorToolRegistry.TerrainAuthoringFor(mode) is not { } authoring) return [];
        return catalog.Assets.Where(asset => asset.Authoring == authoring).ToList();
    }

    /// <summary>
    /// The Asset an area shows on entry: the one it was left with, if that one
    /// still suits it, and otherwise the first it offers. Null when it offers
    /// none.
    /// </summary>
    public static string? Choose(
        EditorMode mode,
        TerrainDisplayCatalog catalog,
        string? remembered)
    {
        var offered = Offered(mode, catalog);
        if (remembered is not null
            && offered.Any(asset => string.Equals(
                asset.AssetKey, remembered, StringComparison.Ordinal)))
        {
            return remembered;
        }
        return offered.Count == 0 ? null : offered[0].AssetKey;
    }

    /// <summary>
    /// The Surface field an area shows, or null for an area that shows none.
    ///
    /// <para>An area that paints cells carries its material as a palette: a bar
    /// of Assets to switch between while drawing. River and Path carry it as one
    /// property of the body being drawn, beside width and heights, which is why
    /// it belongs in that tool's context bar and not in a second navigation
    /// row. River filters by native curve authoring; an independent Path can
    /// present any Terrain Asset. Structures shows no Terrain field at all: a
    /// bridge deck is a row of planks, and a plank is a Placement.</para>
    ///
    /// <para>The field is a choice of <c>asset_key</c>. It never reads or writes
    /// the Asset's runtime <c>surface</c> token, and it does not assume water: a
    /// corridor may carry lava, mud or anything else a Workspace authors as a
    /// curve, and what that means is the runtime's question.</para>
    /// </summary>
    public static TerrainSurfaceField? SurfaceFieldFor(
        EditorMode mode,
        TerrainDisplayCatalog catalog,
        string? remembered)
    {
        if (mode is not EditorMode.Path
            && EditorToolRegistry.TerrainAuthoringFor(mode) != TerrainAuthoring.Curve)
        {
            return null;
        }
        var offered = Offered(mode, catalog);
        // One Asset is not a choice, and none is not a field to choose in - both
        // stay visible so the tool keeps its shape, and neither can be opened.
        return new TerrainSurfaceField(
            offered,
            Choose(mode, catalog, remembered),
            Changeable: offered.Count > 1);
    }
}

/// <summary>
/// What an area's Surface control should show: the Assets to offer, the one
/// that is chosen, and whether there is anything to choose between.
/// </summary>
public sealed record TerrainSurfaceField(
    IReadOnlyList<TerrainDisplayAsset> Options,
    string? SelectedAssetKey,
    bool Changeable);

public sealed class EditorInteractionState
{
    private readonly Dictionary<EditorMode, EditorTool> _activeToolByMode = [];
    private readonly Dictionary<EditorMode, string?> _terrainAssetByMode = [];

    public EditorMode Mode { get; private set; } = EditorMode.Terrain;
    public EditorTool ActiveTool => _activeToolByMode.TryGetValue(Mode, out var tool)
        ? tool
        : EditorToolRegistry.DefaultTool(Mode);
    public bool EraserEnabled { get; private set; }
    public int PropLineOffsetAuthoringPixels { get; private set; }

    /// <summary>
    /// The Terrain Asset the active area is authoring with. Each area keeps its
    /// own, so leaving one and coming back finds the brush where it was left.
    /// Null for an area that offers no Assets at all, which ElevationRegion now does.
    /// </summary>
    public string? SelectedTerrainAssetKey =>
        _terrainAssetByMode.TryGetValue(Mode, out var assetKey) ? assetKey : null;

    public void SelectTerrainAsset(string? assetKey) => _terrainAssetByMode[Mode] = assetKey;

    /// <summary>
    /// How the next curve point's handles behave. Session state, switchable
    /// while a river is being drawn, exactly like the point mode of the
    /// PolyTools Bezier tool: it decides what the next point does, and says
    /// nothing about the points already placed.
    /// </summary>
    public WaterPointMode WaterPointMode { get; private set; } = WaterPointMode.Linear;

    /// <summary>How the next point of an open Path behaves.</summary>
    public RoutePointMode RoutePointMode { get; private set; } = RoutePointMode.Linear;

    /// <summary>How the next point of a closed hill contour behaves.</summary>
    public ElevationRegionPointMode ElevationRegionPointMode { get; private set; } = ElevationRegionPointMode.Linear;

    /// <summary>
    /// The width the next river point is authored with, in metres. A starting value
    /// rather than a rule - the document accepts any positive width, and which
    /// widths a given world uses is that world's business.
    /// </summary>
    public decimal RiverWidthMeters { get; private set; } = DefaultRiverWidthMeters;

    /// <summary>The full deck width of the next bridge, in metres.</summary>
    public decimal BridgeWidthMeters { get; private set; } = BridgeEditing.DefaultWidthMeters;

    /// <summary>
    /// The deck height of the next bridge. Directly authored and therefore on
    /// the Workspace quantum, like every height chosen rather than derived.
    /// </summary>
    public decimal BridgeElevationMeters { get; private set; } =
        SceneDocument.GroundElevationMeters;

    /// <summary>
    /// How many planks the next deck is laid with, and the gap between two of
    /// them. Count is what an author names; the depth of a plank is what falls
    /// out of it once the span is known.
    /// </summary>
    public int BridgePlankCount { get; private set; } = BridgeEditing.DefaultPlankCount;

    public decimal BridgePlankGapMeters { get; private set; } =
        BridgeEditing.DefaultPlankGapMeters;

    public void SetBridgeWidth(decimal widthMeters) => BridgeWidthMeters = widthMeters;

    public void SetBridgeElevation(decimal elevationMeters) =>
        BridgeElevationMeters = elevationMeters;

    public void SetBridgePlankCount(int plankCount) => BridgePlankCount = plankCount;

    public void SetBridgePlankGap(decimal gapMeters) => BridgePlankGapMeters = gapMeters;

    /// <summary>The full width authored onto the next Path point.</summary>
    public decimal PathWidthMeters { get; private set; } = RouteSurfaceEditing.DefaultWidthMeters;

    /// <summary>The grade of the segment arriving at the next Path point.</summary>
    public RouteGradePreset PathGrade { get; private set; } = RouteGradePreset.Level;

    /// <summary>The operation of the segment arriving at the next Path point.</summary>
    public RouteSegmentOperation PathOperation { get; private set; } =
        RouteSegmentOperation.Additive;

    /// <summary>The Terrain headroom removed by the next subtractive segment.</summary>
    public decimal PathClearanceAboveMeters { get; private set; } =
        RouteSurfaceEditing.DefaultClearanceAboveMeters;

    /// <summary>
    /// An explicit starting height for the next Path, or null to copy the
    /// effective Terrain height under its first point. This is session state,
    /// never a stored relationship between Path and Terrain.
    /// </summary>
    public decimal? PathStartElevationOverrideMeters { get; private set; }

    public const decimal DefaultRiverWidthMeters = WaterEditing.DefaultWidthMeters;

    /// <summary>
    /// The water surface the next point takes when it is not snapped to the
    /// Terrain, absolute like every height in the document.
    /// </summary>
    public decimal WaterElevationMeters { get; private set; } =
        SceneDocument.GroundElevationMeters;

    /// <summary>How deep the next point's water is.</summary>
    public decimal WaterChannelDepthMeters { get; private set; } =
        WaterEditing.DefaultChannelDepthMeters;

    /// <summary>The headroom the next point asks for above its surface.</summary>
    public decimal WaterClearanceAboveMeters { get; private set; } =
        WaterEditing.DefaultClearanceAboveMeters;

    /// <summary>
    /// Whether a placed point takes the height of the Terrain under it. On by
    /// default, because a river that follows its valley is the ordinary case
    /// and typing its height for every point would be busywork. It is switched
    /// off to drive a river into a hill, where the whole point is that the
    /// water does not follow the ground.
    ///
    /// <para>It is a way of filling in a number, not a relationship the document
    /// keeps: what gets stored is the height, so later Terrain edits leave the
    /// river where the author put it.</para>
    /// </summary>
    public bool SnapWaterToTerrain { get; private set; } = true;

    public void SelectMode(EditorMode mode)
    {
        Mode = mode;
        if (!_activeToolByMode.ContainsKey(mode))
            _activeToolByMode.Add(mode, EditorToolRegistry.DefaultTool(mode));
    }

    public void SelectTool(EditorTool tool)
    {
        if (!EditorToolRegistry.Supports(Mode, tool))
        {
            throw new InvalidOperationException(
                $"Tool '{tool}' is not supported in mode '{Mode}'.");
        }
        _activeToolByMode[Mode] = tool;
    }

    public void SetEraserEnabled(bool enabled) => EraserEnabled = enabled;

    public void SetPropLineOffset(int authoringPixels)
    {
        if (authoringPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(authoringPixels));
        PropLineOffsetAuthoringPixels = authoringPixels;
    }

    public void SetWaterPointMode(WaterPointMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        WaterPointMode = mode;
    }

    public void SetRoutePointMode(RoutePointMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        RoutePointMode = mode;
    }

    public void SetElevationRegionPointMode(ElevationRegionPointMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ElevationRegionPointMode = mode;
    }

    public void SetRiverWidth(decimal widthMeters)
    {
        if (widthMeters <= 0m) throw new ArgumentOutOfRangeException(nameof(widthMeters));
        RiverWidthMeters = widthMeters;
    }

    public void SetPathWidth(decimal widthMeters)
    {
        if (widthMeters <= 0m) throw new ArgumentOutOfRangeException(nameof(widthMeters));
        PathWidthMeters = widthMeters;
    }

    public void SetPathGrade(RouteGradePreset grade)
    {
        if (!Enum.IsDefined(grade)) throw new ArgumentOutOfRangeException(nameof(grade));
        PathGrade = grade;
    }

    public void SetPathOperation(RouteSegmentOperation operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        PathOperation = operation;
    }

    public void SetPathClearanceAbove(decimal clearanceMeters)
    {
        if (clearanceMeters <= 0m) throw new ArgumentOutOfRangeException(nameof(clearanceMeters));
        PathClearanceAboveMeters = clearanceMeters;
    }

    public void SetPathStartElevationOverride(decimal? elevationMeters) =>
        PathStartElevationOverrideMeters = elevationMeters;

    public void SetWaterElevation(decimal elevationMeters) =>
        WaterElevationMeters = elevationMeters;

    public void SetWaterChannelDepth(decimal depthMeters)
    {
        if (depthMeters <= 0m) throw new ArgumentOutOfRangeException(nameof(depthMeters));
        WaterChannelDepthMeters = depthMeters;
    }

    public void SetWaterClearanceAbove(decimal clearanceMeters)
    {
        if (clearanceMeters < 0m) throw new ArgumentOutOfRangeException(nameof(clearanceMeters));
        WaterClearanceAboveMeters = clearanceMeters;
    }

    public void SetSnapWaterToTerrain(bool enabled) => SnapWaterToTerrain = enabled;
}
