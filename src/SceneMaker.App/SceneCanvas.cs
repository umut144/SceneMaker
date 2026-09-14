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
    // The single area actively being authored - River inside Landscape, Bridge
    // inside Structures - draws with this outline on top of its ordinary
    // highlight, so the one area the author is working stands out even among
    // the other areas the same context is showing for legibility.
    private static readonly Color ActiveAreaAccent = Color.FromHtml("#FFFFFF");
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
    // The river `Re-Attach` is holding. Violet because yellow already means
    // "a press would take this", and during the middle phases of Re-Attach the
    // chosen river is the one thing a press cannot take.
    private static readonly Color ReAttachChosenColor = Color.FromHtml("#B47CFF");
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
    private bool _landscapeContextActive;
    private IReadOnlyDictionary<string, Color> _terrainColors = new Dictionary<string, Color>();
    private TerrainDisplayCatalog? _terrainAssets;
    private BridgeKitResolution? _bridgeKit;
    private PropDisplayCatalog? _propAssets;
    private IReadOnlyDictionary<string, Color> _propColors = new Dictionary<string, Color>();
    private ToolInteraction _interaction = new();
    private bool _pointerOverCanvas;
    private WaterHeatmapValue _waterHeatmapValue;
    // Rasterizing a corridor is cheap but not free, and the authored bodies do
    // not change between frames. The cache is keyed by the document itself, so
    // it renews on an edit, an undo and a Template preview alike without anyone
    // having to remember to invalidate it.
    private SceneDocument? _visibleSource;
    private SceneDocument? _visibleDocument;
    private int _visibleRevision = -1;
    private SceneDocument? _waterOverlayDocument;
    private IReadOnlyList<WaterOverlay> _waterOverlays = [];
    private SceneDocument? _routeOverlayDocument;
    private IReadOnlyList<RouteOverlay> _routeOverlays = [];
    private SceneDocument? _layeredColumnsDocument;
    private LayeredSceneColumns? _layeredColumns;

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
            PointerChanged?.Invoke();
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

    /// <summary>
    /// Whether Landscape is open, on any of its areas - River, Path or Hill.
    /// Landscape is one authoring context split three ways, so entering any
    /// area of it brings all three into view together instead of leaving the
    /// other two dim while the author works one of them.
    /// </summary>
    public bool LandscapeContextActive
    {
        get => _landscapeContextActive;
        set
        {
            _landscapeContextActive = value;
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

    /// <summary>The transient projection used to look at the authored Scene.</summary>
    public CanvasPresentationMode PresentationMode
    {
        get => ViewState.PresentationMode;
        set
        {
            ViewState.SelectPresentation(value);
            QueueRedraw();
        }
    }

    /// <summary>The upper clipping plane used by the horizontal Section view.</summary>
    public decimal SectionElevationMeters
    {
        get => ViewState.SectionElevationMeters;
        set
        {
            ViewState.SetSectionElevation(value);
            QueueRedraw();
        }
    }

    public SectionCutKind SectionCutKind
    {
        get => ViewState.SectionCutKind;
        set
        {
            ViewState.SelectSectionCut(value);
            QueueRedraw();
        }
    }

    public decimal SectionOffsetMeters
    {
        get => ViewState.SectionOffsetMeters;
        set
        {
            ViewState.SetSectionOffset(value);
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
    /// <summary>
    /// How much of the top right corner is taken by controls sitting on the
    /// Canvas. The legend moves below them rather than underneath them.
    /// </summary>
    public float TopRightReservedHeight { get; set; }

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

    /// <summary>
    /// The pointer moved over the Canvas, or left it. Raised for the footer,
    /// which says where it is - the Canvas itself already knows and redraws.
    /// </summary>
    public event Action? PointerChanged;

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

    /// <summary>
    /// Applies the context bar's bridge numbers to the selected bridge, or
    /// nothing when none is selected - in which case those numbers stay what
    /// they were, the defaults for the next bridge.
    /// </summary>
    public ToolOutcome ReshapeSelectedBridge()
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.ReshapeSelectedBridge(context);
        QueueRedraw();
        return outcome;
    }

    /// <summary>The bridge the Select tool currently holds, if any.</summary>
    public BridgeDocument? SelectedBridge =>
        _scene is not null && _interaction.SelectedBridgeId is { } bridgeId
            ? _scene.Document.Bridges.FirstOrDefault(bridge =>
                string.Equals(bridge.BridgeId, bridgeId, StringComparison.Ordinal))
            : null;

    /// <summary>The river the Select tool currently holds, if any.</summary>
    public WaterBodyDocument? SelectedWaterBody =>
        CurrentContext() is { } context ? _interaction.SelectedWaterBody(context) : null;

    /// <summary>Which authored point of it is selected, if any.</summary>
    public int? SelectedWaterPointIndex => _interaction.SelectedWaterPointIndex;

    /// <summary>The Placement the Selector currently holds, if any.</summary>
    public string? SelectedPropInstanceId => _interaction.SelectedPropInstanceId;

    /// <summary>Selects an object by name, the way the Outliner does.</summary>
    public ToolOutcome SelectObject(string objectId)
    {
        var outcome = _interaction.SelectObject(objectId);
        QueueRedraw();
        return outcome;
    }

    /// <summary>
    /// Takes an object off the Canvas, or puts it back. It is a way of looking:
    /// the document is untouched, and the same filtered Scene decides both what
    /// is drawn and what a press can take hold of.
    /// </summary>
    public void SetObjectVisible(string objectId, bool visible)
    {
        _interaction.State.SetHidden(objectId, !visible);
        QueueRedraw();
    }

    /// <summary>
    /// Applies the context bar's water numbers to the selection - one point, or
    /// the whole river when only the body is picked - or nothing when nothing
    /// is selected, in which case those numbers stay the defaults for the next
    /// river.
    /// </summary>
    public ToolOutcome ReshapeSelectedWater()
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.ReshapeSelectedWater(context);
        QueueRedraw();
        return outcome;
    }

    /// <summary>
    /// Declares an activation group and puts the selected river in it, or says
    /// why not. One call because it is one intention: a group with nobody in it
    /// switches nothing.
    /// </summary>
    public ToolOutcome MakeSelectedWaterSwitchable(string group)
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.MakeSelectedWaterSwitchable(context, group);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome RemoveSelectedWaterSwitch()
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.RemoveSelectedWaterSwitch(context);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SetSelectedWaterSwitch(string? name)
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.SetSelectedWaterSwitch(context, name);
        QueueRedraw();
        return outcome;
    }

    public ToolOutcome SetSelectedWaterSwitchInitiallyOn(bool initiallyOn)
    {
        if (CurrentContext() is not { } context) return ToolOutcome.Idle.Instance;
        var outcome = _interaction.SetSelectedWaterSwitchInitiallyOn(context, initiallyOn);
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
        _layeredColumnsDocument = null;
        _layeredColumns = null;
        QueueRedraw();
    }

    /// <summary>
    /// Which Assets a bridge is built from here, or null in a Workspace that
    /// names no Set. It arrives with the catalogs because it is the same kind
    /// of fact: read once when the Workspace opens, never chosen.
    /// </summary>
    public void ConfigureBridgeKit(BridgeKitResolution? kit) => _bridgeKit = kit;

    public void ConfigurePropAssets(PropDisplayCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _propAssets = catalog;
        var colors = new Dictionary<string, Color>();
        foreach (var asset in catalog.Assets)
            colors.Add(asset.AssetKey, Color.FromHtml(asset.Color));
        _propColors = colors;
        QueueRedraw();
    }

    public void ShowScene(LoadedScene? scene, CanvasViewState? restoredView = null)
    {
        _scene = scene;
        _templatePreview = null;
        _templatePreviewMasks = [];
        ViewState = restoredView is null
            ? new CanvasViewState(
                presentationMode: ViewState.PresentationMode,
                sectionElevationMeters: ViewState.SectionElevationMeters,
                sectionCutKind: ViewState.SectionCutKind,
                sectionOffsetMeters: ViewState.SectionOffsetMeters)
            : new CanvasViewState(
                restoredView.PanX,
                restoredView.PanY,
                restoredView.Zoom,
                ViewState.PresentationMode,
                ViewState.SectionElevationMeters,
                ViewState.SectionCutKind,
                ViewState.SectionOffsetMeters);
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

    private LayeredSceneColumns LayeredColumns(SceneDocument document)
    {
        if (ReferenceEquals(_layeredColumnsDocument, document)) return _layeredColumns!;
        _layeredColumns = LayeredSceneColumns.Prepare(document, _metrics!);
        _layeredColumnsDocument = document;
        return _layeredColumns;
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
            PointerHitRadiusAuthoringPixels: 8.0 / ViewState.Zoom,
            BridgeKit: _bridgeKit);
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
            PointerChanged?.Invoke();
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
            PointerChanged?.Invoke();
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

        var document = AsSeen(_templatePreview ?? _scene.Document);
        var zoom = (float)ViewState.Zoom;
        var pan = new Vector2((float)ViewState.PanX, (float)ViewState.PanY);
        var widthAuthoringPixels = _metrics.SceneWidthAuthoringPixels(document);
        var heightAuthoringPixels = _metrics.SceneHeightAuthoringPixels(document);
        var sceneSize = new Vector2(widthAuthoringPixels, heightAuthoringPixels) * zoom;
        var sceneRect = new Rect2(pan, sceneSize);
        DrawRect(sceneRect, SceneBackground);

        // The ordinary view and the analytical height view share the complete
        // Scene range but interpret it differently. Keeping the full range in
        // the ordinary view is what will let a Section plane move later without
        // making every surviving surface change brightness.
        var elevationRange = ElevationRange(document);
        if (PresentationMode == CanvasPresentationMode.Section)
        {
            DrawSection(document, pan, zoom, elevationRange);
        }
        else
        {
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
                highlighted: Mode is EditorMode.Terrain or EditorMode.River
                    || LandscapeContextActive,
                active: Mode == EditorMode.River);
            DrawRouteSurfaces(
                document,
                pan,
                zoom,
                heightAuthoringPixels,
                elevationRange,
                highlighted: Mode == EditorMode.Path || LandscapeContextActive);
            DrawBridges(
                document,
                pan,
                zoom,
                heightAuthoringPixels,
                elevationRange,
                highlighted: Mode == EditorMode.Bridge);
        }
        DrawElevationRegionOutlines(
            document,
            pan,
            zoom,
            heightAuthoringPixels,
            // Full where a hill is authored, and full in Terrain too: with
            // no other mark on the cells, the contour is the only thing that
            // says an authored body lies there. The Map overview is structural
            // rather than an area, so it keeps them as well, and so does
            // Landscape as a whole - River, Path and Hill all read clearly
            // together while any one of them is open.
            highlighted: Mode is EditorMode.Terrain or EditorMode.ElevationRegion
                || MapContextActive || LandscapeContextActive);
        // Placements are not column-resolved yet. Hiding them in Section is
        // more truthful than painting them over a clipped roof with invented
        // occlusion. Their active tool preview remains below, so the view does
        // not disable authoring. A bridge's posts are Placements and follow the
        // same rule; its deck does not need one, because the column already
        // carries it as the surface it is.
        if (PresentationMode != CanvasPresentationMode.Section)
        {
            DrawProps(
                document,
                pan,
                zoom,
                heightAuthoringPixels,
                elevationRange,
                highlighted: Mode == EditorMode.Props);
        }
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
            case EditorMode.Bridge:
                DrawBridgeToolPreview(document, pan, zoom, heightAuthoringPixels);
                DrawBridgeSelection(pan, zoom, heightAuthoringPixels);
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
        if (PresentationMode == CanvasPresentationMode.Heightmap
            && elevationRange is { } range)
            DrawElevationLegend(document, range);
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
            new Vector2(Size.X - BarWidth - (Padding * 2f), Padding + TopRightReservedHeight),
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
                     .Concat(document.Bridges.Select(static bridge => bridge.ElevationMeters))
                     .Concat(WaterOverlays(document).SelectMany(
                         overlay => overlay.Cells.Select(cell =>
                            PresentationMode == CanvasPresentationMode.Heightmap
                             ? WaterElevation(cell)
                             : cell.SurfaceMeters)));
        // An unfinished Path is already geometry the author is judging. Include
        // it in the same scale as the Scene, so Auto start can be verified in
        // the height view before Enter instead of being hidden by Asset colour.
        if (PresentationMode == CanvasPresentationMode.Heightmap
            && Mode == EditorMode.Path
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

    /// <summary>
    /// Keeps an Asset's hue while making its absolute surface height legible in
    /// the ordinary Canvas. Alpha belongs to area emphasis and is intentionally
    /// left alone.
    /// </summary>
    private static Color LitSurfaceColor(
        Color assetColor,
        decimal elevation,
        (decimal Low, decimal High) range)
    {
        var brightness = (float)SurfaceElevationLighting.Brightness(
            elevation, range.Low, range.High);
        return new Color(
            assetColor.R * brightness,
            assetColor.G * brightness,
            assetColor.B * brightness,
            assetColor.A);
    }

    /// <summary>
    /// Projects the engine-neutral resolved columns after clipping them at the
    /// selected elevation. Terrain, water and Paths are deliberately drawn
    /// together: asking the column once is what preserves roofs, voids, fills
    /// and independent surfaces instead of letting Canvas draw order invent a
    /// different Layered-3D result.
    /// </summary>
    private void DrawSection(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        (decimal Low, decimal High)? range)
    {
        var columns = LayeredColumns(document);
        var cellSize = _metrics!.AuthoringPixelsPerWaterCell * zoom;
        var width = _metrics.SceneWidthWaterCells(document);
        var height = _metrics.SceneHeightWaterCells(document);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rectangle = new Rect2(
                    pan + new Vector2(x * cellSize, (height - y - 1) * cellSize),
                    new Vector2(cellSize, cellSize));
                if (rectangle.End.X < 0f || rectangle.End.Y < 0f
                    || rectangle.Position.X > Size.X || rectangle.Position.Y > Size.Y)
                {
                    continue;
                }

                var column = columns.AtWaterCell(x, y);
                var surface = SectionCutKind == SectionCutKind.Between
                    ? column.VisibleBetween(
                        SectionElevationMeters,
                        SectionElevationMeters + SectionOffsetMeters)
                    : column.VisibleAt(SectionElevationMeters);
                // A surface is coloured by whichever catalog enables its Asset.
                // Terrain, water and Paths name Terrain; a bridge deck names the
                // plank it is a row of, which is a Placement. The keys are one
                // namespace, so the first catalog that knows the key is right.
                if (surface is null
                    || !(_terrainColors.TryGetValue(surface.AssetKey, out var color)
                        || _propColors.TryGetValue(surface.AssetKey, out color)))
                {
                    continue;
                }
                if (range is { } lightingSpan)
                {
                    color = LitSurfaceColor(
                        color,
                        surface.ElevationMeters,
                        lightingSpan);
                }
                DrawRect(rectangle, color);
            }
        }
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
            if (PresentationMode == CanvasPresentationMode.Heightmap
                && range is { } heatmapSpan)
            {
                color = ElevationColor(cell.ElevationMeters, heatmapSpan);
            }
            else
            {
                if (!_terrainColors.TryGetValue(cell.AssetKey, out color)) continue;
                if (range is { } lightingSpan)
                    color = LitSurfaceColor(color, cell.ElevationMeters, lightingSpan);
            }
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
                highlighted || PresentationMode == CanvasPresentationMode.Heightmap
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
        bool highlighted,
        bool active = false)
    {
        if (document.WaterBodies.Count == 0) return;
        var cellSize = _metrics!.AuthoringPixelsPerWaterCell * zoom;
        var rows = _metrics.SceneHeightWaterCells(document);
        foreach (var overlay in WaterOverlays(document))
        {
            // A body that has come off the river reads as red without being
            // selected: the author deleted the branch it hung on, and nothing
            // else on the Canvas would say so. The height view keeps its own
            // colours, because there the colour is the answer to a different
            // question and a red cell there would be a wrong height.
            var bodyColor = overlay.Detached ? InvalidPreviewColor : overlay.Color;
            foreach (var cell in overlay.Cells)
            {
                Color color;
                if (PresentationMode == CanvasPresentationMode.Heightmap
                    && range is { } heatmapSpan)
                {
                    color = ElevationColor(WaterElevation(cell), heatmapSpan);
                }
                else
                {
                    color = range is { } lightingSpan
                        ? LitSurfaceColor(bodyColor, cell.SurfaceMeters, lightingSpan)
                        : bodyColor;
                    if (!highlighted)
                        color = new Color(color.R, color.G, color.B, 0.24f);
                }
                var rectangle = new Rect2(
                    pan + new Vector2(cell.X * cellSize, (rows - cell.Y - 1) * cellSize),
                    new Vector2(cellSize, cellSize));
                if (rectangle.End.X < 0f || rectangle.End.Y < 0f
                    || rectangle.Position.X > Size.X || rectangle.Position.Y > Size.Y)
                    continue;
                DrawRect(rectangle, color);
                // River is being authored right now, not merely shown for
                // Landscape legibility: a crisp border on every cell is what
                // pulls it above the other Landscape areas the same view is
                // still drawing.
                if (active)
                    DrawRect(rectangle, ActiveAreaAccent, filled: false, width: 1.5f);
            }
        }
    }

    /// <summary>
    /// The Scene without what the Outliner has hidden. Worked out once per
    /// change rather than once per frame: everything below caches on the
    /// document it was built from, and a fresh instance every frame would throw
    /// all of that away.
    ///
    /// <para>One call, at the top of the draw, is the whole of it. Terrain,
    /// water, routes, bridges and placements all read the document handed to
    /// them, so hiding a body takes its cut out of the section view too - which
    /// is what an author asking to see the map without that river means.</para>
    /// </summary>
    private SceneDocument AsSeen(SceneDocument document)
    {
        var revision = _interaction.State.VisibilityRevision;
        if (_visibleDocument is not null
            && ReferenceEquals(_visibleSource, document)
            && _visibleRevision == revision)
        {
            return _visibleDocument;
        }

        _visibleSource = document;
        _visibleRevision = revision;
        _visibleDocument = SceneVisibility.Without(document, _interaction.State.HiddenObjectIds);
        return _visibleDocument;
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
                WaterGeometry.Corridor(document, _metrics!, body),
                !WaterAttachment.IsAttached(document, _metrics!, body)));
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
    /// <summary>
    /// Finished bridges: the deck as the row of planks it is built from, and a
    /// post drawn at each of its four corners. Both are derived here, exactly
    /// as the placement rule and the export derive them, so what an author
    /// sees is where a plank and a post actually go rather than a second guess
    /// at it. The gaps show, because the gaps are the thing being authored.
    /// </summary>
    private void DrawBridges(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        (decimal Low, decimal High)? range,
        bool highlighted)
    {
        if (_propAssets is null) return;
        foreach (var bridge in document.Bridges)
        {
            if (SpanUnit(bridge) is not { } unit) continue;
            if (BridgeGeometry.TryPlanks(_metrics!, bridge) is not { } layout) continue;

            var deckAsset = _propAssets.Resolve(bridge.PlankAssetKey);
            var deckColor = Color.FromHtml(deckAsset.Color);
            var lit = range is not { } elevationRange
                ? deckColor
                : PresentationMode == CanvasPresentationMode.Heightmap
                    ? ElevationColor(bridge.ElevationMeters, elevationRange)
                    : LitSurfaceColor(deckColor, bridge.ElevationMeters, elevationRange);
            var color = highlighted ? lit : new Color(lit.R, lit.G, lit.B, 0.24f);
            var quads = new List<Vector2[]>(layout.Planks.Count);
            foreach (var plank in layout.Planks)
            {
                var quad = PlankQuad(plank, unit.X, unit.Y, pan, zoom, sceneHeightAuthoringPixels);
                quads.Add(quad);
                DrawColoredPolygon(quad, color);
            }
            // Structures is open on Bridge specifically, not just showing it
            // alongside another Structure: a bright outline on every plank is
            // what makes the whole deck read as the one thing being authored.
            if (highlighted)
            {
                foreach (var quad in quads)
                    DrawPolyline([.. quad, quad[0]], ActiveAreaAccent, 1.5f);
            }

            var postAsset = _propAssets.Resolve(bridge.AnchorAssetKey);
            var postColor = Color.FromHtml(postAsset.Color);
            var outline = highlighted
                ? postColor
                : new Color(postColor.R, postColor.G, postColor.B, 0.4f);
            foreach (var corner in BridgeGeometry.Corners(_metrics!, bridge))
            {
                var anchor = BridgeEditing.CornerAnchor(_metrics!, corner);
                var rectangle = CanvasRectangle(
                    PropEditing.BoundsFor(postAsset, anchor.X, anchor.Y),
                    pan,
                    zoom,
                    sceneHeightAuthoringPixels);
                DrawRect(
                    rectangle,
                    new Color(postColor.R, postColor.G, postColor.B, highlighted ? 0.38f : 0.12f));
                DrawRect(rectangle, outline, filled: false, width: highlighted ? 3f : 2f);
                if (!highlighted) continue;
                DrawRect(rectangle, ActiveAreaAccent, filled: false, width: 1.5f);
                DrawCollisionOutline(
                    PropEditing.CollisionBoundsFor(postAsset, anchor.X, anchor.Y),
                    pan,
                    zoom,
                    sceneHeightAuthoringPixels,
                    outline);
            }
        }
    }

    /// <summary>
    /// The selected bridge, and the drag in progress if there is one. It is
    /// drawn over the finished bridges rather than instead of them, so a drag
    /// shows where the bridge would go beside where it still is - which is what
    /// makes a refused move readable.
    /// </summary>
    private void DrawBridgeSelection(Vector2 pan, float zoom, int sceneHeightAuthoringPixels)
    {
        if (CurrentContext() is not { } context) return;
        var preview = _interaction.BridgeSelection(context);
        if (preview.Bridge is not { } bridge) return;

        var dragging = _interaction.BridgeDrag is not null;
        var color = preview.Kind switch
        {
            BridgeDraftKind.Blocked => InvalidPreviewColor,
            _ when dragging => ValidPreviewColor,
            _ => SelectionColor,
        };

        if (SpanUnit(bridge) is { } unit)
        {
            foreach (var plank in preview.Planks)
            {
                DrawColoredPolygon(
                    PlankQuad(plank, unit.X, unit.Y, pan, zoom, sceneHeightAuthoringPixels),
                    new Color(color.R, color.G, color.B, dragging ? 0.30f : 0.22f));
            }
        }
        if (preview.Corners.Count == 4)
        {
            var deck = new[]
            {
                CornerScreen(preview.Corners[0], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[2], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[3], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[1], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[0], pan, zoom, sceneHeightAuthoringPixels),
            };
            DrawPolyline(deck, color, 3f);
        }
        foreach (var post in preview.Posts)
        {
            var rectangle = CanvasRectangle(post, pan, zoom, sceneHeightAuthoringPixels);
            DrawRect(rectangle, new Color(color.R, color.G, color.B, 0.22f));
            DrawRect(rectangle, color, filled: false, width: 2f);
        }

        // The two ends are what an author grabs, so they are drawn as something
        // grabbable rather than left to be guessed from the deck outline.
        DrawBridgeEndHandle(bridge.StartAuthoringPx, pan, zoom, sceneHeightAuthoringPixels, color);
        DrawBridgeEndHandle(bridge.EndAuthoringPx, pan, zoom, sceneHeightAuthoringPixels, color);

        const int LengthFontSize = 12;
        var caption = preview.Planks.Count == 0
            ? FormattableString.Invariant($"{preview.LengthMeters:0.##} m")
            : FormattableString.Invariant(
                $"{preview.LengthMeters:0.##} m - {preview.Planks.Count} x {preview.Planks[0].DepthMeters:0.###} m");
        DrawString(
            ThemeDB.FallbackFont,
            ScreenOf(bridge.EndAuthoringPx, pan, zoom, sceneHeightAuthoringPixels)
                + new Vector2(12f, -12f),
            caption,
            HorizontalAlignment.Left,
            width: -1f,
            fontSize: LengthFontSize,
            modulate: color);
    }

    private void DrawBridgeEndHandle(
        AuthoringPixelPosition position,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        Color color)
    {
        const float radius = 6f;
        var center = ScreenOf(position, pan, zoom, sceneHeightAuthoringPixels);
        DrawCircle(center, radius, new Color(color.R, color.G, color.B, 0.35f));
        DrawArc(center, radius, 0f, Mathf.Tau, 24, color, 2f);
    }

    private static Vector2 ScreenOf(
        AuthoringPixelPosition position,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels) => pan + new Vector2(
            position.X * zoom,
            (sceneHeightAuthoringPixels - position.Y) * zoom);

    private static (double X, double Y)? SpanUnit(BridgeDocument bridge) => SpanUnit(
        bridge.StartAuthoringPx.X,
        bridge.StartAuthoringPx.Y,
        bridge.EndAuthoringPx.X,
        bridge.EndAuthoringPx.Y);

    /// <summary>
    /// What the second click would author: the planks, the deck outline they
    /// fill, the four posts it would set, and how long it is. Yellow promises
    /// it would be taken, red carries the reason it would not. The planks are
    /// drawn here rather than only after the commit, because the count is a
    /// number an author turns while looking at the draft.
    /// </summary>
    private void DrawBridgeToolPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        if (_propAssets is null) return;
        var kit = (_bridgeKit as BridgeKitResolution.Resolved)?.Kit;
        var preview = ToolPreviewBuilder.BuildBridgeDraft(
            document,
            _propAssets,
            ActiveTool,
            _interaction.BridgeStart,
            _interaction.PointerAuthoring,
            kit?.PlankAssetKey,
            kit?.AnchorAssetKey,
            _interaction.State.BridgeWidthMeters,
            _interaction.State.BridgeElevationMeters,
            _interaction.State.BridgePlankCount,
            _interaction.State.BridgePlankGapMeters);
        if (preview.Start is not { } start) return;

        var color = preview.Kind switch
        {
            BridgeDraftKind.Ready => ValidPreviewColor,
            BridgeDraftKind.Blocked => InvalidPreviewColor,
            _ => DraftPreviewColor,
        };
        DrawAnchor(start.X, start.Y, pan, zoom, sceneHeightAuthoringPixels, color);
        if (preview.End is not { } end) return;

        Vector2 Screen(int x, int y) => pan + new Vector2(
            x * zoom,
            (sceneHeightAuthoringPixels - y) * zoom);

        DrawLine(Screen(start.X, start.Y), Screen(end.X, end.Y), color, 2f);
        if (SpanUnit(start.X, start.Y, end.X, end.Y) is { } unit)
        {
            foreach (var plank in preview.Planks)
            {
                DrawColoredPolygon(
                    PlankQuad(plank, unit.X, unit.Y, pan, zoom, sceneHeightAuthoringPixels),
                    new Color(color.R, color.G, color.B, 0.30f));
            }
        }
        if (preview.Corners.Count == 4)
        {
            // The deck outline in corner order start-left, start-right,
            // end-left, end-right, which walks the rectangle as 0-2-3-1.
            var deck = new[]
            {
                CornerScreen(preview.Corners[0], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[2], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[3], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[1], pan, zoom, sceneHeightAuthoringPixels),
                CornerScreen(preview.Corners[0], pan, zoom, sceneHeightAuthoringPixels),
            };
            DrawPolyline(deck, color, 2f);
        }
        foreach (var post in preview.Posts)
        {
            var rectangle = CanvasRectangle(post, pan, zoom, sceneHeightAuthoringPixels);
            DrawRect(rectangle, new Color(color.R, color.G, color.B, 0.22f));
            DrawRect(rectangle, color, filled: false, width: 2f);
        }

        // The length belongs where the author is looking, not in a field on the
        // other side of the window - and so does what the planks came out at,
        // which is the number the count is really being turned for.
        const int LengthFontSize = 12;
        var caption = preview.Planks.Count == 0
            ? FormattableString.Invariant($"{preview.LengthMeters:0.##} m")
            : FormattableString.Invariant(
                $"{preview.LengthMeters:0.##} m - {preview.Planks.Count} x {preview.Planks[0].DepthMeters:0.###} m");
        DrawString(
            ThemeDB.FallbackFont,
            Screen(end.X, end.Y) + new Vector2(12f, -12f),
            caption,
            HorizontalAlignment.Left,
            width: -1f,
            fontSize: LengthFontSize,
            modulate: color);
    }

    private Vector2 CornerScreen(
        BridgeCorner corner,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels) => MeterScreen(
            (double)corner.XMeters,
            (double)corner.YMeters,
            pan,
            zoom,
            sceneHeightAuthoringPixels);

    private Vector2 MeterScreen(
        double xMeters,
        double yMeters,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        var pixelsPerMeter = (float)_metrics!.AuthoringPixelsPerMeter;
        return pan + new Vector2(
            (float)xMeters * pixelsPerMeter * zoom,
            (sceneHeightAuthoringPixels - ((float)yMeters * pixelsPerMeter)) * zoom);
    }

    /// <summary>
    /// One plank as the quad it covers. Depth runs along the span and width
    /// across it, so a plank stays square to its bridge at whatever angle the
    /// bridge was drawn at - and any angle is allowed on purpose.
    /// </summary>
    private Vector2[] PlankQuad(
        BridgePlank plank,
        double unitX,
        double unitY,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        var alongX = unitX * (double)plank.DepthMeters / 2.0;
        var alongY = unitY * (double)plank.DepthMeters / 2.0;
        var acrossX = -unitY * (double)plank.WidthMeters / 2.0;
        var acrossY = unitX * (double)plank.WidthMeters / 2.0;
        var centerX = (double)plank.CenterXMeters;
        var centerY = (double)plank.CenterYMeters;

        Vector2 Corner(double along, double across) => MeterScreen(
            centerX + (along * alongX) + (across * acrossX),
            centerY + (along * alongY) + (across * acrossY),
            pan,
            zoom,
            sceneHeightAuthoringPixels);

        return
        [
            Corner(-1.0, -1.0),
            Corner(1.0, -1.0),
            Corner(1.0, 1.0),
            Corner(-1.0, 1.0),
        ];
    }

    /// <summary>
    /// Which way a span points, as a unit vector in authoring pixels - which
    /// is the same direction it points in metres, the two differing only by a
    /// scale. None when the two ends are the same place, which has no
    /// direction to give.
    /// </summary>
    private static (double X, double Y)? SpanUnit(int startX, int startY, int endX, int endY)
    {
        double deltaX = endX - startX;
        double deltaY = endY - startY;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (!double.IsFinite(length) || length <= 0.0) return null;
        return (deltaX / length, deltaY / length);
    }

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
            var elevation = (decimal)surface.ElevationAt(
                segment.Chain.StartStation + (length / 2.0));
            var segmentColor = range is not { } elevationRange
                ? color
                : PresentationMode == CanvasPresentationMode.Heightmap
                    ? ElevationColor(elevation, elevationRange)
                    : LitSurfaceColor(color, elevation, elevationRange);
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
            var elevation =
                (first.ElevationMeters + second.ElevationMeters + third.ElevationMeters) / 3m;
            var triangleColor = range is not { } elevationRange
                ? color
                : PresentationMode == CanvasPresentationMode.Heightmap
                    ? ElevationColor(elevation, elevationRange)
                    : LitSurfaceColor(color, elevation, elevationRange);
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
        if (ActiveTool == EditorTool.SelectRiver)
        {
            DrawWaterSelectionPreview(document, pan, zoom, sceneHeightAuthoringPixels);
            return;
        }

        if (ActiveTool == EditorTool.InsertRiverPoint)
        {
            DrawWaterInsertPreview(pan, zoom, sceneHeightAuthoringPixels);
            return;
        }

        if (ActiveTool == EditorTool.ReAttachRiver)
        {
            DrawWaterReAttachPreview(pan, zoom, sceneHeightAuthoringPixels);
            return;
        }

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

    /// <summary>
    /// The river under an `Insert Point` pointer: its curve, its authored
    /// points, and a ring where the new one would land. A river's curve is
    /// invisible unless something is selected, and this is the one tool whose
    /// purpose is to put a handle on that curve - pressing into a blue band
    /// without seeing it is guessing.
    /// </summary>
    private void DrawWaterInsertPreview(Vector2 pan, float zoom, int sceneHeightAuthoringPixels)
    {
        if (CurrentContext() is not { } context) return;
        var preview = _interaction.WaterInsert(context);
        if (preview.Body is not { } body) return;

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

        for (var index = 0; index < body.Points.Count; index++)
        {
            var point = body.Points[index];
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleInAuthoringPx, Screen);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleOutAuthoringPx, Screen);
            var isEnd = index == 0 || index == body.Points.Count - 1;
            DrawCircle(centre, isEnd ? 5.0f : 3.5f, WaterCurveColor);
        }

        // Hollow, because it is not there yet. Absent while the pointer is off
        // the line, which is also the answer to whether a press would take.
        if (preview.Anchor is { } anchor)
        {
            var landing = Screen(anchor.PositionAuthoringPx.X, anchor.PositionAuthoringPx.Y);
            DrawArc(landing, 6.5f, 0f, Mathf.Tau, 24, SelectionColor, 2.0f);
        }

    }

    /// <summary>
    /// The three phases of `Re-Attach`, each drawing only what its own press can
    /// take.
    ///
    /// <para>Phase one lights the river under the pointer yellow, which is what
    /// "a press would take this" means everywhere else here. Phase two holds
    /// that river in violet with its authored points showing and rings the end
    /// a press would carry - no other body can answer, so the ring is a
    /// promise. Phase three draws the straight run the carried end would leave
    /// behind and the place on another river it would land.</para>
    ///
    /// <para>That straight run is usually wrong, and it is drawn before the
    /// press rather than after it so that bending it back with `Insert Point`
    /// is a decision the author has already made.</para>
    /// </summary>
    private void DrawWaterReAttachPreview(Vector2 pan, float zoom, int sceneHeightAuthoringPixels)
    {
        if (CurrentContext() is not { } context) return;
        var preview = _interaction.WaterReAttach(context);

        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        void Curve(IReadOnlyList<ChainPoint> centerline, Color color)
        {
            if (centerline.Count < 2) return;
            var line = new Vector2[centerline.Count];
            for (var index = 0; index < centerline.Count; index++)
                line[index] = Screen(centerline[index].X, centerline[index].Y);
            DrawPolyline(line, color, 3.0f);
        }

        if (preview.Chosen is not { } chosen)
        {
            if (preview.Hovered is null) return;
            Curve(preview.HoveredLine, SelectionColor);
            return;
        }

        Curve(preview.ChosenLine, ReAttachChosenColor);
        for (var index = 0; index < chosen.Points.Count; index++)
        {
            var point = chosen.Points[index];
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            var isEnd = index == 0 || index == chosen.Points.Count - 1;
            DrawCircle(centre, isEnd ? 5.5f : 3.5f, ReAttachChosenColor);
        }

        if (preview.EndIndex is { } endIndex)
        {
            var aimed = chosen.Points[endIndex];
            DrawArc(
                Screen(aimed.PositionAuthoringPx.X, aimed.PositionAuthoringPx.Y),
                8.0f,
                0f,
                Mathf.Tau,
                24,
                SelectionColor,
                2.0f);
            return;
        }

        if (_interaction.ReAttachEnd is not { } carrying) return;
        var carried = carrying.End == WaterEnd.Source ? chosen.Points[0] : chosen.Points[^1];
        var from = Screen(carried.PositionAuthoringPx.X, carried.PositionAuthoringPx.Y);
        DrawCircle(from, 6.5f, SelectionColor);

        if (preview.Target is { } target)
        {
            var landing = Screen(target.PositionAuthoringPx.X, target.PositionAuthoringPx.Y);
            DrawLine(from, landing, SelectionColor, 2.0f);
            DrawArc(landing, 7.0f, 0f, Mathf.Tau, 24, SelectionColor, 2.0f);
            return;
        }

        // Off every other river: the run is drawn in the colour of a press that
        // would be refused rather than not drawn at all, because where the
        // pointer is is still the question being asked.
        if (_interaction.PointerAuthoring is { } pointer)
        {
            DrawLine(
                from,
                Screen(pointer.X, pointer.Y),
                new Color(InvalidPreviewColor.R, InvalidPreviewColor.G, InvalidPreviewColor.B, 0.8f),
                2.0f);
        }
    }

    /// <summary>
    /// The selected river: the cells it claims, its centerline, and its
    /// authored points as something grabbable. While a point or the whole curve
    /// is being dragged this draws the candidate rather than what is stored, so
    /// the author sees where releasing would put it.
    ///
    /// <para>A stated fork the curve no longer supports turns the selection
    /// red. That is a warning and not a refusal - the move is taken either way
    /// - but it is the one thing an author must not first read in an export.
    /// </para>
    /// </summary>
    private void DrawWaterSelectionPreview(
        SceneDocument document,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels)
    {
        if (CurrentContext() is not { } context) return;
        var preview = _interaction.WaterSelection(context);
        if (preview.Body is not { } body) return;

        // What this body states and cannot keep, or a body no longer hanging on
        // anything. Both are this body's problem; a fork somebody else broke is
        // not, and used to turn this selection red anyway.
        var color = preview.Broken.Count > 0 || preview.Detached
            ? InvalidPreviewColor
            : SelectionColor;
        var waterCellSize = _metrics!.AuthoringPixelsPerWaterCell * zoom;
        var rows = _metrics.SceneHeightWaterCells(document);
        foreach (var cell in preview.Cells)
        {
            DrawRect(
                new Rect2(
                    pan + new Vector2(cell.X * waterCellSize, (rows - cell.Y - 1) * waterCellSize),
                    new Vector2(waterCellSize, waterCellSize)),
                new Color(color.R, color.G, color.B, 0.35f));
        }

        Vector2 Screen(double authoringX, double authoringY) => pan + new Vector2(
            (float)authoringX * zoom,
            (sceneHeightAuthoringPixels - (float)authoringY) * zoom);

        if (preview.Centerline.Count >= 2)
        {
            var line = new Vector2[preview.Centerline.Count];
            for (var index = 0; index < preview.Centerline.Count; index++)
                line[index] = Screen(preview.Centerline[index].X, preview.Centerline[index].Y);
            DrawPolyline(line, color, 3.0f);
        }

        for (var index = 0; index < body.Points.Count; index++)
        {
            var point = body.Points[index];
            var centre = Screen(point.PositionAuthoringPx.X, point.PositionAuthoringPx.Y);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleInAuthoringPx, Screen);
            DrawHandle(centre, point.PositionAuthoringPx, point.HandleOutAuthoringPx, Screen);
            var selected = _interaction.SelectedWaterPointIndex == index;
            // The source and the mouth are the whole of a river's flow
            // direction, so they stay bigger than the points between them.
            var isEnd = index == 0 || index == body.Points.Count - 1;
            DrawCircle(centre, selected ? 6.5f : isEnd ? 5.0f : 4.0f, color);
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
            var assetColor = Color.FromHtml(asset.Color);
            var color = range is not { } propSpan
                ? assetColor
                : PresentationMode == CanvasPresentationMode.Heightmap
                    ? ElevationColor(prop.ElevationMeters, propSpan)
                    : LitSurfaceColor(assetColor, prop.ElevationMeters, propSpan);
            var selected = highlighted && prop.InstanceId == _interaction.SelectedPropInstanceId;
            var outline = highlighted
                ? assetColor
                : new Color(assetColor.R, assetColor.G, assetColor.B, 0.72f);
            DrawRect(
                rectangle,
                new Color(color.R, color.G, color.B, highlighted ? 0.38f : 0.10f));
            DrawRect(
                rectangle,
                selected ? SelectionColor : outline,
                filled: false,
                width: selected ? 4f : highlighted ? 3f : 2f);
            if (highlighted)
            {
                DrawCollisionOutline(
                    PropEditing.CollisionBoundsFor(
                        asset,
                        prop.PositionAuthoringPx.X,
                        prop.PositionAuthoringPx.Y),
                    pan,
                    zoom,
                    sceneHeightAuthoringPixels,
                    selected ? SelectionColor : outline);
            }
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

    /// <summary>
    /// The box a Placement occupies, dashed inside the solid outline of what it
    /// is drawn as. It is exactly the box the placement rule reads, never a
    /// second interpretation of it: what lights up has to be what decides,
    /// or an author cannot tell why something was refused.
    /// </summary>
    private void DrawCollisionOutline(
        PropBoundsAuthoringPixels? collision,
        Vector2 pan,
        float zoom,
        int sceneHeightAuthoringPixels,
        Color color)
    {
        // An Asset that occupies nothing has no box to outline. Drawing an
        // empty one at the anchor would say it occupies a point, which is a
        // different claim than the one the placement rule makes.
        if (collision is not { } occupied) return;
        var rectangle = CanvasRectangle(occupied, pan, zoom, sceneHeightAuthoringPixels);
        var topLeft = rectangle.Position;
        var topRight = topLeft + new Vector2(rectangle.Size.X, 0f);
        var bottomRight = topLeft + rectangle.Size;
        var bottomLeft = topLeft + new Vector2(0f, rectangle.Size.Y);

        // Dashes are screen-sized rather than scene-sized, so a collision box
        // stays readable as a box at every zoom instead of turning into one
        // long dash when zoomed out.
        const float dashLength = 6f;
        DrawDashedLine(topLeft, topRight, color, 1.5f, dashLength);
        DrawDashedLine(topRight, bottomRight, color, 1.5f, dashLength);
        DrawDashedLine(bottomRight, bottomLeft, color, 1.5f, dashLength);
        DrawDashedLine(bottomLeft, topLeft, color, 1.5f, dashLength);
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
            DrawCollisionOutline(
                candidate.Collision,
                pan,
                zoom,
                sceneHeightAuthoringPixels,
                color);
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
    private sealed record WaterOverlay(
        Color Color,
        IReadOnlyList<WaterCellSpan> Cells,
        bool Detached);
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
