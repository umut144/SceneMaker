using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SceneMaker.Core;
using SceneMaker.Editor;

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
    // Terrain coverage is checked once per Prop and once per line preview anchor
    // on every frame. Both sets are rebuilt only when their document changes.
    private IReadOnlySet<TerrainCellCoordinate> _sceneTerrain = new HashSet<TerrainCellCoordinate>();
    private IReadOnlySet<TerrainCellCoordinate> _previewTerrain = new HashSet<TerrainCellCoordinate>();
    private IReadOnlyDictionary<string, Color> _terrainColors = new Dictionary<string, Color>();
    private TerrainDisplayCatalog? _terrainAssets;
    private PropDisplayCatalog? _propAssets;
    private ToolInteraction _interaction = new();
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
            _interaction.PointerLeft();
            QueueRedraw();
        };
        MouseEntered += () => _pointerOverCanvas = true;
    }

    public CanvasViewState ViewState { get; private set; } = new();
    public LoadedScene? Scene => _scene;
    public string? SelectedTerrainAssetKey { get; set; }
    public string? SelectedPropAssetKey { get; set; }
    public EditorMode Mode => _interaction.Mode;
    public EditorTool ActiveTool => _interaction.ActiveTool;
    public bool EraserEnabled => _interaction.EraserEnabled;

    /// <summary>Group number a newly placed Template Anchor receives.</summary>
    public int TemplateAnchorGroupNumber { get; set; } = 1;

    /// <summary>
    /// The height the drawing tools author at. Set from the Scene's own default
    /// when a Scene opens, then adjustable per stroke in the context bar.
    /// </summary>
    public decimal ElevationMeters { get; set; } = SceneDocument.GroundElevationMeters;

    /// <summary>The Template Anchor the tools currently have selected, if any.</summary>
    public string? SelectedTemplateAnchorId => _interaction.SelectedTemplateAnchorId;

    public event Action? ViewChanged;

    /// <summary>
    /// The single channel for everything the tools decide. Every pointer and key
    /// event is answered with one outcome, which the editor applies or shows.
    /// </summary>
    public event Action<ToolOutcome>? OutcomeProduced;

    /// <summary>Raised when the primary pointer button is released, which ends
    /// a continuous edit stroke.</summary>
    public event Action? StrokeEnded;

    public void ConfigureInteraction(ToolInteraction interaction)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        QueueRedraw();
    }

    public void SelectMode(EditorMode mode)
    {
        _interaction.SelectMode(mode);
        QueueRedraw();
    }

    public void SelectTool(EditorTool tool)
    {
        _interaction.SelectTool(tool);
        QueueRedraw();
    }

    public void SetEraserEnabled(bool enabled)
    {
        _interaction.SetEraserEnabled(enabled);
        QueueRedraw();
    }

    public void ConfigureTerrainAssets(TerrainDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _terrainAssets = catalog;
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
        _sceneTerrain = AuthoredTerrain(scene?.Document);
        _templatePreview = null;
        _templatePreviewMasks = [];
        _previewTerrain = AuthoredTerrain(null);
        ViewState = new CanvasViewState();
        _interaction.ResetForScene();
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public void UpdateScene(LoadedScene scene)
    {
        _scene = scene;
        _sceneTerrain = AuthoredTerrain(scene.Document);
        QueueRedraw();
    }

    private static IReadOnlySet<TerrainCellCoordinate> AuthoredTerrain(SceneDocument? document) =>
        document is null
            ? new HashSet<TerrainCellCoordinate>()
            : TerrainCoverage.AuthoredCells(document);

    public void ShowTemplatePreview(
        SceneDocument? scene,
        IReadOnlyList<TemplateTerrainMask>? effectiveTerrainMasks = null)
    {
        _templatePreview = scene;
        _previewTerrain = AuthoredTerrain(scene);
        _templatePreviewMasks = effectiveTerrainMasks ?? [];
        QueueRedraw();
    }

    public void SelectProp(string? instanceId)
    {
        _interaction.SelectProp(instanceId);
        QueueRedraw();
    }

    public void SelectTemplateAnchor(string? anchorId)
    {
        _interaction.SelectTemplateAnchor(anchorId);
        QueueRedraw();
    }

    /// <summary>
    /// Keeps the tool selection honest after the document changed, whatever the
    /// reason: an edit, an undo, or a freshly loaded Scene.
    /// </summary>
    public void NotifySceneChanged(SceneDocument? before, SceneDocument after)
    {
        _interaction.SceneChanged(before, after);
        QueueRedraw();
    }

    /// <summary>The Scene, catalogs and pointer state the tools work against.</summary>
    private ToolContext? CurrentContext()
    {
        if (_scene is null || _terrainAssets is null || _propAssets is null || _metrics is null)
            return null;
        return new ToolContext(
            _scene.Document,
            _terrainAssets,
            _propAssets,
            _metrics,
            _sceneTerrain,
            SelectedTerrainAssetKey,
            SelectedPropAssetKey,
            TemplateAnchorGroupNumber,
            ElevationMeters);
    }

    private void Publish(ToolOutcome outcome)
    {
        if (outcome is ToolOutcome.Idle) return;
        OutcomeProduced?.Invoke(outcome);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (CurrentContext() is not { } context) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            var authoring = AuthoringCoordinate(button.Position);
            var cell = TerrainCoordinate(button.Position);
            if (button.Pressed)
            {
                GrabFocus();
                Publish(_interaction.PointerPressed(context, authoring, cell));
            }
            else
            {
                _interaction.PointerMoved(authoring, cell);
                Publish(_interaction.PointerReleased(context));
                StrokeEnded?.Invoke();
            }
            QueueRedraw();
        }
        else if (input is InputEventMouseMotion motion)
        {
            var authoring = AuthoringCoordinate(motion.Position);
            var cell = TerrainCoordinate(motion.Position);
            if ((motion.ButtonMask & MouseButtonMask.Left) != 0)
                Publish(_interaction.PointerDragged(context, authoring, cell));
            else
                _interaction.PointerMoved(authoring, cell);
            QueueRedraw();
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey keyEvent) return;
        // Command/Control shortcuts belong to the application, not the canvas.
        if (keyEvent.IsCommandOrControlPressed()) return;

        if (keyEvent.Pressed && ToolKeyFor(keyEvent.Keycode) is { } toolKey
            && CurrentContext() is { } context)
        {
            var outcome = _interaction.KeyPressed(context, toolKey);
            if (outcome is not ToolOutcome.Idle)
            {
                Publish(outcome);
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }
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
        if (_pointerOverCanvas
            && !Input.IsKeyPressed(Key.Ctrl)
            && !Input.IsKeyPressed(Key.Meta))
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

        var authoredTerrain = _templatePreview is null ? _sceneTerrain : _previewTerrain;
        DrawTerrain(document, pan, zoom, highlighted: Mode == EditorMode.Terrain);
        DrawProps(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            authoredTerrain,
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

    private TerrainCellCoordinate TerrainCoordinate(Vector2 screenPosition)
    {
        var (x, y) = ViewState.ScreenToTerrainCell(
            screenPosition.X,
            screenPosition.Y,
            _scene!.Document.SizeCells.Height,
            _metrics!.AuthoringPixelsPerTerrainCell);
        return new TerrainCellCoordinate(x, y);
    }

    private AuthoringPoint AuthoringCoordinate(Vector2 screenPosition)
    {
        var (x, y) = ViewState.ScreenToAuthoringPixel(
            screenPosition.X,
            screenPosition.Y,
            _metrics!.SceneHeightAuthoringPixels(_scene!.Document));
        return new AuthoringPoint(x, y);
    }

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
        var preview = ToolPreviewBuilder.BuildTerrain(
            document,
            ActiveTool,
            EraserEnabled,
            _interaction.PointerCell,
            _interaction.TerrainLineStart);

        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        var color = preview.Erasing ? InvalidPreviewColor : SelectionColor;
        foreach (var cell in preview.Cells)
        {
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
        IReadOnlySet<TerrainCellCoordinate> authoredTerrain,
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
            var selected = highlighted && prop.InstanceId == _interaction.SelectedPropInstanceId;
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
            if (!TerrainCoverage.IsComplete(authoredTerrain, bounds, _metrics!))
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
            var position = anchor.AnchorId == _interaction.DraggedAnchorId
                           && _interaction.DraggedAnchorPosition is { } preview
                ? new AuthoringPixelPosition { X = preview.X, Y = preview.Y }
                : anchor.PositionAuthoringPx;
            DrawTemplateAnchor(
                position,
                anchor.GroupNumber.ToString(),
                anchor.AnchorId == _interaction.SelectedTemplateAnchorId,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                highlighted);
        }

        if (highlighted
            && ActiveTool == EditorTool.AnchorPlace
            && _interaction.PointerAuthoring is { } pointer)
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
        foreach (var candidate in CurrentPropPreviews())
        {
            var rectangle = CanvasRectangle(
                candidate.Bounds,
                pan,
                zoom,
                sceneHeightAuthoringPixels);
            var missingTerrain = candidate.Kind == PropPreviewKind.MissingTerrain;
            var color = candidate.Kind == PropPreviewKind.Ready
                ? ValidPreviewColor
                : InvalidPreviewColor;
            DrawRect(
                rectangle,
                new Color(color.R, color.G, color.B, missingTerrain ? 0.08f : 0.22f));
            if (missingTerrain)
                DrawDashedRectangle(rectangle, color, 2f);
            else
                DrawRect(rectangle, color, filled: false, width: 2f);
            DrawAnchor(
                candidate.Anchor.X,
                candidate.Anchor.Y,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                color);
        }
    }

    /// <summary>What the Prop tool would author where the pointer currently is.</summary>
    private IReadOnlyList<PropPreview> CurrentPropPreviews() =>
        BuildPropPreviews(
            ActiveTool,
            _interaction.PointerAuthoring,
            _interaction.PropLineStart,
            _interaction.PropLineEnd);

    private IReadOnlyList<PropPreview> BuildPropPreviews(
        EditorTool tool,
        AuthoringPoint? pointer,
        AuthoringPoint? lineStart,
        AuthoringPoint? lineEnd) =>
        _scene is null || _propAssets is null
            ? []
            : ToolPreviewBuilder.BuildProps(
                _scene.Document,
                _propAssets,
                _sceneTerrain,
                SelectedPropAssetKey,
                tool,
                pointer,
                lineStart,
                lineEnd,
                _interaction.State.PropLineOffsetAuthoringPixels);

    private static ToolKey? ToolKeyFor(Key keycode) => keycode switch
    {
        Key.Escape => ToolKey.Escape,
        Key.Enter or Key.KpEnter => ToolKey.Enter,
        _ => null,
    };

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
