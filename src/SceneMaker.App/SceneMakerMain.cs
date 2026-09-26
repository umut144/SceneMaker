using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using SceneMaker.Core;
using SceneMaker.Editor;

namespace SceneMaker.App;

public sealed partial class SceneMakerMain : Control
{
    /// <summary>
    /// The entries of the settings menu. The numbers exist only while the menu
    /// does, so they are left to the compiler: a hand-picked id can silently
    /// collide with another, an enum member cannot.
    /// </summary>
    private enum SettingsMenuItem
    {
        CreateWorkspace = 1,
        LoadWorkspace,
        WorkspaceAssets,
        CreateScene,
        LoadScene,
        LoadTemplate,
        ExportWorkspace,
        ChunkHelper,
    }
    private const double AutosaveDelaySeconds = 1.5;

    private readonly ToolInteraction _interaction = new();
    private readonly SceneCanvas _canvas = new();
    private readonly Label _workspaceLabel = new();
    private readonly Label _sceneLabel = new();
    private readonly Label _toolContextLabel = new();
    private readonly VSeparator _toolContextSeparator = new();
    private readonly OptionButton _sectionCutEdit = new();
    private readonly Label _sectionElevationLabel = new();
    private readonly SpinBox _sectionElevationEdit = new();
    private readonly Label _sectionOffsetLabel = new();
    private readonly SpinBox _sectionOffsetEdit = new();
    private readonly Label _waterHeatmapValueLabel = new();
    private readonly Label _propLineOffsetLabel = new();
    private readonly SpinBox _propLineOffsetEdit = new();
    private readonly Label _surfaceLabel = new();
    private readonly OptionButton _surfaceEdit = new();
    private readonly Label _propInstanceIdLabel = new();
    private readonly LineEdit _propInstanceIdEdit = new();
    private readonly Label _curvePointModeLabel = new();
    private readonly OptionButton _curvePointModeEdit = new();

    /// <summary>
    /// One control for two tools, and two document enums that stay apart. The
    /// items carry these IDs rather than either enum's numbers: an OptionButton
    /// is addressed by item index and answers with one, and assuming index,
    /// item ID and enum value are the same integer is a coincidence that holds
    /// until someone reorders the items or numbers an enum.
    /// </summary>
    private const int CurvePointModeLinear = 1;
    private const int CurvePointModeAligned = 2;
    private const float ViewToggleOverlayWidth = 152f;
    private const float ViewToggleOverlayHeight = 42f;
    private const float ViewToggleOverlayMargin = 8f;
    private const int PathGradeDownFifty = 1;
    private const int PathGradeDownTwentyFive = 2;
    private const int PathGradeLevel = 3;
    private const int PathGradeUpTwentyFive = 4;
    private const int PathGradeUpFifty = 5;
    private const int PathOperationAdditive = 1;
    private const int PathOperationSubtractive = 2;
    private const int SectionCutAtItem = 1;
    private const int SectionCutBetweenItem = 2;

    private readonly Label _riverWidthLabel = new();
    private readonly SpinBox _riverWidthEdit = new();
    private readonly Label _riverSwitchLabel = new();
    private readonly OptionButton _riverSwitchEdit = new();
    private readonly CheckBox _riverInitiallyOnToggle = new();
    private readonly Label _riverNewSwitchLabel = new();
    private readonly LineEdit _riverNewSwitchEdit = new();
    private readonly Button _riverRemoveSwitch = new();

    // Group and state flattened into one list, because a state only means
    // anything inside its group and choosing them separately would allow a
    // pairing that does not exist. Index 0 is "in every state".
    private readonly List<string?> _riverSwitchChoices = [];
    private readonly Label _pathWidthLabel = new();
    private readonly SpinBox _pathWidthEdit = new();
    private readonly Label _bridgePlankCountLabel = new();
    private readonly SpinBox _bridgePlankCountEdit = new();
    private readonly Label _bridgePlankGapLabel = new();
    private readonly SpinBox _bridgePlankGapEdit = new();
    private readonly Label _bridgeWidthLabel = new();
    private readonly SpinBox _bridgeWidthEdit = new();
    private readonly Label _bridgeElevationLabel = new();
    private readonly SpinBox _bridgeElevationEdit = new();
    private readonly Label _pathGradeLabel = new();
    private readonly OptionButton _pathGradeEdit = new();
    private readonly Label _pathOperationLabel = new();
    private readonly OptionButton _pathOperationEdit = new();
    private readonly Label _pathClearanceLabel = new();
    private readonly SpinBox _pathClearanceEdit = new();
    private readonly CheckBox _pathAutoStartToggle = new();
    private readonly Label _pathStartElevationLabel = new();
    private readonly SpinBox _pathStartElevationEdit = new();
    private readonly CheckBox _snapWaterToggle = new();
    private readonly Label _waterElevationLabel = new();
    private readonly SpinBox _waterElevationEdit = new();
    private readonly Label _waterDepthLabel = new();
    private readonly SpinBox _waterDepthEdit = new();
    private readonly Label _waterClearanceLabel = new();
    private readonly SpinBox _waterClearanceEdit = new();
    private readonly Label _waterDerivedSpanLabel = new();
    private readonly Label _selectedPointModeLabel = new();
    private readonly OptionButton _selectedPointModeEdit = new();
    private readonly Button _eraserToggle = new();
    private readonly Button _heatmapToggle = new();
    private readonly Button _sectionToggle = new();
    private readonly MenuButton _waterHeatmapValueEdit = new();
    private readonly Label _viewLabel = new();
    private readonly Label _pointerLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _documentStateLabel = new();
    private readonly Button _undoButton = new();
    private readonly Button _redoButton = new();
    private readonly Timer _autosaveTimer = new();
    private readonly MenuButton _settingsButton = new();
    private readonly ButtonGroup _terrainAssetButtons = new();
    private readonly ButtonGroup _propAssetButtons = new();
    private readonly ButtonGroup _drawingToolButtons = new();
    private readonly HBoxContainer _terrainAssetBar = new();
    private readonly HBoxContainer _propAssetBar = new();
    private readonly HBoxContainer _templateBar = new();
    private readonly HBoxContainer _mapBar = new();
    private readonly HBoxContainer _landscapeBar = new();
    private readonly ButtonGroup _landscapeAreaButtons = new();
    private readonly Dictionary<EditorMode, Button> _landscapeAreaControls = [];
    private readonly HBoxContainer _structuresBar = new();
    private readonly ButtonGroup _structuresAreaButtons = new();
    private readonly Dictionary<EditorMode, Button> _structuresAreaControls = [];
    private readonly HBoxContainer _overviewNavigationBar = new();
    private readonly HBoxContainer _contextNavigationBar = new();
    private readonly HBoxContainer _contextMenuBar = new();
    private readonly HBoxContainer _viewOptionsBar = new();
    private readonly HBoxContainer _viewToggleOverlay = new();

    private readonly PanelContainer _outlinerPanel = new();
    private readonly VBoxContainer _outlinerColumn = new();
    private readonly Label _outlinerHeaderLabel = new();
    private readonly Label _outlinerEmptyLabel = new();
    private readonly Button _outlinerToggle = new();
    private readonly Dictionary<string, Button> _outlinerNames = [];
    private readonly Dictionary<string, CheckBox> _outlinerEyes = [];
    private SceneDocument? _outlinerDocument;
    private EditorMode? _outlinerMode;
    private readonly VBoxContainer _inspectorColumn = new();
    private readonly Label _inspectorHeaderLabel = new();
    private readonly Label _inspectorEmptyLabel = new();

    /// <summary>
    /// One labelled line of the Inspector. The row exists so that hiding a
    /// value hides its whole line: two hidden children in a column still cost
    /// the separation between them, and a panel full of those reads as a panel
    /// with holes in it.
    /// </summary>
    private readonly List<(Container Row, Control Label, Control Edit)> _inspectorRows = [];
    private readonly Button _returnNavigationButton = new();
    private readonly Button _mapNavigationButton = new();
    private readonly Button _landscapeNavigationButton = new();
    private readonly Button _structuresNavigationButton = new();

    /// <summary>
    /// Which Landscape area the group opens on. River, Path and ElevationRegion
    /// are three areas behind one entry, and coming back should land where the
    /// author left off rather than always on the first of them.
    /// </summary>
    private EditorMode _landscapeArea = EditorMode.River;
    private EditorMode _structuresArea = EditorMode.Bridge;

    // Set while the bridge fields are being filled from a selection, so that
    // showing a bridge's numbers cannot be mistaken for editing them.
    private bool _loadingBridgeNumbers;
    private bool _loadingRiverNumbers;
    private readonly SpinBox _mapExtensionCellsEdit = new();
    private readonly Label _mapExtensionMetricsLabel = new();
    private readonly MenuButton _mapResizeButton = new();
    private readonly Button _placeTemplateAnchorButton = new();
    private readonly Button _templatesButton = new();
    private readonly Button _regeneratePreviewButton = new();
    private readonly PopupPanel _templatesPopup = new();
    private readonly LineEdit _templateSearchEdit = new();
    private readonly SpinBox _templateGroupFilter = new();
    private readonly VBoxContainer _templateRows = new();
    private readonly SpinBox _anchorGroupEdit = new();
    private readonly Dictionary<EditorTool, Button> _drawingToolControlsByTool = [];
    private readonly Dictionary<string, Button> _terrainAssetControlsByKey = [];

    private readonly FileDialog _workspaceDirectoryDialog = new();
    private readonly FileDialog _workspaceDirectoryLoadDialog = new();
    private readonly FileDialog _sceneFileDialog = new();
    private readonly FileDialog _templateFileDialog = new();
    private readonly ConfirmationDialog _createWorkspaceDialog = new();
    private readonly ConfirmationDialog _createSceneDialog = new();
    private readonly AcceptDialog _errorDialog = new();
    private readonly ConfirmationDialog _workspaceAssetsDialog = new();
    private readonly VBoxContainer _workspaceAssetRows = new();
    private readonly Dictionary<string, WorkspaceAssetEditorRow> _workspaceAssetEditorRows = [];
    private readonly LineEdit _workspaceIdEdit = new();
    private readonly SpinBox _workspaceMinimumChannelDepthEdit = new();
    private readonly LineEdit _sceneIdEdit = new();
    private readonly SpinBox _sceneWidthEdit = new();
    private readonly SpinBox _sceneHeightEdit = new();
    private readonly Label _sceneWidthMetricsLabel = new();
    private readonly Label _sceneHeightMetricsLabel = new();
    private readonly OptionButton _sceneKindEdit = new();
    private readonly GridContainer _templateCreationFields = new() { Columns = 2 };
    private readonly SpinBox _templateGroupEdit = new();
    private readonly SpinBox _sceneElevationEdit = new();
    private readonly Label _elevationLabel = new();
    private readonly Label _terrainBrushWidthLabel = new();
    private readonly SpinBox _terrainBrushWidthEdit = new();
    private readonly SpinBox _elevationEdit = new();
    private readonly SpinBox _templateInsertionXEdit = new();
    private readonly SpinBox _templateInsertionYEdit = new();
    private readonly CheckBox _templatePivotCenterToggle = new();
    private readonly Label _templateInsertionXMetricsLabel = new();
    private readonly Label _templateInsertionYMetricsLabel = new();

    private readonly EditorController _controller = new();
    private string? _pendingWorkspaceParentDirectory;
    private string? _recentSessionPath;
    private string? _templateLoadError;
    private bool _updatingAnchorGroupEdit;

    private sealed record WorkspaceAssetEditorRow(
        string AssetKey,
        CheckBox Enabled,
        LineEdit DisplayName,
        OptionButton Role,
        LineEdit Color,
        LineEdit Surface,
        OptionButton Authoring,
        LineEdit Category,
        SpinBox Scale,

        /// <summary>
        /// PolyTools' own id for this Asset, carried through unedited. Never a
        /// field on the row: SceneMaker does not let an author retype what
        /// PolyTools publishes, and a Save that dropped it here would silently
        /// unbind every Placement and the bridge kit from their geometry.
        /// </summary>
        string? PolyToolsAssetId);

    public override void _Ready()
    {
        BuildInterface();
        BuildDialogs();
        ShowNavigationOverview();
        _recentSessionPath = ProjectSettings.GlobalizePath("user://recent_session.json");
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--ignore-recent-session") < 0)
            RestoreRecentSession();
        UpdateDocumentStatus();
        _canvas.CallDeferred(Control.MethodName.GrabFocus);
        GD.Print("SceneMaker standalone authoring tool ready");
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true } keyEvent) return;
        if (!keyEvent.IsCommandOrControlPressed()) return;
        switch (keyEvent.Keycode)
        {
            case Key.Z when keyEvent.ShiftPressed:
            case Key.Y:
                RedoEdit();
                break;
            case Key.Z:
                UndoEdit();
                break;
            case Key.S:
                SaveSceneNow();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Writes out any pending edit before the editor goes away. Deliberately
    /// does not go through PersistScene: during teardown the Timer and the
    /// interface nodes it touches may already be on their way out.
    /// </summary>
    public override void _ExitTree()
    {
        var report = _controller.SaveScene();
        if (!report.Succeeded)
            GD.PushWarning($"Could not save the Scene while closing: {report.Message}");
        SaveRecentSession();
    }

    private void BuildInterface()
    {
        var root = new VBoxContainer
        {
            Name = "RootLayout",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _overviewNavigationBar.Name = "NavigationOverview";
        _overviewNavigationBar.CustomMinimumSize = new Vector2(0f, 44f);
        _overviewNavigationBar.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(_overviewNavigationBar);
        AddPerspectiveButton(_overviewNavigationBar, "Terrain", available: true, "Terrain foundation view");
        _landscapeNavigationButton.Text = "Landscape";
        _landscapeNavigationButton.TooltipText =
            "River, Path and Hill: the shapes a landscape is made of.";
        _landscapeNavigationButton.CustomMinimumSize = new Vector2(130f, 0f);
        _landscapeNavigationButton.Pressed += SelectLandscapeContext;
        _overviewNavigationBar.AddChild(_landscapeNavigationButton);
        _structuresNavigationButton.Text = "Structures";
        _structuresNavigationButton.TooltipText =
            "Bridges: the things built onto a landscape rather than out of it.";
        _structuresNavigationButton.CustomMinimumSize = new Vector2(130f, 0f);
        _structuresNavigationButton.Pressed += SelectStructuresContext;
        _overviewNavigationBar.AddChild(_structuresNavigationButton);
        AddPerspectiveButton(
            _overviewNavigationBar,
            "Placements",
            available: true,
            "Placement authoring view");
        AddPerspectiveButton(
            _overviewNavigationBar,
            "Scene Templates",
            available: true,
            "Template Anchor and Workspace Template authoring.");
        _mapNavigationButton.Text = "Map";
        _mapNavigationButton.TooltipText = "View and extend this Scene's map bounds.";
        _mapNavigationButton.CustomMinimumSize = new Vector2(90f, 0f);
        _mapNavigationButton.Pressed += SelectMapContext;
        _overviewNavigationBar.AddChild(_mapNavigationButton);
        _overviewNavigationBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _settingsButton.Name = "Settings";
        _settingsButton.Text = "Settings";
        _settingsButton.CustomMinimumSize = new Vector2(140f, 0f);
        _settingsButton.AddThemeFontSizeOverride("font_size", 14);
        _settingsButton.SetAnchorsPreset(LayoutPreset.TopRight);
        _settingsButton.OffsetLeft = -152f;
        _settingsButton.OffsetTop = 6f;
        _settingsButton.OffsetRight = -12f;
        _settingsButton.OffsetBottom = 42f;
        _settingsButton.ZIndex = 1;
        AddChild(_settingsButton);
        BuildSettingsMenu();

        _contextNavigationBar.Name = "NavigationContext";
        _contextNavigationBar.CustomMinimumSize = new Vector2(0f, 44f);
        _contextNavigationBar.AddThemeFontSizeOverride("font_size", 14);
        _contextNavigationBar.Visible = false;
        root.AddChild(_contextNavigationBar);
        _returnNavigationButton.Name = "Return";
        _returnNavigationButton.Text = "←";
        _returnNavigationButton.TooltipText = "Return to navigation overview";
        _returnNavigationButton.CustomMinimumSize = new Vector2(56f, 0f);
        _returnNavigationButton.AddThemeFontSizeOverride("font_size", 16);
        _returnNavigationButton.Pressed += ShowNavigationOverview;
        _contextNavigationBar.AddChild(_returnNavigationButton);

        _landscapeBar.Name = "LandscapeAreas";
        _contextNavigationBar.AddChild(_landscapeBar);
        BuildLandscapeBar();

        _structuresBar.Name = "StructuresAreas";
        _contextNavigationBar.AddChild(_structuresBar);
        BuildStructuresBar();

        _terrainAssetBar.Name = "TerrainAssets";
        _terrainAssetBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _contextNavigationBar.AddChild(_terrainAssetBar);
        BuildTerrainAssetBar();

        _propAssetBar.Name = "PropAssets";
        _propAssetBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _contextNavigationBar.AddChild(_propAssetBar);
        BuildPropAssetBar();

        _templateBar.Name = "SceneTemplates";
        _templateBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _contextNavigationBar.AddChild(_templateBar);
        BuildTemplateBar();

        _mapBar.Name = "Map";
        _mapBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _contextNavigationBar.AddChild(_mapBar);
        BuildMapBar();

        var documentBar = new HBoxContainer { Name = "DocumentStatus" };
        documentBar.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(documentBar);
        _workspaceLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sceneLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        documentBar.AddChild(_workspaceLabel);
        documentBar.AddChild(_sceneLabel);
        _documentStateLabel.Name = "DocumentState";
        _documentStateLabel.VerticalAlignment = VerticalAlignment.Center;
        _documentStateLabel.CustomMinimumSize = new Vector2(150f, 0f);
        documentBar.AddChild(_documentStateLabel);
        AddDocumentHistoryButton(documentBar, _undoButton, "Undo", UndoEdit,
            "Undo the last edit (Cmd/Ctrl+Z)");
        AddDocumentHistoryButton(documentBar, _redoButton, "Redo", RedoEdit,
            "Redo the last undone edit (Cmd/Ctrl+Shift+Z)");

        var content = new HBoxContainer
        {
            Name = "Content",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(content);

        var toolPanel = new PanelContainer
        {
            Name = "ToolBar",
            CustomMinimumSize = new Vector2(52f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddChild(toolPanel);
        var toolMargin = new MarginContainer();
        toolMargin.AddThemeConstantOverride("margin_left", 4);
        toolMargin.AddThemeConstantOverride("margin_top", 6);
        toolMargin.AddThemeConstantOverride("margin_right", 4);
        toolMargin.AddThemeConstantOverride("margin_bottom", 6);
        toolPanel.AddChild(toolMargin);
        var toolColumn = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
        };
        toolColumn.AddThemeConstantOverride("separation", 6);
        toolMargin.AddChild(toolColumn);
        foreach (var definition in EditorToolRegistry.ToolBarDefinitions)
            AddDrawingToolButton(toolColumn, definition);
        _anchorGroupEdit.MinValue = 1;
        _anchorGroupEdit.MaxValue = int.MaxValue;
        _anchorGroupEdit.Step = 1;
        _anchorGroupEdit.Value = 1;
        _anchorGroupEdit.CustomMinimumSize = new Vector2(44f, 0f);
        _anchorGroupEdit.TooltipText = "Group number for a new or selected Template Anchor";
        _anchorGroupEdit.ValueChanged += OnAnchorGroupChanged;
        toolColumn.AddChild(_anchorGroupEdit);

        // Below the separator: not another tool, but the same tool told to take
        // away instead of put down. It belongs beside the actions it modifies.
        toolColumn.AddChild(new HSeparator());
        _eraserToggle.Name = "EraserToggle";
        _eraserToggle.Text = string.Empty;
        _eraserToggle.Icon = GD.Load<Texture2D>("res://assets/icons/eraser.svg");
        _eraserToggle.ExpandIcon = false;
        _eraserToggle.Alignment = HorizontalAlignment.Center;
        _eraserToggle.ToggleMode = true;
        _eraserToggle.TooltipText = "Use the active drawing tool in erase mode";
        _eraserToggle.CustomMinimumSize = new Vector2(42f, 42f);
        _eraserToggle.AddThemeConstantOverride("icon_max_width", 24);
        _eraserToggle.Toggled += SetEraserEnabled;
        StyleToggleButton(_eraserToggle);
        toolColumn.AddChild(_eraserToggle);

        _contextMenuBar.Name = "ContextMenu";
        _contextMenuBar.CustomMinimumSize = new Vector2(0f, 38f);
        _contextMenuBar.AddThemeFontSizeOverride("font_size", 14);
        _toolContextLabel.Name = "ActiveToolLabel";
        _toolContextLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_toolContextLabel);
        _toolContextSeparator.Name = "ToolContextSeparator";
        _contextMenuBar.AddChild(_toolContextSeparator);
        _terrainBrushWidthLabel.Name = "TerrainBrushWidthLabel";
        _terrainBrushWidthLabel.Text = "Brush";
        _terrainBrushWidthLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_terrainBrushWidthLabel);
        _terrainBrushWidthEdit.Name = "TerrainBrushWidth";
        ConfigureTerrainBrushWidthInput(_terrainBrushWidthEdit);
        _terrainBrushWidthEdit.TooltipText =
            "How many Terrain cells wide Pencil and Line paint or erase, "
            + "centred on the cell under the pointer.";
        _terrainBrushWidthEdit.ValueChanged += SetTerrainBrushWidth;
        _contextMenuBar.AddChild(_terrainBrushWidthEdit);
        _sectionCutEdit.Name = "SectionCut";
        _sectionCutEdit.AddItem("Cut at", SectionCutAtItem);
        _sectionCutEdit.AddItem("Cut between", SectionCutBetweenItem);
        _sectionCutEdit.Selected = _sectionCutEdit.GetItemIndex(SectionCutAtItem);
        _sectionCutEdit.CustomMinimumSize = new Vector2(130f, 0f);
        _sectionCutEdit.TooltipText =
            "Use one upper clipping plane or inspect a finite band starting at one elevation.";
        _sectionCutEdit.ItemSelected += SetSectionCut;
        _viewOptionsBar.AddChild(_sectionCutEdit);
        _sectionElevationLabel.Name = "SectionElevationLabel";
        _sectionElevationLabel.Text = "Start";
        _sectionElevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _viewOptionsBar.AddChild(_sectionElevationLabel);
        _sectionElevationEdit.Name = "SectionElevation";
        ConfigureElevationInput(_sectionElevationEdit);
        _sectionElevationEdit.TooltipText =
            "Remove geometry strictly above this elevation, then look down on what remains.";
        _sectionElevationEdit.ValueChanged += SetSectionElevation;
        _viewOptionsBar.AddChild(_sectionElevationEdit);
        _sectionOffsetLabel.Name = "SectionOffsetLabel";
        _sectionOffsetLabel.Text = "Offset";
        _sectionOffsetLabel.VerticalAlignment = VerticalAlignment.Center;
        _viewOptionsBar.AddChild(_sectionOffsetLabel);
        _sectionOffsetEdit.Name = "SectionOffset";
        ConfigureSectionOffsetInput(_sectionOffsetEdit);
        _sectionOffsetEdit.TooltipText =
            "Positive height of the inspected band; its upper plane is Start + Offset.";
        _sectionOffsetEdit.ValueChanged += SetSectionOffset;
        _viewOptionsBar.AddChild(_sectionOffsetEdit);
        _waterHeatmapValueLabel.Name = "WaterHeatmapValueLabel";
        _waterHeatmapValueLabel.Text = "Water";
        _waterHeatmapValueLabel.VerticalAlignment = VerticalAlignment.Center;
        _viewOptionsBar.AddChild(_waterHeatmapValueLabel);
        _waterHeatmapValueEdit.Name = "WaterHeatmapValue";
        _waterHeatmapValueEdit.Text = "Surface";
        _waterHeatmapValueEdit.CustomMinimumSize = new Vector2(130f, 0f);
        _waterHeatmapValueEdit.Disabled = true;
        var waterHeatmapMenu = _waterHeatmapValueEdit.GetPopup();
        waterHeatmapMenu.AddRadioCheckItem("Surface", (int)WaterHeatmapValue.Surface);
        waterHeatmapMenu.AddRadioCheckItem("Bed", (int)WaterHeatmapValue.Bed);
        waterHeatmapMenu.AddRadioCheckItem("Cut top", (int)WaterHeatmapValue.CutTop);
        waterHeatmapMenu.IdPressed += SetWaterHeatmapValue;
        CheckWaterHeatmapItem(WaterHeatmapValue.Surface);
        _waterHeatmapValueEdit.TooltipText =
            "Which boundary of every water span the Heatmap shows. Terrain and Placements "
            + "continue to show their own elevation.";
        _viewOptionsBar.AddChild(_waterHeatmapValueEdit);
        _propLineOffsetLabel.Name = "PropLineOffsetLabel";
        _propLineOffsetLabel.Text = "Placement Offset";
        _propLineOffsetLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_propLineOffsetLabel);
        _propLineOffsetEdit.Name = "PropLineOffset";
        _propLineOffsetEdit.MinValue = 0;
        _propLineOffsetEdit.MaxValue = int.MaxValue;
        _propLineOffsetEdit.Step = 1;
        _propLineOffsetEdit.AllowGreater = false;
        _propLineOffsetEdit.AllowLesser = false;
        _propLineOffsetEdit.Suffix = " px";
        _propLineOffsetEdit.CustomMinimumSize = new Vector2(130f, 0f);
        _propLineOffsetEdit.ValueChanged += SetPropLineOffset;
        _contextMenuBar.AddChild(_propLineOffsetEdit);
        _surfaceLabel.Name = "SurfaceLabel";
        _surfaceLabel.Text = "Surface";
        _surfaceLabel.VerticalAlignment = VerticalAlignment.Center;
        _surfaceEdit.Name = "Surface";
        _surfaceEdit.CustomMinimumSize = new Vector2(130f, 0f);
        _surfaceEdit.ItemSelected += SelectSurfaceItem;
        _curvePointModeLabel.Name = "CurvePointModeLabel";
        _curvePointModeLabel.Text = "Point";
        _curvePointModeLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_curvePointModeLabel);
        _curvePointModeEdit.Name = "CurvePointMode";
        _curvePointModeEdit.AddItem("Linear", CurvePointModeLinear);
        _curvePointModeEdit.AddItem("Aligned", CurvePointModeAligned);
        SelectCurvePointModeItem(CurvePointModeLinear);
        _curvePointModeEdit.TooltipText =
            "How the next curve point's handles behave. Switchable while drawing; "
            + "it decides what the next point does and leaves the placed ones alone.";
        _curvePointModeEdit.ItemSelected += SetCurvePointMode;
        _contextMenuBar.AddChild(_curvePointModeEdit);
        // The bar's Point decides what the NEXT point does; this one changes the
        // point that is already there. They were one control until the panel
        // existed, and a control that means two things is what put a Depth into
        // the session while the author was editing a river.
        _selectedPointModeLabel.Name = "SelectedPointModeLabel";
        _selectedPointModeLabel.Text = "Point";
        _selectedPointModeLabel.VerticalAlignment = VerticalAlignment.Center;
        _selectedPointModeEdit.Name = "SelectedPointMode";
        _selectedPointModeEdit.AddItem("Linear", CurvePointModeLinear);
        _selectedPointModeEdit.AddItem("Aligned", CurvePointModeAligned);
        _selectedPointModeEdit.Selected =
            _selectedPointModeEdit.GetItemIndex(CurvePointModeLinear);
        _selectedPointModeEdit.TooltipText =
            "Changes the selected authored point. Aligned creates editable cyclic Bezier handles.";
        _selectedPointModeEdit.ItemSelected += SetSelectedPointMode;
        _riverWidthLabel.Name = "RiverWidthLabel";
        _riverWidthLabel.Text = "Width";
        _riverWidthLabel.VerticalAlignment = VerticalAlignment.Center;
        _riverWidthEdit.Name = "RiverWidth";
        ConfigureRiverWidthInput(_riverWidthEdit);
        _riverWidthEdit.TooltipText = "The width of the corridor around the river's centerline.";
        _riverWidthEdit.ValueChanged += SetRiverWidth;

        // A switch and its positions are two questions and used to be one
        // flattened list of "group: state" pairs. Two entries of one switch
        // then read as two switches, which is exactly how it was read.
        _riverSwitchLabel.Name = "RiverSwitchLabel";
        _riverSwitchLabel.Text = "Exists when";
        _riverSwitchLabel.VerticalAlignment = VerticalAlignment.Center;
        _riverSwitchEdit.Name = "RiverSwitch";
        _riverSwitchEdit.TooltipText =
            "Which switch decides whether this body is there. A body on no "
            + "switch is there in every state of the Scene.";
        _riverSwitchEdit.ItemSelected += SetRiverSwitch;

        // Where the Scene stands before anything has flipped it. It belongs to
        // the switch and not to this body, so it moves every river on that
        // switch at once, and the status line says how many.
        _riverInitiallyOnToggle.Name = "RiverInitiallyOn";
        _riverInitiallyOnToggle.Text = "Initially on?";
        _riverInitiallyOnToggle.TooltipText =
            "Whether this switch is on when the map opens. It belongs to the "
            + "switch, so it moves every river on it at once.";
        _riverInitiallyOnToggle.Toggled += SetRiverInitiallyOn;

        // Shown only while the selected river is on no switch: naming one is
        // how a drawn branch becomes switchable, and until recently that meant
        // writing the document by hand.
        _riverNewSwitchLabel.Name = "RiverNewSwitchLabel";
        _riverNewSwitchLabel.Text = "New switch";
        _riverNewSwitchLabel.VerticalAlignment = VerticalAlignment.Center;
        _riverNewSwitchEdit.Name = "RiverNewSwitch";
        _riverNewSwitchEdit.PlaceholderText = "name it, then Enter";
        _riverNewSwitchEdit.CustomMinimumSize = new Vector2(150f, 0f);
        _riverNewSwitchEdit.TooltipText =
            "Makes a switch with the positions dry and flowing, starting dry, "
            + "and puts this river in flowing. The name is what a consumer binds "
            + "its trigger to, so it is worth reading well.";
        _riverNewSwitchEdit.TextSubmitted += DeclareRiverSwitch;

        _riverRemoveSwitch.Name = "RiverRemoveSwitch";
        _riverRemoveSwitch.Text = "Remove switch";
        _riverRemoveSwitch.TooltipText =
            "Takes the switch away, and with it the activation of every river on "
            + "it - those are then there in every state.";
        _riverRemoveSwitch.Pressed += RemoveRiverSwitch;

        _pathWidthLabel.Name = "PathWidthLabel";
        _pathWidthLabel.Text = "Width";
        _pathWidthLabel.VerticalAlignment = VerticalAlignment.Center;
        _pathWidthEdit.Name = "PathWidth";
        ConfigurePathWidthInput(_pathWidthEdit);
        _pathWidthEdit.TooltipText =
            "The full width of the independent route surface at the next point.";
        _pathWidthEdit.ValueChanged += SetPathWidth;
        _pathGradeLabel.Name = "PathGradeLabel";
        _pathGradeLabel.Text = "Grade";
        _pathGradeLabel.VerticalAlignment = VerticalAlignment.Center;
        _pathGradeEdit.Name = "PathGrade";
        _pathGradeEdit.AddItem("-50%", PathGradeDownFifty);
        _pathGradeEdit.AddItem("-25%", PathGradeDownTwentyFive);
        _pathGradeEdit.AddItem("0%", PathGradeLevel);
        _pathGradeEdit.AddItem("+25%", PathGradeUpTwentyFive);
        _pathGradeEdit.AddItem("+50%", PathGradeUpFifty);
        _pathGradeEdit.Selected = _pathGradeEdit.GetItemIndex(PathGradeLevel);
        _pathGradeEdit.CustomMinimumSize = new Vector2(100f, 0f);
        _pathGradeEdit.TooltipText =
            "Rise or fall per horizontal metre on the segment arriving at the next point.";
        _pathGradeEdit.ItemSelected += SetPathGrade;
        _pathOperationLabel.Name = "PathOperationLabel";
        _pathOperationLabel.Text = "Operation";
        _pathOperationLabel.VerticalAlignment = VerticalAlignment.Center;
        _pathOperationEdit.Name = "PathOperation";
        _pathOperationEdit.AddItem("Additive", PathOperationAdditive);
        _pathOperationEdit.AddItem("Subtractive", PathOperationSubtractive);
        _pathOperationEdit.Selected = _pathOperationEdit.GetItemIndex(PathOperationAdditive);
        _pathOperationEdit.CustomMinimumSize = new Vector2(130f, 0f);
        _pathOperationEdit.TooltipText =
            "Whether the segment arriving at the next point stays above Terrain or excavates it.";
        _pathOperationEdit.ItemSelected += SetPathOperation;
        _pathClearanceLabel.Name = "PathClearanceLabel";
        _pathClearanceLabel.Text = "Clearance";
        _pathClearanceLabel.VerticalAlignment = VerticalAlignment.Center;
        _pathClearanceEdit.Name = "PathClearance";
        ConfigurePathClearanceInput(_pathClearanceEdit);
        _pathClearanceEdit.TooltipText =
            "Terrain height removed above the floor of the next subtractive segment.";
        _pathClearanceEdit.ValueChanged += SetPathClearance;
        _pathAutoStartToggle.Name = "PathAutoStart";
        _pathAutoStartToggle.Text = "Auto start";
        _pathAutoStartToggle.ButtonPressed = true;
        _pathAutoStartToggle.TooltipText =
            "Copy the effective Terrain height, including Hills, under the first point.";
        _pathAutoStartToggle.Toggled += SetPathAutoStart;
        _pathStartElevationLabel.Name = "PathStartElevationLabel";
        _pathStartElevationLabel.Text = "Start";
        _pathStartElevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _pathStartElevationEdit.Name = "PathStartElevation";
        ConfigureElevationInput(_pathStartElevationEdit);
        _pathStartElevationEdit.TooltipText =
            "Manual absolute height of the first point when Auto start is off.";
        _pathStartElevationEdit.ValueChanged += SetPathStartElevation;
        _bridgePlankCountLabel.Name = "BridgePlankCountLabel";
        _bridgePlankCountLabel.Text = "Planks";
        _bridgePlankCountLabel.VerticalAlignment = VerticalAlignment.Center;
        _bridgePlankCountEdit.Name = "BridgePlankCount";
        ConfigureBridgePlankCountInput(_bridgePlankCountEdit);
        _bridgePlankCountEdit.TooltipText =
            "How many planks fill the span. The count is what you author; how deep "
            + "one plank comes out is what is left once the gaps are taken off, so a "
            + "longer bridge gets thicker planks rather than more of them.";
        _bridgePlankCountEdit.ValueChanged += SetBridgePlankCount;
        _bridgePlankGapLabel.Name = "BridgePlankGapLabel";
        _bridgePlankGapLabel.Text = "Gap";
        _bridgePlankGapLabel.VerticalAlignment = VerticalAlignment.Center;
        _bridgePlankGapEdit.Name = "BridgePlankGap";
        ConfigureBridgePlankGapInput(_bridgePlankGapEdit);
        _bridgePlankGapEdit.TooltipText =
            "The empty run between two neighbouring planks. Gaps sit between planks "
            + "and never at the ends, so a deck always starts and finishes on wood.";
        _bridgePlankGapEdit.ValueChanged += SetBridgePlankGap;
        _bridgeWidthLabel.Name = "BridgeWidthLabel";
        _bridgeWidthLabel.Text = "Width";
        _bridgeWidthLabel.VerticalAlignment = VerticalAlignment.Center;
        _bridgeWidthEdit.Name = "BridgeWidth";
        ConfigureBridgeWidthInput(_bridgeWidthEdit);
        _bridgeWidthEdit.TooltipText =
            "The full deck width. The four posts sit at the corners it makes, "
            + "so changing it moves them.";
        _bridgeWidthEdit.ValueChanged += SetBridgeWidth;
        _bridgeElevationLabel.Name = "BridgeElevationLabel";
        _bridgeElevationLabel.Text = "Height";
        _bridgeElevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _bridgeElevationEdit.Name = "BridgeElevation";
        ConfigureElevationInput(_bridgeElevationEdit);
        _bridgeElevationEdit.TooltipText =
            "The deck surface, absolute and on the Workspace elevation quantum. "
            + "A bridge is level, so it is one number for the whole span.";
        _bridgeElevationEdit.ValueChanged += SetBridgeElevation;
        _elevationLabel.Name = "ElevationLabel";
        _elevationLabel.Text = "Height";
        _elevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _elevationEdit.Name = "Elevation";
        ConfigureElevationInput(_elevationEdit);
        _elevationEdit.TooltipText = "The height the drawing tools author at.";
        _elevationEdit.ValueChanged += SetAuthoringElevation;
        _propInstanceIdLabel.Name = "PropInstanceIdLabel";
        _propInstanceIdLabel.Text = "Id";
        _propInstanceIdLabel.VerticalAlignment = VerticalAlignment.Center;
        _propInstanceIdEdit.Name = "PropInstanceId";
        _propInstanceIdEdit.Editable = false;
        _propInstanceIdEdit.TooltipText =
            "This Placement's instance_id, stable for as long as it exists: "
            + "SceneMaker never hands the number out again, even after this "
            + "Placement is erased. Select and copy it to reference this exact "
            + "Placement from outside SceneMaker.";
        _snapWaterToggle.Name = "SnapWaterToTerrain";
        _snapWaterToggle.Text = "Snap";
        _snapWaterToggle.ButtonPressed = true;
        _snapWaterToggle.TooltipText =
            "Take the surface height from the Terrain under each placed point. "
            + "Switch it off to drive a corridor into a hill, where it must "
            + "not follow the ground. What gets stored is the height, not the "
            + "relationship: repainting Terrain later never moves the body.";
        _snapWaterToggle.Toggled += SetSnapWaterToTerrain;
        _contextMenuBar.AddChild(_snapWaterToggle);
        _waterElevationLabel.Name = "WaterElevationLabel";
        _waterElevationLabel.Text = "Surface level";
        _waterElevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _waterElevationEdit.Name = "WaterElevation";
        ConfigureElevationInput(_waterElevationEdit);
        _waterElevationEdit.TooltipText =
            "The surface height the next point takes with Snap off, and the fallback "
            + "for the first point when Snap finds no Terrain.";
        _waterElevationEdit.ValueChanged += SetWaterElevation;
        _waterDepthLabel.Name = "WaterDepthLabel";
        _waterDepthLabel.Text = "Depth";
        _waterDepthLabel.VerticalAlignment = VerticalAlignment.Center;
        _waterDepthEdit.Name = "WaterDepth";
        ConfigureWaterSpanInput(_waterDepthEdit, 0.1, WaterEditing.DefaultChannelDepthMeters);
        _waterDepthEdit.TooltipText =
            "How deep the channel is below the surface. The Terrain is carved away "
            + "from the bed upwards, so this is also where the river's floor sits.";
        _waterDepthEdit.ValueChanged += SetWaterDepth;
        _waterClearanceLabel.Name = "WaterClearanceLabel";
        _waterClearanceLabel.Text = "Clearance";
        _waterClearanceLabel.VerticalAlignment = VerticalAlignment.Center;
        _waterClearanceEdit.Name = "WaterClearance";
        ConfigureWaterSpanInput(_waterClearanceEdit, 0.0, WaterEditing.DefaultClearanceAboveMeters);
        _waterClearanceEdit.TooltipText =
            "The headroom the river needs above its surface. Where the ground never "
            + "reaches it the river is open; where it does, that much is left as a tunnel.";
        _waterClearanceEdit.ValueChanged += SetWaterClearance;
        _waterDerivedSpanLabel.Name = "WaterDerivedSpan";
        _waterDerivedSpanLabel.VerticalAlignment = VerticalAlignment.Center;
        _waterDerivedSpanLabel.TooltipText =
            "Derived boundaries only: bed = surface - depth; cut top = surface + clearance.";
        UpdateWaterDerivedSpan();
        _contextMenuBar.AddThemeConstantOverride("separation", 8);

        var canvasColumn = new VBoxContainer
        {
            Name = "CanvasColumn",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddChild(canvasColumn);
        canvasColumn.AddChild(_contextMenuBar);

        var canvasRow = new HBoxContainer
        {
            Name = "CanvasRow",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        canvasColumn.AddChild(canvasRow);

        _canvas.Name = "Canvas";
        _canvas.ConfigureInteraction(_interaction);
        if (_controller.Session is not null)
        {
            _canvas.ConfigureMetrics(_controller.Session.Metrics);
            _canvas.ConfigureTerrainAssets(_controller.Session.TerrainAssets);
            _canvas.ConfigurePropAssets(_controller.Session.PropAssets);
            _canvas.ConfigureBridgeKit(_controller.Session.BridgeKit);
        }
        _canvas.ViewChanged += UpdateViewStatus;
        _canvas.PointerChanged += UpdatePointerStatus;
        _canvas.OutcomeProduced += HandleToolOutcome;
        _canvas.StrokeEnded += EndEditStroke;
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;

        var canvasAndViewOptions = new VBoxContainer
        {
            Name = "CanvasAndViewOptions",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        canvasRow.AddChild(canvasAndViewOptions);
        canvasAndViewOptions.AddChild(_canvas);

        // Height and Section are ways of looking, not ways of working, and they
        // change nothing but this Canvas. They therefore sit on it, in its top
        // right corner, rather than in a column of their own beside it.
        _viewToggleOverlay.Name = "ViewToggles";
        _viewToggleOverlay.AddThemeConstantOverride("separation", 6);
        _viewToggleOverlay.AnchorLeft = 1f;
        _viewToggleOverlay.AnchorRight = 1f;
        _viewToggleOverlay.AnchorTop = 0f;
        _viewToggleOverlay.AnchorBottom = 0f;
        _viewToggleOverlay.OffsetLeft = -(ViewToggleOverlayWidth + ViewToggleOverlayMargin);
        _viewToggleOverlay.OffsetRight = -ViewToggleOverlayMargin;
        _viewToggleOverlay.OffsetTop = ViewToggleOverlayMargin;
        _viewToggleOverlay.OffsetBottom = ViewToggleOverlayMargin + ViewToggleOverlayHeight;
        _canvas.AddChild(_viewToggleOverlay);
        _canvas.TopRightReservedHeight = ViewToggleOverlayMargin + ViewToggleOverlayHeight;
        _heatmapToggle.Name = "HeatmapToggle";
        _heatmapToggle.Text = "m";
        _heatmapToggle.Alignment = HorizontalAlignment.Center;
        _heatmapToggle.ToggleMode = true;
        _heatmapToggle.TooltipText =
            "Show Terrain, Placements and Water by height instead of by Asset";
        _heatmapToggle.CustomMinimumSize = new Vector2(42f, 42f);
        _heatmapToggle.Toggled += SetHeatmapEnabled;
        StyleToggleButton(_heatmapToggle);
        _viewToggleOverlay.AddChild(_heatmapToggle);
        _sectionToggle.Name = "SectionToggle";
        _sectionToggle.Text = "S";
        _sectionToggle.Alignment = HorizontalAlignment.Center;
        _sectionToggle.ToggleMode = true;
        _sectionToggle.TooltipText =
            "Show the highest remaining surface after clipping the Scene at one elevation";
        _sectionToggle.CustomMinimumSize = new Vector2(42f, 42f);
        _sectionToggle.Toggled += SetSectionEnabled;
        StyleToggleButton(_sectionToggle);
        _viewToggleOverlay.AddChild(_sectionToggle);

        // A separator, because a panel is not a way of looking: the two on the
        // left change what the Canvas shows, this one changes what is beside
        // it.
        _viewToggleOverlay.AddChild(new VSeparator());
        _outlinerToggle.Name = "OutlinerToggle";
        _outlinerToggle.Text = "\u2261";
        _outlinerToggle.Alignment = HorizontalAlignment.Center;
        _outlinerToggle.ToggleMode = true;
        _outlinerToggle.ButtonPressed = true;
        _outlinerToggle.TooltipText = "Show the Outliner above the Inspector";
        _outlinerToggle.CustomMinimumSize = new Vector2(42f, 42f);
        _outlinerToggle.Toggled += SetOutlinerVisible;
        StyleToggleButton(_outlinerToggle);
        _viewToggleOverlay.AddChild(_outlinerToggle);

        // A second context bar, mirroring the one above the Canvas but living
        // below it, and reaching only as wide as the Canvas itself: it sits
        // between the ToolBar and the Inspector rather than spanning the full
        // window. Section and Heatmap are view options, not Landscape
        // tools, so their parameters no longer share the top bar with
        // whichever Landscape tool happens to be active; the bar only takes
        // up room while one of the two views is actually on.
        _viewOptionsBar.Name = "ViewOptions";
        _viewOptionsBar.CustomMinimumSize = new Vector2(0f, 38f);
        _viewOptionsBar.AddThemeFontSizeOverride("font_size", 14);
        _viewOptionsBar.AddThemeConstantOverride("separation", 8);
        canvasAndViewOptions.AddChild(_viewOptionsBar);

        // The right column shows what is being worked on: the object that is
        // selected, or the one the next stroke will make. A value that belongs
        // to an object lives here and nowhere else, which is what stops the bar
        // above the Canvas from meaning one thing while drawing and another
        // while selecting.
        // Two halves of one question: which objects are there, and what the
        // chosen one has. They are read one after the other in a single breath
        // - pick a body, change its numbers - so they are both on screen, and a
        // draggable divider decides how much each gets rather than a tab
        // deciding that one of them is gone.
        var rightColumn = new VSplitContainer
        {
            Name = "RightColumn",
            CustomMinimumSize = new Vector2(296f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        canvasRow.AddChild(rightColumn);

        _outlinerPanel.Name = "Outliner";
        _outlinerPanel.CustomMinimumSize = new Vector2(0f, 96f);
        _outlinerPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        _outlinerPanel.SizeFlagsStretchRatio = 1.0f;
        rightColumn.AddChild(_outlinerPanel);
        var outlinerMargin = new MarginContainer();
        outlinerMargin.AddThemeConstantOverride("margin_left", 10);
        outlinerMargin.AddThemeConstantOverride("margin_top", 8);
        outlinerMargin.AddThemeConstantOverride("margin_right", 10);
        outlinerMargin.AddThemeConstantOverride("margin_bottom", 8);
        _outlinerPanel.AddChild(outlinerMargin);
        var outlinerBody = new VBoxContainer { Name = "OutlinerBody" };
        outlinerBody.AddThemeConstantOverride("separation", 6);
        outlinerBody.AddThemeFontSizeOverride("font_size", 14);
        outlinerMargin.AddChild(outlinerBody);
        _outlinerHeaderLabel.Name = "OutlinerHeader";
        outlinerBody.AddChild(_outlinerHeaderLabel);
        outlinerBody.AddChild(new HSeparator());
        _outlinerEmptyLabel.Name = "OutlinerEmpty";
        _outlinerEmptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _outlinerEmptyLabel.Text = "Nothing of this kind in the Scene yet.";
        outlinerBody.AddChild(_outlinerEmptyLabel);
        var outlinerScroll = new ScrollContainer
        {
            Name = "OutlinerScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        outlinerBody.AddChild(outlinerScroll);
        _outlinerColumn.Name = "OutlinerColumn";
        _outlinerColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _outlinerColumn.AddThemeConstantOverride("separation", 2);
        outlinerScroll.AddChild(_outlinerColumn);

        var inspectorPanel = new PanelContainer
        {
            Name = "Inspector",
            CustomMinimumSize = new Vector2(0f, 96f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.6f,
        };
        rightColumn.AddChild(inspectorPanel);
        var inspectorMargin = new MarginContainer();
        inspectorMargin.AddThemeConstantOverride("margin_left", 10);
        inspectorMargin.AddThemeConstantOverride("margin_top", 8);
        inspectorMargin.AddThemeConstantOverride("margin_right", 10);
        inspectorMargin.AddThemeConstantOverride("margin_bottom", 8);
        inspectorPanel.AddChild(inspectorMargin);
        var inspectorScroll = new ScrollContainer
        {
            Name = "InspectorScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        inspectorMargin.AddChild(inspectorScroll);
        _inspectorColumn.Name = "InspectorColumn";
        _inspectorColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inspectorColumn.AddThemeConstantOverride("separation", 6);
        _inspectorColumn.AddThemeFontSizeOverride("font_size", 14);
        inspectorScroll.AddChild(_inspectorColumn);
        _inspectorHeaderLabel.Name = "InspectorHeader";
        _inspectorHeaderLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _inspectorColumn.AddChild(_inspectorHeaderLabel);
        _inspectorColumn.AddChild(new HSeparator());
        BuildInspectorRows();
        _inspectorEmptyLabel.Name = "InspectorEmpty";
        _inspectorEmptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _inspectorEmptyLabel.Text = "This tool sets nothing of its own.";
        _inspectorColumn.AddChild(_inspectorEmptyLabel);

        UpdateToolContextLabel();

        var footer = new HBoxContainer { Name = "Footer" };
        footer.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(footer);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_statusLabel);
        footer.AddChild(_viewLabel);
        footer.AddChild(new VSeparator());
        // Held at a width the longest reading fits, so that the numbers
        // changing under the pointer does not shove Zoom and Pan about.
        _pointerLabel.Name = "PointerPosition";
        _pointerLabel.CustomMinimumSize = new Vector2(210f, 0f);
        _pointerLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _pointerLabel.VerticalAlignment = VerticalAlignment.Center;
        footer.AddChild(_pointerLabel);
        UpdatePointerStatus();
    }

    /// <summary>
    /// The order of the panel, decided in one place rather than falling out of
    /// the order the controls happened to be configured in. Every row is built;
    /// which of them a tool shows is decided by the same visibility rules that
    /// used to decide what the bar showed.
    /// </summary>
    private void BuildInspectorRows()
    {
        AddInspectorRow(_propInstanceIdLabel, _propInstanceIdEdit);
        AddInspectorRow(_surfaceLabel, _surfaceEdit);
        AddInspectorRow(_elevationLabel, _elevationEdit);
        AddInspectorRow(_selectedPointModeLabel, _selectedPointModeEdit);
        AddInspectorRow(_riverWidthLabel, _riverWidthEdit);
        AddInspectorRow(_waterElevationLabel, _waterElevationEdit);
        AddInspectorRow(_waterDepthLabel, _waterDepthEdit);
        AddInspectorRow(_waterClearanceLabel, _waterClearanceEdit);
        AddInspectorRow(_waterDerivedSpanLabel);
        AddInspectorRow(_riverSwitchLabel, _riverSwitchEdit);
        AddInspectorRow(_riverInitiallyOnToggle);
        AddInspectorRow(_riverNewSwitchLabel, _riverNewSwitchEdit);
        AddInspectorRow(_riverRemoveSwitch);
        AddInspectorRow(_pathWidthLabel, _pathWidthEdit);
        AddInspectorRow(_pathGradeLabel, _pathGradeEdit);
        AddInspectorRow(_pathOperationLabel, _pathOperationEdit);
        AddInspectorRow(_pathClearanceLabel, _pathClearanceEdit);
        AddInspectorRow(_pathAutoStartToggle);
        AddInspectorRow(_pathStartElevationLabel, _pathStartElevationEdit);
        AddInspectorRow(_bridgeWidthLabel, _bridgeWidthEdit);
        AddInspectorRow(_bridgeElevationLabel, _bridgeElevationEdit);
        AddInspectorRow(_bridgePlankCountLabel, _bridgePlankCountEdit);
        AddInspectorRow(_bridgePlankGapLabel, _bridgePlankGapEdit);
    }

    /// <summary>
    /// Rebuilds the lines when the Scene or the mode changes. Selection and
    /// visibility are deliberately not in that condition: both are answered by
    /// <see cref="SyncOutlinerState"/> on the lines that are already there, so
    /// clicking a line never frees the very button that is emitting the click.
    /// </summary>
    private void RebuildOutliner()
    {
        var document = _controller.Document;
        var mode = _interaction.Mode;
        if (ReferenceEquals(_outlinerDocument, document) && _outlinerMode == mode)
        {
            SyncOutlinerState();
            return;
        }

        _outlinerDocument = document;
        _outlinerMode = mode;
        _outlinerNames.Clear();
        _outlinerEyes.Clear();
        foreach (var child in _outlinerColumn.GetChildren())
        {
            _outlinerColumn.RemoveChild(child);
            child.QueueFree();
        }

        _outlinerHeaderLabel.Text = EditorToolRegistry.ModeDisplayName(mode);
        var entries = document is null || _controller.Session is null
            ? []
            : OutlinerModel.Build(document, _controller.Session.Metrics, mode);
        foreach (var entry in entries) AddOutlinerRow(entry);
        _outlinerEmptyLabel.Visible = entries.Count == 0;
        SyncOutlinerState();
    }

    private void AddOutlinerRow(OutlinerEntry entry)
    {
        var row = new HBoxContainer
        {
            Name = $"Outline{_outlinerNames.Count}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 4);

        var objectId = entry.ObjectId;
        var eye = new CheckBox
        {
            TooltipText = "Show this on the Canvas. Hiding is a way of looking; "
                + "the Scene keeps the object and so does the export.",
        };
        eye.SetPressedNoSignal(!_interaction.State.IsHidden(objectId));
        eye.Toggled += visible => SetObjectVisible(objectId, visible);
        row.AddChild(eye);
        _outlinerEyes[objectId] = eye;

        // A branch hangs under the body it leaves, and the indent is the whole
        // of what says so.
        if (entry.Depth > 0)
            row.AddChild(new Control { CustomMinimumSize = new Vector2(entry.Depth * 12f, 0f) });

        var name = new Button
        {
            Text = entry.Label,
            ToggleMode = true,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TooltipText = entry.Loose
                ? $"'{objectId}' no longer hangs on anything: what fed it is gone."
                : entry.Absent
                    ? $"'{objectId}' is not there as the Scene opens: its switch, "
                        + "or a switch above it, starts off."
                    : entry.Note ?? objectId,
        };
        StyleToggleButton(name);
        // Red for the same reason the Canvas paints it red, said in words: the
        // list is where a relationship is visible, and being loose is one.
        // Dimmed is not a fault - it is a body the map opens without, and the
        // list is the only place that shows it while the Canvas draws every
        // body whatever its switch says.
        if (entry.Loose) name.AddThemeColorOverride("font_color", LooseInk);
        else if (entry.Absent) name.AddThemeColorOverride("font_color", AbsentInk);
        name.Pressed += () => HandleToolOutcome(_canvas.SelectObject(objectId));
        row.AddChild(name);
        _outlinerNames[objectId] = name;

        if (entry.Note is { } note)
        {
            var label = new Label
            {
                Text = note.Length > 16 ? note[..15] + "\u2026" : note,
                TooltipText = note,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(label);
        }

        _outlinerColumn.AddChild(row);
    }

    /// <summary>
    /// Marks the selected line and matches every eye to the set, without
    /// touching the lines themselves.
    /// </summary>
    private void SyncOutlinerState()
    {
        var selected = SelectedObjectId();
        foreach (var (objectId, button) in _outlinerNames)
        {
            button.ButtonPressed =
                string.Equals(objectId, selected, StringComparison.Ordinal);
        }

        foreach (var (objectId, eye) in _outlinerEyes)
            eye.SetPressedNoSignal(!_interaction.State.IsHidden(objectId));
    }

    /// <summary>What the mode's selection currently holds, by document id.</summary>
    private string? SelectedObjectId() => _interaction.Mode switch
    {
        EditorMode.River => _canvas.SelectedWaterBody?.WaterBodyId,
        EditorMode.ElevationRegion => _canvas.SelectedElevationRegionId,
        EditorMode.Bridge => _canvas.SelectedBridge?.BridgeId,
        EditorMode.Props => _canvas.SelectedPropInstanceId,
        EditorMode.Templates => _canvas.SelectedTemplateAnchorId,
        _ => null,
    };

    private void SetObjectVisible(string objectId, bool visible)
    {
        _canvas.SetObjectVisible(objectId, visible);
        SetStatus(visible
            ? $"'{objectId}' is on the Canvas again."
            : $"'{objectId}' is hidden while you work. The Scene and the export keep it.");
    }

    private void SetOutlinerVisible(bool visible)
    {
        _outlinerPanel.Visible = visible;
        SetStatus(visible ? "Outliner shown." : "Outliner hidden.");
    }

    /// <summary>A control that carries its own caption and takes the full width.</summary>
    private void AddInspectorRow(Control edit) => AddInspectorRow(edit, edit);

    /// <summary>
    /// Puts a caption and its control on one line and remembers the pair, so
    /// that <see cref="SyncInspectorRows"/> can take the whole line away. Two
    /// hidden children in a column still cost the separation between them, and
    /// a panel of those reads as a panel with holes in it.
    /// </summary>
    private void AddInspectorRow(Control label, Control edit)
    {
        var row = new HBoxContainer
        {
            Name = $"Row{_inspectorRows.Count}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);
        if (!ReferenceEquals(label, edit))
        {
            label.CustomMinimumSize = new Vector2(104f, 0f);
            row.AddChild(label);
        }
        edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(edit);
        _inspectorColumn.AddChild(row);
        _inspectorRows.Add((row, label, edit));
    }

    /// <summary>
    /// Hides a line whose contents are hidden, and says so when every line is.
    /// </summary>
    private void SyncInspectorRows()
    {
        var anyVisible = false;
        foreach (var (row, label, edit) in _inspectorRows)
        {
            var visible = label.Visible || edit.Visible;
            row.Visible = visible;
            anyVisible |= visible;
        }

        _inspectorEmptyLabel.Visible = !anyVisible;
    }

    /// <summary>
    /// What the values below belong to: an object that exists, or the one the
    /// next stroke will make. Without this line the same fields would again be
    /// two things at once - which is the reason the panel exists.
    /// </summary>
    private string InspectorHeader(
        WaterBodyDocument? selectedRiverBody,
        bool riverSelecting,
        bool riverActive,
        ElevationRegionDocument? selectedElevationRegion,
        bool elevationRegionActive,
        bool pathActive,
        bool bridgeActive)
    {
        if (selectedRiverBody is { } river)
        {
            return _canvas.SelectedWaterPointIndex is { } picked
                ? $"River {river.WaterBodyId} · point {picked + 1}"
                : $"River {river.WaterBodyId}";
        }

        if (riverSelecting) return "No river selected";
        if (riverActive) return "New river";
        if (selectedElevationRegion is { } hill)
        {
            return _canvas.SelectedElevationRegionPointIndex is { } point
                ? $"Hill {hill.ElevationRegionId} · point {point + 1}"
                : $"Hill {hill.ElevationRegionId}";
        }

        if (elevationRegionActive)
        {
            return _interaction.ActiveTool == EditorTool.SelectElevationRegion
                ? "No hill selected"
                : "New hill";
        }

        if (pathActive) return "New path";
        if (bridgeActive) return "New bridge";
        return EditorToolRegistry.ModeDisplayName(_interaction.Mode);
    }

    private void AddDrawingToolButton(
        Container parent,
        EditorToolDefinition definition)
    {
        var button = new Button
        {
            Name = definition.Tool.ToString(),
            Text = string.Empty,
            Icon = GD.Load<Texture2D>($"res://assets/icons/{definition.IconFileName}"),
            ExpandIcon = false,
            Alignment = HorizontalAlignment.Center,
            ToggleMode = true,
            ButtonGroup = _drawingToolButtons,
            TooltipText = definition.DisplayName,
            CustomMinimumSize = new Vector2(42f, 42f),
            Disabled = true,
        };
        button.AddThemeConstantOverride("icon_max_width", 24);
        StyleToggleButton(button);
        button.Pressed += () => SelectDrawingTool(definition.Tool);
        button.ButtonPressed = definition.Tool == _interaction.ActiveTool;
        _drawingToolControlsByTool.Add(definition.Tool, button);
        parent.AddChild(button);
    }

    /// <summary>
    /// Builds the buttons and nothing else. Choosing an Asset is a decision and
    /// belongs to entering an area, not to the code that adds controls - a
    /// layout pass that also sets editor state writes a status line that its own
    /// caller immediately overwrites.
    /// </summary>
    private void BuildTerrainAssetBar()
    {
        _terrainAssetControlsByKey.Clear();
        // The bar belongs to the one area that paints cells, so its label names
        // that area once and stays. It used to be rewritten per area, which put
        // a second `River ›` beside the River button in the navigation.
        _terrainAssetBar.AddChild(new Label { Text = "Terrain  ›" });
        foreach (var asset in _controller.Session?.TerrainAssets.Assets ?? [])
        {
            var button = new Button
            {
                Text = asset.Name,
                ToggleMode = true,
                ButtonGroup = _terrainAssetButtons,
                TooltipText = $"{asset.Name} · {asset.AssetKey}",
                CustomMinimumSize = new Vector2(120f, 0f),
            };
            StyleAssetButton(button, Color.FromHtml(asset.Color));
            button.Pressed += () => SelectTerrainAsset(asset.AssetKey);
            _terrainAssetBar.AddChild(button);
            _terrainAssetControlsByKey.Add(asset.AssetKey, button);
        }
        _terrainAssetBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

    /// <summary>
    /// Shows the Assets the active area can author and marks the one it holds -
    /// as a palette of chips where cells are painted, and as the Surface field
    /// of the tool's context bar where a curve is drawn. The area asks
    /// `TerrainAreaAssets`; this only reflects the answer.
    /// </summary>
    private void ShowTerrainAssetsForArea()
    {
        if (_controller.Session is not { } session) return;
        var mode = _interaction.Mode;
        // The area decides, the editor state takes the answer, and only then do
        // the controls show it. A control that displayed an Asset the state does
        // not hold would let the author draw with something else than the one
        // they can see.
        var field = TerrainAreaAssets.SurfaceFieldFor(
            mode, session.TerrainAssets, _canvas.SelectedTerrainAssetKey);
        var chosen = field is not null
            ? field.SelectedAssetKey
            : TerrainAreaAssets.Choose(
                mode, session.TerrainAssets, _canvas.SelectedTerrainAssetKey);
        _canvas.SelectedTerrainAssetKey = chosen;

        var offered = TerrainAreaAssets.Offered(mode, session.TerrainAssets)
            .Select(static asset => asset.AssetKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (assetKey, control) in _terrainAssetControlsByKey)
        {
            control.Visible = offered.Contains(assetKey);
            control.ButtonPressed = control.Visible
                && string.Equals(assetKey, chosen, StringComparison.Ordinal);
        }
        ShowSurfaceField(mode, field);
    }

    /// <summary>
    /// Fills the Surface control from the area's answer and decides nothing of
    /// its own - the Asset was chosen and written to the editor state above.
    /// Adding items and setting `Selected` raises no `ItemSelected`, so showing
    /// a Surface can never select one behind the state's back.
    ///
    /// <para>One offered Asset is not a choice and none is not a field to choose
    /// in. Both keep the control in place, because the tool has the same shape
    /// either way, and both say in the tooltip why it cannot be opened.</para>
    /// </summary>
    private void ShowSurfaceField(EditorMode mode, TerrainSurfaceField? field)
    {
        _surfaceEdit.Clear();
        if (field is null) return;

        foreach (var asset in field.Options)
        {
            _surfaceEdit.AddItem(asset.Name);
            _surfaceEdit.SetItemMetadata(_surfaceEdit.ItemCount - 1, asset.AssetKey);
        }
        var selected = -1;
        for (var index = 0; index < field.Options.Count; index++)
        {
            if (!string.Equals(
                    field.Options[index].AssetKey,
                    field.SelectedAssetKey,
                    StringComparison.Ordinal))
            {
                continue;
            }
            selected = index;
            break;
        }
        _surfaceEdit.Selected = selected;
        _surfaceEdit.Disabled = !field.Changeable;
        var area = EditorToolRegistry.ModeDisplayName(mode);
        _surfaceEdit.TooltipText = field switch
        {
            { Changeable: true } =>
                $"The Terrain Asset this {area} is made of. It is the material and "
                + "not the shape: changing it keeps the curve being drawn and "
                + "decides what the finished body carries.",
            { Options.Count: 1 } =>
                $"This Workspace offers only one {area} Surface, so there is "
                + "nothing to choose between.",
            _ => $"This Workspace enables no Terrain Asset that {area} can present, "
                + "so it has no Surface to offer.",
        };
    }

    /// <summary>
    /// The Surface control answers with an item index; the Asset it stands for
    /// travels as that item's metadata rather than as its position, which
    /// changes with what the Workspace offers.
    /// </summary>
    private void SelectSurfaceItem(long index)
    {
        if (index < 0 || index >= _surfaceEdit.ItemCount) return;
        if (_surfaceEdit.GetItemMetadata((int)index).AsString() is { Length: > 0 } assetKey)
            SelectTerrainAsset(assetKey);
    }

    /// <summary>
    /// An Asset button reads as its Asset. Selection is therefore carried by the
    /// background - a filled chip in the Asset's own colour - and never by the
    /// text: the default theme recolours a pressed Button's font white, which
    /// turned the one Asset the author had chosen into the one Asset whose
    /// colour they could no longer see.
    /// </summary>
    private static readonly Color ToggleOnFill = Color.FromHtml("#F2C94C");
    private static readonly Color ToggleOnHoverFill = Color.FromHtml("#FFE083");
    private static readonly Color ToggleOnEdge = Color.FromHtml("#E7B936");
    private static readonly Color ToggleOnInk = Color.FromHtml("#161B24");
    private static readonly Color LooseInk = Color.FromHtml("#FF5C5C");
    private static readonly Color AbsentInk = Color.FromHtml("#6B7686");

    private static readonly string[] ToggleOnInkStates =
    [
        "font_pressed_color",
        "font_hover_pressed_color",
        "icon_pressed_color",
        "icon_hover_pressed_color",
    ];

    /// <summary>
    /// Yellow while a toggle is on. The default theme marks a held-down button
    /// and a toggle that stays on with nearly the same shade, and an Eraser or
    /// a Section view left on silently changes what every later stroke and
    /// every later glance mean. The ink turns dark with the fill, because a
    /// pale glyph on yellow is no more readable than no highlight at all.
    ///
    /// Asset chips are deliberately not styled this way: their colour is the
    /// Asset, and a second meaning for the same fill would take that away.
    /// </summary>
    private static void StyleToggleButton(Button button)
    {
        button.AddThemeStyleboxOverride("pressed", ToggleBox(ToggleOnFill, ToggleOnEdge));
        button.AddThemeStyleboxOverride(
            "hover_pressed", ToggleBox(ToggleOnHoverFill, ToggleOnEdge));
        // Off and hovered: a hint of what the press would turn it into.
        button.AddThemeStyleboxOverride(
            "hover",
            ToggleBox(new Color(ToggleOnFill.R, ToggleOnFill.G, ToggleOnFill.B, 0.16f), null));
        foreach (var state in ToggleOnInkStates)
            button.AddThemeColorOverride(state, ToggleOnInk);
    }

    private static StyleBoxFlat ToggleBox(Color fill, Color? edge)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 4f,
            ContentMarginBottom = 4f,
        };
        if (edge is { } border)
        {
            box.BorderColor = border;
            box.SetBorderWidthAll(1);
        }

        box.SetCornerRadiusAll(4);
        return box;
    }

    private static void StyleAssetButton(Button button, Color color)
    {
        foreach (var state in AssetButtonFontStates)
            button.AddThemeColorOverride(state, color);
        button.AddThemeStyleboxOverride("pressed", AssetButtonBox(color, 0.22f, borderWidth: 1));
        button.AddThemeStyleboxOverride("hover", AssetButtonBox(color, 0.10f, borderWidth: 0));
        button.AddThemeStyleboxOverride(
            "hover_pressed", AssetButtonBox(color, 0.30f, borderWidth: 1));
    }

    private static readonly string[] AssetButtonFontStates =
    [
        "font_color",
        "font_pressed_color",
        "font_hover_color",
        "font_hover_pressed_color",
        "font_focus_color",
    ];

    private static StyleBoxFlat AssetButtonBox(Color color, float fill, int borderWidth)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(color.R, color.G, color.B, fill),
            BorderColor = color,
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 4f,
            ContentMarginBottom = 4f,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(4);
        return box;
    }

    private void BuildPropAssetBar()
    {
        _propAssetBar.AddChild(new Label { Text = "Placements  ›" });
        var session = _controller.Session;
        var bridgeKitAssetKeys = BridgeKitAssetKeys(session);
        // The bridge kit's plank and post are never offered here: nobody
        // authors a bridge's Assets in SceneMaker (see BridgeKit), so a
        // freestanding plank or post in this list would be a way to place one
        // wrongly, not a feature.
        var assets = (session?.PropAssets.Assets ?? [])
            .Where(asset => !bridgeKitAssetKeys.Contains(asset.AssetKey))
            .ToList();
        foreach (var asset in assets.Where(static asset => asset.Category is null))
        {
            AddPropAssetButton(asset);
        }
        // Grouped after the standalone Assets rather than interleaved, so the
        // dropdowns that keep many variants compact read as their own row
        // instead of breaking up the individual buttons around them.
        foreach (var group in assets
            .Where(static asset => asset.Category is not null)
            .GroupBy(static asset => asset.Category!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            AddPropAssetCategoryMenu(group.Key, [.. group]);
        }
        _propAssetBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

    private static HashSet<string> BridgeKitAssetKeys(WorkspaceSession? session) =>
        session?.BridgeKit is BridgeKitResolution.Resolved resolved
            ? new HashSet<string>(StringComparer.Ordinal)
                { resolved.Kit.PlankAssetKey, resolved.Kit.AnchorAssetKey }
            : [];

    private void AddPropAssetButton(PropDisplayAsset asset)
    {
        var button = new Button
        {
            Text = asset.Name,
            ToggleMode = true,
            ButtonGroup = _propAssetButtons,
            TooltipText = PropAssetTooltip(asset),
            CustomMinimumSize = new Vector2(160f, 0f),
        };
        StyleAssetButton(button, Color.FromHtml(asset.Color));
        button.Pressed += () => SelectPropAsset(asset.AssetKey);
        _propAssetBar.AddChild(button);
        if (_canvas.SelectedPropAssetKey is null)
        {
            button.ButtonPressed = true;
            SelectPropAsset(asset.AssetKey);
        }
    }

    /// <summary>
    /// One dropdown for every Asset that shares <paramref name="category"/>,
    /// so a Workspace with many variants of one idea stays as compact in this
    /// bar as a Workspace with none. The button itself carries no Asset color:
    /// unlike a single Placement's button, it stands for several at once.
    /// </summary>
    private void AddPropAssetCategoryMenu(string category, IReadOnlyList<PropDisplayAsset> assets)
    {
        var label = CategoryDisplayName(category);
        var menuButton = new MenuButton
        {
            Text = $"{label} ▾",
            CustomMinimumSize = new Vector2(160f, 0f),
            TooltipText = $"{label} · {assets.Count} Placements",
        };
        var menu = menuButton.GetPopup();
        for (var index = 0; index < assets.Count; index++)
        {
            menu.AddItem(assets[index].Name);
            menu.SetItemTooltip(index, PropAssetTooltip(assets[index]));
        }
        menu.IndexPressed += index => SelectPropAsset(assets[(int)index].AssetKey);
        _propAssetBar.AddChild(menuButton);
        if (_canvas.SelectedPropAssetKey is null && assets.Count > 0)
        {
            SelectPropAsset(assets[0].AssetKey);
        }
    }

    private static string PropAssetTooltip(PropDisplayAsset asset) =>
        $"{asset.Name} · {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} authoring px · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels})";

    /// <summary>
    /// A Category is an open, unread-for-meaning token like a surface - this
    /// is the one place its spelling matters, turning "totems" into "Totems"
    /// for the dropdown that groups it.
    /// </summary>
    private static string CategoryDisplayName(string category) =>
        string.Join(
            ' ',
            category.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(static word => char.ToUpperInvariant(word[0]) + word[1..]));

    private void BuildTemplateBar()
    {
        _placeTemplateAnchorButton.Text = "Place Anchor";
        _placeTemplateAnchorButton.CustomMinimumSize = new Vector2(150f, 0f);
        _placeTemplateAnchorButton.TooltipText =
            "Place a 32 × 32 authoring-pixel Template Anchor on the WorldGrid.";
        _placeTemplateAnchorButton.Pressed += BeginTemplateAnchorPlacement;
        _templateBar.AddChild(_placeTemplateAnchorButton);

        _templatesButton.Text = "Templates ↓";
        _templatesButton.CustomMinimumSize = new Vector2(150f, 0f);
        _templatesButton.Pressed += ShowTemplatesPopup;
        _templateBar.AddChild(_templatesButton);

        _regeneratePreviewButton.Text = "(Re-)Generate Preview";
        _regeneratePreviewButton.CustomMinimumSize = new Vector2(210f, 0f);
        _regeneratePreviewButton.Disabled = true;
        _regeneratePreviewButton.TooltipText =
            "Choose a new deterministic preview selection from every matching Template group.";
        _regeneratePreviewButton.Pressed += GenerateTemplatePreview;
        _templateBar.AddChild(_regeneratePreviewButton);
        _templateBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var popupMargin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(480f, 360f),
        };
        popupMargin.AddThemeConstantOverride("margin_left", 12);
        popupMargin.AddThemeConstantOverride("margin_top", 12);
        popupMargin.AddThemeConstantOverride("margin_right", 12);
        popupMargin.AddThemeConstantOverride("margin_bottom", 12);
        var popupContent = new VBoxContainer();
        popupContent.AddThemeConstantOverride("separation", 8);
        popupMargin.AddChild(popupContent);

        _templateSearchEdit.PlaceholderText = "Search Template name";
        _templateSearchEdit.TextChanged += _ => RenderTemplateRows();
        popupContent.AddChild(_templateSearchEdit);

        var filterRow = new HBoxContainer();
        filterRow.AddChild(new Label { Text = "Group filter" });
        _templateGroupFilter.MinValue = 0;
        _templateGroupFilter.MaxValue = int.MaxValue;
        _templateGroupFilter.Step = 1;
        _templateGroupFilter.Value = 0;
        _templateGroupFilter.TooltipText = "0 shows all Template groups";
        _templateGroupFilter.ValueChanged += _ => RenderTemplateRows();
        filterRow.AddChild(_templateGroupFilter);
        filterRow.AddChild(new Label { Text = "0 = all" });
        popupContent.AddChild(filterRow);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _templateRows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_templateRows);
        popupContent.AddChild(scroll);
        _templatesPopup.AddChild(popupMargin);
        AddChild(_templatesPopup);
    }

    private void BuildMapBar()
    {
        _mapBar.AddChild(new Label { Text = "Map  ›" });
        _mapBar.AddChild(new Label { Text = "Cells" });
        _mapExtensionCellsEdit.MinValue = 1;
        _mapExtensionCellsEdit.MaxValue = int.MaxValue;
        _mapExtensionCellsEdit.Step = 1;
        _mapExtensionCellsEdit.Value = 1;
        _mapExtensionCellsEdit.CustomMinimumSize = new Vector2(100f, 0f);
        _mapExtensionCellsEdit.TooltipText = "WorldGrid Cells to add to or remove from the selected map edge.";
        _mapExtensionCellsEdit.ValueChanged += _ => UpdateMapControls();
        _mapBar.AddChild(_mapExtensionCellsEdit);
        _mapExtensionMetricsLabel.CustomMinimumSize = new Vector2(150f, 0f);
        _mapExtensionMetricsLabel.VerticalAlignment = VerticalAlignment.Center;
        _mapBar.AddChild(_mapExtensionMetricsLabel);
        _mapResizeButton.Text = "Resize Map";
        _mapResizeButton.TooltipText = "Extend or shrink the map by the Cells above, on one edge.";
        BuildMapResizeMenu();
        _mapBar.AddChild(_mapResizeButton);
        _mapBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        UpdateMapControls();
    }

    private void BuildMapResizeMenu()
    {
        var menu = _mapResizeButton.GetPopup();
        menu.AddSeparator("Extend");
        menu.AddItem("North ↑", (int)MapResizeOperation.ExtendNorth);
        menu.AddItem("East →", (int)MapResizeOperation.ExtendEast);
        menu.AddItem("South ↓", (int)MapResizeOperation.ExtendSouth);
        menu.AddItem("West ←", (int)MapResizeOperation.ExtendWest);
        menu.AddSeparator("Shrink");
        menu.AddItem("North ↑", (int)MapResizeOperation.ShrinkNorth);
        menu.AddItem("East →", (int)MapResizeOperation.ShrinkEast);
        menu.AddItem("South ↓", (int)MapResizeOperation.ShrinkSouth);
        menu.AddItem("West ←", (int)MapResizeOperation.ShrinkWest);
        menu.IdPressed += id => ResizeMap((MapResizeOperation)id);
    }

    private void ShowTemplatesPopup()
    {
        ReloadWorkspaceTemplates();
        RenderTemplateRows();
        _templatesPopup.PopupCentered(new Vector2I(500, 380));
    }

    /// <summary>
    /// Reads and validates every Scene Template in the Workspace once, when the
    /// popup opens. Filtering works on this list afterwards: typing in the
    /// search box must not re-read and re-validate the whole Workspace on every
    /// keystroke.
    /// </summary>
    private void ReloadWorkspaceTemplates()
    {
        _autosaveTimer.Stop();
        var report = _controller.ReloadTemplates();
        UpdateDocumentState();
        _templateLoadError = report.Succeeded ? null : report.Message;
    }

    private void RenderTemplateRows()
    {
        foreach (var child in _templateRows.GetChildren())
        {
            _templateRows.RemoveChild(child);
            child.QueueFree();
        }

        if (_controller.Session is null)
        {
            _templateRows.AddChild(new Label { Text = "No Workspace loaded." });
            return;
        }
        if (_templateLoadError is not null)
        {
            _templateRows.AddChild(new Label { Text = _templateLoadError });
            return;
        }

        var search = _templateSearchEdit.Text.Trim();
        var groupFilter = checked((int)_templateGroupFilter.Value);
        var templates = _controller.Templates
            .Where(scene => search.Length == 0
                || scene.Document.SceneId.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(scene => groupFilter == 0
                || scene.Document.TemplateDefinition!.GroupNumber == groupFilter)
            .ToList();
        if (templates.Count == 0)
        {
            _templateRows.AddChild(new Label { Text = "No matching Scene Templates." });
            return;
        }

        foreach (var template in templates)
        {
            var row = new HBoxContainer();
            var name = new Label
            {
                Text = template.Document.SceneId,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            row.AddChild(name);
            var group = new SpinBox
            {
                MinValue = 1,
                MaxValue = int.MaxValue,
                Step = 1,
                Value = template.Document.TemplateDefinition!.GroupNumber,
                CustomMinimumSize = new Vector2(110f, 0f),
                TooltipText = $"Template group for {template.Document.SceneId}",
            };
            group.ValueChanged += value =>
                UpdateWorkspaceTemplateGroup(template.FilePath, checked((int)value));
            row.AddChild(group);
            _templateRows.AddChild(row);
        }
    }

    private void UpdateWorkspaceTemplateGroup(string filePath, int groupNumber)
    {
        _autosaveTimer.Stop();
        var report = _controller.SetTemplateGroup(filePath, groupNumber);
        if (!report.Succeeded)
        {
            ShowError(report.Message);
            return;
        }
        // The rows are deliberately not rebuilt: one of them is the SpinBox
        // that raised this. The controller has brought its listing up to date.
        ShowTemplatePreview();
        if (_controller.Scene is { } open) _canvas.UpdateScene(open);
        UpdateDocumentStatus();
        SetStatus(report.Message);
    }

    private void BuildSettingsMenu()
    {
        var menu = _settingsButton.GetPopup();
        menu.AddSeparator("Workspaces");
        AddSettingsItem(menu, "Create Workspace", SettingsMenuItem.CreateWorkspace);
        AddSettingsItem(menu, "Load Workspace", SettingsMenuItem.LoadWorkspace);
        AddSettingsItem(menu, "Workspace Assets", SettingsMenuItem.WorkspaceAssets);
        menu.AddSeparator("Scenes");
        AddSettingsItem(menu, "Create Scene", SettingsMenuItem.CreateScene);
        AddSettingsItem(menu, "Load Scene", SettingsMenuItem.LoadScene);
        AddSettingsItem(menu, "Load Template", SettingsMenuItem.LoadTemplate);
        menu.AddSeparator("Export");
        AddSettingsItem(menu, "Export Workspace", SettingsMenuItem.ExportWorkspace);
        menu.AddSeparator("Canvas Helpers");
        AddSettingsItem(menu, "Chunk Helper: not applicable", SettingsMenuItem.ChunkHelper);
        SetSettingsItemDisabled(SettingsMenuItem.ChunkHelper, true);
        menu.IdPressed += HandleSettingsMenu;
    }

    private static void AddSettingsItem(PopupMenu menu, string text, SettingsMenuItem item) =>
        menu.AddItem(text, (int)item);

    private void SetSettingsItemDisabled(SettingsMenuItem item, bool disabled)
    {
        var menu = _settingsButton.GetPopup();
        menu.SetItemDisabled(menu.GetItemIndex((int)item), disabled);
    }

    private void BuildDialogs()
    {
        _workspaceDirectoryDialog.Title = "Choose Parent Directory for Workspace";
        _workspaceDirectoryDialog.Access = FileDialog.AccessEnum.Filesystem;
        _workspaceDirectoryDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
        _workspaceDirectoryDialog.UseNativeDialog = true;
        _workspaceDirectoryDialog.DirSelected += directory =>
        {
            _pendingWorkspaceParentDirectory = ResolveFileSystemPath(directory);
            _workspaceIdEdit.Clear();
            _workspaceMinimumChannelDepthEdit.Value = 0.125;
            _createWorkspaceDialog.PopupCentered(new Vector2I(460, 220));
        };
        AddChild(_workspaceDirectoryDialog);

        _workspaceDirectoryLoadDialog.Title = "Load Workspace Folder";
        _workspaceDirectoryLoadDialog.Access = FileDialog.AccessEnum.Filesystem;
        _workspaceDirectoryLoadDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
        _workspaceDirectoryLoadDialog.UseNativeDialog = true;
        _workspaceDirectoryLoadDialog.DirSelected += LoadWorkspaceSelection;
        AddChild(_workspaceDirectoryLoadDialog);

        // Starts inside the open Workspace's scenes/ folder (set right before the
        // popup), so it naturally offers only Scene Instances - templates/
        // never appears alongside it.
        _sceneFileDialog.Title = "Load Scene";
        _sceneFileDialog.Access = FileDialog.AccessEnum.Filesystem;
        _sceneFileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        _sceneFileDialog.Filters = ["*.scene.json ; SceneMaker Scene"];
        _sceneFileDialog.UseNativeDialog = true;
        _sceneFileDialog.FileSelected += LoadScene;
        AddChild(_sceneFileDialog);

        // Same handler as Load Scene - starting inside templates/ instead of
        // scenes/ is what makes this dialog about Scene Templates.
        _templateFileDialog.Title = "Load Template";
        _templateFileDialog.Access = FileDialog.AccessEnum.Filesystem;
        _templateFileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        _templateFileDialog.Filters = ["*.scene.json ; SceneMaker Scene"];
        _templateFileDialog.UseNativeDialog = true;
        _templateFileDialog.FileSelected += LoadScene;
        AddChild(_templateFileDialog);

        _workspaceAssetsDialog.Title = "Workspace Assets";
        _workspaceAssetsDialog.OkButtonText = "Save";
        var assetScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(900f, 420f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _workspaceAssetRows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        assetScroll.AddChild(_workspaceAssetRows);
        _workspaceAssetsDialog.AddChild(assetScroll);
        _workspaceAssetsDialog.Confirmed += SaveWorkspaceAssets;
        AddChild(_workspaceAssetsDialog);

        _createWorkspaceDialog.Title = "Create Workspace";
        _createWorkspaceDialog.OkButtonText = "Create";
        var workspaceFields = new VBoxContainer();
        workspaceFields.AddChild(new Label { Text = "Stable workspace ID" });
        _workspaceIdEdit.PlaceholderText = "my_workspace";
        workspaceFields.AddChild(_workspaceIdEdit);
        workspaceFields.AddChild(new Label { Text = "Minimum channel depth" });
        ConfigureWaterSpanInput(_workspaceMinimumChannelDepthEdit, 0.001, 0.125m);
        _workspaceMinimumChannelDepthEdit.TooltipText =
            "How deep a river bed must be in this World - a fact this Workspace "
            + "holds the author to and never assumes.";
        workspaceFields.AddChild(_workspaceMinimumChannelDepthEdit);
        _createWorkspaceDialog.AddChild(workspaceFields);
        _createWorkspaceDialog.Confirmed += CreateWorkspace;
        AddChild(_createWorkspaceDialog);

        _createSceneDialog.Title = "Create Scene";
        _createSceneDialog.OkButtonText = "Create";
        var sceneFields = new GridContainer { Columns = 3 };
        sceneFields.AddChild(new Label { Text = "Stable scene ID" });
        _sceneIdEdit.PlaceholderText = "scene_name";
        _sceneIdEdit.TextChanged += _ => UpdateCreateSceneButton();
        sceneFields.AddChild(_sceneIdEdit);
        sceneFields.AddChild(new Control());
        sceneFields.AddChild(new Label { Text = "Scene type" });
        _sceneKindEdit.AddItem("Scene Instance", (int)SceneKind.Instance);
        _sceneKindEdit.AddItem("Scene Template", (int)SceneKind.Template);
        _sceneKindEdit.ItemSelected += _ => UpdateTemplateCreationFields();
        sceneFields.AddChild(_sceneKindEdit);
        sceneFields.AddChild(new Control());
        sceneFields.AddChild(new Label { Text = "Width (WorldGrid cells)" });
        ConfigureSizeInput(_sceneWidthEdit);
        _sceneWidthEdit.ValueChanged += _ => UpdateSceneSizeMetrics();
        sceneFields.AddChild(_sceneWidthEdit);
        ConfigureMetricsLabel(_sceneWidthMetricsLabel);
        sceneFields.AddChild(_sceneWidthMetricsLabel);
        sceneFields.AddChild(new Label { Text = "Height (WorldGrid cells)" });
        ConfigureSizeInput(_sceneHeightEdit);
        _sceneHeightEdit.ValueChanged += _ => UpdateSceneSizeMetrics();
        sceneFields.AddChild(_sceneHeightEdit);
        ConfigureMetricsLabel(_sceneHeightMetricsLabel);
        sceneFields.AddChild(_sceneHeightMetricsLabel);
        sceneFields.AddChild(new Label { Text = "Ground height" });
        ConfigureElevationInput(_sceneElevationEdit);
        _sceneElevationEdit.TooltipText =
            "The height a newly authored cell or Placement takes in this Scene.";
        sceneFields.AddChild(_sceneElevationEdit);
        sceneFields.AddChild(new Control());
        _templateCreationFields.AddChild(new Label { Text = "Template group" });
        ConfigurePositiveIntegerInput(_templateGroupEdit, 1);
        _templateCreationFields.AddChild(_templateGroupEdit);
        _templateCreationFields.AddChild(new Label { Text = "Pivot X (authoring px)" });
        ConfigureGridCoordinateInput(_templateInsertionXEdit);
        _templateInsertionXEdit.ValueChanged += _ => UpdateTemplatePivotFields();
        var pivotXRow = new HBoxContainer();
        pivotXRow.AddChild(_templateInsertionXEdit);
        ConfigureMetricsLabel(_templateInsertionXMetricsLabel);
        pivotXRow.AddChild(_templateInsertionXMetricsLabel);
        _templateCreationFields.AddChild(pivotXRow);
        _templateCreationFields.AddChild(new Label { Text = "Pivot Y (authoring px)" });
        ConfigureGridCoordinateInput(_templateInsertionYEdit);
        _templateInsertionYEdit.ValueChanged += _ => UpdateTemplatePivotFields();
        var pivotYRow = new HBoxContainer();
        pivotYRow.AddChild(_templateInsertionYEdit);
        ConfigureMetricsLabel(_templateInsertionYMetricsLabel);
        pivotYRow.AddChild(_templateInsertionYMetricsLabel);
        _templateCreationFields.AddChild(pivotYRow);
        _templatePivotCenterToggle.Text = "Center Pivot automatically";
        _templatePivotCenterToggle.TooltipText = "Use the center of the new Template as its insertion pivot.";
        _templatePivotCenterToggle.Toggled += _ => UpdateTemplatePivotFields();
        _templateCreationFields.AddChild(new Label());
        _templateCreationFields.AddChild(_templatePivotCenterToggle);
        sceneFields.AddChild(_templateCreationFields);
        sceneFields.AddChild(new Control());
        UpdateSceneSizeMetrics();
        _createSceneDialog.AddChild(sceneFields);
        _createSceneDialog.Confirmed += CreateScene;
        AddChild(_createSceneDialog);

        UpdateCreateSceneButton();

        _errorDialog.Title = "SceneMaker";
        AddChild(_errorDialog);

        _autosaveTimer.Name = "Autosave";
        _autosaveTimer.OneShot = true;
        _autosaveTimer.WaitTime = AutosaveDelaySeconds;
        _autosaveTimer.Timeout += PersistScene;
        AddChild(_autosaveTimer);
    }

    private static void ConfigureSizeInput(SpinBox input)
    {
        input.MinValue = 1;
        input.MaxValue = int.MaxValue;
        input.Step = 1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Value = 1;
    }

    private static void ConfigureMetricsLabel(Label label)
    {
        label.CustomMinimumSize = new Vector2(165f, 0f);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", Color.FromHtml("#B8C7D9"));
    }

    private void UpdateSceneSizeMetrics()
    {
        _sceneWidthMetricsLabel.Text = FormatSceneSizeMetrics(_sceneWidthEdit.Value);
        _sceneHeightMetricsLabel.Text = FormatSceneSizeMetrics(_sceneHeightEdit.Value);
        UpdateTemplatePivotFields();
    }

    private string FormatSceneSizeMetrics(double cells)
    {
        if (_controller.Session is null) return string.Empty;
        var integralCells = checked((int)cells);
        var metrics = _controller.Session.Metrics;
        var authoringPixels = checked(integralCells * metrics.AuthoringPixelsPerTerrainCell);
        var meters = integralCells * metrics.TerrainCellMeters;
        return $"= {meters:0.###} m · {authoringPixels} px";
    }

    private static void ConfigurePositiveIntegerInput(SpinBox input, int initialValue)
    {
        input.MinValue = 1;
        input.MaxValue = int.MaxValue;
        input.Step = 1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Value = initialValue;
    }

    /// <summary>
    /// Bounded well short of int.MaxValue on purpose: a brush is a square of
    /// its width squared, and nothing an author is doing with a stroke needs
    /// one thousands of cells across - it would just stall the Scene it was
    /// asked to paint.
    /// </summary>
    private static void ConfigureTerrainBrushWidthInput(SpinBox input)
    {
        input.MinValue = 1;
        input.MaxValue = 64;
        input.Step = 1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Value = 1;
    }

    private static void ConfigureGridCoordinateInput(SpinBox input)
    {
        input.MinValue = 0;
        input.MaxValue = int.MaxValue;
        input.Step = 1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Value = 0;
    }

    private void UpdateTemplateCreationFields()
    {
        _templateCreationFields.Visible = SelectedSceneKind() == SceneKind.Template;
        UpdateTemplatePivotFields();
    }

    private void UpdateCreateSceneButton()
    {
        _createSceneDialog.GetOkButton().Disabled = string.IsNullOrWhiteSpace(_sceneIdEdit.Text);
    }

    private void UpdateTemplatePivotFields()
    {
        if (_controller.Session is null) return;
        var metrics = _controller.Session.Metrics;
        if (_templatePivotCenterToggle.ButtonPressed && SelectedSceneKind() == SceneKind.Template)
        {
            var widthPixels = (decimal)_sceneWidthEdit.Value * metrics.AuthoringPixelsPerTerrainCell;
            var heightPixels = (decimal)_sceneHeightEdit.Value * metrics.AuthoringPixelsPerTerrainCell;
            _templateInsertionXEdit.SetValueNoSignal((double)(widthPixels / 2m));
            _templateInsertionYEdit.SetValueNoSignal((double)(heightPixels / 2m));
        }
        _templateInsertionXEdit.Editable = !_templatePivotCenterToggle.ButtonPressed;
        _templateInsertionYEdit.Editable = !_templatePivotCenterToggle.ButtonPressed;
        _templateInsertionXMetricsLabel.Text = FormatPivotMeters(_templateInsertionXEdit.Value);
        _templateInsertionYMetricsLabel.Text = FormatPivotMeters(_templateInsertionYEdit.Value);
    }

    private string FormatPivotMeters(double authoringPixels)
    {
        if (_controller.Session is null) return string.Empty;
        var meters = (decimal)authoringPixels * _controller.Session.Metrics.MetersPerAuthoringPixel;
        return $"= {meters:0.#####} m";
    }

    private SceneKind SelectedSceneKind() =>
        (SceneKind)_sceneKindEdit.GetItemId(_sceneKindEdit.Selected);

    private void AddPerspectiveButton(
        Container parent,
        string name,
        bool available,
        string tooltip)
    {
        var button = new Button
        {
            Text = name,
            Disabled = !available,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(name == "Scene Templates" ? 150f : 110f, 0f),
        };
        if (available) button.Pressed += () => SelectPerspective(EditorModeForPerspective(name), name);
        parent.AddChild(button);
    }

    /// <summary>
    /// The Landscape areas, side by side inside the context bar. Landscape
    /// is a way in, not a place to be: opening it lands in one of them, and the
    /// other is one click away.
    /// </summary>
    private void BuildLandscapeBar()
    {
        _landscapeBar.AddChild(new Label { Text = "Landscape  ›" });
        AddLandscapeAreaButton(EditorMode.River, "River", "Draw and erase rivers.");
        AddLandscapeAreaButton(
            EditorMode.Path,
            "Path",
            "Draw and erase inclined route surfaces.");
        AddLandscapeAreaButton(
            EditorMode.ElevationRegion,
            "Hill",
            "Draw and reshape raised Terrain regions.");
    }

    /// <summary>
    /// The Structures areas, built the same way Landscape is. Bridge is the
    /// only one today and the bar still exists, because Structures is a family
    /// - fences, walls, stairs, ladders - and a family with one member is what
    /// every family starts as. The alternative was jumping straight into Bridge
    /// and having nowhere to put the second one.
    /// </summary>
    private void BuildStructuresBar()
    {
        _structuresBar.AddChild(new Label { Text = "Structures  ›" });
        AddAreaButton(
            _structuresBar,
            _structuresAreaButtons,
            _structuresAreaControls,
            EditorMode.Bridge,
            "Bridge",
            "Draw, select and reshape straight level spans.");
    }

    private void AddLandscapeAreaButton(EditorMode mode, string name, string tooltip) =>
        AddAreaButton(
            _landscapeBar, _landscapeAreaButtons, _landscapeAreaControls, mode, name, tooltip);

    private void AddAreaButton(
        Container bar,
        ButtonGroup group,
        Dictionary<EditorMode, Button> controls,
        EditorMode mode,
        string name,
        string tooltip)
    {
        var button = new Button
        {
            Name = name,
            Text = name,
            ToggleMode = true,
            ButtonGroup = group,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(110f, 0f),
        };
        StyleToggleButton(button);
        button.Pressed += () => SelectPerspective(mode, name);
        bar.AddChild(button);
        controls.Add(mode, button);
    }

    /// <summary>Opens Landscape on the area it was last left in.</summary>
    private void SelectLandscapeContext() =>
        SelectPerspective(_landscapeArea, EditorToolRegistry.ModeDisplayName(_landscapeArea));

    /// <summary>The same for Structures.</summary>
    private void SelectStructuresContext() =>
        SelectPerspective(_structuresArea, EditorToolRegistry.ModeDisplayName(_structuresArea));

    private static EditorMode EditorModeForPerspective(string perspective) => perspective switch
    {
        "Terrain" => EditorMode.Terrain,
        "River" => EditorMode.River,
        "Path" => EditorMode.Path,
        "Hill" => EditorMode.ElevationRegion,
        "Structures" => EditorMode.Bridge,
        "Placements" => EditorMode.Props,
        "Scene Templates" => EditorMode.Templates,
        _ => throw new ArgumentOutOfRangeException(nameof(perspective)),
    };

    private void HandleSettingsMenu(long id)
    {
        switch ((SettingsMenuItem)id)
        {
            case SettingsMenuItem.CreateWorkspace:
                _workspaceDirectoryDialog.PopupCenteredRatio(0.75f);
                break;
            case SettingsMenuItem.LoadWorkspace:
                OpenWorkspaceFinder();
                break;
            case SettingsMenuItem.WorkspaceAssets:
                ShowWorkspaceAssetsDialog();
                break;
            case SettingsMenuItem.CreateScene:
                if (_controller.Session is null)
                {
                    ShowError("Create or load a Workspace before creating a Scene.");
                    return;
                }
                _sceneIdEdit.Clear();
                _sceneKindEdit.Select(0);
                _templateGroupEdit.Value = 1;
                _templatePivotCenterToggle.ButtonPressed = false;
                _templateInsertionXEdit.Value = 0;
                _templateInsertionYEdit.Value = 0;
                UpdateTemplateCreationFields();
                UpdateSceneSizeMetrics();
                _createSceneDialog.PopupCentered(new Vector2I(760, 420));
                break;
            case SettingsMenuItem.LoadScene:
                if (_controller.Session is null)
                {
                    ShowError("Create or load a Workspace before loading a Scene.");
                    return;
                }
                _sceneFileDialog.CurrentDir = _controller.Session.Workspace.ScenesDirectoryPath;
                _sceneFileDialog.PopupCenteredRatio(0.75f);
                break;
            case SettingsMenuItem.LoadTemplate:
                if (_controller.Session is null)
                {
                    ShowError("Create or load a Workspace before loading a Template.");
                    return;
                }
                _templateFileDialog.CurrentDir = _controller.Session.Workspace.TemplatesDirectoryPath;
                _templateFileDialog.PopupCenteredRatio(0.75f);
                break;
            case SettingsMenuItem.ExportWorkspace:
                ExportWorkspaceCommand();
                break;
        }
    }

    private void OpenWorkspaceFinder()
    {
        if (DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile))
        {
            DisplayServer.FileDialogShow(
                "Load Workspace",
                WorkspaceDialogStartDirectory(),
                "",
                false,
                DisplayServer.FileDialogMode.OpenAny,
                ["*.json ; SceneMaker Workspace config"],
                Callable.From<bool, Variant, int>(HandleWorkspaceFinderResult));
            return;
        }

        // Non-macOS fallback: Godot's folder dialog still enforces config.json.
        _workspaceDirectoryLoadDialog.CurrentDir = WorkspaceDialogStartDirectory();
        _workspaceDirectoryLoadDialog.PopupCenteredRatio(0.75f);
    }

    private void HandleWorkspaceFinderResult(bool accepted, Variant selectedPaths, int _)
    {
        if (!accepted) return;
        var paths = selectedPaths.AsStringArray();
        if (paths.Length == 0)
        {
            SetStatus("Workspace load blocked: the file chooser returned no selection.");
            return;
        }
        LoadWorkspaceSelection(paths[0]);
    }

    private void ExportWorkspaceCommand()
    {
        _autosaveTimer.Stop();
        var report = _controller.ExportWorkspace();
        UpdateDocumentState();
        if (report.Succeeded) ReportExportAndSyncWorld01(report.Message);
        else ShowError(report.Message);
    }

    /// <summary>
    /// A successful Export of the world01 Workspace still leaves world01
    /// itself unsynced until its own sync script runs; run it right here so
    /// no one has to remember a second manual step. Every other Workspace
    /// has no such script, so <see cref="World01Sync.RunIfApplicable"/> is a
    /// no-op for it and this just reports the export as usual.
    /// </summary>
    private void ReportExportAndSyncWorld01(string exportMessage)
    {
        var sync = _controller.Session is { } session ? World01Sync.RunIfApplicable(session) : null;
        if (sync is not { } result)
        {
            SetStatus(exportMessage);
            return;
        }
        var combined = $"{exportMessage} {result.Message}";
        if (result.Succeeded) SetStatus(combined);
        else ShowError(combined);
    }

    private void ShowWorkspaceAssetsDialog()
    {
        if (_controller.Session is null) return;
        foreach (var child in _workspaceAssetRows.GetChildren())
        {
            _workspaceAssetRows.RemoveChild(child);
            child.QueueFree();
        }
        _workspaceAssetEditorRows.Clear();
        _workspaceAssetRows.AddChild(new Label
        {
            Text = "SceneMaker owns these authoring Assets: their names, roles, colors and Terrain semantics. PolyTools contributes only the visible footprint and pivot/anchor used by Placement Assets.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        foreach (var profile in _controller.Session.Configuration.AssetProfiles)
        {
            var isTerrain = profile.Role == WorkspaceAssetRole.Terrain;
            var row = new GridContainer { Columns = 8 };
            var enabled = new CheckBox { Text = profile.AssetKey, ButtonPressed = true };
            enabled.CustomMinimumSize = new Vector2(180f, 0f);
            var displayName = NewAssetField(profile.DisplayName, "Display name");
            var role = new OptionButton();
            role.AddItem("Terrain", (int)WorkspaceAssetRole.Terrain);
            role.AddItem("Placement", (int)WorkspaceAssetRole.Placement);
            role.Selected = role.GetItemIndex((int)profile.Role);
            var color = NewAssetField(profile.Color, "#RRGGBB");
            // Only Terrain presents a surface, and only Terrain may carry one.
            var surface = NewAssetField(
                profile.Surface ?? (isTerrain ? DefaultSurface : string.Empty),
                isTerrain ? "land" : string.Empty);
            surface.Editable = isTerrain;
            // How a Terrain Asset is authored decides which tools it offers, so
            // it is chosen here rather than guessed from its surface.
            var authoring = new OptionButton { Disabled = !isTerrain };
            authoring.AddItem("cells", (int)TerrainAuthoring.Cells);
            authoring.AddItem("curve", (int)TerrainAuthoring.Curve);
            authoring.Selected = authoring.GetItemIndex(
                (int)(profile.Authoring ?? TerrainAuthoring.Cells));
            // Optional on every role: whichever Assets share a token are
            // offered together as one dropdown instead of one button each.
            var category = NewAssetField(profile.Category ?? string.Empty, "e.g. totems");
            // Only a Placement has a PolyTools footprint to scale; Terrain
            // never declares one (see WorkspaceAssetProfile.Scale).
            var scale = new SpinBox { Editable = !isTerrain };
            ConfigureScaleInput(scale, profile.Scale ?? 1m);
            row.AddChild(enabled);
            row.AddChild(displayName);
            row.AddChild(role);
            row.AddChild(color);
            row.AddChild(surface);
            row.AddChild(authoring);
            row.AddChild(category);
            row.AddChild(scale);
            _workspaceAssetRows.AddChild(row);
            var editorRow = new WorkspaceAssetEditorRow(
                profile.AssetKey,
                enabled,
                displayName,
                role,
                color,
                surface,
                authoring,
                category,
                scale,
                profile.PolyToolsAssetId);
            role.ItemSelected += item => SetWorkspaceAssetRole(editorRow, item);
            _workspaceAssetEditorRows.Add(profile.AssetKey, editorRow);
        }
        _workspaceAssetsDialog.PopupCentered(new Vector2I(760, 520));
    }

    private static void SetWorkspaceAssetRole(WorkspaceAssetEditorRow row, long item)
    {
        var role = (WorkspaceAssetRole)row.Role.GetItemId((int)item);
        var isTerrain = role == WorkspaceAssetRole.Terrain;
        row.Surface.Editable = isTerrain;
        row.Surface.PlaceholderText = isTerrain ? DefaultSurface : string.Empty;
        row.Authoring.Disabled = !isTerrain;
        if (!isTerrain) row.Surface.Text = string.Empty;
        row.Scale.Editable = !isTerrain;
        if (isTerrain) row.Scale.Value = 1;
    }

    /// <summary>What a Terrain Asset presents unless the author says otherwise.</summary>
    private const string DefaultSurface = "land";

    /// <summary>A vertical extent in metres: a depth below or a height above.</summary>
    private static void ConfigureWaterSpanInput(SpinBox input, double minimum, decimal value)
    {
        input.MinValue = minimum;
        input.MaxValue = 1000;
        input.Step = 0.1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)value;
    }

    private static void ConfigureElevationInput(SpinBox input)
    {
        input.MinValue = -1000;
        input.MaxValue = 1000;
        input.Step = 0.001;
        input.CustomArrowStep = 0.1;
        input.CustomArrowRound = true;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(130f, 0f);
        input.Value = (double)SceneDocument.GroundElevationMeters;
        input.Editable = false;
    }

    private static void ConfigureSectionOffsetInput(SpinBox input)
    {
        input.MinValue = 0.001;
        input.MaxValue = 1000;
        input.Step = 0.001;
        input.CustomArrowStep = 0.1;
        input.CustomArrowRound = true;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(130f, 0f);
        input.Value = 1.0;
        input.Editable = false;
    }

    /// <summary>
    /// Godot counts in doubles, the documents in decimals. An authored absolute
    /// height is canonical only after the open Workspace has snapped it to its
    /// vertical grid; setting the control without a signal makes typed values
    /// visibly agree with the number that will be stored.
    /// </summary>
    private decimal ElevationOf(SpinBox input, double value)
    {
        var metrics = _controller.Session?.Metrics
            ?? throw new InvalidOperationException(
                "An absolute height requires an open Workspace.");
        var elevation = metrics.SnapElevation((decimal)value);
        input.SetValueNoSignal((double)elevation);
        return elevation;
    }

    private decimal SectionOffsetOf(double value)
    {
        var metrics = _controller.Session?.Metrics
            ?? throw new InvalidOperationException(
                "A Section offset requires an open Workspace.");
        var offset = metrics.SnapElevation((decimal)value);
        if (offset <= 0m) offset = metrics.ElevationQuantumMeters;
        _sectionOffsetEdit.SetValueNoSignal((double)offset);
        return offset;
    }

    /// <summary>
    /// Widths and vertical extents are not absolute elevations and therefore do
    /// not use the Workspace's vertical grid. Keep their compact decimal form
    /// so they do not churn a document with a value only nearly what was typed.
    /// </summary>
    private static decimal DecimalOf(double value) => decimal.Parse(
        value.ToString("0.0##", CultureInfo.InvariantCulture),
        CultureInfo.InvariantCulture);

    /// <summary>
    /// A Scene opens at its own ground height. What the author set when the
    /// Scene was created is therefore where the context bar starts, every time
    /// it is opened.
    /// </summary>
    private void ShowAuthoringElevation()
    {
        var elevation = _controller.Document?.DefaultElevationMeters
            ?? SceneDocument.GroundElevationMeters;
        _canvas.ElevationMeters = elevation;
        _elevationEdit.SetValueNoSignal((double)elevation);
    }

    private void SetAuthoringElevation(double value)
    {
        var elevation = ElevationOf(_elevationEdit, value);
        if (_interaction.Mode == EditorMode.ElevationRegion
            && _interaction.ActiveTool == EditorTool.SelectElevationRegion)
        {
            HandleToolOutcome(_canvas.SetSelectedElevationRegionElevation(elevation));
            return;
        }
        _canvas.ElevationMeters = elevation;
        SetStatus($"Drawing at {elevation:0.###} m.");
    }

    private void SetTerrainBrushWidth(double value)
    {
        var width = checked((int)value);
        _canvas.TerrainBrushWidthCells = width;
        SetStatus(width == 1
            ? "Brush: single cell."
            : $"Brush: {width}×{width} Terrain cells.");
    }

    private static LineEdit NewAssetField(string value, string placeholder) => new()
    {
        Text = value,
        PlaceholderText = placeholder,
        CustomMinimumSize = new Vector2(100f, 0f),
    };

    /// <summary>
    /// A unitless multiplier, not a measurement - so it gets its own field
    /// rather than reusing <see cref="ConfigureWaterSpanInput"/>'s "m" suffix.
    /// </summary>
    private static void ConfigureScaleInput(SpinBox input, decimal value)
    {
        input.MinValue = 0.01;
        input.MaxValue = 1000;
        input.Step = 0.01;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = "x";
        input.CustomMinimumSize = new Vector2(90f, 0f);
        input.Value = (double)value;
    }

    private void SaveWorkspaceAssets()
    {
        if (_controller.Session is null) return;
        List<WorkspaceAssetProfile> profiles = [];
        foreach (var row in _workspaceAssetEditorRows.Values)
        {
            if (!row.Enabled.ButtonPressed) continue;
            var role = (WorkspaceAssetRole)row.Role.GetItemId(row.Role.Selected);
            var isTerrain = role == WorkspaceAssetRole.Terrain;
            var surface = isTerrain ? row.Surface.Text.Trim() : null;
            var authoring = isTerrain
                ? (TerrainAuthoring)row.Authoring.GetItemId(row.Authoring.Selected)
                : (TerrainAuthoring?)null;
            var category = row.Category.Text.Trim();
            var scale = isTerrain ? (decimal?)null : (decimal)row.Scale.Value;
            if (scale == 1m) scale = null;
            profiles.Add(new WorkspaceAssetProfile(
                row.AssetKey,
                row.DisplayName.Text.Trim(),
                role,
                row.Color.Text.Trim(),
                surface,
                authoring,
                row.PolyToolsAssetId,
                category.Length == 0 ? null : category,
                scale));
        }

        var report = _controller.SaveAssetProfiles(profiles);
        if (!report.Succeeded)
        {
            ShowError(report.Message);
            return;
        }
        ShowSession();
        UpdateDocumentStatus();
        SetStatus(report.Message);
    }

    private void CreateWorkspace()
    {
        if (_pendingWorkspaceParentDirectory is null)
        {
            ShowError("Choose a parent directory for the Workspace first.");
            return;
        }

        var report = _controller.CreateWorkspace(
            _pendingWorkspaceParentDirectory,
            _workspaceIdEdit.Text.Trim(),
            DecimalOf(_workspaceMinimumChannelDepthEdit.Value));
        if (!report.Succeeded)
        {
            ShowError(report.Message);
            return;
        }
        CloseOpenScene();
        UpdateDocumentStatus();
        SetStatus(report.Message);
    }

    private void LoadWorkspaceSelection(string selectedPath)
    {
        var report = _controller.OpenWorkspaceAt(ResolveFileSystemPath(selectedPath));
        if (!report.Succeeded)
        {
            SetStatus(report.Message);
            return;
        }
        ShowSession();
        CloseOpenScene();
        SaveRecentSession();
        UpdateDocumentStatus();
        SetStatus(report.Message);
    }

    /// <summary>Drops whatever Scene was open, without writing anything.</summary>
    private void CloseOpenScene()
    {
        _controller.CloseScene();
        ClearTemplatePreview();
        _canvas.ShowScene(null);
    }

    private void CreateScene()
    {
        var sceneId = _sceneIdEdit.Text.Trim();
        var widthCells = checked((int)_sceneWidthEdit.Value);
        var heightCells = checked((int)_sceneHeightEdit.Value);
        var groundHeight = ElevationOf(_sceneElevationEdit, _sceneElevationEdit.Value);
        var report = SelectedSceneKind() == SceneKind.Template
            ? _controller.CreateTemplate(
                sceneId,
                widthCells,
                heightCells,
                checked((int)_templateGroupEdit.Value),
                checked((int)_templateInsertionXEdit.Value),
                checked((int)_templateInsertionYEdit.Value),
                groundHeight)
            : _controller.CreateInstance(sceneId, widthCells, heightCells, groundHeight);
        ShowOpenedScene(report);
    }

    private void LoadScene(string filePath)
    {
        ShowOpenedScene(_controller.OpenScene(ResolveFileSystemPath(filePath)));
    }

    /// <summary>Brings the interface in line with a Scene that was just opened.</summary>
    private void ShowOpenedScene(EditorReport report)
    {
        if (!report.Succeeded)
        {
            ShowError(report.Message);
            return;
        }
        ClearTemplatePreview();
        ShowAuthoringElevation();
        _canvas.ShowScene(_controller.Scene);
        SaveRecentSession();
        UpdateDocumentStatus();
        SetStatus(report.Message);
    }

    private void SelectPerspective(EditorMode mode, string perspective)
    {
        var outcome = _canvas.SelectMode(mode);
        _canvas.MapContextActive = false;
        _overviewNavigationBar.Visible = false;
        _contextNavigationBar.Visible = true;
        ShowLandscapeAreas(mode);
        ShowStructuresAreas(mode);
        _terrainAssetBar.Visible =
            EditorToolRegistry.TerrainAuthoringFor(mode) == TerrainAuthoring.Cells;
        _propAssetBar.Visible = mode == EditorMode.Props;
        _templateBar.Visible = mode == EditorMode.Templates;
        _mapBar.Visible = false;
        ShowTerrainAssetsForArea();
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        UpdateTemplateControls();
        SetStatus(WithDiscardedDraft(
            MissingTerrainAssetNotice() ?? $"Selected {perspective} perspective.", outcome));
    }

    /// <summary>
    /// Shows the Landscape areas while one of them is open, and marks the one
    /// that is. Entering a Landscape area is also what it remembers.
    /// </summary>
    private void ShowLandscapeAreas(EditorMode mode)
    {
        var inLandscape = _landscapeAreaControls.ContainsKey(mode);
        if (inLandscape) _landscapeArea = mode;
        _landscapeBar.Visible = inLandscape;
        _canvas.LandscapeContextActive = inLandscape;
        foreach (var (area, control) in _landscapeAreaControls)
            control.ButtonPressed = inLandscape && area == mode;
    }

    /// <summary>The same for Structures.</summary>
    private void ShowStructuresAreas(EditorMode mode)
    {
        var inStructures = _structuresAreaControls.ContainsKey(mode);
        if (inStructures) _structuresArea = mode;
        _structuresBar.Visible = inStructures;
        foreach (var (area, control) in _structuresAreaControls)
            control.ButtonPressed = inStructures && area == mode;
    }

    /// <summary>
    /// Why the active area cannot draw, when it cannot. A Workspace that enables
    /// no Asset of the kind an area authors leaves that area reachable and its
    /// tool disabled, and this is what the status line says instead of leaving
    /// the author with a button that does nothing.
    /// </summary>
    private string? MissingTerrainAssetNotice()
    {
        if (_controller.Session is not { } session) return null;
        var mode = _interaction.Mode;
        if (TerrainAreaAssets.Offered(mode, session.TerrainAssets).Count > 0) return null;
        var field = TerrainAreaAssets.SurfaceFieldFor(
            mode, session.TerrainAssets, _canvas.SelectedTerrainAssetKey);
        if (field is not null)
        {
            return $"{EditorToolRegistry.ModeDisplayName(mode)} needs a Terrain Asset Surface, "
                + "and this Workspace enables none. Drawing stays disabled here.";
        }
        if (EditorToolRegistry.TerrainAuthoringFor(mode) is not { } authoring) return null;
        var kind = authoring == TerrainAuthoring.Curve ? "curve" : "cells";
        return $"{EditorToolRegistry.ModeDisplayName(mode)} needs a Terrain Asset authored as "
            + $"'{kind}', and this Workspace enables none. Drawing stays disabled here.";
    }

    private void SelectMapContext()
    {
        _canvas.MapContextActive = true;
        _canvas.LandscapeContextActive = false;
        _overviewNavigationBar.Visible = false;
        _contextNavigationBar.Visible = true;
        _landscapeBar.Visible = false;
        _structuresBar.Visible = false;
        _terrainAssetBar.Visible = false;
        _propAssetBar.Visible = false;
        _templateBar.Visible = false;
        _mapBar.Visible = true;
        UpdateMapControls();
        SetStatus("Map bounds.");
    }

    private void ShowNavigationOverview()
    {
        _canvas.MapContextActive = false;
        _canvas.LandscapeContextActive = false;
        _overviewNavigationBar.Visible = true;
        _contextNavigationBar.Visible = false;
        UpdateDrawingToolAvailability();
        UpdateTemplateControls();
        SetStatus("Navigation overview.");
    }

    /// <summary>
    /// The Map Resize dropdown's operations. North and East only push or pull
    /// the far edge; South and West move the origin, so <see cref="ResizeMap"/>
    /// passes the open Workspace's metrics to every operation, since
    /// <see cref="MapEditing"/> now validates every edge against it.
    /// </summary>
    private enum MapResizeOperation
    {
        ExtendNorth,
        ExtendEast,
        ExtendSouth,
        ExtendWest,
        ShrinkNorth,
        ShrinkEast,
        ShrinkSouth,
        ShrinkWest,
    }

    private void ResizeMap(MapResizeOperation operation)
    {
        if (_controller.Scene is null || _controller.Session is null) return;
        var cells = checked((int)_mapExtensionCellsEdit.Value);
        var metrics = _controller.Session.Metrics;
        var isShrink = operation is MapResizeOperation.ShrinkNorth or MapResizeOperation.ShrinkEast
            or MapResizeOperation.ShrinkSouth or MapResizeOperation.ShrinkWest;
        var (verb, direction) = operation switch
        {
            MapResizeOperation.ExtendNorth => ("Extended", "north"),
            MapResizeOperation.ExtendEast => ("Extended", "east"),
            MapResizeOperation.ExtendSouth => ("Extended", "south"),
            MapResizeOperation.ExtendWest => ("Extended", "west"),
            MapResizeOperation.ShrinkNorth => ("Shrank", "north"),
            MapResizeOperation.ShrinkEast => ("Shrank", "east"),
            MapResizeOperation.ShrinkSouth => ("Shrank", "south"),
            MapResizeOperation.ShrinkWest => ("Shrank", "west"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        ExecuteSceneCommand(new ToolOutcome.Edit(
            "Resize Map",
            document => operation switch
            {
                MapResizeOperation.ExtendNorth => MapEditing.ExtendNorth(document, cells, metrics),
                MapResizeOperation.ExtendEast => MapEditing.ExtendEast(document, cells, metrics),
                MapResizeOperation.ExtendSouth => MapEditing.ExtendSouth(document, cells, metrics),
                MapResizeOperation.ExtendWest => MapEditing.ExtendWest(document, cells, metrics),
                MapResizeOperation.ShrinkNorth => MapEditing.ShrinkNorth(document, cells, metrics),
                MapResizeOperation.ShrinkEast => MapEditing.ShrinkEast(document, cells, metrics),
                MapResizeOperation.ShrinkSouth => MapEditing.ShrinkSouth(document, cells, metrics),
                MapResizeOperation.ShrinkWest => MapEditing.ShrinkWest(document, cells, metrics),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            },
            Describe: (before, after) =>
            {
                var message = $"{verb} Map {direction} by {cells} Cells without moving other authored data.";
                if (!isShrink) return message;
                // Terrain and Placements the removed strip carried are gone
                // rather than refused - the only two authored kinds a Shrink
                // ever takes back this way (see MapEditing).
                var droppedTerrain = before.TerrainCells.Count - after.TerrainCells.Count;
                var droppedProps = before.Props.Count - after.Props.Count;
                return droppedTerrain == 0 && droppedProps == 0
                    ? message
                    : message + $" Dropped {droppedTerrain} Terrain Cell(s) and {droppedProps} Placement(s) that no longer fit.";
            }));
        UpdateDocumentStatus();
    }

    private void SelectDrawingTool(EditorTool tool)
    {
        var outcome = _canvas.SelectTool(tool);
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        SetStatus(WithDiscardedDraft(
            $"Selected {EditorToolRegistry.Resolve(tool).DisplayName}.", outcome));
    }

    private void SetEraserEnabled(bool enabled)
    {
        var outcome = _canvas.SetEraserEnabled(enabled);
        SetStatus(WithDiscardedDraft(enabled ? "Eraser enabled." : "Eraser disabled.", outcome));
    }

    /// <summary>
    /// Appends what the tools gave up, if anything. `ToolInteraction` decides
    /// whether a draft is lost and says so; the application only shows it.
    /// </summary>
    private static string WithDiscardedDraft(string status, ToolOutcome outcome) =>
        outcome is ToolOutcome.Message discarded ? $"{status} {discarded.Text}" : status;

    private void SetHeatmapEnabled(bool enabled)
    {
        if (!enabled && _canvas.PresentationMode != CanvasPresentationMode.Heightmap) return;
        SelectCanvasPresentation(enabled
            ? CanvasPresentationMode.Heightmap
            : CanvasPresentationMode.Normal);
        SetStatus(enabled
            ? "Height view on. Drawing and placing work as usual."
            : "Height view off.");
    }

    private void SetSectionEnabled(bool enabled)
    {
        if (!enabled && _canvas.PresentationMode != CanvasPresentationMode.Section) return;
        SelectCanvasPresentation(enabled
            ? CanvasPresentationMode.Section
            : CanvasPresentationMode.Normal);
        SetStatus(enabled
            ? SectionStatus()
            : "Section view off.");
    }

    private void SelectCanvasPresentation(CanvasPresentationMode mode)
    {
        _canvas.PresentationMode = mode;
        _heatmapToggle.SetPressedNoSignal(mode == CanvasPresentationMode.Heightmap);
        _sectionToggle.SetPressedNoSignal(mode == CanvasPresentationMode.Section);
        UpdateWaterHeatmapAvailability();
        UpdateToolContextLabel();
    }

    private void SetSectionElevation(double value)
    {
        var elevation = ElevationOf(_sectionElevationEdit, value);
        _canvas.SectionElevationMeters = elevation;
        SetStatus(SectionStatus());
    }

    private void SetSectionCut(long itemIndex)
    {
        var kind = _sectionCutEdit.GetItemId((int)itemIndex) switch
        {
            SectionCutAtItem => SectionCutKind.At,
            SectionCutBetweenItem => SectionCutKind.Between,
            _ => throw new InvalidOperationException("Unknown Section cut kind."),
        };
        _canvas.SectionCutKind = kind;
        UpdateToolContextLabel();
        SetStatus(SectionStatus());
    }

    private void SetSectionOffset(double value)
    {
        _canvas.SectionOffsetMeters = SectionOffsetOf(value);
        SetStatus(SectionStatus());
    }

    private string SectionStatus() => _canvas.SectionCutKind switch
    {
        SectionCutKind.At => $"Section view: cut at {_canvas.SectionElevationMeters:0.###} m.",
        SectionCutKind.Between =>
            $"Section view: {_canvas.SectionElevationMeters:0.###}–"
            + $"{_canvas.SectionElevationMeters + _canvas.SectionOffsetMeters:0.###} m "
            + $"(offset {_canvas.SectionOffsetMeters:0.###} m).",
        _ => throw new InvalidOperationException("Unknown Section cut kind."),
    };

    /// <summary>Marks one radio item and clears the rest.</summary>
    private void CheckWaterHeatmapItem(WaterHeatmapValue value)
    {
        var menu = _waterHeatmapValueEdit.GetPopup();
        for (var index = 0; index < menu.ItemCount; index++)
            menu.SetItemChecked(index, menu.GetItemId(index) == (int)value);
    }

    private void UpdateWaterHeatmapAvailability() =>
        _waterHeatmapValueEdit.Disabled =
            _canvas.PresentationMode != CanvasPresentationMode.Heightmap
            || _controller.Document?.WaterBodies.Count is not > 0;

    private void SetWaterHeatmapValue(long item)
    {
        var value = (WaterHeatmapValue)item;
        _canvas.WaterHeatmapValue = value;
        _waterHeatmapValueEdit.Text = value switch
        {
            WaterHeatmapValue.Surface => "Surface",
            WaterHeatmapValue.Bed => "Bed",
            WaterHeatmapValue.CutTop => "Cut top",
            _ => throw new InvalidOperationException("Unknown water heatmap value."),
        };
        CheckWaterHeatmapItem(value);
        SetStatus(value switch
        {
            WaterHeatmapValue.Surface => "Heatmap: water cells show their surface.",
            WaterHeatmapValue.Bed => "Heatmap: water cells show the river bed.",
            WaterHeatmapValue.CutTop => "Heatmap: water cells show the top of the Terrain cut.",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
    }

    private void SetPropLineOffset(double value)
    {
        var offset = checked((int)value);
        _interaction.State.SetPropLineOffset(offset);
        SetStatus($"Placement Line offset set to {offset} authoring px.");
    }

    /// <summary>
    /// Widths step by whole water cells, because a corridor is measured in
    /// them. Which widths a world actually uses is that world's business, so
    /// the editor offers a range rather than a list.
    /// </summary>
    private void ConfigureRiverWidthInput(SpinBox input)
    {
        var waterCell = _controller.Session is { } session
            ? (double)session.Metrics.WaterCellMeters
            : 0.5;
        input.MinValue = waterCell;
        input.MaxValue = 1024.0;
        input.Step = waterCell;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)EditorInteractionState.DefaultRiverWidthMeters;
    }

    private static void ConfigureBridgeWidthInput(SpinBox input)
    {
        input.MinValue = 0.125;
        input.MaxValue = 1024.0;
        input.Step = 0.125;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)BridgeEditing.DefaultWidthMeters;
    }

    private static void ConfigureBridgePlankCountInput(SpinBox input)
    {
        input.MinValue = 1.0;
        input.MaxValue = 512.0;
        input.Step = 1.0;
        input.Rounded = true;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.CustomMinimumSize = new Vector2(90f, 0f);
        input.Value = BridgeEditing.DefaultPlankCount;
    }

    private static void ConfigureBridgePlankGapInput(SpinBox input)
    {
        // Zero is a real answer - a deck with no gaps is a closed deck - so the
        // floor is zero rather than the smallest step.
        input.MinValue = 0.0;
        input.MaxValue = 16.0;
        input.Step = 0.01;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)BridgeEditing.DefaultPlankGapMeters;
    }

    private void SetBridgeWidth(double value)
    {
        _interaction.State.SetBridgeWidth(DecimalOf(value));
        ApplyBridgeNumbers();
    }

    /// <summary>
    /// The bridge numbers are defaults for the next bridge while nothing is
    /// selected, and edits of that one while something is. Nothing distinguishes
    /// the two cases in the bar itself: the same field means the same thing,
    /// and what it acts on is whatever is in front of the author.
    /// </summary>
    private void ApplyBridgeNumbers()
    {
        if (_loadingBridgeNumbers) return;
        HandleToolOutcome(_canvas.ReshapeSelectedBridge());
    }

    /// <summary>
    /// Fills the bridge fields from the selected bridge. Without this the first
    /// turn of any field would write the defaults over the bridge the author
    /// had just picked - the fields have to be showing it before they can edit
    /// it.
    /// </summary>
    private void ShowSelectedBridgeNumbers()
    {
        if (_canvas.SelectedBridge is not { } bridge) return;
        _loadingBridgeNumbers = true;
        try
        {
            _bridgeWidthEdit.Value = (double)bridge.WidthMeters;
            _bridgeElevationEdit.Value = (double)bridge.ElevationMeters;
            _bridgePlankCountEdit.Value = bridge.PlankCount;
            _bridgePlankGapEdit.Value = (double)bridge.PlankGapMeters;
        }
        finally
        {
            _loadingBridgeNumbers = false;
        }
        _interaction.State.SetBridgeWidth(bridge.WidthMeters);
        _interaction.State.SetBridgeElevation(bridge.ElevationMeters);
        _interaction.State.SetBridgePlankCount(bridge.PlankCount);
        _interaction.State.SetBridgePlankGap(bridge.PlankGapMeters);
    }

    private void SetBridgePlankCount(double value)
    {
        _interaction.State.SetBridgePlankCount(
            (int)Math.Round(value, MidpointRounding.AwayFromZero));
        ApplyBridgeNumbers();
    }

    private void SetBridgePlankGap(double value)
    {
        _interaction.State.SetBridgePlankGap(DecimalOf(value));
        ApplyBridgeNumbers();
    }

    /// <summary>
    /// A directly authored height, so it snaps to the Workspace quantum the
    /// moment it is typed rather than being refused when the bridge is placed.
    /// </summary>
    private void SetBridgeElevation(double value)
    {
        _interaction.State.SetBridgeElevation(ElevationOf(_bridgeElevationEdit, value));
        ApplyBridgeNumbers();
    }

    private static void ConfigurePathWidthInput(SpinBox input)
    {
        input.MinValue = 0.125;
        input.MaxValue = 1024.0;
        input.Step = 0.125;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)RouteSurfaceEditing.DefaultWidthMeters;
    }

    private void ConfigurePathClearanceInput(SpinBox input)
    {
        var quantum = _controller.Session is { } session
            ? session.Metrics.ElevationQuantumMeters
            : 0.125m;
        input.MinValue = (double)quantum;
        input.MaxValue = 1024.0;
        input.Step = ElevationDisplayStep(quantum);
        input.CustomArrowStep = (double)quantum;
        input.CustomArrowRound = true;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(110f, 0f);
        input.Value = (double)RouteSurfaceEditing.DefaultClearanceAboveMeters;
    }

    /// <summary>
    /// The Inspector's Point: it always changes the point that is selected, and
    /// is only on offer while one is.
    /// </summary>
    private void SetSelectedPointMode(long item)
    {
        var aligned = _selectedPointModeEdit.GetItemId((int)item) == CurvePointModeAligned;
        HandleToolOutcome(_canvas.SetSelectedElevationRegionPointMode(
            aligned ? ElevationRegionPointMode.Aligned : ElevationRegionPointMode.Linear));
    }

    /// <summary>
    /// The bar's Point: it always decides what the next drawn point does, and
    /// never reaches a point that has already been placed.
    /// </summary>
    private void SetCurvePointMode(long item)
    {
        var aligned = _curvePointModeEdit.GetItemId((int)item) == CurvePointModeAligned;
        if (_interaction.ActiveTool == EditorTool.DrawElevationRegion)
        {
            _interaction.State.SetElevationRegionPointMode(
                aligned ? ElevationRegionPointMode.Aligned : ElevationRegionPointMode.Linear);
            SetStatus(aligned
                ? "Hill: drag the next point to pull its handle, or click for an automatic one."
                : "Hill: the next contour point makes straight edges.");
            return;
        }

        if (_interaction.ActiveTool == EditorTool.DrawPath)
        {
            _interaction.State.SetRoutePointMode(
                aligned ? RoutePointMode.Aligned : RoutePointMode.Linear);
            SetStatus(aligned
                ? "Path: drag the next point to pull its handle, or click for an automatic one."
                : "Path: the next point makes its segments straight.");
            return;
        }

        _interaction.State.SetWaterPointMode(
            aligned ? WaterPointMode.Aligned : WaterPointMode.Linear);
        SetStatus(aligned
            ? "River: drag the next point to pull its handle, or click for an automatic one."
            : "River: the next point makes its segments straight.");
    }

    /// <summary>Shows the item with this ID, whatever index it happens to sit at.</summary>
    private void SelectCurvePointModeItem(int itemId)
    {
        for (var index = 0; index < _curvePointModeEdit.ItemCount; index++)
        {
            if (_curvePointModeEdit.GetItemId(index) != itemId) continue;
            _curvePointModeEdit.Selected = index;
            return;
        }
    }

    private void SetRiverWidth(double value)
    {
        var width = DecimalOf(value);
        _interaction.State.SetRiverWidth(width);
        if (ApplyRiverNumbers()) return;
        SetStatus($"River width set to {width:0.###} m.");
    }

    /// <summary>
    /// The water numbers are defaults for the next river while nothing is
    /// selected, and edits of the selected point while something is. Nothing in
    /// the bar distinguishes the two: the same field means the same thing, and
    /// what it acts on is whatever is in front of the author.
    /// </summary>
    private bool ApplyRiverNumbers()
    {
        if (_loadingRiverNumbers) return false;
        if (_interaction.ActiveTool != EditorTool.SelectRiver) return false;
        // A picked point, or the whole selected river. What is not allowed is
        // reporting that a number was set while the document keeps the old one.
        if (_canvas.SelectedWaterBody is null) return false;
        HandleToolOutcome(_canvas.ReshapeSelectedWater());
        return true;
    }

    /// <summary>
    /// Fills the water fields and the activation from the selection. Without
    /// this the first turn of any field would write the defaults over the point
    /// the author had just picked - the fields have to be showing it before
    /// they can edit it.
    /// </summary>
    private void ShowSelectedRiverNumbers()
    {
        _loadingRiverNumbers = true;
        try
        {
            RebuildRiverSwitchChoices();
        }
        finally
        {
            _loadingRiverNumbers = false;
        }
        if (_canvas.SelectedWaterBody is not { } body) return;
        _loadingRiverNumbers = true;
        try
        {
            // Without a picked point the fields show the source and act on the
            // whole river, so what is displayed is what typing would write.
            var shown = _canvas.SelectedWaterPointIndex is { } index && index < body.Points.Count
                ? index
                : 0;
            {
                var point = body.Points[shown];
                _riverWidthEdit.Value = (double)point.WidthMeters;
                _waterElevationEdit.Value = (double)point.ElevationMeters;
                _waterDepthEdit.Value = (double)point.ChannelDepthMeters;
                _waterClearanceEdit.Value = (double)point.ClearanceAboveMeters;
                _interaction.State.SetRiverWidth(point.WidthMeters);
                _interaction.State.SetWaterElevation(point.ElevationMeters);
                _interaction.State.SetWaterChannelDepth(point.ChannelDepthMeters);
                _interaction.State.SetWaterClearanceAbove(point.ClearanceAboveMeters);
            }
            SelectRiverSwitchItem(body.Switch);
            _riverInitiallyOnToggle.SetPressedNoSignal(InitiallyOn(body.Switch));
        }
        finally
        {
            _loadingRiverNumbers = false;
        }
        UpdateWaterDerivedSpan();
    }

    /// <summary>
    /// The switches the Scene declares, with "Every state" first for a body
    /// that hangs on none of them. Their positions are a second question, asked
    /// by the boxes below.
    /// </summary>
    private void RebuildRiverSwitchChoices()
    {
        _riverSwitchChoices.Clear();
        _riverSwitchEdit.Clear();
        _riverSwitchChoices.Add(null);
        _riverSwitchEdit.AddItem("Every state", 0);
        var document = _controller.Document;
        if (document is null) return;
        foreach (var declared in document.Switches)
        {
            _riverSwitchEdit.AddItem(declared.Switch, _riverSwitchChoices.Count);
            _riverSwitchChoices.Add(declared.Switch);
        }
    }

    private void SelectRiverSwitchItem(string? name)
    {
        var wanted = 0;
        if (name is not null)
        {
            for (var index = 1; index < _riverSwitchChoices.Count; index++)
            {
                if (!string.Equals(_riverSwitchChoices[index], name, StringComparison.Ordinal))
                    continue;
                wanted = index;
                break;
            }
        }

        SelectOptionItem(_riverSwitchEdit, wanted);
    }

    private bool InitiallyOn(string? name) =>
        name is not null
        && _controller.Document?.Switches.FirstOrDefault(candidate =>
            string.Equals(candidate.Switch, name, StringComparison.Ordinal))
            is { InitiallyOn: true };

    private static void SelectOptionItem(OptionButton button, int itemId)
    {
        for (var index = 0; index < button.ItemCount; index++)
        {
            if (button.GetItemId(index) != itemId) continue;
            button.Selected = index;
            return;
        }
    }

    private void SetRiverSwitch(long item)
    {
        if (_loadingRiverNumbers) return;
        var id = _riverSwitchEdit.GetItemId((int)item);
        if (id < 0 || id >= _riverSwitchChoices.Count) return;
        HandleToolOutcome(_canvas.SetSelectedWaterSwitch(_riverSwitchChoices[id]));
    }

    private void SetRiverInitiallyOn(bool initiallyOn)
    {
        if (_loadingRiverNumbers) return;
        HandleToolOutcome(_canvas.SetSelectedWaterSwitchInitiallyOn(initiallyOn));
    }

    private void DeclareRiverSwitch(string text)
    {
        var outcome = _canvas.MakeSelectedWaterSwitchable(text);
        if (outcome is ToolOutcome.Edit) _riverNewSwitchEdit.Clear();
        HandleToolOutcome(outcome);
    }

    private void RemoveRiverSwitch() =>
        HandleToolOutcome(_canvas.RemoveSelectedWaterSwitch());

    private void SetPathWidth(double value)
    {
        var width = DecimalOf(value);
        _interaction.State.SetPathWidth(width);
        SetStatus($"Path width for the next point set to {width:0.###} m.");
    }

    private void SetPathGrade(long item)
    {
        var grade = _pathGradeEdit.GetItemId((int)item) switch
        {
            PathGradeDownFifty => RouteGradePreset.DownFiftyPercent,
            PathGradeDownTwentyFive => RouteGradePreset.DownTwentyFivePercent,
            PathGradeLevel => RouteGradePreset.Level,
            PathGradeUpTwentyFive => RouteGradePreset.UpTwentyFivePercent,
            PathGradeUpFifty => RouteGradePreset.UpFiftyPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        _interaction.State.SetPathGrade(grade);
        SetStatus($"Path grade for the next segment set to {PathGradeText(grade)}.");
    }

    private void SetPathOperation(long item)
    {
        var operation = _pathOperationEdit.GetItemId((int)item) switch
        {
            PathOperationAdditive => RouteSegmentOperation.Additive,
            PathOperationSubtractive => RouteSegmentOperation.Subtractive,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        _interaction.State.SetPathOperation(operation);
        UpdateToolContextLabel();
        SetStatus(operation == RouteSegmentOperation.Additive
            ? "The next Path segment is additive."
            : $"The next Path segment is subtractive with {_interaction.State.PathClearanceAboveMeters:0.###} m clearance.");
    }

    private void SetPathClearance(double value)
    {
        var clearance = DecimalOf(value);
        _interaction.State.SetPathClearanceAbove(clearance);
        SetStatus($"Path clearance for the next subtractive segment set to {clearance:0.###} m.");
    }

    private void SetPathAutoStart(bool enabled)
    {
        _pathStartElevationEdit.Editable = !enabled && _controller.Session is not null;
        _interaction.State.SetPathStartElevationOverride(enabled
            ? null
            : ElevationOf(_pathStartElevationEdit, _pathStartElevationEdit.Value));
        SetStatus(enabled
            ? "Path start follows the effective Terrain under the first point."
            : $"Path start fixed at {_interaction.State.PathStartElevationOverrideMeters:0.###} m.");
    }

    private void SetPathStartElevation(double value)
    {
        var elevation = ElevationOf(_pathStartElevationEdit, value);
        if (!_pathAutoStartToggle.ButtonPressed)
            _interaction.State.SetPathStartElevationOverride(elevation);
        SetStatus($"Path start elevation set to {elevation:0.###} m.");
    }

    private static string PathGradeText(RouteGradePreset grade) => grade switch
    {
        RouteGradePreset.DownFiftyPercent => "-50%",
        RouteGradePreset.DownTwentyFivePercent => "-25%",
        RouteGradePreset.Level => "0%",
        RouteGradePreset.UpTwentyFivePercent => "+25%",
        RouteGradePreset.UpFiftyPercent => "+50%",
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    private void SetSnapWaterToTerrain(bool enabled)
    {
        _interaction.State.SetSnapWaterToTerrain(enabled);
        UpdateWaterDerivedSpan();
        SetStatus(enabled
            ? "Snap on: a point takes the Terrain height; Water is the no-Terrain fallback."
            : "Snap off: a placed point takes the water level from the context bar.");
    }

    private void SetWaterElevation(double value)
    {
        var elevation = ElevationOf(_waterElevationEdit, value);
        _interaction.State.SetWaterElevation(elevation);
        UpdateWaterDerivedSpan();
        if (ApplyRiverNumbers()) return;
        SetStatus($"Water level set to {elevation:0.###} m.");
    }

    private void SetWaterDepth(double value)
    {
        var depth = DecimalOf(value);
        _interaction.State.SetWaterChannelDepth(depth);
        UpdateWaterDerivedSpan();
        if (ApplyRiverNumbers()) return;
        SetStatus($"Channel depth set to {depth:0.###} m; the bed sits that far below the surface.");
    }

    private void SetWaterClearance(double value)
    {
        var clearance = DecimalOf(value);
        _interaction.State.SetWaterClearanceAbove(clearance);
        UpdateWaterDerivedSpan();
        if (ApplyRiverNumbers()) return;
        SetStatus($"Clearance set to {clearance:0.###} m of headroom above the water.");
    }

    private void UpdateWaterDerivedSpan()
    {
        var water = _interaction.State.WaterElevationMeters;
        var bed = water - _interaction.State.WaterChannelDepthMeters;
        var cutTop = water + _interaction.State.WaterClearanceAboveMeters;
        _waterDerivedSpanLabel.Text = _interaction.State.SnapWaterToTerrain
            ? $"Fallback bed {bed:0.###} · cut {cutTop:0.###} m"
            : $"Bed {bed:0.###} · Cut {cutTop:0.###} m";
    }

    private void UpdateToolContextLabel()
    {
        _toolContextLabel.Text =
            $"{EditorToolRegistry.ModeDisplayName(_interaction.Mode)}:"
            + EditorToolRegistry.Resolve(_interaction.ActiveTool).DisplayName;
        var propLineActive = _interaction.Mode == EditorMode.Props
            && _interaction.ActiveTool == EditorTool.Line;
        var riverActive = _interaction.Mode == EditorMode.River;
        var pathActive = _interaction.Mode == EditorMode.Path;
        var elevationRegionActive = _interaction.Mode == EditorMode.ElevationRegion;
        var bridgeActive = _interaction.Mode == EditorMode.Bridge;
        var selectedProp = _interaction.Mode == EditorMode.Props
            && _canvas.SelectedPropInstanceId is { } selectedPropInstanceId
            ? _controller.Document?.Props.FirstOrDefault(
                prop => prop.InstanceId == selectedPropInstanceId)
            : null;
        var elevationRegionDrawing = elevationRegionActive
            && _interaction.ActiveTool == EditorTool.DrawElevationRegion;
        var selectedElevationRegion = elevationRegionActive
            && _interaction.ActiveTool == EditorTool.SelectElevationRegion
            && _canvas.SelectedElevationRegionId is { } selectedBodyId
            ? _controller.Document?.ElevationRegions
                .FirstOrDefault(body => body.ElevationRegionId == selectedBodyId)
            : null;
        var selectedElevationRegionPoint = selectedElevationRegion is not null
            && _canvas.SelectedElevationRegionPointIndex is { } selectedPointIndex
            ? selectedElevationRegion.Points.ElementAtOrDefault(selectedPointIndex)
            : null;
        var elevationRegionPointEditing = selectedElevationRegionPoint is not null;
        var riverSelecting = riverActive && _interaction.ActiveTool == EditorTool.SelectRiver;
        var selectedRiverBody = riverSelecting ? _canvas.SelectedWaterBody : null;
        // The point mode decides what the NEXT drawn point does, which is
        // nothing while a body is being selected. Handles of an existing river
        // point are not editable yet, so the field is hidden rather than left
        // sitting there meaning something it cannot do.
        var curveActive = (riverActive && !riverSelecting) || pathActive
            || elevationRegionDrawing;
        // Only the two strokes a brush actually widens. Fill spreads over a
        // connected region regardless of brush width, and the Eraser under
        // Fill is the same region eraser, so neither shows this field.
        var terrainBrushActive = _interaction.Mode == EditorMode.Terrain
            && _interaction.ActiveTool is EditorTool.Pencil or EditorTool.Line;
        var sectionActive = _canvas.PresentationMode == CanvasPresentationMode.Section;
        var heatmapActive = _canvas.PresentationMode == CanvasPresentationMode.Heightmap;
        _toolContextSeparator.Visible = propLineActive || curveActive || terrainBrushActive;
        _terrainBrushWidthLabel.Visible = terrainBrushActive;
        _terrainBrushWidthEdit.Visible = terrainBrushActive;
        _viewOptionsBar.Visible = sectionActive || heatmapActive;
        var sectionBetween = _canvas.SectionCutKind == SectionCutKind.Between;
        _sectionCutEdit.Visible = sectionActive;
        _sectionCutEdit.Selected = _sectionCutEdit.GetItemIndex(
            sectionBetween ? SectionCutBetweenItem : SectionCutAtItem);
        _sectionElevationLabel.Visible = sectionActive && sectionBetween;
        _sectionElevationEdit.Visible = sectionActive;
        _sectionOffsetLabel.Visible = sectionActive && sectionBetween;
        _sectionOffsetEdit.Visible = sectionActive && sectionBetween;
        _waterHeatmapValueLabel.Visible = heatmapActive;
        _waterHeatmapValueEdit.Visible = heatmapActive;
        _propLineOffsetLabel.Visible = propLineActive;
        _propLineOffsetEdit.Visible = propLineActive;
        _curvePointModeLabel.Visible = curveActive;
        _curvePointModeEdit.Visible = curveActive;
        // The material of a curve body or independent route is one of its
        // properties, so it sits beside width and heights rather than in a bar.
        var surfaceActive = _controller.Session is { } session
            && TerrainAreaAssets.SurfaceFieldFor(
                _interaction.Mode,
                session.TerrainAssets,
                _canvas.SelectedTerrainAssetKey) is not null;
        _surfaceLabel.Visible = surfaceActive;
        _surfaceEdit.Visible = surfaceActive;
        // River, Path and Hill keep their own point modes; the shared control
        // only shows whichever one the active tool authors with.
        var aligned = elevationRegionDrawing
            ? _interaction.State.ElevationRegionPointMode == ElevationRegionPointMode.Aligned
            : pathActive
                ? _interaction.State.RoutePointMode == RoutePointMode.Aligned
                : _interaction.State.WaterPointMode == WaterPointMode.Aligned;
        SelectCurvePointModeItem(aligned ? CurvePointModeAligned : CurvePointModeLinear);
        _curvePointModeEdit.TooltipText =
            "How the next curve point's handles behave. Switchable while drawing; "
            + "it decides what the next point does and leaves the placed ones alone.";
        _selectedPointModeLabel.Visible = elevationRegionPointEditing;
        _selectedPointModeEdit.Visible = elevationRegionPointEditing;
        if (selectedElevationRegionPoint is { } authoredPoint)
        {
            _selectedPointModeEdit.Selected = _selectedPointModeEdit.GetItemIndex(
                authoredPoint.Mode == ElevationRegionPointMode.Aligned
                    ? CurvePointModeAligned
                    : CurvePointModeLinear);
        }
        _riverWidthLabel.Visible = riverActive;
        _riverWidthEdit.Visible = riverActive;
        _riverSwitchLabel.Visible = selectedRiverBody is not null;
        _riverSwitchEdit.Visible = selectedRiverBody is not null;
        _riverInitiallyOnToggle.Visible = selectedRiverBody?.Switch is not null;
        // One of the two at a time: name a group while the river is in none,
        // take it away while it is in one.
        _riverNewSwitchLabel.Visible = selectedRiverBody is { Switch: null };
        _riverNewSwitchEdit.Visible = selectedRiverBody is { Switch: null };
        _riverRemoveSwitch.Visible = selectedRiverBody?.Switch is not null;
        _pathWidthLabel.Visible = pathActive;
        _pathWidthEdit.Visible = pathActive;
        _bridgePlankCountLabel.Visible = bridgeActive;
        _bridgePlankCountEdit.Visible = bridgeActive;
        _bridgePlankGapLabel.Visible = bridgeActive;
        _bridgePlankGapEdit.Visible = bridgeActive;
        _bridgeWidthLabel.Visible = bridgeActive;
        _bridgeWidthEdit.Visible = bridgeActive;
        _bridgeElevationLabel.Visible = bridgeActive;
        _bridgeElevationEdit.Visible = bridgeActive;
        _pathGradeLabel.Visible = pathActive;
        _pathGradeEdit.Visible = pathActive;
        _pathGradeEdit.Selected = _pathGradeEdit.GetItemIndex(
            PathGradeItemId(_interaction.State.PathGrade));
        _pathOperationLabel.Visible = pathActive;
        _pathOperationEdit.Visible = pathActive;
        _pathOperationEdit.Selected = _pathOperationEdit.GetItemIndex(
            _interaction.State.PathOperation == RouteSegmentOperation.Subtractive
                ? PathOperationSubtractive
                : PathOperationAdditive);
        var subtractivePath = pathActive
            && _interaction.State.PathOperation == RouteSegmentOperation.Subtractive;
        _pathClearanceLabel.Visible = subtractivePath;
        _pathClearanceEdit.Visible = subtractivePath;
        _pathAutoStartToggle.Visible = pathActive;
        _pathStartElevationLabel.Visible = pathActive;
        _pathStartElevationEdit.Visible = pathActive;
        _pathStartElevationEdit.Editable = !_pathAutoStartToggle.ButtonPressed
            && _controller.Session is not null;
        _snapWaterToggle.Visible = riverActive;
        _waterElevationLabel.Visible = riverActive;
        _waterElevationEdit.Visible = riverActive;
        _waterDepthLabel.Visible = riverActive;
        _waterDepthEdit.Visible = riverActive;
        _waterClearanceLabel.Visible = riverActive;
        _waterClearanceEdit.Visible = riverActive;
        _waterDerivedSpanLabel.Visible = riverActive;
        // Height authors Terrain, Placements and Hills. River carries its own
        // profile, Path derives its points from Start plus Grade, and a bridge
        // has one height of its own beside its width, so leaving the general
        // field in any of the three would offer a number that changes nothing -
        // and in Structures it read as a second Height beside the real one.
        // Section only changes what is drawn, not what can be authored (see
        // SceneCanvas's DrawProps), so it stays out of this condition: Height
        // keeps working - independently of the Section bar's own Start field -
        // exactly as it did before Section had a bar of its own.
        var elevationRegionHeightEditing = selectedElevationRegion is not null;
        var elevationActive = !riverActive && !pathActive && !bridgeActive
            && (!elevationRegionActive || elevationRegionDrawing || elevationRegionHeightEditing);
        _elevationLabel.Visible = elevationActive;
        _elevationEdit.Visible = elevationActive;
        _elevationEdit.SetValueNoSignal((double)(elevationRegionHeightEditing
            ? selectedElevationRegion!.ElevationMeters
            : _canvas.ElevationMeters));
        _elevationEdit.TooltipText = elevationRegionHeightEditing
            ? "The selected Hill's absolute top elevation."
            : "The height the drawing tools author at.";
        _propInstanceIdLabel.Visible = selectedProp is not null;
        _propInstanceIdEdit.Visible = selectedProp is not null;
        _propInstanceIdEdit.Text = selectedProp?.InstanceId ?? string.Empty;
        RebuildOutliner();
        _inspectorHeaderLabel.Text = InspectorHeader(
            selectedRiverBody,
            riverSelecting,
            riverActive,
            selectedElevationRegion,
            elevationRegionActive,
            pathActive,
            bridgeActive);
        SyncInspectorRows();
    }

    private static int PathGradeItemId(RouteGradePreset grade) => grade switch
    {
        RouteGradePreset.DownFiftyPercent => PathGradeDownFifty,
        RouteGradePreset.DownTwentyFivePercent => PathGradeDownTwentyFive,
        RouteGradePreset.Level => PathGradeLevel,
        RouteGradePreset.UpTwentyFivePercent => PathGradeUpTwentyFive,
        RouteGradePreset.UpFiftyPercent => PathGradeUpFifty,
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    /// <summary>
    /// Choosing an Asset changes the material and nothing else. The area already
    /// decided which Assets are on offer, so no tool has to be swapped out from
    /// under the author - and an unfinished contour survives a change of mind
    /// about what it is made of.
    /// </summary>
    private void SelectTerrainAsset(string assetKey)
    {
        var asset = _controller.Session!.TerrainAssets.Resolve(assetKey);
        var mode = _interaction.Mode;
        _canvas.SelectedTerrainAssetKey = assetKey;
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        // One path, two ways of saying it: a palette hands the author a brush,
        // a Surface field names what the body being drawn is made of.
        SetStatus(TerrainAreaAssets.SurfaceFieldFor(
                mode,
                _controller.Session.TerrainAssets,
                assetKey) is not null
            ? $"Selected {EditorToolRegistry.ModeDisplayName(mode)} Surface '{asset.Name}' ({asset.AssetKey})."
            : $"Selected Terrain '{asset.Name}' ({asset.AssetKey}).");
    }

    private void SelectPropAsset(string assetKey)
    {
        var asset = _controller.Session!.PropAssets.Resolve(assetKey);
        _canvas.SelectedPropAssetKey = assetKey;
        SetStatus($"Selected Placement '{asset.Name}' · footprint {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels}).");
    }

    private void BeginTemplateAnchorPlacement()
    {
        if (_controller.Document?.SceneKind != SceneKind.Instance)
        {
            SetStatus("Place Anchor is available only while editing a Scene Instance.");
            return;
        }
        foreach (var control in _drawingToolControlsByTool.Values)
            control.ButtonPressed = false;
        _canvas.SelectTool(EditorTool.AnchorPlace);
        UpdateToolContextLabel();
        SetStatus(
            $"Place Anchor: click a WorldGrid intersection for group {(int)_anchorGroupEdit.Value}.");
    }

    private void GenerateTemplatePreview()
    {
        if (_controller.Document?.SceneKind != SceneKind.Instance) return;
        _autosaveTimer.Stop();
        var report = _controller.GenerateTemplatePreview();
        UpdateDocumentState();
        ShowTemplatePreview();
        if (report.Succeeded) SetStatus(report.Message);
        else ShowError(report.Message);
    }

    /// <summary>Shows the Template Preview the controller holds, if it holds one.</summary>
    private void ShowTemplatePreview()
    {
        if (_controller.TemplatePreview is { } preview)
        {
            _canvas.ShowTemplatePreview(preview.ComposedScene, preview.EffectiveTerrainMasks);
            return;
        }
        _canvas.ShowTemplatePreview(null);
    }

    private void ClearTemplatePreview()
    {
        _controller.ClearTemplatePreview();
        _canvas.ShowTemplatePreview(null);
    }

    /// <summary>
    /// Records one edit in memory and schedules the Workspace write. Continuous
    /// input passes a stroke key so that a whole drag collapses into a single
    /// undo step.
    /// </summary>
    /// <summary>
    /// The single entry point for everything the tools decide. Input reaches the
    /// editor as one outcome instead of one event per tool action.
    /// </summary>
    private void HandleToolOutcome(ToolOutcome outcome)
    {
        switch (outcome)
        {
            case ToolOutcome.Message message:
                SetStatus(message.Text);
                SyncSelectedAnchorGroup();
                UpdateTemplateControls();
                break;
            case ToolOutcome.Edit edit:
                ExecuteSceneCommand(edit);
                break;
        }
        if (_interaction.ActiveTool == EditorTool.SelectBridge) ShowSelectedBridgeNumbers();
        if (_interaction.ActiveTool == EditorTool.SelectRiver) ShowSelectedRiverNumbers();
        UpdateToolContextLabel();
    }

    /// <summary>
    /// Hands one edit to the controller and shows what it did. Every document
    /// change in the editor goes through here.
    /// </summary>
    private void ExecuteSceneCommand(ToolOutcome.Edit edit)
    {
        var result = _controller.Apply(edit);
        if (result.Changed)
        {
            ShowEditedScene();
            _canvas.NotifySceneChanged(result.Before!, result.After!);
            SyncSelectedAnchorGroup();
            UpdateTemplateControls();
        }
        Report(result.Report);
    }

    /// <summary>Shows the group of the Anchor the tools currently have selected.</summary>
    private void SyncSelectedAnchorGroup()
    {
        if (_controller.Scene is null || _canvas.SelectedTemplateAnchorId is not { } anchorId) return;
        var anchor = _controller.Document!.TemplateAnchors
            .FirstOrDefault(value => value.AnchorId == anchorId);
        if (anchor is null) return;
        _updatingAnchorGroupEdit = true;
        _anchorGroupEdit.Value = anchor.GroupNumber;
        _updatingAnchorGroupEdit = false;
    }

    private void OnAnchorGroupChanged(double value)
    {
        var groupNumber = checked((int)value);
        _canvas.TemplateAnchorGroupNumber = groupNumber;
        if (_updatingAnchorGroupEdit
            || _controller.Scene is null
            || _controller.Session is null
            || _canvas.SelectedTemplateAnchorId is not { } anchorId)
        {
            return;
        }
        ExecuteSceneCommand(new ToolOutcome.Edit(
            "Assign Anchor Group",
            document => TemplateEditing.SetAnchorGroup(document, anchorId, groupNumber),
            Describe: (_, _) =>
                $"Assigned Template Anchor '{anchorId}' to group {groupNumber}."));
    }

    /// <summary>Shows an edited Scene and schedules the write.</summary>
    private void ShowEditedScene()
    {
        if (_controller.Scene is not { } scene) return;
        ClearTemplatePreview();
        _canvas.UpdateScene(scene);
        UpdateWaterHeatmapAvailability();
        _autosaveTimer.Start();
        UpdateDocumentState();
    }

    private void EndEditStroke() => _controller.EndEditStroke();

    /// <summary>
    /// Writes the Scene out if it differs from the stored copy. Safe to call at
    /// any time; it is a no-op when there is nothing to write.
    /// </summary>
    private void PersistScene()
    {
        _autosaveTimer.Stop();
        var report = _controller.SaveScene();
        if (report.Succeeded) SaveRecentSession();
        Report(report);
        if (report.Succeeded) UpdateDocumentState();
    }

    /// <summary>
    /// Undo takes back what the author just did. While a tool is holding an
    /// unfinished draft - a river being drawn, a Prop line with a fixed start -
    /// that is the last point they placed, not an edit they finished minutes
    /// ago. Reaching past the draft into the history would undo the wrong thing
    /// and leave the half-drawn river standing.
    /// </summary>
    /// <summary>
    /// Writes the Scene and says what happened. A Scene with nothing to write
    /// is told apart from one that was written: reporting a save either way
    /// reads as confirmation that whatever the author just drew is on disk,
    /// which is exactly what it is not.
    /// </summary>
    private void SaveSceneNow()
    {
        _autosaveTimer.Stop();
        var hadChanges = _controller.IsDirty == true;
        var save = _controller.SaveScene();
        if (save.Succeeded) SaveRecentSession();
        UpdateDocumentState();
        if (!save.Succeeded)
        {
            SetStatus(save.Message);
            return;
        }
        if (_controller.Document is not { } scene)
        {
            SetStatus("No Scene to save.");
            return;
        }
        SetStatus(hadChanges
            ? $"Saved Scene '{scene.SceneId}'."
            : $"Scene '{scene.SceneId}' has no unsaved change.");
    }

    private void UndoEdit()
    {
        if (_interaction.UndoDraftStep() is { } stepped)
        {
            HandleToolOutcome(stepped);
            _canvas.QueueRedraw();
            return;
        }
        StepHistory(_controller.Undo());
    }

    /// <summary>
    /// The other half of the same rule: while a draft is open both keys belong
    /// to it, and there redo has nothing to give back.
    /// </summary>
    private void RedoEdit()
    {
        if (_interaction.HasUnfinishedDraft)
        {
            SetStatus(
                "Redo: nothing to restore while a draft is open. Enter finishes it, Escape drops it.");
            return;
        }
        StepHistory(_controller.Redo());
    }

    private void StepHistory(EditorReport report)
    {
        if (report.Succeeded && _controller.Scene is { } scene)
        {
            _canvas.SelectProp(null);
            _canvas.SelectTemplateAnchor(null);
            ClearTemplatePreview();
            _canvas.UpdateScene(scene);
            _autosaveTimer.Start();
            UpdateDocumentStatus();
            // The same three the tools run after an edit. An undo changes the
            // document as thoroughly as any edit does, and leaving the panels
            // showing the state before it is worse than not showing it: the
            // Outliner would keep a hierarchy the document no longer has.
            if (_interaction.ActiveTool == EditorTool.SelectBridge) ShowSelectedBridgeNumbers();
            if (_interaction.ActiveTool == EditorTool.SelectRiver) ShowSelectedRiverNumbers();
            UpdateToolContextLabel();
        }

        SetStatus(report.Message);
    }

    private void UpdateDocumentState()
    {
        _undoButton.Disabled = !_controller.CanUndo;
        _redoButton.Disabled = !_controller.CanRedo;
        _documentStateLabel.Text = _controller.IsDirty switch
        {
            null => string.Empty,
            true => "unsaved",
            false => "saved",
        };
    }

    /// <summary>Shows a report that may deliberately carry no message.</summary>
    private void Report(EditorReport report)
    {
        if (report.HasMessage) SetStatus(report.Message);
    }

    private static void AddDocumentHistoryButton(
        Container parent,
        Button button,
        string text,
        Action pressed,
        string tooltip)
    {
        button.Name = text;
        button.Text = text;
        button.TooltipText = tooltip;
        button.CustomMinimumSize = new Vector2(80f, 0f);
        button.Disabled = true;
        button.Pressed += pressed;
        parent.AddChild(button);
    }

    private void UpdateDocumentStatus()
    {
        _workspaceLabel.Text = _controller.Session is null
            ? "Workspace: none"
            : $"Workspace: {_controller.Session.WorkspaceKey}";
        _sceneLabel.Text = _controller.Scene is null
            ? "Scene: none"
            : $"Scene: {_controller.Document!.SceneId}  ·  {(_controller.Document!.SceneKind == SceneKind.Instance ? "Instance" : "Template")}  ·  {_controller.Document!.SizeCells.Width} × {_controller.Document!.SizeCells.Height} cells";

        var sceneActionsAvailable = _controller.Session is not null;
        SetElevationInputsEditable(sceneActionsAvailable);
        SetSettingsItemDisabled(SettingsMenuItem.WorkspaceAssets, !sceneActionsAvailable);
        SetSettingsItemDisabled(SettingsMenuItem.CreateScene, !sceneActionsAvailable);
        SetSettingsItemDisabled(SettingsMenuItem.LoadScene, !sceneActionsAvailable);
        SetSettingsItemDisabled(SettingsMenuItem.LoadTemplate, !sceneActionsAvailable);
        SetSettingsItemDisabled(SettingsMenuItem.ExportWorkspace, !sceneActionsAvailable);
        UpdateDrawingToolAvailability();
        UpdateWaterHeatmapAvailability();
        UpdateTemplateControls();
        UpdateMapControls();
        UpdateDocumentState();
        UpdateViewStatus();
        UpdatePointerStatus();
    }

    private void UpdateMapControls()
    {
        var scene = _controller.Document;
        var enabled = scene is not null;
        _mapNavigationButton.Disabled = !enabled;
        _mapExtensionCellsEdit.Editable = enabled;
        _mapResizeButton.Disabled = !enabled;
        if (scene is null)
        {
            _mapExtensionMetricsLabel.Text = string.Empty;
            return;
        }

        // A Scene can only be open while a session is open.
        var metrics = _controller.Session!.Metrics;
        var extensionCells = checked((int)_mapExtensionCellsEdit.Value);
        _mapExtensionMetricsLabel.Text =
            $"= {extensionCells * metrics.TerrainCellMeters:0.###} m · {extensionCells * metrics.AuthoringPixelsPerTerrainCell} px";
    }

    private void UpdateDrawingToolAvailability()
    {
        var templateMode = _interaction.Mode == EditorMode.Templates;
        var instanceActive = _controller.Document?.SceneKind == SceneKind.Instance;
        // An area whose Asset kind the Workspace does not enable keeps its tools
        // visible and dead rather than hiding them: the area exists, it just has
        // nothing to draw with, and the status line says so.
        var withoutAsset = (EditorToolRegistry.TerrainAuthoringFor(_interaction.Mode) is not null
                || _interaction.Mode == EditorMode.Path)
            && _canvas.SelectedTerrainAssetKey is null;
        foreach (var (tool, control) in _drawingToolControlsByTool)
        {
            control.Visible = EditorToolRegistry.Supports(_interaction.Mode, tool);
            // Select River picks a body that already exists and never asks what
            // it is made of, so the Asset guard would disable the one tool in
            // the area that does not need one.
            control.Disabled = _controller.Scene is null
                || withoutAsset && tool != EditorTool.SelectRiver
                || templateMode && !instanceActive;
            control.ButtonPressed = control.Visible && tool == _interaction.ActiveTool;
        }
        _anchorGroupEdit.Visible = templateMode && instanceActive;
    }

    private void UpdateTemplateControls()
    {
        var workspaceActive = _controller.Session is not null;
        var instanceActive = _controller.Document?.SceneKind == SceneKind.Instance;
        _placeTemplateAnchorButton.Disabled = !instanceActive;
        _templatesButton.Disabled = !workspaceActive;
        _anchorGroupEdit.Editable = instanceActive;
        _regeneratePreviewButton.Disabled = !instanceActive
            || _controller.Document!.TemplateAnchors.Count == 0;
    }

    /// <summary>
    /// Where the pointer is, in the two units an author works in: meters,
    /// which is what a consumer reads, and the Terrain cell, which is what a
    /// press actually paints.
    ///
    /// <para>Outside the Scene the numbers are still said rather than hidden -
    /// a negative meter is how far past the edge the pointer is, which is the
    /// question being asked when someone is out there at all.</para>
    /// </summary>
    private void UpdatePointerStatus()
    {
        if (_controller.Session is not { } session
            || _controller.Document is null
            || _interaction.PointerAuthoring is not { } pointer)
        {
            _pointerLabel.Text = "x -  y -";
            return;
        }

        var x = pointer.X * session.Metrics.MetersPerAuthoringPixel;
        var y = pointer.Y * session.Metrics.MetersPerAuthoringPixel;
        var place = _interaction.PointerCell is { } cell
            ? FormattableString.Invariant($"  ·  cell {cell.X}, {cell.Y}")
            : string.Empty;
        _pointerLabel.Text = FormattableString.Invariant($"x {x:0.00} m  y {y:0.00} m") + place;
    }

    private void UpdateViewStatus()
    {
        _viewLabel.Text = $"Zoom {_canvas.ViewState.Zoom:0.##}×  ·  Pan ({_canvas.ViewState.PanX:0}, {_canvas.ViewState.PanY:0})";
    }

    private void TryDocumentAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is SceneMakerDocumentException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or OverflowException)
        {
            ShowError(exception.Message);
        }
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void ShowError(string message)
    {
        SetStatus(message);
        _errorDialog.DialogText = message;
        _errorDialog.PopupCentered(new Vector2I(560, 180));
    }

    private static string ResolveFileSystemPath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal)
        || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    /// <summary>
    /// Brings the interface in line with the Workspace the controller has open.
    /// Called after any command that swapped the session.
    /// </summary>
    private void ShowSession()
    {
        if (_controller.Session is not { } session)
        {
            SetElevationInputsEditable(false);
            return;
        }
        _canvas.ConfigureMetrics(session.Metrics);
        _canvas.ConfigureTerrainAssets(session.TerrainAssets);
        _canvas.ConfigurePropAssets(session.PropAssets);
        _canvas.ConfigureBridgeKit(session.BridgeKit);
        ConfigureRiverWidthInput(_riverWidthEdit);
        _interaction.State.SetRiverWidth(DecimalOf(_riverWidthEdit.Value));
        ConfigurePathWidthInput(_pathWidthEdit);
        _interaction.State.SetPathWidth(DecimalOf(_pathWidthEdit.Value));
        ConfigurePathClearanceInput(_pathClearanceEdit);
        _interaction.State.SetPathClearanceAbove(DecimalOf(_pathClearanceEdit.Value));
        ConfigureBridgeWidthInput(_bridgeWidthEdit);
        _interaction.State.SetBridgeWidth(DecimalOf(_bridgeWidthEdit.Value));
        ConfigureBridgePlankCountInput(_bridgePlankCountEdit);
        _interaction.State.SetBridgePlankCount(
            (int)Math.Round(_bridgePlankCountEdit.Value, MidpointRounding.AwayFromZero));
        ConfigureBridgePlankGapInput(_bridgePlankGapEdit);
        _interaction.State.SetBridgePlankGap(DecimalOf(_bridgePlankGapEdit.Value));
        ConfigureElevationInputs(session.Metrics);
        _pathAutoStartToggle.ButtonPressed = true;
        _interaction.State.SetPathStartElevationOverride(null);
        UpdateSceneSizeMetrics();
        RebuildAssetBars();
        // The bars only hold buttons now, so the Asset an area shows is chosen
        // here - once, after they exist.
        ShowTerrainAssetsForArea();
        UpdateDrawingToolAvailability();
    }

    private void ConfigureElevationInputs(WorkspaceMetrics metrics)
    {
        ConfigureElevationInput(_elevationEdit, metrics);
        ConfigureElevationInput(_waterElevationEdit, metrics);
        ConfigureElevationInput(_pathStartElevationEdit, metrics);
        ConfigureElevationInput(_bridgeElevationEdit, metrics);
        ConfigureElevationInput(_sceneElevationEdit, metrics);
        ConfigureElevationInput(_sectionElevationEdit, metrics);
        ConfigureSectionOffsetInput(_sectionOffsetEdit, metrics);

        // How shallow a channel may be is the Workspace's statement, so the
        // field cannot offer less than it and a river carried over from a
        // shallower World is lifted to it rather than saved as it was.
        _waterDepthEdit.MinValue = (double)metrics.MinimumChannelDepthMeters;
        if (_waterDepthEdit.Value < _waterDepthEdit.MinValue)
            _waterDepthEdit.Value = _waterDepthEdit.MinValue;
        _interaction.State.SetWaterChannelDepth(DecimalOf(_waterDepthEdit.Value));

        var authoringElevation = ElevationOf(_elevationEdit, _elevationEdit.Value);
        _canvas.ElevationMeters = authoringElevation;
        _interaction.State.SetBridgeElevation(
            ElevationOf(_bridgeElevationEdit, _bridgeElevationEdit.Value));
        _interaction.State.SetWaterElevation(
            ElevationOf(_waterElevationEdit, _waterElevationEdit.Value));
        _ = ElevationOf(_sceneElevationEdit, _sceneElevationEdit.Value);
        _canvas.SectionElevationMeters = ElevationOf(
            _sectionElevationEdit,
            _sectionElevationEdit.Value);
        _canvas.SectionOffsetMeters = SectionOffsetOf(_sectionOffsetEdit.Value);
    }

    private static void ConfigureElevationInput(SpinBox input, WorkspaceMetrics metrics)
    {
        // SpinBox derives its visible decimal count from Range.Step by order of
        // magnitude. A non-decimal quantum such as 0.125 would therefore be
        // rendered with only one decimal if it were also the Range step. Keep
        // the display step at the smallest decimal place the quantum uses, and
        // give the arrows the actual quantum. ElevationOf remains the authority
        // that snaps typed values to that same quantum.
        input.Step = ElevationDisplayStep(metrics.ElevationQuantumMeters);
        input.CustomArrowStep = (double)metrics.ElevationQuantumMeters;
        input.CustomArrowRound = true;
        input.Editable = true;
    }

    private static void ConfigureSectionOffsetInput(
        SpinBox input,
        WorkspaceMetrics metrics)
    {
        input.MinValue = (double)metrics.ElevationQuantumMeters;
        input.Step = ElevationDisplayStep(metrics.ElevationQuantumMeters);
        input.CustomArrowStep = (double)metrics.ElevationQuantumMeters;
        input.CustomArrowRound = true;
        input.Editable = true;
    }

    private static double ElevationDisplayStep(decimal quantumMeters)
    {
        var text = quantumMeters.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);
        var decimalPoint = text.IndexOf('.', StringComparison.Ordinal);
        var decimalPlaces = decimalPoint < 0 ? 0 : text.Length - decimalPoint - 1;
        return Math.Pow(10.0, -decimalPlaces);
    }

    private void SetElevationInputsEditable(bool editable)
    {
        _elevationEdit.Editable = editable;
        _waterElevationEdit.Editable = editable;
        _pathStartElevationEdit.Editable = editable && !_pathAutoStartToggle.ButtonPressed;
        _pathClearanceEdit.Editable = editable;
        _bridgeElevationEdit.Editable = editable;
        _sceneElevationEdit.Editable = editable;
        _sectionElevationEdit.Editable = editable;
        _sectionOffsetEdit.Editable = editable;
    }

    private void RebuildAssetBars()
    {
        RebuildAssetBar(_terrainAssetBar, BuildTerrainAssetBar);
        RebuildAssetBar(_propAssetBar, BuildPropAssetBar);
    }

    private static void RebuildAssetBar(Container container, Action build)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
        build();
    }

    private void SaveRecentSession()
    {
        if (_recentSessionPath is null) return;
        try
        {
            _controller.SaveRecentSession(_recentSessionPath, _canvas.ViewState);
        }
        catch (Exception exception)
            when (exception is SceneMakerDocumentException
                or IOException
                or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save the recent session: {exception.Message}");
        }
    }

    private void RestoreRecentSession()
    {
        var report = _controller.RestoreRecentSession(_recentSessionPath!);
        if (!report.Succeeded) DiscardRecentSession();
        ShowSession();
        ClearTemplatePreview();
        ShowAuthoringElevation();
        _canvas.ShowScene(_controller.Scene, _controller.RestoredCanvasView);
        Report(report);
    }

    private void DiscardRecentSession()
    {
        if (_recentSessionPath is null) return;
        try
        {
            if (File.Exists(_recentSessionPath)) File.Delete(_recentSessionPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not discard obsolete recent session: {exception.Message}");
        }
    }

    private string WorkspaceDialogStartDirectory()
    {
        if (_controller.LastWorkspaceDirectory is { } directory && Directory.Exists(directory))
            return directory;
        var defaultDirectory = ProjectSettings.GlobalizePath("res://workspaces");
        return Directory.Exists(defaultDirectory)
            ? defaultDirectory
            : ProjectSettings.GlobalizePath("res://");
    }
}
