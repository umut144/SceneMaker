using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>Keys the tools react to beyond pointer input.</summary>
public enum ToolKey
{
    Enter,
    Escape,
}

/// <summary>
/// Everything the tools need to know about what is being edited. The editor
/// assembles one of these per input event; the tools never reach for state of
/// their own beyond their pointer bookkeeping.
/// </summary>
public sealed record ToolContext(
    SceneDocument Scene,
    TerrainDisplayCatalog TerrainAssets,
    PropDisplayCatalog PropAssets,
    WorkspaceMetrics Metrics,
    string? SelectedTerrainAssetKey,
    string? SelectedPropAssetKey,
    int TemplateAnchorGroupNumber,
    decimal ElevationMeters,

    /// <summary>
    /// How many Terrain cells wide the Pencil and Line strokes paint or
    /// erase, centred on the cell the author is pointing at. 1 is a single
    /// cell, exactly what every stroke always painted before a brush width
    /// existed.
    /// </summary>
    int TerrainBrushWidthCells = 1,
    double PointerHitRadiusAuthoringPixels = 8.0,

    /// <summary>
    /// Which Assets a bridge is built from here, or the reason there are none.
    /// It is not a selection: the Set says it, so no tool asks and no field
    /// offers it. Null only where a caller never had a Workspace to ask.
    /// </summary>
    BridgeKitResolution? BridgeKit = null);

/// <summary>
/// The single answer a tool gives to an input event. Closed hierarchy: an input
/// either does nothing, says something, or produces exactly one edit.
/// </summary>
public abstract record ToolOutcome
{
    private ToolOutcome() { }

    /// <summary>The input meant nothing for the active tool.</summary>
    public sealed record Idle : ToolOutcome
    {
        public static Idle Instance { get; } = new();
    }

    /// <summary>Only the status line changes.</summary>
    public sealed record Message(string Text) : ToolOutcome;

    /// <summary>
    /// One edit for the editor to apply and record.
    /// </summary>
    /// <param name="Name">Shown when the edit is refused, as "{Name} blocked: …".</param>
    /// <param name="Apply">The pure document transform.</param>
    /// <param name="StrokeKey">
    /// Set for continuous input, so that a whole drag collapses into one undo step.
    /// </param>
    /// <param name="Describe">
    /// Builds the status line from the documents before and after, for messages
    /// that count what actually changed.
    /// </param>
    /// <param name="NoChangeText">
    /// Shown when the transform returns its input unchanged. Null means the
    /// no-op passes silently.
    /// </param>
    public sealed record Edit(
        string Name,
        Func<SceneDocument, SceneDocument> Apply,
        string? StrokeKey = null,
        Func<SceneDocument, SceneDocument, string>? Describe = null,
        string? NoChangeText = null) : ToolOutcome;
}
