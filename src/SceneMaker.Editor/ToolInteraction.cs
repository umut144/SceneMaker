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
    private AuthoringPoint? _propLineEnd;
    private TerrainCellCoordinate? _terrainLineStart;
    private TerrainCellCoordinate? _terrainLineEnd;
    private bool _terrainLineDragging;
    private readonly List<WaterDraftPoint> _riverDraft = [];
    private WaterDraftPoint? _riverPending;
    private string? _draggedAnchorId;
    private AuthoringPoint? _draggedAnchorPosition;

    /// <summary>Mode, tool and the options that combine with them.</summary>
    public EditorInteractionState State { get; } = new();

    public EditorMode Mode => State.Mode;
    public EditorTool ActiveTool => State.ActiveTool;
    public bool EraserEnabled => State.EraserEnabled;

    public string? SelectedPropInstanceId { get; private set; }
    public string? SelectedTemplateAnchorId { get; private set; }

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

    public string? DraggedAnchorId => _draggedAnchorId;
    public AuthoringPoint? DraggedAnchorPosition => _draggedAnchorPosition;

    public void SelectMode(EditorMode mode)
    {
        State.SelectMode(mode);
        ResetTransient();
    }

    public void SelectTool(EditorTool tool)
    {
        State.SelectTool(tool);
        ResetTransient();
    }

    public void SetEraserEnabled(bool enabled) => State.SetEraserEnabled(enabled);

    /// <summary>Forgets pointer state and selection, for a new or closed Scene.</summary>
    public void ResetForScene()
    {
        ResetTransient();
        _pointerAuthoring = null;
        _pointerCell = null;
        SelectedPropInstanceId = null;
        SelectedTemplateAnchorId = null;
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
        EditorMode.Terrain when ActiveTool == EditorTool.DrawRiver =>
            _riverPending is not null || _riverDraft.Count > 0,
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
        return Mode == EditorMode.Terrain ? CancelRiverPoint() : CancelPropLineStep();
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
        if (_draggedAnchorId is not null) ClearAnchorDrag();
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
            case EditorMode.Terrain when ActiveTool == EditorTool.DrawRiver && _riverPending is not null:
                return DragRiverHandle(context, authoring);
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
        if (Mode == EditorMode.Terrain && ActiveTool == EditorTool.DrawRiver)
            return key == ToolKey.Enter ? FinishRiver(context) : CancelRiverPoint();
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
            return new ToolOutcome.Message("Line Draw: choose a Prop asset first.");

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
                $"Line Draw blocked: {blocked} of {preview.Count} Prop previews are invalid.");
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
                return $"Line Draw placed {added} Prop{Plural(added)} with exact non-overlapping footprints.";
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
        EditorTool.DrawRiver when EraserEnabled => EraseWaterBody(context, authoring),
        EditorTool.DrawRiver => BeginRiverPoint(context, authoring),
        _ => ToolOutcome.Idle.Instance,
    };

    private ToolOutcome PropPressed(ToolContext context, AuthoringPoint point)
    {
        if (context.SelectedPropAssetKey is not { } assetKey) return ToolOutcome.Idle.Instance;
        switch (ActiveTool)
        {
            case EditorTool.Selector:
                var found = PropEditing.FindAt(context.Scene, context.PropAssets, point.X, point.Y);
                SelectedPropInstanceId = found?.InstanceId;
                return new ToolOutcome.Message(found is null
                    ? "No Prop selected."
                    : $"Selected '{found.InstanceId}' · anchor ({found.PositionAuthoringPx.X}, {found.PositionAuthoringPx.Y}).");

            case EditorTool.Pencil when EraserEnabled:
                return EraseProp(context, point, strokeKey: null);

            case EditorTool.Pencil:
                var validation = PropEditing.ValidateCandidate(
                    context.Scene, context.PropAssets, point.X, point.Y, assetKey, context.AuthoredTerrain);
                if (!validation.IsValid)
                    return new ToolOutcome.Message($"Pencil Draw blocked: {validation.Reason}");
                var warning = validation.HasCompleteTerrain
                    ? null
                    : $"Prop authored with export warning: {validation.Warning}";
                var assetName = context.PropAssets.Resolve(assetKey).Name;
                return new ToolOutcome.Edit(
                    "Pencil Draw",
                    document => PropEditing.Place(
                        document, context.PropAssets, point.X, point.Y, assetKey, context.ElevationMeters),
                    Describe: (_, _) => warning
                        ?? $"Placed {assetName} anchor at ({point.X}, {point.Y}) authoring px.");

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
    private ToolOutcome BeginRiverPoint(ToolContext context, AuthoringPoint point)
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

        _riverPending = new WaterDraftPoint(snapped.X, snapped.Y, State.WaterPointMode);
        var ordinal = _riverDraft.Count + 1;
        return new ToolOutcome.Message(State.WaterPointMode == WaterPointMode.Linear
            ? $"River: point {ordinal} at ({snapped.X}, {snapped.Y})."
            : $"River: point {ordinal} at ({snapped.X}, {snapped.Y}); drag to pull its handle.");
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
        var width = State.RiverWidthMeters;
        var elevation = context.ElevationMeters;
        var assetName = context.TerrainAssets.Resolve(assetKey).Name;
        var placed = _riverDraft.Count;
        _riverDraft.Clear();
        return new ToolOutcome.Edit(
            "River",
            document => WaterEditing.PlaceRiver(
                document, context.TerrainAssets, points, assetKey, width, elevation),
            Describe: (before, after) =>
            {
                var added = after.WaterBodies.FirstOrDefault(body =>
                    before.WaterBodies.All(previous => previous.WaterBodyId != body.WaterBodyId));
                var name = added?.WaterBodyId ?? "river";
                return $"Authored {name} from {placed} points · {assetName} · {width:0.###} m wide · {elevation:0.###} m.";
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
        var warnings = ToolPreviewBuilder.CountOf(preview, PropPreviewKind.MissingTerrain);
        return new ToolOutcome.Message(blocked > 0
            ? $"Line Draw: end fixed; {blocked} of {preview.Count} previews are blocked. Press Escape to revise."
            : warnings > 0
                ? $"Line Draw: end fixed; {warnings} of {preview.Count} previews lack Terrain but may be authored. Enter confirms; Escape revises."
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
            Describe: (_, _) =>
                $"Erased Terrain at cell ({cell.X}, {cell.Y}); uncovered spatial instances are export warnings.");

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
            Describe: (_, _) => $"Erased Prop at ({point.X}, {point.Y}) authoring px.");

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
                return $"Line Eraser removed {erased} Prop{Plural(erased)}.";
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
            context.AuthoredTerrain,
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

    private void ResetTransient()
    {
        ClearPropLine();
        ClearTerrainLine();
        ClearAnchorDrag();
        ClearRiverDraft();
    }

    private void ClearRiverDraft()
    {
        _riverDraft.Clear();
        _riverPending = null;
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
