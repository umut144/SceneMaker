using System;
using System.Collections.Generic;
using System.Linq;

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
    AnchorPlace,
    AnchorMove,
}

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
        EditorMode.Props => EditorTool.Pencil,
        EditorMode.Templates => EditorTool.Selector,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

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
}
