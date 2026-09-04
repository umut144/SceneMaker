using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SceneMaker.Core;
using SceneMaker.Editor;

namespace SceneMaker.App;

/// <summary>Which boundary of a water span the height view colours.</summary>
public enum WaterHeatmapValue
{
    Surface,
    Bed,
    CutTop,
}

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
    // A draft that is neither promised nor refused yet. Cyan rather than red:
    // too few points is the ordinary state of a contour being drawn.
    private static readonly Color DraftPreviewColor = Color.FromHtml("#8FE3FF");
    // One colour per authored hill, so two bodies that meet are still two
    // bodies. Earth, orange, violet and magenta on purpose: they have to read
    // apart from the Assets underneath them - world01's grass is #99E550 and its
    // river #3C7DD9 - and from the four colours that already mean something
    // here: #FFD866 ready and selected, #FF5C5C blocked and erasing, #8FE3FF an
    // unfinished draft and its handles, #FFFFFF a Template preview. One entry
    // per ElevationRegionPalette.Size, indexed by ElevationRegionOutline.PaletteIndex.
    private static readonly Color[] ElevationRegionOutlineColors =
    [
        Color.FromHtml("#D9801F"),
        Color.FromHtml("#8A5A33"),
        Color.FromHtml("#AE72E0"),
        Color.FromHtml("#D4508C"),
        Color.FromHtml("#A03A22"),
        Color.FromHtml("#5B4396"),
    ];
    private static readonly Color TemplateAnchorFill = Color.FromHtml("#FFFFFF");
    private static readonly Color TemplateAnchorBorder = Color.FromHtml("#7B8491");
    private static readonly Color TemplateAnchorText = Color.FromHtml("#252A31");
    private static readonly Color TemplatePreviewOutline = Color.FromHtml("#FFFFFF");
    private static readonly Color WaterCurveColor = Color.FromHtml("#FFD866");
    private static readonly Color WaterHandleColor = Color.FromHtml("#8FE3FF");

    /// <summary>
    /// One hue, dark to light, for reading height as magnitude. It is anchored
    /// dark because the canvas is dark: low ground recedes toward the surface
    /// and high ground stands out. The darkest step still clears the background,
    /// so a low cell stays visible rather than disappearing into it.
    /// </summary>
    private static readonly Color[] ElevationRamp =
    [
        Color.FromHtml("#184F95"),
        Color.FromHtml("#1C5CAB"),
        Color.FromHtml("#256ABF"),
        Color.FromHtml("#2A78D6"),
        Color.FromHtml("#3987E5"),
        Color.FromHtml("#5598E7"),
        Color.FromHtml("#6DA7EC"),
        Color.FromHtml("#86B6EF"),
        Color.FromHtml("#9EC5F4"),
        Color.FromHtml("#B7D3F6"),
        Color.FromHtml("#CDE2FB"),
    ];

    private static readonly Color LegendInk = Color.FromHtml("#E4E9F0");
    private static readonly Color LegendMutedInk = Color.FromHtml("#96A1B2");
    private static readonly Color LegendSurface = Color.FromHtml("#0F1520");

    private LoadedScene? _scene;
    private WorkspaceMetrics? _metrics;
    private SceneDocument? _templatePreview;
    private IReadOnlyList<TemplateTerrainMask> _templatePreviewMasks = [];
    // Folding painted cells and hill contours together is not free, so the
    // result is rebuilt only when the document it was derived from changes.
    private SceneDocument? _effectiveTerrainDocument;
    private IReadOnlyList<TerrainCellDocument> _effectiveTerrain = [];
    // Its own cache with its own key: a contour depends on the bodies alone,
    // where the fold above depends on the whole document.
    private readonly ElevationRegionOutlineCache _elevationRegionOutlines = new();
    private bool _mapContextActive;
    private IReadOnlyDictionary<string, Color> _terrainColors = new Dictionary<string, Color>();
    private TerrainDisplayCatalog? _terrainAssets;
    private PropDisplayCatalog? _propAssets;
    private ToolInteraction _interaction = new();
    private bool _pointerOverCanvas;
    private bool _heatmapEnabled;
    private WaterHeatmapValue _waterHeatmapValue;
    // Rasterizing a corridor is cheap but not free, and the authored bodies do
    // not change between frames. The cache is keyed by the document itself, so
    // it renews on an edit, an undo and a Template preview alike without anyone
    // having to remember to invalidate it.
    private SceneDocument? _waterOverlayDocument;
    private IReadOnlyList<WaterOverlay> _waterOverlays = [];
    private SceneDocument? _routeOverlayDocument;
    private IReadOnlyList<RouteOverlay> _routeOverlays = [];

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
    /// <summary>
    /// The Terrain Asset the active area authors with. Held per area in
    /// `EditorInteractionState`, not here: which Asset an area shows is a
    /// decision, and decisions do not live in the Godot node.
    /// </summary>
    public string? SelectedTerrainAssetKey
    {
        get => _interaction.State.SelectedTerrainAssetKey;
        set
        {
            _interaction.State.SelectTerrainAsset(value);
            QueueRedraw();
        }
    }

    public string? SelectedPropAssetKey { get; set; }

    /// <summary>
    /// Whether the Map context is open. It is a structural overview rather than
    /// an authoring area, so hill bodies stay fully drawn there.
    /// </summary>
    public bool MapContextActive
    {
        get => _mapContextActive;
        set
        {
            _mapContextActive = value;
            QueueRedraw();
        }
    }
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

    /// <summary>
    /// Draws Terrain and Props by their height instead of by their Asset. It is
    /// a way of looking, not a mode of working: every tool keeps working while
    /// it is on.
    /// </summary>
    public bool HeatmapEnabled
    {
        get => _heatmapEnabled;
        set
        {
            _heatmapEnabled = value;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Water has three useful heights rather than one. Terrain and Props keep
    /// showing their elevation while this chooses which water-span boundary is
    /// compared with them.
    /// </summary>
    public WaterHeatmapValue WaterHeatmapValue
    {
        get => _waterHeatmapValue;
        set
        {
            _waterHeatmapValue = value;
            QueueRedraw();
        }
    }

    /// <summary>The Template Anchor the tools currently have selected, if any.</summary>
    public string? SelectedTemplateAnchorId => _interaction.SelectedTemplateAnchorId;

    public int? SelectedElevationRegionPointIndex => _interaction.SelectedElevationRegionPointIndex;
    public string? SelectedElevationRegionId => _interaction.SelectedElevationRegionId;

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

    /// <summary>
    /// Switches mode and answers with whatever the tools had to give up for it,
    /// so the caller can put it in the status line. The canvas decides nothing
    /// about transient tool state; it only redraws and passes the answer on.
    /// </summary>
    public ToolOutcome SelectMode(EditorMode mode)
    {
        var outcome = _interaction.SelectMode(mode);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SelectTool(EditorTool tool)
    {
        var outcome = _interaction.SelectTool(tool);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SetEraserEnabled(bool enabled)
    {
        var outcome = _interaction.SetEraserEnabled(enabled);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SetSelectedElevationRegionPointMode(ElevationRegionPointMode mode)
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.SetSelectedElevationRegionPointMode(context, mode);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SetSelectedElevationRegionElevation(decimal elevationMeters)
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.SetSelectedElevationRegionElevation(context, elevationMeters);
        QueueRedraw();
        return outcome;
    }

    public void ConfigureTerrainAssets(TerrainDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _terrainAssets = catalog;
        var colors = new Dictionary<string, Color>();
        foreach (var asset in catalog.Assets)
            colors.Add(asset.AssetKey, Color.FromHtml(asset.Color));
        _terrainColors = colors;
        _routeOverlayDocument = null;
        _routeOverlays = [];
        QueueRedraw();
    }

    public void ConfigureMetrics(WorkspaceMetrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _effectiveTerrainDocument = null;
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
        _interaction.ResetForScene();
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    public void UpdateScene(LoadedScene scene)
    {
        _scene = scene;
        QueueRedraw();
    }

    private IReadOnlyList<TerrainCellDocument> EffectiveTerrain(SceneDocument document)
    {
        if (ReferenceEquals(_effectiveTerrainDocument, document)) return _effectiveTerrain;
        _effectiveTerrain = _metrics is null
            ? document.TerrainCells
            : ElevationRegionGeometry.EffectiveTerrainCells(document, _metrics);
        _effectiveTerrainDocument = document;
        return _effectiveTerrain;
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
            SelectedTerrainAssetKey,
            SelectedPropAssetKey,
            TemplateAnchorGroupNumber,
            ElevationMeters,
            PointerHitRadiusAuthoringPixels: 8.0 / ViewState.Zoom);
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

        var elevationRange = _heatmapEnabled ? ElevationRange(document) : null;
        DrawTerrain(
            document,
            pan,
            zoom,
            elevationRange,
            highlighted: Mode is EditorMode.Terrain or EditorMode.ElevationRegion);
        DrawWater(
            document,
            pan,
            zoom,
            elevationRange,
            highlighted: Mode is EditorMode.Terrain or EditorMode.River);
        DrawRouteSurfaces(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            elevationRange,
            highlighted: Mode == EditorMode.Path);
        DrawElevationRegionOutlines(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            // Full where a hill is authored, and full in Terrain too: with
            // no other mark on the cells, the contour is the only thing that
            // says an authored body lies there. The Map overview is structural
            // rather than an area, so it keeps them as well.
            highlighted: Mode is EditorMode.Terrain or EditorMode.ElevationRegion || MapContextActive);
        DrawProps(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            elevationRange,
            highlighted: Mode == EditorMode.Props);
        switch (Mode)
        {
            case EditorMode.Props:
                DrawPropToolPreview(pan, zoom, heightAuthoringPixels);
                break;
            case EditorMode.Terrain:
                DrawTerrainToolPreview(document, pan, zoom);
                break;
            case EditorMode.River:
                DrawWaterToolPreview(document, pan, zoom, heightAuthoringPixels);
                break;
            case EditorMode.Path:
                DrawPathToolPreview(
                    document,
                    pan,
                    zoom,
                    heightAuthoringPixels,
                    elevationRange);
                break;
            case EditorMode.ElevationRegion:
                DrawElevationRegionToolPreview(document, pan, zoom, heightAuthoringPixels);
                break;
            default:
                break;
        }

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
        if (elevationRange is { } range) DrawElevationLegend(document, range);
    }

    /// <summary>
    /// Without a scale the colours mean nothing, so the heat map carries its
    /// own: the ramp it actually uses, with the two heights it is stretched
    /// between. A Scene at one height says so instead of showing a range.
    /// </summary>
    private void DrawElevationLegend(
        SceneDocument document,
        (decimal Low, decimal High) range)
    {
        var font = ThemeDB.FallbackFont;
        const int FontSize = 12;
        const float BarWidth = 176f;
        const float BarHeight = 10f;
        const float Padding = 8f;

        var panel = new Rect2(
            new Vector2(Size.X - BarWidth - (Padding * 2f), Padding),
            new Vector2(BarWidth + (Padding * 2f), BarHeight + 34f));
        DrawRect(panel, new Color(LegendSurface.R, LegendSurface.G, LegendSurface.B, 0.88f));
        DrawRect(panel, new Color(LegendMutedInk.R, LegendMutedInk.G, LegendMutedInk.B, 0.45f),
            filled: false, width: 1f);

        DrawString(
            font,
            panel.Position + new Vector2(Padding, 14f),
            document.WaterBodies.Count == 0
                ? "Height (m)"
                : $"Height · {WaterHeatmapLabel()} (m)",
            HorizontalAlignment.Left,
            width: -1f,
            fontSize: FontSize,
            modulate: LegendMutedInk);

        var barTop = panel.Position.Y + 20f;
        // One rectangle per ramp step is enough at this size and avoids a
        // gradient texture the canvas would otherwise have to own.
        var stepWidth = BarWidth / ElevationRamp.Length;
        for (var step = 0; step < ElevationRamp.Length; step++)
        {
            DrawRect(
                new Rect2(
                    new Vector2(panel.Position.X + Padding + (step * stepWidth), barTop),
                    new Vector2(stepWidth + 1f, BarHeight)),
                ElevationRamp[step]);
        }

        var baseline = barTop + BarHeight + 13f;
        if (range.Low == range.High)
        {
            DrawString(
                font,
                new Vector2(panel.Position.X + Padding, baseline),
                $"all at {range.High:0.###}",
                HorizontalAlignment.Left,
                width: -1f,
                fontSize: FontSize,
                modulate: LegendInk);
            return;
        }

        DrawString(
            font,
            new Vector2(panel.Position.X + Padding, baseline),
            $"{range.Low:0.###}",
            HorizontalAlignment.Left,
            width: -1f,
            fontSize: FontSize,
            modulate: LegendInk);
        DrawString(
            font,
            new Vector2(panel.Position.X + Padding, baseline),
            $"{range.High:0.###}",
            HorizontalAlignment.Right,
            width: BarWidth,
            fontSize: FontSize,
            modulate: LegendInk);
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

    /// <summary>
    /// The lowest and highest height currently represented in the Scene, or
    /// null when it has none. Water contributes the selected span boundary, so
    /// changing Surface/Bed/Cut top changes one coherent scale for everything.
    /// </summary>
    private (decimal Low, decimal High)? ElevationRange(SceneDocument document)
    {
        decimal? low = null;
        decimal? high = null;
        IEnumerable<decimal> elevations = EffectiveTerrain(document)
                     .Select(static cell => cell.ElevationMeters)
                     .Concat(document.Props.Select(static prop => prop.ElevationMeters))
                     .Concat(document.RouteSurfaces.SelectMany(
                         static route => route.Points.Select(
                             static point => point.ElevationMeters)))
                     .Concat(WaterOverlays(document).SelectMany(
                         overlay => overlay.Cells.Select(WaterElevation)));
        // An unfinished Path is already geometry the author is judging. Include
        // it in the same scale as the Scene, so Auto start can be verified in
        // the height view before Enter instead of being hidden by Asset colour.
        if (Mode == EditorMode.Path
            && !EraserEnabled
            && CurrentContext() is { } context)
        {
            elevations = elevations.Concat(
                _interaction.PathPreview(context).Curve.Select(
                    static point => point.ElevationMeters));
        }
        foreach (var elevation in elevations)
        {
            low = low is null || elevation < low ? elevation : low;
            high = high is null || elevation > high ? elevation : high;
        }
        return low is null || high is null ? null : (low.Value, high.Value);
    }

    private decimal WaterElevation(WaterCellSpan cell) => _waterHeatmapValue switch
    {
        WaterHeatmapValue.Surface => cell.SurfaceMeters,
        WaterHeatmapValue.Bed => cell.BedMeters,
        WaterHeatmapValue.CutTop => cell.CutTopMeters,
        _ => throw new InvalidOperationException("Unknown water heatmap value."),
    };

    private string WaterHeatmapLabel() => _waterHeatmapValue switch
    {
        WaterHeatmapValue.Surface => "Water surface",
        WaterHeatmapValue.Bed => "River bed",
        WaterHeatmapValue.CutTop => "Cut top",
        _ => throw new InvalidOperationException("Unknown water heatmap value."),
    };

    private static Color ElevationColor(decimal elevation, (decimal Low, decimal High) range)
    {
        var span = range.High - range.Low;
        var position = span == 0m ? 1f : (float)((elevation - range.Low) / span);
        var scaled = Math.Clamp(position, 0f, 1f) * (ElevationRamp.Length - 1);
        var lower = (int)scaled;
        if (lower >= ElevationRamp.Length - 1) return ElevationRamp[^1];
        return ElevationRamp[lower].Lerp(ElevationRamp[lower + 1], scaled - lower);
    }

    private void DrawTerrain(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        (decimal Low, decimal High)? range,
        bool highlighted)
    {
        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        foreach (var cell in EffectiveTerrain(document))
        {
            Color color;
            if (range is { } span) color = ElevationColor(cell.ElevationMeters, span);
            else if (!_terrainColors.TryGetValue(cell.AssetKey, out color)) continue;
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
                highlighted || _heatmapEnabled
                    ? color
                    : new Color(color.R, color.G, color.B, 0.24f));
        }
    }

    /// <summary>
    /// The authored water, drawn as the cells it covers rather than as the
    /// curve behind it: what the corridor rule produces is what the simulation
    /// reads, so it is what the author has to be able to see.
    /// </summary>
    private void DrawWater(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        (decimal Low, decimal High)? range,
        bool highlighted)
    {
        if (document.WaterBodies.Count == 0) return;
        var cellSize = _metrics!.AuthoringPixelsPerWaterCell * zoom;
        var rows = _metrics.SceneHeightWaterCells(document);
        foreach (var overlay in WaterOverlays(document))
        {
            foreach (var cell in overlay.Cells)
            {
                var color = range is { } span
                    ? ElevationColor(WaterElevation(cell), span)
                    : highlighted
                        ? overlay.Color
                        : new Color(overlay.Color.R, overlay.Color.G, overlay.Color.B, 0.24f);
                var rectangle = new Rect2(
                    pan + new Vector2(cell.X * cellSize, (rows - cell.Y - 1) * cellSize),
                    new Vector2(cellSize, cellSize));
                if (rectangle.End.X < 0f || rectangle.End.Y < 0f
                    || rectangle.Position.X > Size.X || rectangle.Position.Y > Size.Y)
                    continue;
                DrawRect(rectangle, color);
            }
        }
    }

    private IReadOnlyList<WaterOverlay> WaterOverlays(SceneDocument document)
    {
        if (ReferenceEquals(_waterOverlayDocument, document)) return _waterOverlays;

        List<WaterOverlay> overlays = new(document.WaterBodies.Count);
        foreach (var body in document.WaterBodies)
        {
            if (!_terrainColors.TryGetValue(body.AssetKey, out var color)) continue;
            overlays.Add(new WaterOverlay(
                color,
                WaterGeometry.Corridor(document, _metrics!, body)));
        }
        _waterOverlayDocument = document;
        _waterOverlays = overlays;
        return _waterOverlays;
    }

    /// <summary>
    /// Route surfaces remain continuous bands in plan; drawing them through a
    /// cell raster here would falsely turn their inclined height profile into
    /// Terrain steps.
    /// </summary>
    private void DrawRouteSurfaces(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        (decimal Low, decimal High)? range,
        bool highlighted)
    {
        foreach (var overlay in RouteOverlays(document))
        {
            var color = highlighted
                ? overlay.Color
                : new Color(overlay.Color.R, overlay.Color.G, overlay.Color.B, 0.24f);
            DrawBakedRouteBand(
                overlay.Bake,
                color,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                range);
            var outline = highlighted
                ? SelectionColor
                : new Color(SelectionColor.R, SelectionColor.G, SelectionColor.B, 0.28f);
            DrawRouteCenterline(overlay.Surface, outline, pan, zoom, sceneHeightAuthoringPixels);
        }
    }

    private IReadOnlyList<RouteOverlay> RouteOverlays(SceneDocument document)
    {
        if (ReferenceEquals(_routeOverlayDocument, document)) return _routeOverlays;

        List<RouteOverlay> overlays = new(document.RouteSurfaces.Count);
        foreach (var route in document.RouteSurfaces)
        {
            if (!_terrainColors.TryGetValue(route.AssetKey, out var color)) continue;
            overlays.Add(new RouteOverlay(
                color,
                RouteSurfaceGeometry.Prepare(_metrics!, route),
                RouteSurfaceBake.Build(_metrics!, route)));
        }
        _routeOverlayDocument = document;
        _routeOverlays = overlays;
        return _routeOverlays;
    }

    private void DrawRouteBand(
        PreparedRouteSurface surface,
        Color color,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        (decimal Low, decimal High)? range = null)
    {
        Vector2 Screen(double x, double y) => pan + new Vector2(
            (float)x * zoom,
            (sceneHeightAuthoringPixels - (float)y) * zoom);

        foreach (var segment in surface.Corridor.Segments)
        {
            var chain = segment.Chain;
            var length = Math.Sqrt(chain.LengthSquared);
            if (length <= 0.0) continue;
            var normalX = -chain.DeltaY / length;
            var normalY = chain.DeltaX / length;
            var endX = chain.StartX + chain.DeltaX;
            var endY = chain.StartY + chain.DeltaY;
            Vector2[] polygon =
            [
                Screen(
                    chain.StartX + normalX * segment.StartHalfWidth,
                    chain.StartY + normalY * segment.StartHalfWidth),
                Screen(
                    endX + normalX * segment.EndHalfWidth,
                    endY + normalY * segment.EndHalfWidth),
                Screen(
                    endX - normalX * segment.EndHalfWidth,
                    endY - normalY * segment.EndHalfWidth),
                Screen(
                    chain.StartX - normalX * segment.StartHalfWidth,
                    chain.StartY - normalY * segment.StartHalfWidth),
            ];
            var segmentColor = range is { } elevationRange
                ? ElevationColor(
                    (decimal)surface.ElevationAt(
                        segment.Chain.StartStation + (length / 2.0)),
                    elevationRange)
                : color;
            DrawColoredPolygon(polygon, segmentColor);
            if (!segment.CapsAtStart)
                DrawCircle(
                    Screen(chain.StartX, chain.StartY),
                    (float)segment.StartHalfWidth * zoom,
                    segmentColor);
            if (!segment.CapsAtEnd)
                DrawCircle(
                    Screen(endX, endY),
                    (float)segment.EndHalfWidth * zoom,
                    segmentColor);
        }
    }

    /// <summary>
    /// Draws persisted Paths from the same runtime bake that the export ships.
    /// This is the visible contract: changing tessellation, joins or caps means
    /// changing one Core bake rather than two implementations.
    /// </summary>
    private void DrawBakedRouteBand(
        BakedRouteSurface bake,
        Color color,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        (decimal Low, decimal High)? range = null)
    {
        var pixelsPerMeter = (float)_metrics!.AuthoringPixelsPerMeter;
        Vector2 Screen(RouteSurfaceBakeVertex vertex) => pan + new Vector2(
            (float)vertex.XMeters * pixelsPerMeter * zoom,
            (sceneHeightAuthoringPixels - (float)vertex.YMeters * pixelsPerMeter) * zoom);

        for (var index = 0; index < bake.TriangleIndices.Count; index += 3)
        {
            var first = bake.Vertices[bake.TriangleIndices[index]];
            var second = bake.Vertices[bake.TriangleIndices[index + 1]];
            var third = bake.Vertices[bake.TriangleIndices[index + 2]];
            var triangleColor = range is { } elevationRange
                ? ElevationColor(
                    (first.ElevationMeters + second.ElevationMeters + third.ElevationMeters) / 3m,
                    elevationRange)
                : color;
            DrawColoredPolygon(
                [Screen(first), Screen(second), Screen(third)],
                triangleColor);
        }
    }

    private void DrawRouteCenterline(
        PreparedRouteSurface surface,
        Color color,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        var points = surface.Centerline.Points;
        if (points.Count < 2) return;
        var line = new Vector2[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            line[index] = pan + new Vector2(
                (float)points[index].X * zoom,
                (sceneHeightAuthoringPixels - (float)points[index].Y) * zoom);
        }
        DrawPolyline(line, color, 2.0f);
    }

    /// <summary>
    /// Every authored hill as its own closed contour in its own colour.
    ///
    /// <para>The fill stays what the fold produced - the painted Terrain
    /// Asset's colour - so the surface material is still readable; the contour
    /// says where one body ends, which folded cells alone never could. A
    /// hill carries no material, so two of them over the same paint are one
    /// indistinguishable surface without it, and the height view was the only
    /// way to guess at their edges. It is not an analysis, so it is drawn
    /// whether or not that view is on.</para>
    /// </summary>
    private void DrawElevationRegionOutlines(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        bool highlighted)
    {
        var outlines = _elevationRegionOutlines.For(document);
        if (outlines.Count == 0) return;

        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        foreach (var outline in outlines)
        {
            // The selection preview draws this body again, either as stored or
            // as the live candidate. Leaving the cached contour underneath it
            // would show two shapes during a drag and make a refused move look
            // as though both contours were part of the hill.
            if (Mode == EditorMode.ElevationRegion
                && ActiveTool == EditorTool.SelectElevationRegion
                && string.Equals(
                    outline.ElevationRegionId,
                    _interaction.SelectedElevationRegionId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            var color = ElevationRegionOutlineColors[outline.PaletteIndex];
            if (!highlighted) color = new Color(color.R, color.G, color.B, 0.32f);
            // The ring does not repeat its first point, so the line closes here.
            var line = new Vector2[outline.Points.Count + 1];
            for (var index = 0; index < outline.Points.Count; index++)
                line[index] = Screen(outline.Points[index].X, outline.Points[index].Y);
            line[^1] = line[0];
            DrawPolyline(line, color, 2.0f);
        }
    }

    /// <summary>
    /// The river being drawn: its corridor in the Asset's own colour, so the
    /// author sees the water rather than a symbol for it, and over that the
    /// curve, its points and the handles that shape them.
    /// </summary>
    private void DrawWaterToolPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        var preview = ToolPreviewBuilder.BuildWaterDraft(
            document,
            _metrics!,
            ActiveTool,
            _interaction.RiverDraft,
            _interaction.RiverPendingPoint);
        if (preview.Points.Count == 0) return;

        var waterCellSize = _metrics!.AuthoringPixelsPerWaterCell * zoom;
        var rows = _metrics.SceneHeightWaterCells(document);
        var fill = SelectedTerrainAssetKey is { } assetKey
            && _terrainColors.TryGetValue(assetKey, out var assetColor)
                ? assetColor
                : WaterCurveColor;
        foreach (var cell in preview.Cells)
        {
            DrawRect(
                new Rect2(
                    pan + new Vector2(cell.X * waterCellSize, (rows - cell.Y - 1) * waterCellSize),
                    new Vector2(waterCellSize, waterCellSize)),
                new Color(fill.R, fill.G, fill.B, 0.55f));
        }

        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        if (preview.Centerline.Count >= 2)
        {
            var line = new Vector2[preview.Centerline.Count];
            for (var index = 0; index < preview.Centerline.Count; index++)
                line[index] = Screen(preview.Centerline[index].X, preview.Centerline[index].Y);
            DrawPolyline(line, WaterCurveColor, 2.0f);
        }

        for (var index = 0; index < preview.Curve.Count; index++)
        {
            var point = preview.Curve[index];
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleInAuthoringPx, Screen);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleOutAuthoringPx, Screen);
            // The source and the mouth are what the runtime reads as flow
            // direction, so they are drawn as more than another point.
            var isEnd = index == 0 || index == preview.Curve.Count - 1;
            DrawCircle(centre, isEnd ? 5.0f : 3.5f, WaterCurveColor);
        }
    }

    private void DrawPathToolPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        (decimal Low, decimal High)? elevationRange)
    {
        if (EraserEnabled)
        {
            if (_interaction.PointerAuthoring is not { } pointer) return;
            var route = RouteSurfaceEditing.FindAt(
                document, _metrics!, pointer.X, pointer.Y);
            if (route is null) return;
            DrawBakedRouteBand(
                RouteSurfaceBake.Build(_metrics!, route),
                new Color(InvalidPreviewColor.R, InvalidPreviewColor.G, InvalidPreviewColor.B, 0.55f),
                pan,
                zoom,
                sceneHeightAuthoringPixels);
            return;
        }

        var preview = _interaction.PathPreview(CurrentContext()!);
        if (preview.Points.Count == 0) return;
        var fill = SelectedTerrainAssetKey is { } assetKey
            && _terrainColors.TryGetValue(assetKey, out var assetColor)
                ? assetColor
                : DraftPreviewColor;
        if (preview.Surface is { } surface)
        {
            DrawRouteBand(
                surface,
                new Color(fill.R, fill.G, fill.B, 0.55f),
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                elevationRange);
            DrawRouteCenterline(
                surface,
                ValidPreviewColor,
                pan,
                zoom,
                sceneHeightAuthoringPixels);
        }

        Vector2 Screen(double x, double y) => pan + new Vector2(
            (float)x * zoom,
            (sceneHeightAuthoringPixels - (float)y) * zoom);
        var ink = preview.Surface is null && preview.Points.Count >= 2
            ? InvalidPreviewColor
            : DraftPreviewColor;
        foreach (var point in preview.Curve)
        {
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleInAuthoringPx, Screen);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleOutAuthoringPx, Screen);
            DrawCircle(centre, 4.0f, ink);
        }
    }

    private void DrawElevationRegionToolPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        if (ActiveTool == EditorTool.SelectElevationRegion)
        {
            DrawElevationRegionSelectionPreview(pan, zoom, sceneHeightAuthoringPixels);
            return;
        }
        if (EraserEnabled)
        {
            DrawElevationRegionEraserPreview(document, pan, zoom);
            return;
        }

        var preview = ToolPreviewBuilder.BuildElevationRegionDraft(
            document,
            _metrics!,
            ActiveTool,
            _interaction.ElevationRegionDraft,
            _interaction.ElevationRegionPendingPoint,
            ElevationMeters);
        if (preview.Points.Count == 0) return;

        // Yellow is a promise that Enter would take this contour, red that it
        // would refuse it, and cyan that the author is not finished asking.
        var outlineColor = preview.Kind switch
        {
            ElevationRegionDraftKind.Ready => ValidPreviewColor,
            ElevationRegionDraftKind.Blocked => InvalidPreviewColor,
            _ => DraftPreviewColor,
        };
        // Each raised cell in the colour of the Asset it already carries, which
        // the preview hands over with it. A contour across a sand and grass
        // boundary therefore previews as sand and grass, because that is what
        // the hill would lift: the height, not the material.
        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        foreach (var cell in preview.RaisedCells)
        {
            var fill = _terrainColors.TryGetValue(cell.AssetKey, out var assetColor)
                ? assetColor
                : outlineColor;
            DrawRect(
                CellRectangle(document, cell.X, cell.Y, pan, cellSize),
                new Color(fill.R, fill.G, fill.B, 0.55f));
        }

        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        if (preview.Outline.Count >= 2)
        {
            var line = new Vector2[preview.Outline.Count + 1];
            for (var index = 0; index < preview.Outline.Count; index++)
                line[index] = Screen(preview.Outline[index].X, preview.Outline[index].Y);
            line[^1] = line[0];
            DrawPolyline(line, outlineColor, 2.0f);
        }
        else if (preview.Points.Count >= 2)
        {
            var line = preview.Points
                .Select(point => Screen(point.X, point.Y))
                .ToArray();
            DrawPolyline(line, outlineColor, 2.0f);
        }

        foreach (var point in preview.Curve)
        {
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleInAuthoringPx, Screen);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleOutAuthoringPx, Screen);
            DrawCircle(centre, 4.0f, outlineColor);
        }
    }

    /// <summary>
    /// A selected hill exposes its authored anchors and handles. While one
    /// anchor moves, this draws the candidate contour rather than the stored
    /// one; an invalid candidate turns red and will be refused on release.
    /// </summary>
    private void DrawElevationRegionSelectionPreview(
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        if (CurrentContext() is not { } context) return;
        var preview = _interaction.ElevationRegionSelectionPreview(context);
        if (preview.Body is not { } body) return;

        var color = preview.Kind == ElevationRegionDraftKind.Blocked
            ? InvalidPreviewColor
            : ElevationRegionOutlineColors[ElevationRegionPalette.IndexOf(body.ElevationRegionId)];
        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        if (preview.Outline.Count >= 2)
        {
            var line = new Vector2[preview.Outline.Count + 1];
            for (var index = 0; index < preview.Outline.Count; index++)
                line[index] = Screen(preview.Outline[index].X, preview.Outline[index].Y);
            line[^1] = line[0];
            DrawPolyline(line, color, 3.0f);
        }

        for (var index = 0; index < body.Points.Count; index++)
        {
            var point = body.Points[index];
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawElevationRegionSelectionHandle(
                centre, point, index, ElevationRegionHandleSide.In, Screen);
            DrawElevationRegionSelectionHandle(
                centre, point, index, ElevationRegionHandleSide.Out, Screen);
            DrawCircle(
                centre,
                _interaction.SelectedElevationRegionPointIndex == index ? 6.0f : 4.0f,
                color);
        }
    }

    private void DrawElevationRegionSelectionHandle(
        Vector2 centre,
        ElevationRegionPointDocument point,
        int pointIndex,
        ElevationRegionHandleSide side,
        Func<double, double, Vector2> screen)
    {
        var handle = side == ElevationRegionHandleSide.In
            ? point.HandleInAuthoringPx
            : point.HandleOutAuthoringPx;
        if (handle.IsZero()) return;
        var tip = screen(
            point.PositionAuthoringPx.X + handle.X,
            point.PositionAuthoringPx.Y + handle.Y);
        DrawLine(centre, tip, WaterHandleColor, 1.5f);
        var selected = _interaction.SelectedElevationRegionPointIndex == pointIndex;
        var dragged = selected && _interaction.DraggedElevationRegionHandleSide == side;
        DrawCircle(tip, dragged ? 5.0f : selected ? 4.0f : 3.0f, WaterHandleColor);
    }

    /// <summary>
    /// What a click would remove, said in two marks because it is two things.
    /// The contour in the erase colour is the body itself, all of it, including
    /// the part standing over unpainted ground; the filled cells are the painted
    /// Terrain whose height this body is holding up, and only those drop.
    /// Marking one cell under the pointer would say a cell goes, and a hill
    /// is erased whole.
    /// </summary>
    private void DrawElevationRegionEraserPreview(SceneDocument document, Vector2 pan, float zoom)
    {
        var preview = ToolPreviewBuilder.BuildElevationRegionEraser(
            document,
            _metrics!,
            ActiveTool,
            EraserEnabled,
            _interaction.PointerCell);
        if (preview.ElevationRegionId is not { } bodyId) return;

        var cellSize = _metrics!.AuthoringPixelsPerTerrainCell * zoom;
        foreach (var cell in preview.LoweredCells)
        {
            var rectangle = CellRectangle(document, cell.X, cell.Y, pan, cellSize);
            DrawRect(
                rectangle,
                new Color(
                    InvalidPreviewColor.R,
                    InvalidPreviewColor.G,
                    InvalidPreviewColor.B,
                    0.35f));
            DrawRect(rectangle, InvalidPreviewColor, filled: false, width: 2f);
        }

        // The outline the canvas already derived for every body, picked out by
        // ID rather than flattened again.
        var outline = _elevationRegionOutlines.For(document)
            .FirstOrDefault(candidate => string.Equals(
                candidate.ElevationRegionId, bodyId, StringComparison.Ordinal));
        if (outline is null || outline.Points.Count < 2) return;

        var sceneHeightAuthoringPixels = _metrics.SceneHeightAuthoringPixels(document);
        var line = new Vector2[outline.Points.Count + 1];
        for (var index = 0; index < outline.Points.Count; index++)
        {
            line[index] = pan + new Vector2(
                (float)outline.Points[index].X * zoom,
                (sceneHeightAuthoringPixels - (float)outline.Points[index].Y) * zoom);
        }
        line[^1] = line[0];
        DrawPolyline(line, InvalidPreviewColor, 2.5f);
    }

    /// <summary>One Terrain cell's rectangle on screen, y flipped to Godot's.</summary>
    private static Rect2 CellRectangle(
        SceneDocument document,
        int cellX,
        int cellY,
        Vector2 pan,
        float cellSize) =>
        new(
            pan + new Vector2(cellX * cellSize, (document.SizeCells.Height - cellY - 1) * cellSize),
            new Vector2(cellSize, cellSize));

    private void DrawHandle(
        Vector2 centre,
        AuthoringPixelPosition position,
        AuthoringPixelOffset handle,
        Func<double, double, Vector2> screen)
    {
        if (handle.IsZero()) return;
        var tip = screen(position.X + handle.X, position.Y + handle.Y);
        DrawLine(centre, tip, WaterHandleColor, 1.5f);
        DrawCircle(tip, 3.0f, WaterHandleColor);
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
        (decimal Low, decimal High)? range,
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
            var color = range is { } propSpan
                ? ElevationColor(prop.ElevationMeters, propSpan)
                : Color.FromHtml(asset.Color);
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
            var color = candidate.Kind == PropPreviewKind.Ready
                ? ValidPreviewColor
                : InvalidPreviewColor;
            DrawRect(rectangle, new Color(color.R, color.G, color.B, 0.22f));
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
                SelectedPropAssetKey,
                tool,
                pointer,
                lineStart,
                lineEnd,
                _interaction.State.PropLineOffsetAuthoringPixels);

    /// <summary>One authored body's cells, in the colour of its Asset.</summary>
    private sealed record WaterOverlay(Color Color, IReadOnlyList<WaterCellSpan> Cells);
    private sealed record RouteOverlay(
        Color Color,
        PreparedRouteSurface Surface,
        BakedRouteSurface Bake);

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
