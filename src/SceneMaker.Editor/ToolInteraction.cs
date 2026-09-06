using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// The editor's tool state machine: which point a line was started from, which
/// Template Anchor is being dragged, what is selected, and what each pointer or
/// key event means for the active mode and tool.
///
/// It answers every input with exactly one <see cref="ToolOutcome"/>, so the
/// editor has a single place to route input through instead of one event and one
/// handler per tool action. Nothing here touches Godot, so all of it is testable.
/// </summary>
public sealed class ToolInteraction
{
    public const string TerrainPaintStroke = "terrain-paint";
    public const string TerrainEraseStroke = "terrain-erase";
    public const string TerrainRegionEraseStroke = "terrain-region-erase";
    public const string PropEraseStroke = "prop-erase";

    private AuthoringPoint? _pointerAuthoring;
    private TerrainCellCoordinate? _pointerCell;
    private AuthoringPoint? _propLineStart;
    private AuthoringPoint? _bridgeStart;
    private AuthoringPoint? _propLineEnd;
    private TerrainCellCoordinate? _terrainLineStart;
    private TerrainCellCoordinate? _terrainLineEnd;
    private bool _terrainLineDragging;
    private readonly List<WaterDraftPoint> _riverDraft = [];
    private WaterDraftPoint? _riverPending;
    private readonly List<GradedRouteDraftPoint> _pathDraft = [];
    private GradedRouteDraftPoint? _pathPending;
    private decimal? _pathStartElevationMeters;
    private readonly List<ElevationRegionDraftPoint> _elevationRegionDraft = [];
    private ElevationRegionDraftPoint? _elevationRegionPending;
    private int? _draggedElevationRegionPointIndex;
    private AuthoringPoint? _draggedElevationRegionPointPosition;
    private ElevationRegionHandleSide? _draggedElevationRegionHandleSide;
    private AuthoringPixelOffset? _draggedElevationRegionHandleOffset;
    private string? _draggedAnchorId;
    private AuthoringPoint? _draggedAnchorPosition;

    // What a bridge drag is holding. An end drag moves one end, a body drag
    // moves both by the same offset - which is the whole difference between
    // reshaping a bridge and carrying it somewhere else.
    private BridgeEnd? _draggedBridgeEnd;
    private bool _draggingBridgeBody;
    private AuthoringPoint? _bridgeDragPointerOrigin;
    private AuthoringPixelPosition? _bridgeDragStartOrigin;
    private AuthoringPixelPosition? _bridgeDragEndOrigin;
    private AuthoringPoint? _bridgeDragPointer;

    /// <summary>Mode, tool and the options that combine with them.</summary>
    public EditorInteractionState State { get; } = new();

    public EditorMode Mode => State.Mode;
    public EditorTool ActiveTool => State.ActiveTool;
    public bool EraserEnabled => State.EraserEnabled;

    public string? SelectedPropInstanceId { get; private set; }
    public string? SelectedTemplateAnchorId { get; private set; }
    public string? SelectedElevationRegionId { get; private set; }
    public int? SelectedElevationRegionPointIndex { get; private set; }
    public string? SelectedBridgeId { get; private set; }

    /// <summary>
    /// Where the selected bridge's two ends currently sit while it is being
    /// dragged, or null when nothing is being dragged. The Canvas asks for this
    /// rather than reading the document, because during a drag the document
    /// still holds where the bridge was.
    /// </summary>
    public (AuthoringPixelPosition Start, AuthoringPixelPosition End)? BridgeDrag
    {
        get
        {
            if (_bridgeDragStartOrigin is not { } start
                || _bridgeDragEndOrigin is not { } end
                || _bridgeDragPointerOrigin is not { } origin
                || _bridgeDragPointer is not { } pointer)
            {
                return null;
            }
            var deltaX = pointer.X - origin.X;
            var deltaY = pointer.Y - origin.Y;
            if (_draggingBridgeBody)
            {
                return (Offset(start, deltaX, deltaY), Offset(end, deltaX, deltaY));
            }
            return _draggedBridgeEnd == BridgeEnd.Start
                ? (Offset(start, deltaX, deltaY), end)
                : (start, Offset(end, deltaX, deltaY));
        }
    }

    private static AuthoringPixelPosition Offset(AuthoringPixelPosition position, int x, int y) =>
        new() { X = position.X + x, Y = position.Y + y };

    public AuthoringPoint? PointerAuthoring => _pointerAuthoring;
    public TerrainCellCoordinate? PointerCell => _pointerCell;
    public AuthoringPoint? PropLineStart => _propLineStart;
    public AuthoringPoint? PropLineEnd => _propLineEnd;

    /// <summary>Set only while a Terrain line is actually being dragged.</summary>
    public TerrainCellCoordinate? TerrainLineStart => _terrainLineDragging ? _terrainLineStart : null;

    /// <summary>The curve points placed so far, source first.</summary>
    public IReadOnlyList<WaterDraftPoint> RiverDraft => _riverDraft;

    /// <summary>
    /// The point being placed right now: fixed by pressing, still gathering its
    /// handle until the button is released.
    /// </summary>
    public WaterDraftPoint? RiverPendingPoint => _riverPending;

    public IReadOnlyList<GradedRouteDraftPoint> PathDraft => _pathDraft;
    public GradedRouteDraftPoint? PathPendingPoint => _pathPending;

    /// <summary>
    /// The end a bridge was started from, or null when none is being drawn. A
    /// bridge has exactly two ends, so one click fixes the first and the next
    /// finishes it - there is no growing draft to keep.
    /// </summary>
    public AuthoringPoint? BridgeStart => _bridgeStart;

    /// <summary>The closed hill contour points placed so far.</summary>
    public IReadOnlyList<ElevationRegionDraftPoint> ElevationRegionDraft => _elevationRegionDraft;

    /// <summary>The hill point currently gathering an aligned handle.</summary>
    public ElevationRegionDraftPoint? ElevationRegionPendingPoint => _elevationRegionPending;

    public int? DraggedElevationRegionPointIndex => _draggedElevationRegionPointIndex;
    public AuthoringPoint? DraggedElevationRegionPointPosition => _draggedElevationRegionPointPosition;
    public ElevationRegionHandleSide? DraggedElevationRegionHandleSide => _draggedElevationRegionHandleSide;

    public string? DraggedAnchorId => _draggedAnchorId;
    public AuthoringPoint? DraggedAnchorPosition => _draggedAnchorPosition;

    /// <summary>
    /// Switches mode and drops whatever the old tool was still holding. What was
    /// dropped is said out loud: a half-drawn contour that vanishes without a
    /// word is indistinguishable from one the editor lost.
    /// </summary>
    public ToolOutcome SelectMode(EditorMode mode)
    {
        var discarded = DiscardedDraftText();
        State.SelectMode(mode);
        ResetTransient();
        return Discarded(discarded);
    }

    /// <summary>
    /// Switches tool and drops the unfinished draft, reporting it. This is also
    /// the path the tool bar takes on its own when a chosen Asset does not suit
    /// the active tool, which is exactly when an unannounced loss would be most
    /// confusing - the author changed Asset, not tool.
    /// </summary>
    public ToolOutcome SelectTool(EditorTool tool)
    {
        var discarded = DiscardedDraftText();
        State.SelectTool(tool);
        ResetTransient();
        return Discarded(discarded);
    }

    /// <summary>
    /// Turning the eraser on ends the curve or contour it interrupts, in both
    /// Landscape areas alike. Keeping the draft alive in the background would
    /// leave the next click meaning something the canvas is no longer showing,
    /// and switching the eraser off again would resurrect a drawing the author
    /// had stopped making. Turning it off starts nothing.
    /// </summary>
    public ToolOutcome SetEraserEnabled(bool enabled)
    {
        string? discarded = null;
        if (enabled && Mode is EditorMode.River or EditorMode.Path or EditorMode.ElevationRegion)
        {
            discarded = DiscardedDraftText();
            ClearRiverDraft();
            ClearPathDraft();
            ClearElevationRegionDraft();
        }
        State.SetEraserEnabled(enabled);
        return Discarded(discarded);
    }

    private static ToolOutcome Discarded(string? text) =>
        text is null ? ToolOutcome.Idle.Instance : new ToolOutcome.Message(text);

    /// <summary>
    /// What the active tool would lose right now, or null when it holds nothing.
    /// </summary>
    private string? DiscardedDraftText()
    {
        if (Mode == EditorMode.ElevationRegion && ActiveTool == EditorTool.DrawElevationRegion)
        {
            var placed = _elevationRegionDraft.Count + (_elevationRegionPending is null ? 0 : 1);
            return placed == 0
                ? null
                : $"The unfinished hill contour of {placed} point{Plural(placed)} was discarded.";
        }
        if (Mode == EditorMode.River && ActiveTool == EditorTool.DrawRiver)
        {
            var placed = _riverDraft.Count + (_riverPending is null ? 0 : 1);
            return placed == 0
                ? null
                : $"The unfinished river of {placed} point{Plural(placed)} was discarded.";
        }
        if (Mode == EditorMode.Path && ActiveTool == EditorTool.DrawPath)
        {
            var placed = _pathDraft.Count + (_pathPending is null ? 0 : 1);
            return placed == 0
                ? null
                : $"The unfinished Path of {placed} point{Plural(placed)} was discarded.";
        }
        if (Mode == EditorMode.Bridge && ActiveTool == EditorTool.DrawBridge
            && _bridgeStart is not null)
        {
            return "The bridge start was discarded.";
        }
        if (Mode == EditorMode.Props && ActiveTool == EditorTool.Line
            && (_propLineStart is not null || _propLineEnd is not null))
        {
            return "The unfinished Placement line was discarded.";
        }
        return null;
    }

    /// <summary>Forgets pointer state and selection, for a new or closed Scene.</summary>
    public void ResetForScene()
    {
        ResetTransient();
        _pointerAuthoring = null;
        _pointerCell = null;
        SelectedPropInstanceId = null;
        SelectedTemplateAnchorId = null;
        SelectedElevationRegionId = null;
        SelectedElevationRegionPointIndex = null;
        SelectedBridgeId = null;
        ClearBridgeDrag();
    }

    public void PointerMoved(AuthoringPoint authoring, TerrainCellCoordinate cell)
    {
        _pointerAuthoring = authoring;
        _pointerCell = cell;
    }

    public void PointerLeft()
    {
        _pointerAuthoring = null;
        _pointerCell = null;
    }

    /// <summary>
    /// Whether the active tool is holding work the author has begun and not
    /// finished - a river being drawn, a Prop line with a fixed start.
    /// </summary>
    public bool HasUnfinishedDraft => Mode switch
    {
        EditorMode.River when ActiveTool == EditorTool.DrawRiver =>
            _riverPending is not null || _riverDraft.Count > 0,
        EditorMode.Path when ActiveTool == EditorTool.DrawPath =>
            _pathPending is not null || _pathDraft.Count > 0,
        EditorMode.ElevationRegion when ActiveTool == EditorTool.DrawElevationRegion =>
            _elevationRegionPending is not null || _elevationRegionDraft.Count > 0,
        EditorMode.Bridge when ActiveTool == EditorTool.DrawBridge => _bridgeStart is not null,
        EditorMode.Props when ActiveTool == EditorTool.Line =>
            _propLineStart is not null || _propLineEnd is not null,
        _ => false,
    };

    /// <summary>
    /// One step back inside that unfinished work, or null when there is none
    /// and the undo belongs to the document history instead.
    ///
    /// <para>Undo means "take back what I just did", and while a river is being
    /// drawn what the author just did was place a point. Reaching past it into
    /// the history would undo an edit they finished minutes ago, leave the
    /// half-drawn river standing, and give no hint that either happened.</para>
    /// </summary>
    public ToolOutcome? UndoDraftStep()
    {
        if (!HasUnfinishedDraft) return null;
        if (Mode == EditorMode.ElevationRegion) return CancelElevationRegionPoint();
        if (Mode == EditorMode.River) return CancelRiverPoint();
        if (Mode == EditorMode.Path) return CancelPathPoint();
        return CancelPropLineStep();
    }

    public void SelectProp(string? instanceId) => SelectedPropInstanceId = instanceId;

    public void SelectTemplateAnchor(string? anchorId) => SelectedTemplateAnchorId = anchorId;

    /// <summary>
    /// Keeps the selection honest after the document changed for any reason —
    /// an edit, an undo, or a fresh load. A Template Anchor that appeared in
    /// exactly one place becomes the selected one, which is how a freshly placed
    /// Anchor ends up selected without the caller having to know its ID.
    /// </summary>
    public void SceneChanged(SceneDocument? before, SceneDocument after)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is not null)
        {
            var appeared = after.TemplateAnchors
                .Where(anchor => before.TemplateAnchors.All(
                    previous => previous.AnchorId != anchor.AnchorId))
                .ToList();
            if (appeared.Count == 1) SelectedTemplateAnchorId = appeared[0].AnchorId;
        }

        if (SelectedPropInstanceId is { } propId
            && after.Props.All(prop => prop.InstanceId != propId))
        {
            SelectedPropInstanceId = null;
        }
        if (SelectedTemplateAnchorId is { } anchorId
            && after.TemplateAnchors.All(anchor => anchor.AnchorId != anchorId))
        {
            SelectedTemplateAnchorId = null;
        }
        if (SelectedElevationRegionId is { } elevationRegionId
            && after.ElevationRegions.All(body => body.ElevationRegionId != elevationRegionId))
        {
            SelectedElevationRegionId = null;
            SelectedElevationRegionPointIndex = null;
        }
        if (SelectedElevationRegionId is { } selectedBodyId
            && SelectedElevationRegionPointIndex is { } selectedPointIndex
            && after.ElevationRegions.First(body => body.ElevationRegionId == selectedBodyId)
                .Points.Count <= selectedPointIndex)
        {
            SelectedElevationRegionPointIndex = null;
        }
        if (_draggedAnchorId is not null) ClearAnchorDrag();
        if (_draggedElevationRegionPointIndex is not null) ClearElevationRegionPointDrag();
        if (_draggedElevationRegionHandleSide is not null) ClearElevationRegionHandleDrag();
    }

    public ToolOutcome PointerPressed(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell)
    {
        ArgumentNullException.ThrowIfNull(context);
        PointerMoved(authoring, cell);
        return Mode switch
        {
            EditorMode.Terrain => TerrainPressed(context, authoring, cell),
            EditorMode.River => RiverPressed(context, authoring, cell),
            EditorMode.Path => PathPressed(context, authoring, cell),
            EditorMode.Bridge => BridgePressed(context, authoring),
            EditorMode.ElevationRegion => ElevationRegionPressed(context, authoring, cell),
            EditorMode.Props => PropPressed(context, authoring),
            EditorMode.Templates => TemplatePressed(context, authoring),
            _ => ToolOutcome.Idle.Instance,
        };
    }

    /// <summary>Called while the primary pointer button is held.</summary>
    public ToolOutcome PointerDragged(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell)
    {
        ArgumentNullException.ThrowIfNull(context);
        PointerMoved(authoring, cell);
        switch (Mode)
        {
            case EditorMode.Terrain when ActiveTool == EditorTool.Line && _terrainLineDragging:
                _terrainLineEnd = cell;
                return ToolOutcome.Idle.Instance;
            case EditorMode.Terrain when ActiveTool == EditorTool.Pencil && EraserEnabled:
                return EraseTerrainCell(cell, TerrainEraseStroke);
            case EditorMode.Terrain when ActiveTool == EditorTool.Pencil
                                         && context.SelectedTerrainAssetKey is not null:
                return PaintTerrainCell(context, cell, TerrainPaintStroke);
            case EditorMode.Terrain when ActiveTool == EditorTool.Fill && EraserEnabled:
                return EraseTerrainRegion(cell, TerrainRegionEraseStroke);
            case EditorMode.River when ActiveTool == EditorTool.DrawRiver && _riverPending is not null:
                return DragRiverHandle(context, authoring);
            case EditorMode.Path when ActiveTool == EditorTool.DrawPath && _pathPending is not null:
                return DragPathHandle(context, authoring);
            case EditorMode.ElevationRegion when ActiveTool == EditorTool.DrawElevationRegion
                                          && _elevationRegionPending is not null:
                return DragElevationRegionHandle(context, authoring);
            case EditorMode.ElevationRegion when ActiveTool == EditorTool.SelectElevationRegion
                                          && _draggedElevationRegionHandleSide is not null:
                return DragSelectedElevationRegionHandle(context, authoring);
            case EditorMode.ElevationRegion when ActiveTool == EditorTool.SelectElevationRegion
                                          && _draggedElevationRegionPointIndex is not null:
                return DragSelectedElevationRegionPoint(context, authoring);
            case EditorMode.Bridge when ActiveTool == EditorTool.SelectBridge
                                        && _bridgeDragPointerOrigin is not null:
                _bridgeDragPointer = authoring;
                return ToolOutcome.Idle.Instance;
            case EditorMode.Props when ActiveTool == EditorTool.Pencil && EraserEnabled:
                return EraseProp(context, authoring, PropEraseStroke);
            case EditorMode.Templates when ActiveTool == EditorTool.AnchorMove
                                           && _draggedAnchorId is not null:
                _draggedAnchorPosition = SnapToGrid(context, authoring);
                return ToolOutcome.Idle.Instance;
            default:
                return ToolOutcome.Idle.Instance;
        }
    }

    public ToolOutcome PointerReleased(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode == EditorMode.Terrain
            && ActiveTool == EditorTool.Line
            && _terrainLineDragging
            && _terrainLineStart is { } start
            && (_pointerCell ?? _terrainLineEnd) is { } end)
        {
            ClearTerrainLine();
            return EraserEnabled ? EraseTerrainLine(start, end) : PaintTerrainLine(context, start, end);
        }

        if (_riverPending is not null) return CommitRiverPoint();
        if (_pathPending is not null) return CommitPathPoint();
        if (_elevationRegionPending is not null) return CommitElevationRegionPoint(context);
        if (_draggedElevationRegionHandleSide is not null) return FinishElevationRegionHandleMove(context);
        if (_draggedElevationRegionPointIndex is not null) return FinishElevationRegionPointMove(context);

        if (_bridgeDragPointerOrigin is not null) return FinishBridgeDrag(context);

        if (_draggedAnchorId is { } anchorId && _draggedAnchorPosition is { } position)
        {
            ClearAnchorDrag();
            return MoveTemplateAnchor(context, anchorId, position);
        }
        return ToolOutcome.Idle.Instance;
    }

    public ToolOutcome KeyPressed(ToolContext context, ToolKey key)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode == EditorMode.River && ActiveTool == EditorTool.DrawRiver)
            return key == ToolKey.Enter ? FinishRiver(context) : CancelRiverPoint();
        if (Mode == EditorMode.Path && ActiveTool == EditorTool.DrawPath)
            return key == ToolKey.Enter ? FinishPath(context) : CancelPathPoint();
        if (Mode == EditorMode.Bridge && ActiveTool == EditorTool.DrawBridge)
        {
            // There is nothing for Enter to finish: the second click already
            // does. Escape gives the fixed end back.
            if (key == ToolKey.Enter || _bridgeStart is null) return ToolOutcome.Idle.Instance;
            _bridgeStart = null;
            return new ToolOutcome.Message("The bridge start was discarded.");
        }
        if (Mode == EditorMode.ElevationRegion && ActiveTool == EditorTool.DrawElevationRegion)
            return key == ToolKey.Enter ? FinishElevationRegion(context) : CancelElevationRegionPoint();
        if (Mode == EditorMode.Bridge && ActiveTool == EditorTool.SelectBridge)
        {
            if (key == ToolKey.Enter) return ToolOutcome.Idle.Instance;
            if (SelectedBridgeId is null) return ToolOutcome.Idle.Instance;
            SelectedBridgeId = null;
            ClearBridgeDrag();
            return new ToolOutcome.Message("Bridge selection cleared.");
        }
        if (Mode == EditorMode.ElevationRegion && ActiveTool == EditorTool.SelectElevationRegion)
        {
            if (key == ToolKey.Enter) return ToolOutcome.Idle.Instance;
            SelectedElevationRegionId = null;
            SelectedElevationRegionPointIndex = null;
            ClearElevationRegionPointDrag();
            ClearElevationRegionHandleDrag();
            return new ToolOutcome.Message("Hill selection cleared.");
        }
        if (Mode != EditorMode.Props || ActiveTool != EditorTool.Line)
            return ToolOutcome.Idle.Instance;

        if (key == ToolKey.Escape) return CancelPropLineStep();

        if (_propLineStart is not { } start || _propLineEnd is not { } end)
        {
            return new ToolOutcome.Message(_propLineStart is null
                ? "Line Draw: choose a start point before confirming."
                : "Line Draw: choose and lock an end point before confirming.");
        }
        if (context.SelectedPropAssetKey is not { } assetKey)
            return new ToolOutcome.Message("Line Draw: choose a Placement asset first.");

        if (EraserEnabled)
        {
            ClearPropLine();
            return ErasePropLine(context, assetKey, start, end);
        }

        var preview = PropPreviews(context, assetKey, start, end);
        var blocked = ToolPreviewBuilder.CountOf(preview, PropPreviewKind.Blocked);
        if (blocked > 0)
        {
            return new ToolOutcome.Message(
                $"Line Draw blocked: {blocked} of {preview.Count} Placement previews are invalid.");
        }

        ClearPropLine();
        var offset = State.PropLineOffsetAuthoringPixels;
        return new ToolOutcome.Edit(
            "Line Draw",
            document => PropEditing.PlaceLine(
                document,
                context.PropAssets,
                start.X,
                start.Y,
                end.X,
                end.Y,
                assetKey,
                context.ElevationMeters,
                offset),
            Describe: (before, after) =>
            {
                var added = after.Props.Count - before.Props.Count;
                return $"Line Draw placed {added} Placement{Plural(added)} with exact non-overlapping footprints.";
            });
    }

    private ToolOutcome TerrainPressed(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell) => ActiveTool switch
    {
        EditorTool.Pencil when EraserEnabled => EraseTerrainCell(cell, TerrainEraseStroke),
        EditorTool.Pencil when context.SelectedTerrainAssetKey is not null =>
            PaintTerrainCell(context, cell, TerrainPaintStroke),
        EditorTool.Fill when EraserEnabled => EraseTerrainRegion(cell, TerrainRegionEraseStroke),
        EditorTool.Fill when context.SelectedTerrainAssetKey is not null => FillTerrainRegion(context, cell),
        EditorTool.Line => BeginTerrainLine(cell),
        _ => ToolOutcome.Idle.Instance,
    };

    private ToolOutcome RiverPressed(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell) => ActiveTool switch
    {
        EditorTool.DrawRiver when EraserEnabled => EraseWaterBody(context, authoring),
        EditorTool.DrawRiver => BeginRiverPoint(context, authoring, cell),
        _ => ToolOutcome.Idle.Instance,
    };

    private ToolOutcome PathPressed(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell) => ActiveTool switch
    {
        EditorTool.DrawPath when EraserEnabled => ErasePath(context, authoring),
        EditorTool.DrawPath => BeginPathPoint(context, authoring, cell),
        _ => ToolOutcome.Idle.Instance,
    };

    private ToolOutcome BridgePressed(
        ToolContext context,
        AuthoringPoint authoring) => ActiveTool switch
    {
        EditorTool.SelectBridge => SelectOrGrabBridge(context, authoring),
        EditorTool.DrawBridge when EraserEnabled => EraseBridge(context, authoring),
        EditorTool.DrawBridge => BeginOrFinishBridge(context, authoring),
        _ => ToolOutcome.Idle.Instance,
    };

    /// <summary>
    /// The first click fixes an end, the second builds the bridge. A bridge is
    /// two ends and nothing else, so there is no Enter to press and nothing
    /// half-authored to leave behind.
    /// </summary>
    private ToolOutcome BeginOrFinishBridge(ToolContext context, AuthoringPoint point)
    {
        if (context.Scene.SceneKind != SceneKind.Instance)
            return new ToolOutcome.Message("Bridge: a Scene Template cannot carry bridges.");
        if (context.BridgeKit is not BridgeKitResolution.Resolved(var kit))
        {
            return new ToolOutcome.Message(context.BridgeKit is BridgeKitResolution.Unavailable why
                ? $"Bridge: {why.Reason}"
                : "Bridge: this Workspace names no PolyTools Set to build bridges from.");
        }
        var plankAssetKey = kit.PlankAssetKey;
        var anchorAssetKey = kit.AnchorAssetKey;
        if (!IsInsideScene(context, point))
            return new ToolOutcome.Message("Bridge: an end has to sit inside the Scene.");

        if (_bridgeStart is not { } start)
        {
            _bridgeStart = point;
            return new ToolOutcome.Message("Bridge: start set. Click the other end.");
        }

        var width = State.BridgeWidthMeters;
        var elevation = State.BridgeElevationMeters;
        var plankCount = State.BridgePlankCount;
        var plankGap = State.BridgePlankGapMeters;
        var validation = BridgeEditing.ValidateCandidate(
            context.Scene,
            context.PropAssets,
            start.X,
            start.Y,
            point.X,
            point.Y,
            plankAssetKey,
            anchorAssetKey,
            width,
            elevation,
            plankCount,
            plankGap);
        if (!validation.IsValid)
            return new ToolOutcome.Message($"Bridge: {validation.Reason}");

        _bridgeStart = null;
        return new ToolOutcome.Edit(
            "Draw Bridge",
            document => BridgeEditing.Place(
                document,
                context.PropAssets,
                start.X,
                start.Y,
                point.X,
                point.Y,
                plankAssetKey,
                anchorAssetKey,
                width,
                elevation,
                plankCount,
                plankGap),
            Describe: (_, after) => FormattableString.Invariant(
                $"Bridge '{after.Bridges[^1].BridgeId}' spans {BridgeLengthMeters(context, start, point):0.##} m on {plankCount} planks and set four posts."));
    }

    /// <summary>
    /// One press does all three: grabbing an end of the selected bridge,
    /// grabbing its deck to carry the whole thing, or choosing a different
    /// bridge. Ends are tested before decks so a corner stays reachable where
    /// two bridges meet, and pressing empty ground clears the selection - the
    /// same answer every other selection tool gives.
    /// </summary>
    private ToolOutcome SelectOrGrabBridge(ToolContext context, AuthoringPoint point)
    {
        if (SelectedBridgeId is { } selectedId
            && BridgeEditing.FindEndAt(
                    context.Scene,
                    point.X,
                    point.Y,
                    context.PointerHitRadiusAuthoringPixels) is { } hit
            && string.Equals(hit.Bridge.BridgeId, selectedId, StringComparison.Ordinal))
        {
            BeginBridgeDrag(hit.Bridge, point, end: hit.End);
            return ToolOutcome.Idle.Instance;
        }

        var found = BridgeEditing.FindAt(context.Scene, context.Metrics, point.X, point.Y);
        if (found is null)
        {
            if (SelectedBridgeId is null) return ToolOutcome.Idle.Instance;
            SelectedBridgeId = null;
            ClearBridgeDrag();
            return new ToolOutcome.Message("Bridge selection cleared.");
        }

        var wasSelected = string.Equals(found.BridgeId, SelectedBridgeId, StringComparison.Ordinal);
        SelectedBridgeId = found.BridgeId;
        if (wasSelected)
        {
            BeginBridgeDrag(found, point, end: null);
            return ToolOutcome.Idle.Instance;
        }
        return new ToolOutcome.Message(
            FormattableString.Invariant(
                $"Selected '{found.BridgeId}': {found.PlankCount} planks, {found.WidthMeters:0.##} m wide at {found.ElevationMeters:0.###} m."));
    }

    private void BeginBridgeDrag(BridgeDocument bridge, AuthoringPoint point, BridgeEnd? end)
    {
        _draggedBridgeEnd = end;
        _draggingBridgeBody = end is null;
        _bridgeDragPointerOrigin = point;
        _bridgeDragPointer = point;
        _bridgeDragStartOrigin = bridge.StartAuthoringPx;
        _bridgeDragEndOrigin = bridge.EndAuthoringPx;
    }

    private void ClearBridgeDrag()
    {
        _draggedBridgeEnd = null;
        _draggingBridgeBody = false;
        _bridgeDragPointerOrigin = null;
        _bridgeDragPointer = null;
        _bridgeDragStartOrigin = null;
        _bridgeDragEndOrigin = null;
    }

    /// <summary>
    /// Commits what the drag was showing, or says why it cannot be taken. A
    /// drag that moved nothing is not an edit and leaves no undo step behind.
    /// </summary>
    private ToolOutcome FinishBridgeDrag(ToolContext context)
    {
        var moved = BridgeDrag;
        var bridgeId = SelectedBridgeId;
        var wholeBridge = _draggingBridgeBody;
        ClearBridgeDrag();
        if (bridgeId is null || moved is not { } ends) return ToolOutcome.Idle.Instance;

        var stored = context.Scene.Bridges.FirstOrDefault(bridge =>
            string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal));
        if (stored is null)
        {
            SelectedBridgeId = null;
            return new ToolOutcome.Message("The selected bridge no longer exists.");
        }
        if (stored.StartAuthoringPx == ends.Start && stored.EndAuthoringPx == ends.End)
            return ToolOutcome.Idle.Instance;

        var validation = BridgeEditing.ValidateReshape(
            context.Scene,
            context.PropAssets,
            bridgeId,
            ends.Start.X,
            ends.Start.Y,
            ends.End.X,
            ends.End.Y,
            stored.WidthMeters,
            stored.ElevationMeters,
            stored.PlankCount,
            stored.PlankGapMeters);
        if (!validation.IsValid)
            return new ToolOutcome.Message($"Bridge: {validation.Reason}");

        var name = wholeBridge ? "Move Bridge" : "Move Bridge End";
        return new ToolOutcome.Edit(
            name,
            document => BridgeEditing.Reshape(
                document,
                context.PropAssets,
                bridgeId,
                ends.Start.X,
                ends.Start.Y,
                ends.End.X,
                ends.End.Y,
                stored.WidthMeters,
                stored.ElevationMeters,
                stored.PlankCount,
                stored.PlankGapMeters),
            Describe: (_, after) => FormattableString.Invariant(
                $"{name}: '{bridgeId}' now spans {BridgeGeometry.LengthMeters(context.Metrics, BridgeEditing.Require(after, bridgeId)):0.##} m."));
    }

    /// <summary>
    /// Applies the context bar's numbers to the selected bridge. The fields are
    /// defaults for the next bridge while nothing is selected and edits of that
    /// one while something is, which is why changing a width puts the bridge
    /// back through the same validation a fresh one goes through.
    /// </summary>
    public ToolOutcome ReshapeSelectedBridge(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode != EditorMode.Bridge || ActiveTool != EditorTool.SelectBridge
            || SelectedBridgeId is not { } bridgeId)
        {
            return ToolOutcome.Idle.Instance;
        }
        var stored = context.Scene.Bridges.FirstOrDefault(bridge =>
            string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal));
        if (stored is null)
        {
            SelectedBridgeId = null;
            return ToolOutcome.Idle.Instance;
        }

        var width = State.BridgeWidthMeters;
        var elevation = State.BridgeElevationMeters;
        var plankCount = State.BridgePlankCount;
        var plankGap = State.BridgePlankGapMeters;
        if (stored.WidthMeters == width && stored.ElevationMeters == elevation
            && stored.PlankCount == plankCount && stored.PlankGapMeters == plankGap)
        {
            return ToolOutcome.Idle.Instance;
        }

        var validation = BridgeEditing.ValidateReshape(
            context.Scene,
            context.PropAssets,
            bridgeId,
            stored.StartAuthoringPx.X,
            stored.StartAuthoringPx.Y,
            stored.EndAuthoringPx.X,
            stored.EndAuthoringPx.Y,
            width,
            elevation,
            plankCount,
            plankGap);
        if (!validation.IsValid)
            return new ToolOutcome.Message($"Bridge: {validation.Reason}");

        return new ToolOutcome.Edit(
            "Reshape Bridge",
            document => BridgeEditing.Reshape(
                document,
                context.PropAssets,
                bridgeId,
                stored.StartAuthoringPx.X,
                stored.StartAuthoringPx.Y,
                stored.EndAuthoringPx.X,
                stored.EndAuthoringPx.Y,
                width,
                elevation,
                plankCount,
                plankGap),
            Describe: (_, _) => FormattableString.Invariant(
                $"Bridge '{bridgeId}': {plankCount} planks, {width:0.##} m wide at {elevation:0.###} m."));
    }

    /// <summary>What the Canvas draws for the selected bridge, dragged or not.</summary>
    public BridgeSelectionPreview BridgeSelection(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToolPreviewBuilder.BuildBridgeSelection(
            context.Scene,
            context.PropAssets,
            ActiveTool,
            SelectedBridgeId,
            BridgeDrag);
    }

    private static double BridgeLengthMeters(
        ToolContext context,
        AuthoringPoint start,
        AuthoringPoint end)
    {
        var pixelsPerMeter = (double)context.Metrics.AuthoringPixelsPerMeter;
        var deltaX = (end.X - start.X) / pixelsPerMeter;
        var deltaY = (end.Y - start.Y) / pixelsPerMeter;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    /// <summary>
    /// Erases the whole bridge under the pointer, posts included. There is no
    /// way to erase a post on its own, because there is no post on its own.
    /// </summary>
    private static ToolOutcome EraseBridge(ToolContext context, AuthoringPoint point)
    {
        var bridge = BridgeEditing.FindAt(
            context.Scene,
            context.Metrics,
            point.X,
            point.Y);
        if (bridge is null) return new ToolOutcome.Message("Bridge Eraser: no bridge here.");
        var bridgeId = bridge.BridgeId;
        return new ToolOutcome.Edit(
            "Bridge Eraser",
            document => BridgeEditing.Remove(document, bridgeId),
            Describe: (_, _) => $"Removed bridge '{bridgeId}' and its posts.");
    }

    private ToolOutcome ElevationRegionPressed(
        ToolContext context,
        AuthoringPoint authoring,
        TerrainCellCoordinate cell) => ActiveTool switch
    {
        EditorTool.DrawElevationRegion when EraserEnabled => EraseElevationRegion(context, cell),
        EditorTool.DrawElevationRegion => BeginElevationRegionPoint(context, authoring),
        EditorTool.SelectElevationRegion => SelectOrBeginElevationRegionPoint(context, authoring, cell),
        _ => ToolOutcome.Idle.Instance,
    };

    private ToolOutcome BeginElevationRegionPoint(ToolContext context, AuthoringPoint point)
    {
        if (context.Scene.SceneKind != SceneKind.Instance)
            return new ToolOutcome.Message("Hill: a Scene Template cannot carry Hills.");

        var snapped = new AuthoringPoint(
            context.Metrics.SnapToTerrainGrid(point.X),
            context.Metrics.SnapToTerrainGrid(point.Y));
        if (!IsInsideScene(context, snapped))
            return new ToolOutcome.Message("Hill: a contour point has to sit inside the Scene.");
        if (_elevationRegionDraft.Count > 0
            && _elevationRegionDraft[^1].X == snapped.X
            && _elevationRegionDraft[^1].Y == snapped.Y)
        {
            return new ToolOutcome.Message(
                "Hill: that is the point you just placed; choose a different one.");
        }

        _elevationRegionPending = new ElevationRegionDraftPoint(
            snapped.X, snapped.Y, State.ElevationRegionPointMode);
        var ordinal = _elevationRegionDraft.Count + 1;
        return new ToolOutcome.Message(State.ElevationRegionPointMode == ElevationRegionPointMode.Linear
            ? $"Hill: point {ordinal} at ({snapped.X}, {snapped.Y}) · top {context.ElevationMeters:0.###} m."
            : $"Hill: point {ordinal} at ({snapped.X}, {snapped.Y}) · top {context.ElevationMeters:0.###} m; drag to pull its handle.");
    }

    private ToolOutcome DragElevationRegionHandle(ToolContext context, AuthoringPoint point)
    {
        if (_elevationRegionPending is not { } pending) return ToolOutcome.Idle.Instance;
        if (pending.Mode == ElevationRegionPointMode.Linear) return ToolOutcome.Idle.Instance;

        var deltaX = point.X - pending.X;
        var deltaY = point.Y - pending.Y;
        var threshold = context.Metrics.AuthoringPixelsPerTerrainCell / 2;
        var pulled = deltaX * deltaX + deltaY * deltaY >= threshold * threshold;
        _elevationRegionPending = pending with
        {
            DraggedHandleOut = pulled
                ? new AuthoringPixelOffset { X = deltaX, Y = deltaY }
                : null,
        };
        return ToolOutcome.Idle.Instance;
    }

    private ToolOutcome CommitElevationRegionPoint(ToolContext context)
    {
        if (_elevationRegionPending is not { } pending) return ToolOutcome.Idle.Instance;
        _elevationRegionPending = null;
        _elevationRegionDraft.Add(pending);
        if (_elevationRegionDraft.Count < ToolPreviewBuilder.MinimumElevationRegionDraftPoints)
        {
            return new ToolOutcome.Message(
                $"Hill: {_elevationRegionDraft.Count} point{Plural(_elevationRegionDraft.Count)} placed; a contour needs at least three.");
        }

        // Said before Enter rather than after it: a contour that would lift
        // nothing is worth authoring, and the author should know that is what
        // they are about to author.
        var preview = ElevationRegionPreview(context);
        var closes = $"Hill: {_elevationRegionDraft.Count} points. Enter closes it, Escape takes the last one back.";
        return new ToolOutcome.Message(
            preview is { Kind: ElevationRegionDraftKind.Ready, RaisedCells.Count: 0 }
                ? $"{closes} It {ToolPreviewBuilder.RaisesNoTerrain}"
                : closes);
    }

    /// <summary>
    /// The draft the canvas is showing, asked the same way the canvas asks it.
    /// Enter reads this and nothing else, so a yellow contour cannot be refused
    /// and a red one cannot slip through.
    /// </summary>
    public ElevationRegionDraftPreview ElevationRegionPreview(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToolPreviewBuilder.BuildElevationRegionDraft(
            context.Scene,
            context.Metrics,
            ActiveTool,
            _elevationRegionDraft,
            _elevationRegionPending,
            context.ElevationMeters);
    }

    /// <summary>
    /// The selected contour or the reshape currently following the pointer.
    /// Both the canvas and pointer release read this one answer.
    /// </summary>
    public ElevationRegionSelectionPreview ElevationRegionSelectionPreview(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToolPreviewBuilder.BuildElevationRegionSelection(
            context.Scene,
            ActiveTool,
            SelectedElevationRegionId,
            ElevationRegionCandidatePoints(context));
    }

    private ToolOutcome SelectOrBeginElevationRegionPoint(
        ToolContext context,
        AuthoringPoint point,
        TerrainCellCoordinate cell)
    {
        var handleHit = FindElevationRegionHandle(context, point);
        if (handleHit is not null)
        {
            SelectedElevationRegionPointIndex = handleHit.PointIndex;
            ClearElevationRegionPointDrag();
            _draggedElevationRegionHandleSide = handleHit.Side;
            _draggedElevationRegionHandleOffset = handleHit.Offset;
            return new ToolOutcome.Message(
                $"Selected '{SelectedElevationRegionId}' · point {handleHit.PointIndex + 1} "
                + $"{handleHit.Side.ToString().ToLowerInvariant()} handle; drag to shape it.");
        }

        var pointHit = FindElevationRegionPoint(context, point);
        if (pointHit is not null)
        {
            SelectedElevationRegionId = pointHit.Body.ElevationRegionId;
            SelectedElevationRegionPointIndex = pointHit.PointIndex;
            ClearElevationRegionHandleDrag();
            _draggedElevationRegionPointIndex = pointHit.PointIndex;
            var position = pointHit.Body.Points[pointHit.PointIndex].PositionAuthoringPx;
            _draggedElevationRegionPointPosition = new AuthoringPoint(position.X, position.Y);
            return new ToolOutcome.Message(
                $"Selected '{pointHit.Body.ElevationRegionId}' · point {pointHit.PointIndex + 1}; drag to move it.");
        }

        var body = ElevationRegionEditing.FindAtCell(context.Scene, context.Metrics, cell);
        SelectedElevationRegionId = body?.ElevationRegionId;
        SelectedElevationRegionPointIndex = null;
        ClearElevationRegionPointDrag();
        ClearElevationRegionHandleDrag();
        return new ToolOutcome.Message(body is null
            ? "No Hill selected."
            : $"Selected '{body.ElevationRegionId}' · top {body.ElevationMeters:0.###} m · {body.Points.Count} points.");
    }

    private ToolOutcome DragSelectedElevationRegionPoint(ToolContext context, AuthoringPoint point)
    {
        var snapped = SnapToGrid(context, point);
        if (IsInsideScene(context, snapped)) _draggedElevationRegionPointPosition = snapped;
        return ToolOutcome.Idle.Instance;
    }

    private ToolOutcome DragSelectedElevationRegionHandle(ToolContext context, AuthoringPoint point)
    {
        if (SelectedElevationRegionId is not { } bodyId
            || SelectedElevationRegionPointIndex is not { } pointIndex)
        {
            return ToolOutcome.Idle.Instance;
        }
        var body = context.Scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (body is null || pointIndex >= body.Points.Count) return ToolOutcome.Idle.Instance;
        var anchor = body.Points[pointIndex].PositionAuthoringPx;
        _draggedElevationRegionHandleOffset = new AuthoringPixelOffset
        {
            X = point.X - anchor.X,
            Y = point.Y - anchor.Y,
        };
        return ToolOutcome.Idle.Instance;
    }

    private ToolOutcome FinishElevationRegionPointMove(ToolContext context)
    {
        if (SelectedElevationRegionId is not { } bodyId
            || _draggedElevationRegionPointIndex is not { } pointIndex
            || _draggedElevationRegionPointPosition is not { } position)
        {
            ClearElevationRegionPointDrag();
            return ToolOutcome.Idle.Instance;
        }

        var preview = ElevationRegionSelectionPreview(context);
        ClearElevationRegionPointDrag();
        return FinishElevationRegionReshape(
            context,
            preview,
            "Move Hill Point",
            $"Moved point {pointIndex + 1} of '{bodyId}' to ({position.X}, {position.Y}).");
    }

    private ToolOutcome FinishElevationRegionHandleMove(ToolContext context)
    {
        if (SelectedElevationRegionId is not { } bodyId
            || SelectedElevationRegionPointIndex is not { } pointIndex
            || _draggedElevationRegionHandleSide is not { } side)
        {
            ClearElevationRegionHandleDrag();
            return ToolOutcome.Idle.Instance;
        }

        var preview = ElevationRegionSelectionPreview(context);
        ClearElevationRegionHandleDrag();
        return FinishElevationRegionReshape(
            context,
            preview,
            "Move Hill Handle",
            $"Moved {side.ToString().ToLowerInvariant()} handle of point {pointIndex + 1} "
                + $"on '{bodyId}'.");
    }

    private static ToolOutcome FinishElevationRegionReshape(
        ToolContext context,
        ElevationRegionSelectionPreview preview,
        string editName,
        string description)
    {
        if (preview.Kind == ElevationRegionDraftKind.Blocked || preview.Body is null)
            return new ToolOutcome.Message($"{editName} blocked: {preview.Explanation}");
        var bodyId = preview.Body.ElevationRegionId;
        var stored = context.Scene.ElevationRegions.FirstOrDefault(body => string.Equals(
            body.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (stored is null) return new ToolOutcome.Message($"{editName}: body no longer exists.");
        if (stored.Points.SequenceEqual(preview.Body.Points)) return ToolOutcome.Idle.Instance;

        var points = preview.Body.Points;
        return new ToolOutcome.Edit(
            editName,
            document => ElevationRegionEditing.Reshape(document, bodyId, points),
            Describe: (_, _) => description);
    }

    /// <summary>Changes the selected authored point through the normal edit path.</summary>
    public ToolOutcome SetSelectedElevationRegionPointMode(
        ToolContext context,
        ElevationRegionPointMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode != EditorMode.ElevationRegion || ActiveTool != EditorTool.SelectElevationRegion
            || SelectedElevationRegionId is not { } bodyId
            || SelectedElevationRegionPointIndex is not { } pointIndex)
        {
            return new ToolOutcome.Message("Select a Hill point before changing its mode.");
        }

        var body = context.Scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (body is null || pointIndex >= body.Points.Count)
            return new ToolOutcome.Message("The selected Hill point no longer exists.");
        if (body.Points[pointIndex].Mode == mode) return ToolOutcome.Idle.Instance;

        var points = ElevationRegionEditing.WithPointMode(body.Points, pointIndex, mode);
        var reshape = ElevationRegionEditing.TryReshape(context.Scene, bodyId, points);
        if (reshape.Body is null)
        {
            return new ToolOutcome.Message(
                $"Change Hill Point blocked: {reshape.Reason}");
        }
        return new ToolOutcome.Edit(
            "Change Hill Point",
            document => ElevationRegionEditing.Reshape(document, bodyId, points),
            Describe: (_, _) =>
                $"Changed point {pointIndex + 1} of '{bodyId}' to {mode}.");
    }

    /// <summary>Changes the selected body's absolute top as one document edit.</summary>
    public ToolOutcome SetSelectedElevationRegionElevation(
        ToolContext context,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode != EditorMode.ElevationRegion || ActiveTool != EditorTool.SelectElevationRegion
            || SelectedElevationRegionId is not { } bodyId)
        {
            return new ToolOutcome.Message("Select a Hill before changing its height.");
        }

        var body = context.Scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (body is null)
            return new ToolOutcome.Message("The selected Hill no longer exists.");
        if (body.ElevationMeters == elevationMeters) return ToolOutcome.Idle.Instance;

        try
        {
            _ = ElevationRegionEditing.SetElevation(
                context.Scene, context.Metrics, bodyId, elevationMeters);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new ToolOutcome.Message($"Change Hill Height blocked: {exception.Message}");
        }
        return new ToolOutcome.Edit(
            "Change Hill Height",
            document => ElevationRegionEditing.SetElevation(
                document, context.Metrics, bodyId, elevationMeters),
            Describe: (_, _) =>
                $"Changed '{bodyId}' top to {elevationMeters:0.###} m.");
    }

    private IReadOnlyList<ElevationRegionPointDocument>? ElevationRegionCandidatePoints(
        ToolContext context)
    {
        if (SelectedElevationRegionId is not { } bodyId) return null;
        var body = context.Scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (body is null) return null;

        if (_draggedElevationRegionPointIndex is { } pointIndex
            && _draggedElevationRegionPointPosition is { } position)
        {
            var points = body.Points.ToList();
            points[pointIndex] = points[pointIndex] with
            {
                PositionAuthoringPx = new AuthoringPixelPosition
                {
                    X = position.X,
                    Y = position.Y,
                },
            };
            return points;
        }
        if (_draggedElevationRegionHandleSide is { } side
            && _draggedElevationRegionHandleOffset is { } offset
            && SelectedElevationRegionPointIndex is { } selectedPointIndex)
        {
            return ElevationRegionEditing.WithMovedHandle(
                body.Points, selectedPointIndex, side, offset);
        }
        return null;
    }

    private ElevationRegionPointHit? FindElevationRegionPoint(ToolContext context, AuthoringPoint point)
    {
        var maximumDistanceSquared = context.PointerHitRadiusAuthoringPixels
            * context.PointerHitRadiusAuthoringPixels;
        return context.Scene.ElevationRegions
            .SelectMany(body => body.Points.Select((curvePoint, index) =>
            {
                var deltaX = (double)curvePoint.PositionAuthoringPx.X - point.X;
                var deltaY = (double)curvePoint.PositionAuthoringPx.Y - point.Y;
                return new ElevationRegionPointHit(body, index, deltaX * deltaX + deltaY * deltaY);
            }))
            .Where(hit => hit.DistanceSquared <= maximumDistanceSquared)
            .OrderByDescending(hit => string.Equals(
                hit.Body.ElevationRegionId, SelectedElevationRegionId, StringComparison.Ordinal))
            .ThenBy(static hit => hit.DistanceSquared)
            .ThenByDescending(static hit => hit.Body.ElevationMeters)
            .ThenBy(static hit => hit.Body.ElevationRegionId, StringComparer.Ordinal)
            .ThenBy(static hit => hit.PointIndex)
            .FirstOrDefault();
    }

    private ElevationRegionHandleHit? FindElevationRegionHandle(ToolContext context, AuthoringPoint point)
    {
        if (SelectedElevationRegionId is not { } bodyId) return null;
        var body = context.Scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (body is null) return null;

        var maximumDistanceSquared = context.PointerHitRadiusAuthoringPixels
            * context.PointerHitRadiusAuthoringPixels;
        List<ElevationRegionHandleHit> hits = [];
        for (var index = 0; index < body.Points.Count; index++)
        {
            var curvePoint = body.Points[index];
            if (curvePoint.Mode != ElevationRegionPointMode.Aligned) continue;
            AddHandleHit(hits, point, curvePoint, index, ElevationRegionHandleSide.In);
            AddHandleHit(hits, point, curvePoint, index, ElevationRegionHandleSide.Out);
        }
        return hits
            .Where(hit => hit.DistanceSquared <= maximumDistanceSquared)
            .OrderByDescending(hit => hit.PointIndex == SelectedElevationRegionPointIndex)
            .ThenBy(static hit => hit.DistanceSquared)
            .ThenBy(static hit => hit.PointIndex)
            .ThenBy(static hit => hit.Side)
            .FirstOrDefault();
    }

    private static void AddHandleHit(
        ICollection<ElevationRegionHandleHit> hits,
        AuthoringPoint pointer,
        ElevationRegionPointDocument point,
        int pointIndex,
        ElevationRegionHandleSide side)
    {
        var offset = side == ElevationRegionHandleSide.In
            ? point.HandleInAuthoringPx
            : point.HandleOutAuthoringPx;
        if (offset.IsZero()) return;
        var deltaX = (double)point.PositionAuthoringPx.X + offset.X - pointer.X;
        var deltaY = (double)point.PositionAuthoringPx.Y + offset.Y - pointer.Y;
        hits.Add(new ElevationRegionHandleHit(
            pointIndex, side, offset, deltaX * deltaX + deltaY * deltaY));
    }

    private sealed record ElevationRegionPointHit(
        ElevationRegionDocument Body,
        int PointIndex,
        double DistanceSquared);

    private sealed record ElevationRegionHandleHit(
        int PointIndex,
        ElevationRegionHandleSide Side,
        AuthoringPixelOffset Offset,
        double DistanceSquared);

    private ToolOutcome FinishElevationRegion(ToolContext context)
    {
        if (_elevationRegionPending is { } pending)
        {
            _elevationRegionDraft.Add(pending);
            _elevationRegionPending = null;
        }

        var preview = ElevationRegionPreview(context);
        if (preview.Kind == ElevationRegionDraftKind.Incomplete)
            return new ToolOutcome.Message($"Hill: {preview.Explanation}");
        if (preview.Kind == ElevationRegionDraftKind.Blocked)
            return new ToolOutcome.Message($"Hill blocked: {preview.Explanation}");

        var points = preview.Curve;
        var placed = _elevationRegionDraft.Count;
        var elevation = context.ElevationMeters;
        var raisesNothing = preview.RaisedCells.Count == 0;
        _elevationRegionDraft.Clear();
        return new ToolOutcome.Edit(
            "Hill",
            document => ElevationRegionEditing.Place(document, context.Metrics, points, elevation),
            Describe: (before, after) =>
            {
                var added = after.ElevationRegions.FirstOrDefault(body =>
                    before.ElevationRegions.All(previous =>
                        previous.ElevationRegionId != body.ElevationRegionId));
                var authored =
                    $"Authored {added?.ElevationRegionId ?? "hill"} from {placed} points · top {elevation:0.###} m";
                // Saved either way. The note says the body is doing nothing yet,
                // not that anything went wrong.
                return raisesNothing
                    ? $"{authored}; {ToolPreviewBuilder.RaisesNoTerrain}"
                    : $"{authored}.";
            });
    }

    private ToolOutcome CancelElevationRegionPoint()
    {
        if (_elevationRegionPending is not null)
        {
            _elevationRegionPending = null;
            return new ToolOutcome.Message("Hill: point released.");
        }
        if (_elevationRegionDraft.Count == 0)
            return new ToolOutcome.Message("Hill: nothing to take back.");

        _elevationRegionDraft.RemoveAt(_elevationRegionDraft.Count - 1);
        return new ToolOutcome.Message(_elevationRegionDraft.Count == 0
            ? "Hill: draft cleared."
            : $"Hill: {_elevationRegionDraft.Count} point{Plural(_elevationRegionDraft.Count)} left.");
    }

    /// <summary>
    /// Removes the body the hover preview highlighted. Both ask
    /// <see cref="ElevationRegionEditing.FindAtCell"/> about the cell under the
    /// pointer, so what lights up and what disappears are one answer. What the
    /// body covers and what it is holding up are two different sets, and only
    /// the second one changes height when it goes.
    /// </summary>
    private static ToolOutcome EraseElevationRegion(ToolContext context, TerrainCellCoordinate cell)
    {
        var body = ElevationRegionEditing.FindAtCell(context.Scene, context.Metrics, cell);
        if (body is null) return new ToolOutcome.Message("Hill Eraser: no hill here.");
        var bodyId = body.ElevationRegionId;
        return new ToolOutcome.Edit(
            "Hill Eraser",
            document => ElevationRegionEditing.Remove(document, bodyId),
            Describe: (_, _) => $"Removed Hill '{bodyId}'.");
    }

    private ToolOutcome PropPressed(ToolContext context, AuthoringPoint point)
    {
        if (context.SelectedPropAssetKey is not { } assetKey) return ToolOutcome.Idle.Instance;
        switch (ActiveTool)
        {
            case EditorTool.Selector:
                var found = PropEditing.FindAt(context.Scene, context.PropAssets, point.X, point.Y);
                SelectedPropInstanceId = found?.InstanceId;
                return new ToolOutcome.Message(found is null
                    ? "No Placement selected."
                    : $"Selected '{found.InstanceId}' · anchor ({found.PositionAuthoringPx.X}, {found.PositionAuthoringPx.Y}).");

            case EditorTool.Pencil when EraserEnabled:
                return EraseProp(context, point, strokeKey: null);

            case EditorTool.Pencil:
                var validation = PropEditing.ValidateCandidate(
                    context.Scene, context.PropAssets, point.X, point.Y, assetKey);
                if (!validation.IsValid)
                    return new ToolOutcome.Message($"Pencil Draw blocked: {validation.Reason}");
                var assetName = context.PropAssets.Resolve(assetKey).Name;
                return new ToolOutcome.Edit(
                    "Pencil Draw",
                    document => PropEditing.Place(
                        document, context.PropAssets, point.X, point.Y, assetKey, context.ElevationMeters),
                    Describe: (_, _) =>
                        $"Placed {assetName} anchor at ({point.X}, {point.Y}) authoring px.");

            case EditorTool.Line:
                return ContinuePropLine(context, assetKey, point);

            default:
                return ToolOutcome.Idle.Instance;
        }
    }

    private ToolOutcome TemplatePressed(ToolContext context, AuthoringPoint point)
    {
        if (context.Scene.SceneKind != SceneKind.Instance) return ToolOutcome.Idle.Instance;
        switch (ActiveTool)
        {
            case EditorTool.AnchorPlace:
                var group = context.TemplateAnchorGroupNumber;
                return new ToolOutcome.Edit(
                    "Place Anchor",
                    document => TemplateEditing.PlaceAnchor(
                        document, context.Metrics, point.X, point.Y, group),
                    Describe: (before, after) =>
                    {
                        var added = after.TemplateAnchors.FirstOrDefault(anchor =>
                            before.TemplateAnchors.All(previous => previous.AnchorId != anchor.AnchorId));
                        return added is null
                            ? "Placed Template Anchor."
                            : $"Placed Template Anchor '{added.AnchorId}' for group {added.GroupNumber} at ({added.PositionAuthoringPx.X}, {added.PositionAuthoringPx.Y}).";
                    });

            case EditorTool.Selector:
                var anchor = TemplateEditing.FindAnchorAt(context.Scene, point.X, point.Y);
                SelectedTemplateAnchorId = anchor?.AnchorId;
                return new ToolOutcome.Message(anchor is null
                    ? "No Template Anchor selected."
                    : $"Selected '{anchor.AnchorId}' · group {anchor.GroupNumber} · position ({anchor.PositionAuthoringPx.X}, {anchor.PositionAuthoringPx.Y}).");

            case EditorTool.AnchorMove:
                var dragged = TemplateEditing.FindAnchorAt(context.Scene, point.X, point.Y);
                if (dragged is null)
                    return new ToolOutcome.Message("Move Anchor: no Template Anchor selected.");
                SelectedTemplateAnchorId = dragged.AnchorId;
                _draggedAnchorId = dragged.AnchorId;
                _draggedAnchorPosition = SnapToGrid(context, point);
                return new ToolOutcome.Message(
                    $"Move Anchor: dragging '{dragged.AnchorId}'.");

            default:
                return ToolOutcome.Idle.Instance;
        }
    }

    /// <summary>
    /// Fixes the next curve point where the pointer went down, snapped to the
    /// water grid. Nothing is authored yet: the point is only committed on
    /// release, because what happens in between is the handle being pulled out
    /// of it.
    /// </summary>
    private ToolOutcome BeginRiverPoint(
        ToolContext context,
        AuthoringPoint point,
        TerrainCellCoordinate cell)
    {
        if (context.Scene.SceneKind != SceneKind.Instance)
            return new ToolOutcome.Message("River: a Scene Template cannot carry water.");
        if (context.SelectedTerrainAssetKey is null)
            return new ToolOutcome.Message("River: choose a Terrain asset first.");

        var snapped = new AuthoringPoint(
            context.Metrics.SnapToWaterGrid(point.X),
            context.Metrics.SnapToWaterGrid(point.Y));
        if (!IsInsideScene(context, snapped))
            return new ToolOutcome.Message("River: a curve point has to sit inside the Scene.");
        if (_riverDraft.Count > 0
            && _riverDraft[^1].X == snapped.X
            && _riverDraft[^1].Y == snapped.Y)
        {
            return new ToolOutcome.Message(
                "River: that is the point you just placed; choose a different one.");
        }

        var (elevation, fromTerrain) = WaterElevationFor(context, cell);
        _riverPending = new WaterDraftPoint(
            snapped.X,
            snapped.Y,
            State.WaterPointMode,
            elevation,
            State.WaterChannelDepthMeters,
            State.WaterClearanceAboveMeters,
            State.RiverWidthMeters);

        var ordinal = _riverDraft.Count + 1;
        var bed = elevation - State.WaterChannelDepthMeters;
        var cutTop = elevation + State.WaterClearanceAboveMeters;
        var height = fromTerrain
            ? $"water {elevation:0.###} m, snapped to the Terrain"
            : $"water {elevation:0.###} m";
        height += $" · bed {bed:0.###} m · cut top {cutTop:0.###} m";
        return new ToolOutcome.Message(State.WaterPointMode == WaterPointMode.Linear
            ? $"River: point {ordinal} at ({snapped.X}, {snapped.Y}) · {height}."
            : $"River: point {ordinal} at ({snapped.X}, {snapped.Y}) · {height}; drag to pull its handle.");
    }

    /// <summary>
    /// The water surface a new point takes. Snapped to the Terrain under it
    /// when the author asked for that and there is Terrain there; otherwise the
    /// previous point's height, so an unpainted patch does not put a step into
    /// the river; and otherwise what the context bar says.
    /// </summary>
    private (decimal Elevation, bool Snapped) WaterElevationFor(
        ToolContext context,
        TerrainCellCoordinate cell)
    {
        if (State.SnapWaterToTerrain)
        {
            if (TerrainEditing.ElevationAt(context.Scene, cell.X, cell.Y) is { } top)
                return (top, true);
            return (
                _riverDraft.Count > 0
                    ? _riverDraft[^1].ElevationMeters
                    : State.WaterElevationMeters,
                false);
        }
        return (State.WaterElevationMeters, false);
    }

    /// <summary>
    /// The handle follows the pointer exactly and is not snapped: a handle is a
    /// curve control rather than a place, and quantizing it would quantize the
    /// shape of the curve. A drag shorter than half a water cell counts as none,
    /// so a click that shifts by a pixel still places a plain point.
    /// </summary>
    private ToolOutcome DragRiverHandle(ToolContext context, AuthoringPoint point)
    {
        if (_riverPending is not { } pending) return ToolOutcome.Idle.Instance;
        if (pending.Mode == WaterPointMode.Linear) return ToolOutcome.Idle.Instance;

        var deltaX = point.X - pending.X;
        var deltaY = point.Y - pending.Y;
        var threshold = context.Metrics.AuthoringPixelsPerWaterCell / 2;
        var pulled = deltaX * deltaX + deltaY * deltaY >= threshold * threshold;
        _riverPending = pending with
        {
            DraggedHandleOut = pulled
                ? new AuthoringPixelOffset { X = deltaX, Y = deltaY }
                : null,
        };
        return ToolOutcome.Idle.Instance;
    }

    private ToolOutcome CommitRiverPoint()
    {
        if (_riverPending is not { } pending) return ToolOutcome.Idle.Instance;
        _riverPending = null;
        _riverDraft.Add(pending);
        return new ToolOutcome.Message(_riverDraft.Count < 2
            ? "River: source placed. Keep placing points; Enter finishes at the mouth."
            : $"River: {_riverDraft.Count} points. Enter finishes it, Escape takes the last one back.");
    }

    /// <summary>
    /// Turns the draft into one authored river. The whole curve is a single
    /// edit, so one undo takes back the river rather than its last point.
    /// </summary>
    private ToolOutcome FinishRiver(ToolContext context)
    {
        if (context.SelectedTerrainAssetKey is not { } assetKey)
            return new ToolOutcome.Message("River: choose a Terrain asset first.");
        if (_riverPending is null && _riverDraft.Count < 2)
        {
            return new ToolOutcome.Message(
                "River: a river needs a source and a mouth; place at least two points.");
        }

        if (_riverPending is { } pending)
        {
            _riverDraft.Add(pending);
            _riverPending = null;
        }
        if (_riverDraft.Count < 2)
        {
            return new ToolOutcome.Message(
                "River: a river needs a source and a mouth; place at least two points.");
        }

        var points = WaterEditing.ResolveCurve(_riverDraft);
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        var placed = _riverDraft.Count;
        var source = points[0].ElevationMeters;
        var mouth = points[^1].ElevationMeters;
        _riverDraft.Clear();
        return new ToolOutcome.Edit(
            "River",
            document => WaterEditing.PlaceRiver(
                document, context.TerrainAssets, points, assetKey),
            Describe: (before, after) =>
            {
                var added = after.WaterBodies.FirstOrDefault(body =>
                    before.WaterBodies.All(previous => previous.WaterBodyId != body.WaterBodyId));
                var name = added?.WaterBodyId ?? "river";
                var sourceWidth = points[0].WidthMeters;
                var mouthWidth = points[^1].WidthMeters;
                var width = sourceWidth == mouthWidth
                    ? $"{sourceWidth:0.###} m wide"
                    : $"{sourceWidth:0.###} to {mouthWidth:0.###} m wide";
                return $"Authored {name} from {placed} points · {assetName} · {width} · water {source:0.###} m to {mouth:0.###} m.";
            });
    }

    /// <summary>
    /// Escape steps back through the draft: first the point still being placed,
    /// then the points already placed, one at a time.
    /// </summary>
    private ToolOutcome CancelRiverPoint()
    {
        if (_riverPending is not null)
        {
            _riverPending = null;
            return new ToolOutcome.Message("River: point released.");
        }
        if (_riverDraft.Count == 0)
            return new ToolOutcome.Message("River: nothing to take back.");

        _riverDraft.RemoveAt(_riverDraft.Count - 1);
        return new ToolOutcome.Message(_riverDraft.Count == 0
            ? "River: draft cleared."
            : $"River: {_riverDraft.Count} point{Plural(_riverDraft.Count)} left.");
    }

    private ToolOutcome BeginPathPoint(
        ToolContext context,
        AuthoringPoint point,
        TerrainCellCoordinate cell)
    {
        if (context.Scene.SceneKind != SceneKind.Instance)
            return new ToolOutcome.Message("Path: a Scene Template cannot carry Paths.");
        if (context.SelectedTerrainAssetKey is null)
            return new ToolOutcome.Message("Path: choose a Surface first.");
        if (!IsInsideScene(context, point))
            return new ToolOutcome.Message("Path: a point has to sit inside the Scene.");

        if (_pathDraft.Count == 0 && _pathPending is null)
        {
            _pathStartElevationMeters = State.PathStartElevationOverrideMeters
                ?? ElevationRegionGeometry.EffectiveElevationAt(
                    context.Scene,
                    context.Metrics,
                    cell.X,
                    cell.Y);
            if (_pathStartElevationMeters is not { } start)
            {
                return new ToolOutcome.Message(
                    "Path: there is no Terrain here; set a Start elevation first.");
            }
            if (!context.Metrics.IsElevationAligned(start))
            {
                _pathStartElevationMeters = null;
                return new ToolOutcome.Message(
                    FormattableString.Invariant(
                        $"Path: the Start elevation must align to {context.Metrics.ElevationQuantumMeters:0.############################} m."));
            }
        }

        var grade = _pathDraft.Count == 0
            ? RouteGradePreset.Level
            : State.PathGrade;
        var operation = _pathDraft.Count == 0
            ? RouteSegmentOperation.Additive
            : State.PathOperation;
        decimal? clearance = operation == RouteSegmentOperation.Subtractive
            ? State.PathClearanceAboveMeters
            : null;
        _pathPending = new GradedRouteDraftPoint(
            point.X,
            point.Y,
            State.RoutePointMode,
            State.PathWidthMeters,
            grade,
            OperationFromPrevious: operation,
            ClearanceAboveMetersFromPrevious: clearance);
        var ordinal = _pathDraft.Count + 1;
        var startElevation = _pathStartElevationMeters!.Value;
        var firstPoint = _pathDraft.Count == 0;
        var gradeText = firstPoint
            ? $"next grade {GradeText(State.PathGrade)}"
            : $"grade {GradeText(grade)}";
        var operationText = firstPoint
            ? $"next segment {OperationText(
                State.PathOperation,
                State.PathOperation == RouteSegmentOperation.Subtractive
                    ? State.PathClearanceAboveMeters
                    : null)}"
            : OperationText(operation, clearance);
        return new ToolOutcome.Message(State.RoutePointMode == RoutePointMode.Linear
            ? $"Path: point {ordinal} at ({point.X}, {point.Y}) · "
                + $"start {startElevation:0.###} m · {gradeText} · {operationText} · "
                + $"width {State.PathWidthMeters:0.###} m."
            : $"Path: point {ordinal} at ({point.X}, {point.Y}) · "
                + $"start {startElevation:0.###} m · {gradeText} · {operationText} · "
                + $"width {State.PathWidthMeters:0.###} m; drag to pull its handle.");
    }

    private ToolOutcome DragPathHandle(ToolContext context, AuthoringPoint point)
    {
        if (_pathPending is not { } pending) return ToolOutcome.Idle.Instance;
        if (pending.Mode == RoutePointMode.Linear) return ToolOutcome.Idle.Instance;

        var deltaX = point.X - pending.X;
        var deltaY = point.Y - pending.Y;
        var threshold = context.Metrics.AuthoringPixelsPerTerrainCell / 2;
        var pulled = deltaX * deltaX + deltaY * deltaY >= threshold * threshold;
        _pathPending = pending with
        {
            DraggedHandleOut = pulled
                ? new AuthoringPixelOffset { X = deltaX, Y = deltaY }
                : null,
        };
        return ToolOutcome.Idle.Instance;
    }

    private ToolOutcome CommitPathPoint()
    {
        if (_pathPending is not { } pending) return ToolOutcome.Idle.Instance;
        _pathPending = null;
        if (_pathDraft.Count > 0
            && _pathDraft[^1].X == pending.X
            && _pathDraft[^1].Y == pending.Y
            && pending.DraggedHandleOut is null)
        {
            return new ToolOutcome.Message(
                "Path: a repeated point needs a pulled handle to create horizontal run.");
        }
        _pathDraft.Add(pending);
        return new ToolOutcome.Message(_pathDraft.Count < 2
            ? "Path: start placed. Keep placing points; Enter finishes it."
            : $"Path: {_pathDraft.Count} points. Enter finishes it, Escape takes the last one back.");
    }

    public RouteDraftPreview PathPreview(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToolPreviewBuilder.BuildRouteDraft(
            context.Metrics,
            ActiveTool,
            _pathStartElevationMeters,
            _pathDraft,
            _pathPending);
    }

    private ToolOutcome FinishPath(ToolContext context)
    {
        if (context.SelectedTerrainAssetKey is not { } assetKey)
            return new ToolOutcome.Message("Path: choose a Surface first.");
        if (_pathPending is { } pending)
        {
            _pathPending = null;
            if (_pathDraft.Count > 0
                && _pathDraft[^1].X == pending.X
                && _pathDraft[^1].Y == pending.Y
                && pending.DraggedHandleOut is null)
            {
                return new ToolOutcome.Message(
                    "Path: a repeated point needs a pulled handle to create horizontal run.");
            }
            _pathDraft.Add(pending);
        }

        var preview = PathPreview(context);
        if (preview.Surface is null)
            return new ToolOutcome.Message($"Path: {preview.Explanation ?? "place at least two points"}.");

        var points = preview.Curve;
        var segments = _pathDraft
            .Skip(1)
            .Select(static point => new RouteSegmentAuthoring(
                point.GradeFromPrevious,
                point.OperationFromPrevious,
                point.ClearanceAboveMetersFromPrevious))
            .ToArray();
        var placed = _pathDraft.Count;
        var start = points[0].ElevationMeters;
        var end = points[^1].ElevationMeters;
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        ClearPathDraft();
        return new ToolOutcome.Edit(
            "Path",
            document => RouteSurfaceEditing.Place(
                document,
                context.TerrainAssets,
                context.Metrics,
                points,
                segments,
                assetKey),
            Describe: (before, after) =>
            {
                var added = after.RouteSurfaces.FirstOrDefault(route =>
                    before.RouteSurfaces.All(previous =>
                        previous.RouteSurfaceId != route.RouteSurfaceId));
                var name = added?.RouteSurfaceId ?? "route";
                var subtractive = added?.Segments.Count(static segment =>
                    segment.Operation == RouteSegmentOperation.Subtractive) ?? 0;
                var operationSummary = subtractive == 0
                    ? "additive"
                    : $"{subtractive} subtractive segment{Plural(subtractive)}";
                return $"Authored {name} from {placed} points · {assetName} · {operationSummary} · surface {start:0.###} m to {end:0.###} m.";
            });
    }

    private ToolOutcome CancelPathPoint()
    {
        if (_pathPending is not null)
        {
            _pathPending = null;
            if (_pathDraft.Count == 0) _pathStartElevationMeters = null;
            return new ToolOutcome.Message("Path: point released.");
        }
        if (_pathDraft.Count == 0)
            return new ToolOutcome.Message("Path: nothing to take back.");

        _pathDraft.RemoveAt(_pathDraft.Count - 1);
        if (_pathDraft.Count == 0) _pathStartElevationMeters = null;
        return new ToolOutcome.Message(_pathDraft.Count == 0
            ? "Path: draft cleared."
            : $"Path: {_pathDraft.Count} point{Plural(_pathDraft.Count)} left.");
    }

    private static ToolOutcome ErasePath(ToolContext context, AuthoringPoint point)
    {
        var route = RouteSurfaceEditing.FindAt(
            context.Scene,
            context.Metrics,
            point.X,
            point.Y);
        if (route is null) return new ToolOutcome.Message("Path Eraser: no Path here.");
        var routeId = route.RouteSurfaceId;
        return new ToolOutcome.Edit(
            "Path Eraser",
            document => RouteSurfaceEditing.Remove(document, routeId),
            Describe: (_, _) => $"Removed Path '{routeId}'.");
    }

    /// <summary>
    /// Erases the whole body under the pointer. Single cells are not erasable
    /// on purpose: they are derived from the curve, so rubbing one out would be
    /// undone by the next time the corridor is worked out.
    /// </summary>
    private static ToolOutcome EraseWaterBody(ToolContext context, AuthoringPoint point)
    {
        var body = WaterEditing.FindAt(context.Scene, context.Metrics, point.X, point.Y);
        if (body is null) return new ToolOutcome.Message("River Eraser: no water here.");
        var bodyId = body.WaterBodyId;
        return new ToolOutcome.Edit(
            "River Eraser",
            document => WaterEditing.Remove(document, bodyId),
            Describe: (_, _) => $"Removed water body '{bodyId}'.");
    }

    private static bool IsInsideScene(ToolContext context, AuthoringPoint point) =>
        point.X >= 0 && point.X <= context.Metrics.SceneWidthAuthoringPixels(context.Scene)
        && point.Y >= 0 && point.Y <= context.Metrics.SceneHeightAuthoringPixels(context.Scene);

    private ToolOutcome CancelPropLineStep()
    {
        if (_propLineEnd is not null)
        {
            _propLineEnd = null;
            return new ToolOutcome.Message("Line Draw: end point released; choose a new end point.");
        }
        if (_propLineStart is not null)
        {
            _propLineStart = null;
            return new ToolOutcome.Message("Line Draw: start point released; choose a new start point.");
        }
        return new ToolOutcome.Message("Line Draw: choose a start point.");
    }

    private ToolOutcome BeginTerrainLine(TerrainCellCoordinate cell)
    {
        _terrainLineStart = cell;
        _terrainLineEnd = cell;
        _terrainLineDragging = true;
        return new ToolOutcome.Message(
            $"Terrain Line: drag from cell ({cell.X}, {cell.Y}) and release to apply.");
    }

    private ToolOutcome ContinuePropLine(ToolContext context, string assetKey, AuthoringPoint point)
    {
        if (_propLineStart is null)
        {
            _propLineStart = point;
            return new ToolOutcome.Message(
                $"Line Draw: start fixed at ({point.X}, {point.Y}); choose an end point.");
        }
        if (_propLineEnd is not null)
        {
            return new ToolOutcome.Message(
                "Line Draw: end point is fixed. Press Enter to confirm or Escape to revise it.");
        }

        _propLineEnd = point;
        var preview = PropPreviews(context, assetKey, _propLineStart.Value, point);
        var blocked = ToolPreviewBuilder.CountOf(preview, PropPreviewKind.Blocked);
        return new ToolOutcome.Message(blocked > 0
            ? $"Line Draw: end fixed; {blocked} of {preview.Count} previews are blocked. Press Escape to revise."
            : $"Line Draw: end fixed; {preview.Count} previews ready. Press Enter to confirm or Escape to revise.");
    }

    private ToolOutcome PaintTerrainCell(
        ToolContext context,
        TerrainCellCoordinate cell,
        string strokeKey)
    {
        if (context.SelectedTerrainAssetKey is not { } assetKey) return ToolOutcome.Idle.Instance;
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        return new ToolOutcome.Edit(
            "Pencil Draw",
            document => TerrainEditing.Paint(
                document, context.TerrainAssets, cell.X, cell.Y, assetKey, context.ElevationMeters),
            strokeKey,
            Describe: (_, _) => $"Painted {assetName} at Terrain cell ({cell.X}, {cell.Y}).");
    }

    private static ToolOutcome EraseTerrainCell(TerrainCellCoordinate cell, string strokeKey) =>
        new ToolOutcome.Edit(
            "Eraser",
            document => TerrainEditing.Erase(document, cell.X, cell.Y),
            strokeKey,
            Describe: (_, _) => $"Erased Terrain at cell ({cell.X}, {cell.Y}).");

    private static ToolOutcome FillTerrainRegion(ToolContext context, TerrainCellCoordinate cell)
    {
        var assetKey = context.SelectedTerrainAssetKey!;
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        return new ToolOutcome.Edit(
            "Terrain Fill",
            document => TerrainEditing.Fill(
                document, context.TerrainAssets, cell.X, cell.Y, assetKey, context.ElevationMeters),
            Describe: (_, _) => $"Filled the connected region at ({cell.X}, {cell.Y}) with {assetName}.",
            NoChangeText: "Terrain Fill made no change because source and target Terrain are identical.");
    }

    private static ToolOutcome EraseTerrainRegion(TerrainCellCoordinate cell, string strokeKey) =>
        new ToolOutcome.Edit(
            "Terrain Eraser Fill",
            document => TerrainEditing.EraseFill(document, cell.X, cell.Y),
            strokeKey,
            Describe: (_, _) => $"Erased the connected Terrain region at ({cell.X}, {cell.Y}).",
            NoChangeText: "Terrain Eraser Fill made no change because the region is already empty.");

    private static ToolOutcome PaintTerrainLine(
        ToolContext context,
        TerrainCellCoordinate start,
        TerrainCellCoordinate end)
    {
        if (context.SelectedTerrainAssetKey is not { } assetKey) return ToolOutcome.Idle.Instance;
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        var count = TerrainEditing.LineCells(start.X, start.Y, end.X, end.Y).Count;
        return new ToolOutcome.Edit(
            "Line Draw",
            document => TerrainEditing.PaintLine(
                document,
                context.TerrainAssets,
                start.X,
                start.Y,
                end.X,
                end.Y,
                assetKey,
                context.ElevationMeters),
            Describe: (_, _) => $"Line Draw painted {count} Terrain cells with {assetName}.");
    }

    private static ToolOutcome EraseTerrainLine(TerrainCellCoordinate start, TerrainCellCoordinate end) =>
        new ToolOutcome.Edit(
            "Line Eraser",
            document => TerrainEditing.EraseLine(document, start.X, start.Y, end.X, end.Y),
            Describe: (before, after) =>
            {
                var erased = before.TerrainCells.Count - after.TerrainCells.Count;
                return $"Line Eraser removed {erased} Terrain cell{Plural(erased)}.";
            },
            NoChangeText: "Line Eraser made no change because the selected Terrain cells are empty.");

    private static ToolOutcome EraseProp(ToolContext context, AuthoringPoint point, string? strokeKey) =>
        new ToolOutcome.Edit(
            "Eraser",
            document => PropEditing.EraseAt(document, context.PropAssets, point.X, point.Y),
            strokeKey,
            Describe: (_, _) => $"Erased Placement at ({point.X}, {point.Y}) authoring px.");

    private ToolOutcome ErasePropLine(
        ToolContext context,
        string assetKey,
        AuthoringPoint start,
        AuthoringPoint end)
    {
        var offset = State.PropLineOffsetAuthoringPixels;
        return new ToolOutcome.Edit(
            "Line Eraser",
            document =>
            {
                var asset = context.PropAssets.Resolve(assetKey);
                var result = document;
                foreach (var anchor in PropEditing.LineAnchors(
                             asset, start.X, start.Y, end.X, end.Y, offset))
                {
                    result = PropEditing.EraseAt(result, context.PropAssets, anchor.X, anchor.Y);
                }
                return result;
            },
            Describe: (before, after) =>
            {
                var erased = before.Props.Count - after.Props.Count;
                return $"Line Eraser removed {erased} Placement{Plural(erased)}.";
            });
    }

    private static ToolOutcome MoveTemplateAnchor(
        ToolContext context,
        string anchorId,
        AuthoringPoint position) =>
        new ToolOutcome.Edit(
            "Move Anchor",
            document => TemplateEditing.MoveAnchor(
                document, context.Metrics, anchorId, position.X, position.Y),
            Describe: (_, _) =>
                $"Moved Template Anchor '{anchorId}' to ({position.X}, {position.Y}).");

    private IReadOnlyList<PropPreview> PropPreviews(
        ToolContext context,
        string assetKey,
        AuthoringPoint start,
        AuthoringPoint end) =>
        ToolPreviewBuilder.BuildProps(
            context.Scene,
            context.PropAssets,
            assetKey,
            EditorTool.Line,
            pointer: null,
            start,
            end,
            State.PropLineOffsetAuthoringPixels);

    private static AuthoringPoint SnapToGrid(ToolContext context, AuthoringPoint point) => new(
        TemplateEditing.SnapToWorldGrid(point.X, context.Metrics.AuthoringPixelsPerTerrainCell),
        TemplateEditing.SnapToWorldGrid(point.Y, context.Metrics.AuthoringPixelsPerTerrainCell));

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static string GradeText(RouteGradePreset grade) => grade switch
    {
        RouteGradePreset.DownFiftyPercent => "-50%",
        RouteGradePreset.DownTwentyFivePercent => "-25%",
        RouteGradePreset.Level => "0%",
        RouteGradePreset.UpTwentyFivePercent => "+25%",
        RouteGradePreset.UpFiftyPercent => "+50%",
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    private static string OperationText(
        RouteSegmentOperation operation,
        decimal? clearanceAboveMeters) => operation switch
    {
        RouteSegmentOperation.Additive => "additive",
        RouteSegmentOperation.Subtractive =>
            $"subtractive · clearance {clearanceAboveMeters:0.###} m",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private void ResetTransient()
    {
        ClearPropLine();
        ClearTerrainLine();
        ClearAnchorDrag();
        ClearElevationRegionPointDrag();
        ClearElevationRegionHandleDrag();
        ClearRiverDraft();
        ClearPathDraft();
        ClearElevationRegionDraft();
        ClearBridgeDrag();
        _bridgeStart = null;
    }

    private void ClearRiverDraft()
    {
        _riverDraft.Clear();
        _riverPending = null;
    }

    private void ClearPathDraft()
    {
        _pathDraft.Clear();
        _pathPending = null;
        _pathStartElevationMeters = null;
    }

    private void ClearElevationRegionDraft()
    {
        _elevationRegionDraft.Clear();
        _elevationRegionPending = null;
    }

    private void ClearElevationRegionPointDrag()
    {
        _draggedElevationRegionPointIndex = null;
        _draggedElevationRegionPointPosition = null;
    }

    private void ClearElevationRegionHandleDrag()
    {
        _draggedElevationRegionHandleSide = null;
        _draggedElevationRegionHandleOffset = null;
    }

    private void ClearPropLine()
    {
        _propLineStart = null;
        _propLineEnd = null;
    }

    private void ClearTerrainLine()
    {
        _terrainLineStart = null;
        _terrainLineEnd = null;
        _terrainLineDragging = false;
    }

    private void ClearAnchorDrag()
    {
        _draggedAnchorId = null;
        _draggedAnchorPosition = null;
    }
}
