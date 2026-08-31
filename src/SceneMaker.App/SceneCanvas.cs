using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SceneMaker.Core;

namespace SceneMaker.App;

public sealed partial class SceneCanvas : Control
{
    private static readonly Color CanvasBackground = Color.FromHtml("#101722");
    private static readonly Color SceneBackground = Color.FromHtml("#182434");
    private static readonly Color SceneBorder = Color.FromHtml("#78A6C8");
    private static readonly Color CellGrid = new(0.45f, 0.62f, 0.75f, 0.38f);
    private static readonly Color AuthoringGrid = new(0.45f, 0.62f, 0.75f, 0.12f);
    private static readonly Color SelectionColor = Color.FromHtml("#FFD866");
    private static readonly Color ValidPreviewColor = Color.FromHtml("#FFD866");
    private static readonly Color InvalidPreviewColor = Color.FromHtml("#FF5C5C");
    private static readonly Color TemplateAnchorFill = Color.FromHtml("#FFFFFF");
    private static readonly Color TemplateAnchorBorder = Color.FromHtml("#7B8491");
    private static readonly Color TemplateAnchorText = Color.FromHtml("#252A31");
    private static readonly Color TemplatePreviewOutline = Color.FromHtml("#FFFFFF");

    private LoadedScene? _scene;
    private WorkspaceMetrics? _metrics;
    private SceneDocument? _templatePreview;
    private IReadOnlyList<TemplateTerrainMask> _templatePreviewMasks = [];
    private IReadOnlyDictionary<string, Color> _terrainColors = new Dictionary<string, Color>();
    private PropDisplayCatalog? _propAssets;
    private EditorInteractionState _interactionState = new();
    private string? _selectedPropInstanceId;
    private string? _selectedTemplateAnchorId;
    private string? _draggedTemplateAnchorId;
    private (int X, int Y)? _draggedTemplateAnchorPosition;
    private (int X, int Y)? _pointerAuthoringPosition;
    private (int X, int Y)? _pointerTerrainPosition;
    private (int X, int Y)? _lineStart;
    private (int X, int Y)? _lineEnd;
    private bool _terrainLineDragging;
    private bool _pointerOverCanvas;

    public SceneCanvas()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseExited += () =>
        {
            _pointerOverCanvas = false;
            _pointerAuthoringPosition = null;
            _pointerTerrainPosition = null;
            QueueRedraw();
        };
        MouseEntered += () => _pointerOverCanvas = true;
    }

    public CanvasViewState ViewState { get; private set; } = new();
    public LoadedScene? Scene => _scene;
    public string? SelectedTerrainAssetKey { get; set; }
    public string? SelectedPropAssetKey { get; set; }
    public EditorMode Mode => _interactionState.Mode;
    public EditorTool ActiveTool => _interactionState.ActiveTool;
    public bool EraserEnabled => _interactionState.EraserEnabled;
    public event Action? ViewChanged;
    public event Action<int, int>? TerrainPaintRequested;
    public event Action<int, int>? TerrainEraseRequested;
    public event Action<int, int>? TerrainFillEraseRequested;
    public event Action<int, int>? TerrainFillRequested;
    public event Action<int, int, int, int>? TerrainLineRequested;
    public event Action<int, int, int, int>? TerrainLineEraseRequested;
    public event Action<int, int>? PropRequested;
    public event Action<int, int, int, int>? PropLineRequested;
    public event Action<int, int, int, int>? PropLineEraseRequested;
    public event Action<int, int>? PropEraseRequested;
    public event Action<int, int>? PropSelectRequested;
    public event Action<int, int>? TemplateAnchorPlaceRequested;
    public event Action<int, int>? TemplateAnchorSelectRequested;
    public event Action<string, int, int>? TemplateAnchorMoveRequested;
    public event Action<string>? ToolStatusRequested;

    public void ConfigureInteractionState(EditorInteractionState state)
    {
        _interactionState = state ?? throw new ArgumentNullException(nameof(state));
        ResetTransientInteraction();
    }

    public void SelectMode(EditorMode mode)
    {
        _interactionState.SelectMode(mode);
        ResetTransientInteraction();
    }

    public void SelectTool(EditorTool tool)
    {
        _interactionState.SelectTool(tool);
        ResetTransientInteraction();
    }

    public void SetEraserEnabled(bool enabled)
    {
        _interactionState.SetEraserEnabled(enabled);
        QueueRedraw();
    }

    private void ResetTransientInteraction()
    {
        _lineStart = null;
        _lineEnd = null;
        _draggedTemplateAnchorId = null;
        _draggedTemplateAnchorPosition = null;
        _terrainLineDragging = false;
        QueueRedraw();
    }

    public void ConfigureTerrainAssets(TerrainDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var colors = new Dictionary<string, Color>();
        foreach (var asset in catalog.Assets)
            colors.Add(asset.AssetKey, Color.FromHtml(asset.Color));
        _terrainColors = colors;
        QueueRedraw();
    }

    public void ConfigureMetrics(WorkspaceMetrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        QueueRedraw();
    }

    public void ConfigurePropAssets(PropDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _propAssets = catalog;
        QueueRedraw();
    }

    public void ShowScene(LoadedScene? scene)
    {
        _scene = scene;
        _templatePreview = null;
        _templatePreviewMasks = [];
        ViewState = new CanvasViewState();
        _selectedPropInstanceId = null;
        _selectedTemplateAnchorId = null;
        _draggedTemplateAnchorId = null;
        _draggedTemplateAnchorPosition = null;
        _pointerAuthoringPosition = null;
        _pointerTerrainPosition = null;
        _lineStart = null;
        _lineEnd = null;
        _terrainLineDragging = false;
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public void UpdateScene(LoadedScene scene)
    {
        _scene = scene;
        QueueRedraw();
    }

    public void ShowTemplatePreview(
        SceneDocument? scene,
        IReadOnlyList<TemplateTerrainMask>? effectiveTerrainMasks = null)
    {
        _templatePreview = scene;
        _templatePreviewMasks = effectiveTerrainMasks ?? [];
        QueueRedraw();
    }

    public void SelectProp(string? instanceId)
    {
        _selectedPropInstanceId = instanceId;
        QueueRedraw();
    }

    public void SelectTemplateAnchor(string? anchorId)
    {
        _selectedTemplateAnchorId = anchorId;
        QueueRedraw();
    }

    public void CompleteLinePlacement()
    {
        _lineStart = null;
        _lineEnd = null;
        _terrainLineDragging = false;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouseButton
            && mouseButton.ButtonIndex == MouseButton.Left
            && mouseButton.Pressed)
        {
            GrabFocus();
            UpdatePointer(mouseButton.Position);
            BeginPrimaryAction(mouseButton.Position);
        }
        else if (input is InputEventMouseButton releasedMouseButton
                 && releasedMouseButton.ButtonIndex == MouseButton.Left
                 && !releasedMouseButton.Pressed)
        {
            UpdatePointer(releasedMouseButton.Position);
            CompletePrimaryAction();
        }
        else if (input is InputEventMouseMotion mouseMotion)
        {
            UpdatePointer(mouseMotion.Position);
            ContinuePrimaryAction(
                mouseMotion.Position,
                (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0);
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey keyEvent) return;

        if (keyEvent.Pressed
            && Mode == EditorMode.Props
            && ActiveTool == EditorTool.Line
            && HandleLineKey(keyEvent.Keycode))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        const double zoomFactor = 1.25;
        var changed = true;
        switch (keyEvent.Keycode)
        {
            case Key.Q:
                if (!keyEvent.Pressed) return;
                ZoomAtCenter(1.0 / zoomFactor);
                break;
            case Key.E:
                if (!keyEvent.Pressed) return;
                ZoomAtCenter(zoomFactor);
                break;
            case Key.W:
            case Key.A:
            case Key.S:
            case Key.D:
                if (!_pointerOverCanvas)
                {
                    changed = false;
                    break;
                }
                GetViewport().SetInputAsHandled();
                return;
            default:
                changed = false;
                break;
        }

        if (!changed) return;
        GetViewport().SetInputAsHandled();
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public override void _Process(double delta)
    {
        var inputX = 0.0;
        var inputY = 0.0;
        if (_pointerOverCanvas)
        {
            if (Input.IsKeyPressed(Key.A)) inputX += 1.0;
            if (Input.IsKeyPressed(Key.D)) inputX -= 1.0;
            if (Input.IsKeyPressed(Key.W)) inputY += 1.0;
            if (Input.IsKeyPressed(Key.S)) inputY -= 1.0;
        }
        if (!ViewState.AdvanceKeyboardPan(inputX, inputY, delta)) return;
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    private bool HandleLineKey(Key key)
    {
        if (key == Key.Escape)
        {
            if (_lineEnd is not null)
            {
                _lineEnd = null;
                ToolStatusRequested?.Invoke(
                    "Line Draw: end point released; choose a new end point.");
            }
            else if (_lineStart is not null)
            {
                _lineStart = null;
                ToolStatusRequested?.Invoke(
                    "Line Draw: start point released; choose a new start point.");
            }
            else
            {
                ToolStatusRequested?.Invoke("Line Draw: choose a start point.");
            }
            QueueRedraw();
            return true;
        }

        if (key is not (Key.Enter or Key.KpEnter)) return false;
        if (_lineStart is not { } start || _lineEnd is not { } end)
        {
            ToolStatusRequested?.Invoke(
                _lineStart is null
                    ? "Line Draw: choose a start point before confirming."
                    : "Line Draw: choose and lock an end point before confirming.");
            return true;
        }

        if (EraserEnabled)
        {
            PropLineEraseRequested?.Invoke(start.X, start.Y, end.X, end.Y);
            return true;
        }

        var preview = PropLinePreview(start, end);
        var invalidCount = preview.Count(candidate => !candidate.Validation.IsValid);
        if (invalidCount > 0)
        {
            ToolStatusRequested?.Invoke(
                $"Line Draw blocked: {invalidCount} of {preview.Count} Prop previews are invalid.");
            return true;
        }

        PropLineRequested?.Invoke(start.X, start.Y, end.X, end.Y);
        var warningCount = preview.Count(candidate =>
            candidate.Validation.IsValid && !candidate.Validation.HasCompleteTerrain);
        if (warningCount > 0)
        {
            ToolStatusRequested?.Invoke(
                $"Line Draw authored {preview.Count} Props; {warningCount} lack complete Terrain and block export.");
        }
        return true;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), CanvasBackground);
        if (_scene is null || _metrics is null) return;

        var document = _templatePreview ?? _scene.Document;
        var zoom = (float)ViewState.Zoom;
        var pan = new Vector2((float)ViewState.PanX, (float)ViewState.PanY);
        var widthAuthoringPixels = _metrics.SceneWidthAuthoringPixels(document);
        var heightAuthoringPixels = _metrics.SceneHeightAuthoringPixels(document);
        var sceneSize = new Vector2(widthAuthoringPixels, heightAuthoringPixels) * zoom;
        var sceneRect = new Rect2(pan, sceneSize);
        DrawRect(sceneRect, SceneBackground);

        DrawTerrain(document, pan, zoom, highlighted: Mode == EditorMode.Terrain);
        DrawProps(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            highlighted: Mode == EditorMode.Props);
        if (Mode == EditorMode.Props)
            DrawPropToolPreview(pan, zoom, heightAuthoringPixels);
        else if (Mode == EditorMode.Terrain)
            DrawTerrainToolPreview(document, pan, zoom);

        var visible = new Rect2(Vector2.Zero, Size).Intersection(sceneRect);
        if (visible.Size.X > 0f && visible.Size.Y > 0f)
        {
            DrawGrid(
                visible,
                pan,
                zoom,
                widthAuthoringPixels,
                heightAuthoringPixels,
                _metrics.AuthoringPixelsPerTerrainCell,
                CellGrid,
                1.0f);

            if (zoom >= 4.0f)
            {
                DrawGrid(
                    visible,
                    pan,
                    zoom,
                    widthAuthoringPixels,
                    heightAuthoringPixels,
                    stepAuthoringPixels: 1,
                    AuthoringGrid,
                    1.0f);
            }
        }

        DrawTemplatePreviewOutlines(
            pan,
            zoom,
            document.SizeCells.Height);

        DrawTemplateAnchors(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            highlighted: Mode == EditorMode.Templates);

        DrawRect(sceneRect, SceneBorder, filled: false, width: 2.0f);
    }

    private void ZoomAtCenter(double factor)
    {
        ViewState.ZoomBy(factor, Size.X * 0.5, Size.Y * 0.5);
    }

    private void BeginPrimaryAction(Vector2 screenPosition)
    {
        if (_scene is null) return;
        if (Mode == EditorMode.Terrain)
        {
            var (cellX, cellY) = TerrainCoordinate(screenPosition);
            switch (ActiveTool)
            {
                case EditorTool.Pencil when EraserEnabled:
                    TerrainEraseRequested?.Invoke(cellX, cellY);
                    break;
                case EditorTool.Pencil when SelectedTerrainAssetKey is not null:
                    TerrainPaintRequested?.Invoke(cellX, cellY);
                    break;
                case EditorTool.Fill when EraserEnabled:
                    TerrainFillEraseRequested?.Invoke(cellX, cellY);
                    break;
                case EditorTool.Fill when SelectedTerrainAssetKey is not null:
                    TerrainFillRequested?.Invoke(cellX, cellY);
                    break;
                case EditorTool.Line:
                    _lineStart = (cellX, cellY);
                    _lineEnd = (cellX, cellY);
                    _terrainLineDragging = true;
                    ToolStatusRequested?.Invoke(
                        $"Terrain Line: drag from cell ({cellX}, {cellY}) and release to apply.");
                    QueueRedraw();
                    break;
            }
        }
        else if (Mode == EditorMode.Props && SelectedPropAssetKey is not null)
        {
            var coordinate = AuthoringCoordinate(screenPosition);
            switch (ActiveTool)
            {
                case EditorTool.Selector:
                    PropSelectRequested?.Invoke(coordinate.X, coordinate.Y);
                    break;
                case EditorTool.Pencil when EraserEnabled:
                    PropEraseRequested?.Invoke(coordinate.X, coordinate.Y);
                    break;
                case EditorTool.Pencil:
                    var validation = ValidateProp(coordinate);
                    if (validation.IsValid)
                    {
                        PropRequested?.Invoke(coordinate.X, coordinate.Y);
                        if (!validation.HasCompleteTerrain)
                        {
                            ToolStatusRequested?.Invoke(
                                $"Prop authored with export warning: {validation.Warning}");
                        }
                    }
                    else
                    {
                        ToolStatusRequested?.Invoke(
                            $"Pencil Draw blocked: {validation.Reason}");
                    }
                    break;
                case EditorTool.Line:
                    if (_lineStart is null)
                    {
                        _lineStart = coordinate;
                        ToolStatusRequested?.Invoke(
                            $"Line Draw: start fixed at ({coordinate.X}, {coordinate.Y}); choose an end point.");
                    }
                    else if (_lineEnd is null)
                    {
                        _lineEnd = coordinate;
                        var preview = PropLinePreview(_lineStart.Value, coordinate);
                        var invalidCount = preview.Count(candidate => !candidate.Validation.IsValid);
                        var warningCount = preview.Count(candidate =>
                            candidate.Validation.IsValid && !candidate.Validation.HasCompleteTerrain);
                        ToolStatusRequested?.Invoke(invalidCount > 0
                            ? $"Line Draw: end fixed; {invalidCount} of {preview.Count} previews are blocked. Press Escape to revise."
                            : warningCount > 0
                                ? $"Line Draw: end fixed; {warningCount} of {preview.Count} previews lack Terrain but may be authored. Enter confirms; Escape revises."
                                : $"Line Draw: end fixed; {preview.Count} previews ready. Press Enter to confirm or Escape to revise.");
                    }
                    else
                    {
                        ToolStatusRequested?.Invoke(
                            "Line Draw: end point is fixed. Press Enter to confirm or Escape to revise it.");
                    }
                    QueueRedraw();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        else if (Mode == EditorMode.Templates
                 && _scene.Document.SceneKind == SceneKind.Instance)
        {
            var coordinate = AuthoringCoordinate(screenPosition);
            switch (ActiveTool)
            {
                case EditorTool.AnchorPlace:
                    TemplateAnchorPlaceRequested?.Invoke(coordinate.X, coordinate.Y);
                    break;
                case EditorTool.Selector:
                    TemplateAnchorSelectRequested?.Invoke(coordinate.X, coordinate.Y);
                    break;
                case EditorTool.AnchorMove:
                    var anchor = TemplateEditing.FindAnchorAt(
                        _scene.Document,
                        coordinate.X,
                        coordinate.Y);
                    if (anchor is null)
                    {
                        ToolStatusRequested?.Invoke("Move Anchor: no Template Anchor selected.");
                        break;
                    }
                    _selectedTemplateAnchorId = anchor.AnchorId;
                    _draggedTemplateAnchorId = anchor.AnchorId;
                    _draggedTemplateAnchorPosition = (
                        TemplateEditing.SnapToWorldGrid(coordinate.X, _metrics!.AuthoringPixelsPerTerrainCell),
                        TemplateEditing.SnapToWorldGrid(coordinate.Y, _metrics!.AuthoringPixelsPerTerrainCell));
                    TemplateAnchorSelectRequested?.Invoke(coordinate.X, coordinate.Y);
                    QueueRedraw();
                    break;
            }
        }
    }

    private void ContinuePrimaryAction(Vector2 screenPosition, bool leftButtonPressed)
    {
        if (_scene is null) return;
        if (Mode == EditorMode.Terrain && leftButtonPressed)
        {
            var (cellX, cellY) = TerrainCoordinate(screenPosition);
            if (ActiveTool == EditorTool.Line && _terrainLineDragging)
            {
                _lineEnd = (cellX, cellY);
                QueueRedraw();
            }
            else if (ActiveTool == EditorTool.Pencil && EraserEnabled)
                TerrainEraseRequested?.Invoke(cellX, cellY);
            else if (ActiveTool == EditorTool.Pencil && SelectedTerrainAssetKey is not null)
                TerrainPaintRequested?.Invoke(cellX, cellY);
            else if (ActiveTool == EditorTool.Fill && EraserEnabled)
                TerrainFillEraseRequested?.Invoke(cellX, cellY);
        }
        else if (Mode == EditorMode.Props)
        {
            var coordinate = AuthoringCoordinate(screenPosition);
            if (ActiveTool == EditorTool.Pencil && EraserEnabled && leftButtonPressed)
                PropEraseRequested?.Invoke(coordinate.X, coordinate.Y);
        }
        else if (Mode == EditorMode.Templates
                 && ActiveTool == EditorTool.AnchorMove
                 && leftButtonPressed
                 && _draggedTemplateAnchorId is not null)
        {
            var coordinate = AuthoringCoordinate(screenPosition);
            _draggedTemplateAnchorPosition = (
                TemplateEditing.SnapToWorldGrid(coordinate.X, _metrics!.AuthoringPixelsPerTerrainCell),
                TemplateEditing.SnapToWorldGrid(coordinate.Y, _metrics!.AuthoringPixelsPerTerrainCell));
            QueueRedraw();
        }
    }

    private void CompletePrimaryAction()
    {
        if (Mode == EditorMode.Terrain
            && ActiveTool == EditorTool.Line
            && _terrainLineDragging
            && _lineStart is { } terrainStart
            && (_pointerTerrainPosition ?? _lineEnd) is { } terrainEnd)
        {
            _terrainLineDragging = false;
            if (EraserEnabled)
            {
                TerrainLineEraseRequested?.Invoke(
                    terrainStart.X,
                    terrainStart.Y,
                    terrainEnd.X,
                    terrainEnd.Y);
            }
            else
            {
                TerrainLineRequested?.Invoke(
                    terrainStart.X,
                    terrainStart.Y,
                    terrainEnd.X,
                    terrainEnd.Y);
            }
            return;
        }

        if (_draggedTemplateAnchorId is not { } anchorId
            || _draggedTemplateAnchorPosition is not { } position)
            return;
        _draggedTemplateAnchorId = null;
        _draggedTemplateAnchorPosition = null;
        TemplateAnchorMoveRequested?.Invoke(anchorId, position.X, position.Y);
        QueueRedraw();
    }

    private void UpdatePointer(Vector2 screenPosition)
    {
        if (_scene is null) return;
        if (Mode == EditorMode.Terrain)
        {
            _pointerTerrainPosition = TerrainCoordinate(screenPosition);
            _pointerAuthoringPosition = null;
            QueueRedraw();
            return;
        }
        _pointerTerrainPosition = null;
        _pointerAuthoringPosition = AuthoringCoordinate(screenPosition);
        QueueRedraw();
    }

    private (int X, int Y) TerrainCoordinate(Vector2 screenPosition) =>
        ViewState.ScreenToTerrainCell(
            screenPosition.X,
            screenPosition.Y,
            _scene!.Document.SizeCells.Height,
            _metrics!.AuthoringPixelsPerTerrainCell);

    private (int X, int Y) AuthoringCoordinate(Vector2 screenPosition) =>
        ViewState.ScreenToAuthoringPixel(
            screenPosition.X,
            screenPosition.Y,
            _metrics!.SceneHeightAuthoringPixels(_scene!.Document));

    private void DrawTerrain(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        bool highlighted)
    {
        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        foreach (var cell in document.TerrainCells)
        {
            if (!_terrainColors.TryGetValue(cell.AssetKey, out var color)) continue;
            var rectangle = new Rect2(
                pan + new Vector2(
                    cell.X * cellSize,
                    (document.SizeCells.Height - cell.Y - 1) * cellSize),
                new Vector2(cellSize, cellSize));
            if (rectangle.End.X < 0f || rectangle.End.Y < 0f
                || rectangle.Position.X > Size.X || rectangle.Position.Y > Size.Y)
                continue;
            DrawRect(
                rectangle,
                highlighted
                    ? color
                    : new Color(color.R, color.G, color.B, 0.24f));
        }
    }

    private void DrawTerrainToolPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom)
    {
        if (ActiveTool is not (EditorTool.Pencil or EditorTool.Line)
            || _pointerTerrainPosition is not { } pointer)
        {
            return;
        }

        IReadOnlyList<TerrainCellCoordinate> cells = ActiveTool == EditorTool.Line
            && _terrainLineDragging
            && _lineStart is { } start
                ? TerrainEditing.LineCells(start.X, start.Y, pointer.X, pointer.Y)
                : [new TerrainCellCoordinate(pointer.X, pointer.Y)];
        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        var color = EraserEnabled ? InvalidPreviewColor : SelectionColor;
        foreach (var cell in cells)
        {
            if (cell.X < 0 || cell.X >= document.SizeCells.Width
                || cell.Y < 0 || cell.Y >= document.SizeCells.Height)
            {
                continue;
            }

            var rectangle = new Rect2(
                pan + new Vector2(
                    cell.X * cellSize,
                    (document.SizeCells.Height - cell.Y - 1) * cellSize),
                new Vector2(cellSize, cellSize));
            DrawRect(
                rectangle,
                new Color(color.R, color.G, color.B, 0.25f));
            DrawRect(rectangle, color, filled: false, width: 2f);
        }
    }

    private void DrawProps(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        bool highlighted)
    {
        if (_propAssets is null) return;
        foreach (var prop in document.Props)
        {
            var asset = _propAssets.Resolve(prop.AssetKey);
            var bounds = PropEditing.BoundsFor(
                asset,
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y);
            var rectangle = CanvasRectangle(
                bounds,
                pan,
                zoom,
                sceneHeightAuthoringPixels);
            var color = Color.FromHtml(asset.Color);
            var selected = highlighted && prop.InstanceId == _selectedPropInstanceId;
            var outline = highlighted
                ? color
                : new Color(color.R, color.G, color.B, 0.32f);
            DrawRect(
                rectangle,
                new Color(color.R, color.G, color.B, highlighted ? 0.38f : 0.10f));
            DrawRect(
                rectangle,
                selected ? SelectionColor : outline,
                filled: false,
                width: selected ? 3f : highlighted ? 2f : 1f);
            if (!TerrainCoverage.IsComplete(document, bounds, _metrics!))
                DrawDashedRectangle(rectangle, InvalidPreviewColor, 2.5f);
            DrawAnchor(
                prop.PositionAuthoringPx.X,
                prop.PositionAuthoringPx.Y,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                outline);
        }
    }

    private void DrawTemplateAnchors(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        bool highlighted)
    {
        if (document.SceneKind == SceneKind.Template
            && document.TemplateDefinition is { } definition)
        {
            DrawTemplateAnchor(
                definition.InsertionAnchorAuthoringPx,
                $"T{definition.GroupNumber}",
                selected: false,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                highlighted);
            return;
        }

        foreach (var anchor in document.TemplateAnchors)
        {
            var position = anchor.AnchorId == _draggedTemplateAnchorId
                           && _draggedTemplateAnchorPosition is { } preview
                ? new AuthoringPixelPosition { X = preview.X, Y = preview.Y }
                : anchor.PositionAuthoringPx;
            DrawTemplateAnchor(
                position,
                anchor.GroupNumber.ToString(),
                anchor.AnchorId == _selectedTemplateAnchorId,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                highlighted);
        }

        if (highlighted
            && ActiveTool == EditorTool.AnchorPlace
            && _pointerAuthoringPosition is { } pointer)
        {
            DrawTemplateAnchor(
                new AuthoringPixelPosition
                {
                    X = TemplateEditing.SnapToWorldGrid(pointer.X, _metrics!.AuthoringPixelsPerTerrainCell),
                    Y = TemplateEditing.SnapToWorldGrid(pointer.Y, _metrics!.AuthoringPixelsPerTerrainCell),
                },
                "+",
                selected: true,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                highlighted: true,
                preview: true);
        }
    }

    private void DrawTemplatePreviewOutlines(
        Vector2 pan,
        float zoom,
        int sceneHeightCells)
    {
        if (_templatePreview is null || _templatePreviewMasks.Count == 0) return;
        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        foreach (var mask in _templatePreviewMasks)
        {
            var cells = mask.Cells.Select(static cell => (cell.X, cell.Y)).ToHashSet();
            foreach (var cell in cells)
            {
                var topLeft = pan + new Vector2(
                    cell.X * cellSize,
                    (sceneHeightCells - cell.Y - 1) * cellSize);
                var topRight = topLeft + new Vector2(cellSize, 0f);
                var bottomLeft = topLeft + new Vector2(0f, cellSize);
                var bottomRight = topLeft + new Vector2(cellSize, cellSize);
                if (!cells.Contains((cell.X, cell.Y + 1)))
                    DrawDashedSegment(topLeft, topRight, TemplatePreviewOutline, 2f);
                if (!cells.Contains((cell.X + 1, cell.Y)))
                    DrawDashedSegment(topRight, bottomRight, TemplatePreviewOutline, 2f);
                if (!cells.Contains((cell.X, cell.Y - 1)))
                    DrawDashedSegment(bottomRight, bottomLeft, TemplatePreviewOutline, 2f);
                if (!cells.Contains((cell.X - 1, cell.Y)))
                    DrawDashedSegment(bottomLeft, topLeft, TemplatePreviewOutline, 2f);
            }
        }
    }

    private void DrawTemplateAnchor(
        AuthoringPixelPosition position,
        string label,
        bool selected,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        bool highlighted,
        bool preview = false)
    {
        var size = TemplateEditing.AnchorVisualSizeAuthoringPixels * zoom;
        var center = pan + new Vector2(
            position.X * zoom,
            (sceneHeightAuthoringPixels - position.Y) * zoom);
        var rectangle = new Rect2(
            center - new Vector2(size * 0.5f, size * 0.5f),
            new Vector2(size, size));
        var alpha = highlighted ? preview ? 0.65f : 1f : 0.28f;
        var fill = new Color(
            TemplateAnchorFill.R,
            TemplateAnchorFill.G,
            TemplateAnchorFill.B,
            alpha);
        var borderBase = selected ? SelectionColor : TemplateAnchorBorder;
        var border = new Color(borderBase.R, borderBase.G, borderBase.B, alpha);
        DrawRect(rectangle, fill);
        DrawRect(rectangle, border, filled: false, width: selected ? 3f : 2f);

        if (size < 18f) return;
        var font = ThemeDB.FallbackFont;
        var fontSize = Math.Clamp((int)(14f * zoom), 10, 28);
        var textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1f, fontSize);
        var baseline = center + new Vector2(-textSize.X * 0.5f, textSize.Y * 0.32f);
        var textColor = new Color(
            TemplateAnchorText.R,
            TemplateAnchorText.G,
            TemplateAnchorText.B,
            alpha);
        DrawString(font, baseline, label, HorizontalAlignment.Left, -1f, fontSize, textColor);
    }

    private void DrawPropToolPreview(
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        if (_propAssets is null || SelectedPropAssetKey is not { } assetKey) return;

        var asset = _propAssets.Resolve(assetKey);
        IReadOnlyList<(int X, int Y, PropValidationResult Validation)> preview;
        if (ActiveTool == EditorTool.Pencil)
        {
            if (_pointerAuthoringPosition is not { } pointer) return;
            preview = [(pointer.X, pointer.Y, ValidateProp(pointer))];
        }
        else if (ActiveTool == EditorTool.Line)
        {
            if (_lineStart is not { } start)
            {
                if (_pointerAuthoringPosition is not { } pointer) return;
                preview = [(pointer.X, pointer.Y, ValidateProp(pointer))];
            }
            else
            {
                var endpoint = _lineEnd ?? _pointerAuthoringPosition;
                if (endpoint is null) return;
                preview = PropLinePreview(start, endpoint.Value);
            }
        }
        else
        {
            return;
        }

        foreach (var candidate in preview)
        {
            var bounds = PropEditing.BoundsFor(asset, candidate.X, candidate.Y);
            var rectangle = CanvasRectangle(
                bounds,
                pan,
                zoom,
                sceneHeightAuthoringPixels);
            var color = candidate.Validation.IsValid && candidate.Validation.HasCompleteTerrain
                ? ValidPreviewColor
                : InvalidPreviewColor;
            DrawRect(
                rectangle,
                new Color(color.R, color.G, color.B,
                    candidate.Validation.HasCompleteTerrain ? 0.22f : 0.08f));
            if (candidate.Validation.IsValid && !candidate.Validation.HasCompleteTerrain)
                DrawDashedRectangle(rectangle, color, 2f);
            else
                DrawRect(rectangle, color, filled: false, width: 2f);
            DrawAnchor(
                candidate.X,
                candidate.Y,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                color);
        }
    }

    private PropValidationResult ValidateProp((int X, int Y) coordinate) =>
        PropEditing.ValidateCandidate(
            _scene!.Document,
            _propAssets!,
            coordinate.X,
            coordinate.Y,
            SelectedPropAssetKey!);

    private IReadOnlyList<(int X, int Y, PropValidationResult Validation)> PropLinePreview(
        (int X, int Y) start,
        (int X, int Y) end)
    {
        var asset = _propAssets!.Resolve(SelectedPropAssetKey!);
        return PropEditing.LineAnchors(
                asset,
                start.X,
                start.Y,
                end.X,
                end.Y,
                _interactionState.PropLineOffsetAuthoringPixels)
            .Select(anchor => (
                anchor.X,
                anchor.Y,
                ValidateProp(anchor)))
            .ToList();
    }

    private static Rect2 CanvasRectangle(
        PropBoundsAuthoringPixels bounds,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels) => new(
            pan + new Vector2(
                bounds.Left * zoom,
                (sceneHeightAuthoringPixels - bounds.Top) * zoom),
            new Vector2(bounds.Width, bounds.Height) * zoom);

    private void DrawAnchor(
        int anchorX,
        int anchorY,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        Color color)
    {
        var anchorSize = Math.Max(3f, zoom);
        var anchor = pan + new Vector2(
            anchorX * zoom,
            (sceneHeightAuthoringPixels - anchorY) * zoom);
        DrawRect(
            new Rect2(
                anchor - new Vector2(anchorSize * 0.5f, anchorSize * 0.5f),
                new Vector2(anchorSize, anchorSize)),
            color);
    }

    private void DrawDashedRectangle(Rect2 rectangle, Color color, float width)
    {
        var topLeft = rectangle.Position;
        var topRight = new Vector2(rectangle.End.X, rectangle.Position.Y);
        var bottomRight = rectangle.End;
        var bottomLeft = new Vector2(rectangle.Position.X, rectangle.End.Y);
        DrawDashedSegment(topLeft, topRight, color, width);
        DrawDashedSegment(topRight, bottomRight, color, width);
        DrawDashedSegment(bottomRight, bottomLeft, color, width);
        DrawDashedSegment(bottomLeft, topLeft, color, width);
    }

    private void DrawDashedSegment(Vector2 from, Vector2 to, Color color, float width)
    {
        const float dashLength = 8f;
        const float gapLength = 5f;
        var displacement = to - from;
        var length = displacement.Length();
        if (length <= 0f) return;
        var direction = displacement / length;
        for (var offset = 0f; offset < length; offset += dashLength + gapLength)
        {
            DrawLine(
                from + (direction * offset),
                from + (direction * Math.Min(offset + dashLength, length)),
                color,
                width);
        }
    }

    private void DrawGrid(
        Rect2 visible,
        Vector2 pan,
        float zoom,
        int sceneWidth,
        int sceneHeight,
        int stepAuthoringPixels,
        Color color,
        float width)
    {
        var logicalLeft = Math.Clamp((visible.Position.X - pan.X) / zoom, 0f, sceneWidth);
        var logicalRight = Math.Clamp((visible.End.X - pan.X) / zoom, 0f, sceneWidth);
        var logicalTop = Math.Clamp((visible.Position.Y - pan.Y) / zoom, 0f, sceneHeight);
        var logicalBottom = Math.Clamp((visible.End.Y - pan.Y) / zoom, 0f, sceneHeight);

        var firstX = Math.Max(0, (int)MathF.Floor(logicalLeft / stepAuthoringPixels));
        var lastX = Math.Min(
            sceneWidth / stepAuthoringPixels,
            (int)MathF.Ceiling(logicalRight / stepAuthoringPixels));
        for (var index = firstX; index <= lastX; index++)
        {
            var screenX = pan.X + (index * stepAuthoringPixels * zoom);
            DrawLine(
                new Vector2(screenX, visible.Position.Y),
                new Vector2(screenX, visible.End.Y),
                color,
                width);
        }

        var firstY = Math.Max(0, (int)MathF.Floor(logicalTop / stepAuthoringPixels));
        var lastY = Math.Min(
            sceneHeight / stepAuthoringPixels,
            (int)MathF.Ceiling(logicalBottom / stepAuthoringPixels));
        for (var index = firstY; index <= lastY; index++)
        {
            var screenY = pan.Y + (index * stepAuthoringPixels * zoom);
            DrawLine(
                new Vector2(visible.Position.X, screenY),
                new Vector2(visible.End.X, screenY),
                color,
                width);
        }
    }
}
