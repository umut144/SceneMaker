using System;
using System.Collections.Generic;
using System.Linq;
using SceneMaker.Core;

namespace SceneMaker.Editor;

public enum EditorMode
{
    Terrain,
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
    AnchorPlace,
    AnchorMove,
}

/// <summary>
/// One tool in the tool bar. <paramref name="TerrainAuthoring"/> says which kind
/// of Terrain Asset it authors - painted cells or a drawn curve - and is null
/// for tools that have nothing to do with Terrain. It is what lets the tool bar
/// offer a Pencil for grass and a Bezier for a river without either of them
/// having to be explained away.
/// </summary>
public sealed record EditorToolDefinition(
    EditorTool Tool,
    string DisplayName,
    string IconFileName,
    IReadOnlySet<EditorMode> SupportedModes,
    bool ShowInToolBar = true,
    TerrainAuthoring? TerrainAuthoring = null);

public static class EditorToolRegistry
{
    private static readonly IReadOnlyList<EditorToolDefinition> Definitions =
    [
        Define(EditorTool.Selector, "Selector", "select.svg",
            EditorMode.Props, EditorMode.Templates),
        Define(EditorTool.Pencil, "Pencil", "pencil.svg",
            TerrainAuthoring.Cells, EditorMode.Terrain, EditorMode.Props),
        Define(EditorTool.Line, "Line", "line.svg",
            TerrainAuthoring.Cells, EditorMode.Terrain, EditorMode.Props),
        Define(EditorTool.Fill, "Fill", "fill.svg",
            TerrainAuthoring.Cells, EditorMode.Terrain),
        Define(EditorTool.DrawRiver, "Draw River", "river.svg",
            TerrainAuthoring.Curve, EditorMode.Terrain),
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

    /// <summary>
    /// Whether a mode offers this tool for the Terrain Asset in hand. Outside
    /// Terrain, and while no Asset is chosen, the authoring says nothing and
    /// every supported tool is offered.
    /// </summary>
    public static bool Offers(EditorMode mode, EditorTool tool, TerrainAuthoring? terrainAuthoring)
    {
        if (!Supports(mode, tool)) return false;
        if (mode != EditorMode.Terrain || terrainAuthoring is null) return true;
        return Resolve(tool).TerrainAuthoring == terrainAuthoring;
    }

    public static EditorTool DefaultTool(EditorMode mode) => mode switch
    {
        EditorMode.Terrain => EditorTool.Pencil,
        EditorMode.Props => EditorTool.Pencil,
        EditorMode.Templates => EditorTool.Selector,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The tool to fall back to when the active one is not offered for the
    /// Asset in hand - the first one the tool bar shows for it, so choosing a
    /// river never leaves the author holding a Pencil that cannot draw it.
    /// </summary>
    public static EditorTool DefaultTool(EditorMode mode, TerrainAuthoring? terrainAuthoring)
    {
        if (mode != EditorMode.Terrain || terrainAuthoring is null) return DefaultTool(mode);
        foreach (var definition in ToolBarDefinitions)
        {
            if (Offers(mode, definition.Tool, terrainAuthoring)) return definition.Tool;
        }
        return DefaultTool(mode);
    }

    public static string ModeDisplayName(EditorMode mode) => mode switch
    {
        EditorMode.Terrain => "Terrain",
        EditorMode.Props => "Prop",
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
        TerrainAuthoring terrainAuthoring,
        params EditorMode[] modes) =>
        new(tool, displayName, iconFileName, modes.ToHashSet(), true, terrainAuthoring);

    private static EditorToolDefinition Define(
        EditorTool tool,
        string displayName,
        string iconFileName,
        bool showInToolBar,
        params EditorMode[] modes) =>
        new(tool, displayName, iconFileName, modes.ToHashSet(), showInToolBar);
}

public sealed class EditorInteractionState
{
    private readonly Dictionary<EditorMode, EditorTool> _activeToolByMode = [];

    public EditorMode Mode { get; private set; } = EditorMode.Terrain;
    public EditorTool ActiveTool => _activeToolByMode.TryGetValue(Mode, out var tool)
        ? tool
        : EditorToolRegistry.DefaultTool(Mode);
    public bool EraserEnabled { get; private set; }
    public int PropLineOffsetAuthoringPixels { get; private set; }

    /// <summary>
    /// How the next curve point's handles behave. Session state, switchable
    /// while a river is being drawn, exactly like the point mode of the
    /// PolyTools Bezier tool: it decides what the next point does, and says
    /// nothing about the points already placed.
    /// </summary>
    public WaterPointMode WaterPointMode { get; private set; } = WaterPointMode.Linear;

    /// <summary>
    /// The width the next river is authored with, in metres. A starting value
    /// rather than a rule - the document accepts any positive width, and which
    /// widths a given world uses is that world's business.
    /// </summary>
    public decimal RiverWidthMeters { get; private set; } = DefaultRiverWidthMeters;

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
    /// off to drive a river into a mountain, where the whole point is that the
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

    public void SetRiverWidth(decimal widthMeters)
    {
        if (widthMeters <= 0m) throw new ArgumentOutOfRangeException(nameof(widthMeters));
        RiverWidthMeters = widthMeters;
    }

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
