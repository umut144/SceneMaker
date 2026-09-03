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
    private readonly Label _propLineOffsetLabel = new();
    private readonly SpinBox _propLineOffsetEdit = new();
    private readonly Label _waterPointModeLabel = new();
    private readonly OptionButton _waterPointModeEdit = new();
    private readonly Label _riverWidthLabel = new();
    private readonly SpinBox _riverWidthEdit = new();
    private readonly CheckBox _snapWaterToggle = new();
    private readonly Label _waterElevationLabel = new();
    private readonly SpinBox _waterElevationEdit = new();
    private readonly Label _waterDepthLabel = new();
    private readonly SpinBox _waterDepthEdit = new();
    private readonly Label _waterClearanceLabel = new();
    private readonly SpinBox _waterClearanceEdit = new();
    private readonly Label _waterDerivedSpanLabel = new();
    private readonly Button _eraserToggle = new();
    private readonly Button _heatmapToggle = new();
    private readonly MenuButton _waterHeatmapValueEdit = new();
    private readonly Label _viewLabel = new();
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
    private readonly HBoxContainer _overviewNavigationBar = new();
    private readonly HBoxContainer _contextNavigationBar = new();
    private readonly HBoxContainer _contextMenuBar = new();
    private readonly VBoxContainer _toolOptionsBar = new();
    private readonly Button _returnNavigationButton = new();
    private readonly Button _mapNavigationButton = new();
    private readonly Label _mapDimensionsLabel = new();
    private readonly SpinBox _mapExtensionCellsEdit = new();
    private readonly Label _mapExtensionMetricsLabel = new();
    private readonly Button _extendNorthButton = new();
    private readonly Button _extendEastButton = new();
    private readonly Button _placeTemplateAnchorButton = new();
    private readonly Button _templatesButton = new();
    private readonly Button _regeneratePreviewButton = new();
    private readonly PopupPanel _templatesPopup = new();
    private readonly LineEdit _templateSearchEdit = new();
    private readonly SpinBox _templateGroupFilter = new();
    private readonly VBoxContainer _templateRows = new();
    private readonly SpinBox _anchorGroupEdit = new();
    private readonly Dictionary<EditorTool, Button> _drawingToolControlsByTool = [];

    private readonly FileDialog _workspaceDirectoryDialog = new();
    private readonly FileDialog _workspaceDirectoryLoadDialog = new();
    private readonly FileDialog _sceneFileDialog = new();
    private readonly ConfirmationDialog _createWorkspaceDialog = new();
    private readonly ConfirmationDialog _createSceneDialog = new();
    private readonly AcceptDialog _errorDialog = new();
    private readonly ConfirmationDialog _workspaceAssetsDialog = new();
    private readonly VBoxContainer _workspaceAssetRows = new();
    private readonly Dictionary<string, WorkspaceAssetEditorRow> _workspaceAssetEditorRows = [];
    private readonly LineEdit _workspaceIdEdit = new();
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
        PolyToolsCatalogAsset Asset,
        CheckBox Enabled,
        LineEdit Color,
        LineEdit Surface,
        OptionButton Authoring);

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
        AddPerspectiveButton(_overviewNavigationBar, "Props", available: true, "Prop authoring view");
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

        _contextMenuBar.Name = "ContextMenu";
        _contextMenuBar.CustomMinimumSize = new Vector2(0f, 38f);
        _contextMenuBar.AddThemeFontSizeOverride("font_size", 14);
        _toolContextLabel.Name = "ActiveToolLabel";
        _toolContextLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_toolContextLabel);
        _toolContextSeparator.Name = "ToolContextSeparator";
        _contextMenuBar.AddChild(_toolContextSeparator);
        _elevationLabel.Name = "ElevationLabel";
        _elevationLabel.Text = "Height";
        _elevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_elevationLabel);
        _elevationEdit.Name = "Elevation";
        ConfigureElevationInput(_elevationEdit);
        _elevationEdit.TooltipText = "The height the drawing tools author at.";
        _elevationEdit.ValueChanged += SetAuthoringElevation;
        _contextMenuBar.AddChild(_elevationEdit);
        _propLineOffsetLabel.Name = "PropLineOffsetLabel";
        _propLineOffsetLabel.Text = "Prop Offset";
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
        _waterPointModeLabel.Name = "WaterPointModeLabel";
        _waterPointModeLabel.Text = "Point";
        _waterPointModeLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_waterPointModeLabel);
        _waterPointModeEdit.Name = "WaterPointMode";
        _waterPointModeEdit.AddItem("Linear", (int)WaterPointMode.Linear);
        _waterPointModeEdit.AddItem("Aligned", (int)WaterPointMode.Aligned);
        _waterPointModeEdit.Selected = (int)WaterPointMode.Linear;
        _waterPointModeEdit.TooltipText =
            "How the next curve point's handles behave. Switchable while drawing; "
            + "it decides what the next point does and leaves the placed ones alone.";
        _waterPointModeEdit.ItemSelected += SetWaterPointMode;
        _contextMenuBar.AddChild(_waterPointModeEdit);
        _riverWidthLabel.Name = "RiverWidthLabel";
        _riverWidthLabel.Text = "Width";
        _riverWidthLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_riverWidthLabel);
        _riverWidthEdit.Name = "RiverWidth";
        ConfigureRiverWidthInput(_riverWidthEdit);
        _riverWidthEdit.TooltipText = "The width of the corridor around the river's centerline.";
        _riverWidthEdit.ValueChanged += SetRiverWidth;
        _contextMenuBar.AddChild(_riverWidthEdit);
        _snapWaterToggle.Name = "SnapWaterToTerrain";
        _snapWaterToggle.Text = "Snap";
        _snapWaterToggle.ButtonPressed = true;
        _snapWaterToggle.TooltipText =
            "Take the water level from the Terrain under each placed point. "
            + "Switch it off to drive a river into a mountain, where the water "
            + "must not follow the ground. What gets stored is the height, not "
            + "the relationship: repainting Terrain later never moves the river.";
        _snapWaterToggle.Toggled += SetSnapWaterToTerrain;
        _contextMenuBar.AddChild(_snapWaterToggle);
        _waterElevationLabel.Name = "WaterElevationLabel";
        _waterElevationLabel.Text = "Water";
        _waterElevationLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_waterElevationLabel);
        _waterElevationEdit.Name = "WaterElevation";
        ConfigureElevationInput(_waterElevationEdit);
        _waterElevationEdit.TooltipText =
            "The water surface the next point takes with Snap off, and the fallback "
            + "for the first point when Snap finds no Terrain.";
        _waterElevationEdit.ValueChanged += SetWaterElevation;
        _contextMenuBar.AddChild(_waterElevationEdit);
        _waterDepthLabel.Name = "WaterDepthLabel";
        _waterDepthLabel.Text = "Depth";
        _waterDepthLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_waterDepthLabel);
        _waterDepthEdit.Name = "WaterDepth";
        ConfigureWaterSpanInput(_waterDepthEdit, 0.1, WaterEditing.DefaultChannelDepthMeters);
        _waterDepthEdit.TooltipText =
            "How deep the channel is below the surface. The Terrain is carved away "
            + "from the bed upwards, so this is also where the river's floor sits.";
        _waterDepthEdit.ValueChanged += SetWaterDepth;
        _contextMenuBar.AddChild(_waterDepthEdit);
        _waterClearanceLabel.Name = "WaterClearanceLabel";
        _waterClearanceLabel.Text = "Clearance";
        _waterClearanceLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_waterClearanceLabel);
        _waterClearanceEdit.Name = "WaterClearance";
        ConfigureWaterSpanInput(_waterClearanceEdit, 0.0, WaterEditing.DefaultClearanceAboveMeters);
        _waterClearanceEdit.TooltipText =
            "The headroom the river needs above its surface. Where the ground never "
            + "reaches it the river is open; where it does, that much is left as a tunnel.";
        _waterClearanceEdit.ValueChanged += SetWaterClearance;
        _contextMenuBar.AddChild(_waterClearanceEdit);
        _waterDerivedSpanLabel.Name = "WaterDerivedSpan";
        _waterDerivedSpanLabel.VerticalAlignment = VerticalAlignment.Center;
        _waterDerivedSpanLabel.TooltipText =
            "Derived boundaries only: bed = water - depth; cut top = water + clearance.";
        _contextMenuBar.AddChild(_waterDerivedSpanLabel);
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
        }
        _canvas.ViewChanged += UpdateViewStatus;
        _canvas.OutcomeProduced += HandleToolOutcome;
        _canvas.StrokeEnded += EndEditStroke;
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        canvasRow.AddChild(_canvas);

        var toolOptionsPanel = new PanelContainer
        {
            Name = "ToolOptionsBar",
            CustomMinimumSize = new Vector2(52f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        canvasRow.AddChild(toolOptionsPanel);
        var toolOptionsMargin = new MarginContainer();
        toolOptionsMargin.AddThemeConstantOverride("margin_left", 4);
        toolOptionsMargin.AddThemeConstantOverride("margin_top", 6);
        toolOptionsMargin.AddThemeConstantOverride("margin_right", 4);
        toolOptionsMargin.AddThemeConstantOverride("margin_bottom", 6);
        toolOptionsPanel.AddChild(toolOptionsMargin);
        _toolOptionsBar.Name = "ToolOptions";
        _toolOptionsBar.Alignment = BoxContainer.AlignmentMode.Begin;
        _toolOptionsBar.AddThemeConstantOverride("separation", 6);
        toolOptionsMargin.AddChild(_toolOptionsBar);

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
        _toolOptionsBar.AddChild(_eraserToggle);

        // A separator, because what follows is a way of looking rather than a
        // way of working: the tools keep doing what they did.
        _toolOptionsBar.AddChild(new HSeparator());

        _heatmapToggle.Name = "HeatmapToggle";
        _heatmapToggle.Text = "m";
        _heatmapToggle.Alignment = HorizontalAlignment.Center;
        _heatmapToggle.ToggleMode = true;
        _heatmapToggle.TooltipText = "Show Terrain, Props and Water by height instead of by Asset";
        _heatmapToggle.CustomMinimumSize = new Vector2(42f, 42f);
        _heatmapToggle.Toggled += SetHeatmapEnabled;
        _toolOptionsBar.AddChild(_heatmapToggle);
        _waterHeatmapValueEdit.Name = "WaterHeatmapValue";
        _waterHeatmapValueEdit.Text = "S";
        _waterHeatmapValueEdit.Alignment = HorizontalAlignment.Center;
        _waterHeatmapValueEdit.CustomMinimumSize = new Vector2(42f, 42f);
        _waterHeatmapValueEdit.Disabled = true;
        var waterHeatmapMenu = _waterHeatmapValueEdit.GetPopup();
        // Radio items, so the menu shows which boundary is being drawn rather
        // than leaving the single letter on the button to carry it alone.
        waterHeatmapMenu.AddRadioCheckItem("Surface", (int)WaterHeatmapValue.Surface);
        waterHeatmapMenu.AddRadioCheckItem("Bed", (int)WaterHeatmapValue.Bed);
        waterHeatmapMenu.AddRadioCheckItem("Cut top", (int)WaterHeatmapValue.CutTop);
        waterHeatmapMenu.IdPressed += SetWaterHeatmapValue;
        CheckWaterHeatmapItem(WaterHeatmapValue.Surface);
        _waterHeatmapValueEdit.TooltipText =
            "Which boundary of every water span the height view shows. Terrain and Props "
            + "continue to show their own elevation.";
        _toolOptionsBar.AddChild(_waterHeatmapValueEdit);
        UpdateToolContextLabel();

        var footer = new HBoxContainer { Name = "Footer" };
        footer.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(footer);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_statusLabel);
        footer.AddChild(_viewLabel);
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
        button.Pressed += () => SelectDrawingTool(definition.Tool);
        button.ButtonPressed = definition.Tool == _interaction.ActiveTool;
        _drawingToolControlsByTool.Add(definition.Tool, button);
        parent.AddChild(button);
    }

    private void BuildTerrainAssetBar()
    {
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
            if (_canvas.SelectedTerrainAssetKey is null)
            {
                button.ButtonPressed = true;
                SelectTerrainAsset(asset.AssetKey);
            }
        }
        _terrainAssetBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

    /// <summary>
    /// An Asset button reads as its Asset. Selection is therefore carried by the
    /// background - a filled chip in the Asset's own colour - and never by the
    /// text: the default theme recolours a pressed Button's font white, which
    /// turned the one Asset the author had chosen into the one Asset whose
    /// colour they could no longer see.
    /// </summary>
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
        _propAssetBar.AddChild(new Label { Text = "Props  ›" });
        foreach (var asset in _controller.Session?.PropAssets.Assets ?? [])
        {
            var button = new Button
            {
                Text = asset.Name,
                ToggleMode = true,
                ButtonGroup = _propAssetButtons,
                TooltipText = $"{asset.Name} · {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} authoring px · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels})",
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
        _propAssetBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

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
        _mapDimensionsLabel.CustomMinimumSize = new Vector2(260f, 0f);
        _mapDimensionsLabel.VerticalAlignment = VerticalAlignment.Center;
        _mapBar.AddChild(_mapDimensionsLabel);
        _mapBar.AddChild(new Label { Text = "Extend" });
        _mapExtensionCellsEdit.MinValue = 1;
        _mapExtensionCellsEdit.MaxValue = int.MaxValue;
        _mapExtensionCellsEdit.Step = 1;
        _mapExtensionCellsEdit.Value = 1;
        _mapExtensionCellsEdit.CustomMinimumSize = new Vector2(100f, 0f);
        _mapExtensionCellsEdit.TooltipText = "WorldGrid Cells to add to the selected map edge.";
        _mapExtensionCellsEdit.ValueChanged += _ => UpdateMapControls();
        _mapBar.AddChild(_mapExtensionCellsEdit);
        _mapExtensionMetricsLabel.CustomMinimumSize = new Vector2(150f, 0f);
        _mapExtensionMetricsLabel.VerticalAlignment = VerticalAlignment.Center;
        _mapBar.AddChild(_mapExtensionMetricsLabel);
        _extendNorthButton.Text = "North ↑";
        _extendNorthButton.TooltipText = "Add Cells above the existing map without moving authored data.";
        _extendNorthButton.Pressed += () => ExtendMap(north: true);
        _mapBar.AddChild(_extendNorthButton);
        _extendEastButton.Text = "East →";
        _extendEastButton.TooltipText = "Add Cells right of the existing map without moving authored data.";
        _extendEastButton.Pressed += () => ExtendMap(north: false);
        _mapBar.AddChild(_extendEastButton);
        _mapBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        UpdateMapControls();
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
            _createWorkspaceDialog.PopupCentered(new Vector2I(460, 180));
        };
        AddChild(_workspaceDirectoryDialog);

        _workspaceDirectoryLoadDialog.Title = "Load Workspace Folder";
        _workspaceDirectoryLoadDialog.Access = FileDialog.AccessEnum.Filesystem;
        _workspaceDirectoryLoadDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
        _workspaceDirectoryLoadDialog.UseNativeDialog = true;
        _workspaceDirectoryLoadDialog.DirSelected += LoadWorkspaceSelection;
        AddChild(_workspaceDirectoryLoadDialog);

        _sceneFileDialog.Title = "Load Scene";
        _sceneFileDialog.Access = FileDialog.AccessEnum.Filesystem;
        _sceneFileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        _sceneFileDialog.Filters = ["*.scene.json ; SceneMaker Scene"];
        _sceneFileDialog.UseNativeDialog = true;
        _sceneFileDialog.FileSelected += LoadScene;
        AddChild(_sceneFileDialog);

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
            "The height a newly authored cell or Prop takes in this Scene.";
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

    private static EditorMode EditorModeForPerspective(string perspective) => perspective switch
    {
        "Terrain" => EditorMode.Terrain,
        "Props" => EditorMode.Props,
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
                _sceneFileDialog.CurrentDir = _controller.Session.DirectoryPath;
                _sceneFileDialog.PopupCenteredRatio(0.75f);
                break;
            case SettingsMenuItem.ExportWorkspace:
                ExportWorkspace();
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

    private void ExportWorkspace()
    {
        _autosaveTimer.Stop();
        var report = _controller.ExportWorkspace();
        UpdateDocumentState();
        if (report.Succeeded) SetStatus(report.Message);
        else ShowError(report.Message);
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
            Text = "Assets come from the synchronized PolyTools catalog. SceneMaker owns enablement, authoring color, the surface a Terrain Asset presents to the game, and whether it is painted as cells or drawn as a curve; whether an Asset is Terrain or a Prop is PolyTools data.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        foreach (var asset in _controller.Session.Catalog.Assets)
        {
            var profile = _controller.Session.Configuration.AssetProfiles
                .SingleOrDefault(value => value.AssetKey == asset.AssetKey);
            var isTerrain = asset.AssetType == PolyToolsAssetType.Terrain;
            var row = new GridContainer { Columns = 5 };
            var enabled = new CheckBox { Text = asset.AssetKey, ButtonPressed = profile is not null };
            enabled.CustomMinimumSize = new Vector2(180f, 0f);
            var color = NewAssetField(profile?.Color ?? string.Empty, "#RRGGBB");
            // Only Terrain presents a surface, and only Terrain may carry one.
            var surface = NewAssetField(
                profile?.Surface ?? (isTerrain ? DefaultSurface : string.Empty),
                isTerrain ? "land" : string.Empty);
            surface.Editable = isTerrain;
            // How a Terrain Asset is authored decides which tools it offers, so
            // it is chosen here rather than guessed from its surface.
            var authoring = new OptionButton { Disabled = !isTerrain };
            authoring.AddItem("cells", (int)TerrainAuthoring.Cells);
            authoring.AddItem("curve", (int)TerrainAuthoring.Curve);
            authoring.Selected = (int)(profile?.Authoring ?? TerrainAuthoring.Cells);
            row.AddChild(enabled);
            row.AddChild(new Label { Text = asset.AssetType.ToString() });
            row.AddChild(color);
            row.AddChild(surface);
            row.AddChild(authoring);
            _workspaceAssetRows.AddChild(row);
            _workspaceAssetEditorRows.Add(asset.AssetKey,
                new WorkspaceAssetEditorRow(asset, enabled, color, surface, authoring));
        }
        _workspaceAssetsDialog.PopupCentered(new Vector2I(760, 520));
    }

    /// <summary>What a Terrain Asset presents unless the author says otherwise.</summary>
    private const string DefaultSurface = "land";

    /// <summary>A height in metres, in the tenth-of-a-metre steps ramps use.</summary>
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
        input.Step = 0.1;
        input.AllowGreater = false;
        input.AllowLesser = false;
        input.Suffix = " m";
        input.CustomMinimumSize = new Vector2(130f, 0f);
        input.Value = (double)SceneDocument.GroundElevationMeters;
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
        _canvas.ElevationMeters = elevation;
        SetStatus($"Drawing at {elevation:0.###} m.");
    }

    private static LineEdit NewAssetField(string value, string placeholder) => new()
    {
        Text = value,
        PlaceholderText = placeholder,
        CustomMinimumSize = new Vector2(100f, 0f),
    };

    private void SaveWorkspaceAssets()
    {
        if (_controller.Session is null) return;
        List<WorkspaceAssetProfile> profiles = [];
        foreach (var row in _workspaceAssetEditorRows.Values)
        {
            if (!row.Enabled.ButtonPressed) continue;
            var isTerrain = row.Asset.AssetType == PolyToolsAssetType.Terrain;
            var surface = isTerrain ? row.Surface.Text.Trim() : null;
            var authoring = isTerrain
                ? (TerrainAuthoring)row.Authoring.GetItemId(row.Authoring.Selected)
                : (TerrainAuthoring?)null;
            profiles.Add(new WorkspaceAssetProfile(
                row.Asset.AssetKey,
                row.Color.Text.Trim(),
                surface,
                authoring));
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
            _workspaceIdEdit.Text.Trim());
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
        _canvas.SelectMode(mode);
        _overviewNavigationBar.Visible = false;
        _contextNavigationBar.Visible = true;
        _terrainAssetBar.Visible = mode == EditorMode.Terrain;
        _propAssetBar.Visible = mode == EditorMode.Props;
        _templateBar.Visible = mode == EditorMode.Templates;
        _mapBar.Visible = false;
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        UpdateTemplateControls();
        SetStatus($"Selected {perspective} perspective.");
    }

    private void SelectMapContext()
    {
        _overviewNavigationBar.Visible = false;
        _contextNavigationBar.Visible = true;
        _terrainAssetBar.Visible = false;
        _propAssetBar.Visible = false;
        _templateBar.Visible = false;
        _mapBar.Visible = true;
        UpdateMapControls();
        SetStatus("Map bounds.");
    }

    private void ShowNavigationOverview()
    {
        _overviewNavigationBar.Visible = true;
        _contextNavigationBar.Visible = false;
        UpdateDrawingToolAvailability();
        UpdateTemplateControls();
        SetStatus("Navigation overview.");
    }

    private void ExtendMap(bool north)
    {
        if (_controller.Scene is null || _controller.Session is null) return;
        var cells = checked((int)_mapExtensionCellsEdit.Value);
        var direction = north ? "north" : "east";
        ExecuteSceneCommand(new ToolOutcome.Edit(
            "Extend Map",
            document => north
                ? MapEditing.ExtendNorth(document, cells)
                : MapEditing.ExtendEast(document, cells),
            Describe: (_, _) =>
                $"Extended Map {direction} by {cells} Cells without moving authored data."));
        UpdateDocumentStatus();
    }

    private void SelectDrawingTool(EditorTool tool)
    {
        _canvas.SelectTool(tool);
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        SetStatus($"Selected {EditorToolRegistry.Resolve(tool).DisplayName}.");
    }

    private void SetEraserEnabled(bool enabled)
    {
        _canvas.SetEraserEnabled(enabled);
        SetStatus(enabled ? "Eraser enabled." : "Eraser disabled.");
    }

    private void SetHeatmapEnabled(bool enabled)
    {
        _canvas.HeatmapEnabled = enabled;
        UpdateWaterHeatmapAvailability();
        SetStatus(enabled
            ? "Height view on. Drawing and placing work as usual."
            : "Height view off.");
    }

    /// <summary>Marks one radio item and clears the rest.</summary>
    private void CheckWaterHeatmapItem(WaterHeatmapValue value)
    {
        var menu = _waterHeatmapValueEdit.GetPopup();
        for (var index = 0; index < menu.ItemCount; index++)
            menu.SetItemChecked(index, menu.GetItemId(index) == (int)value);
    }

    private void UpdateWaterHeatmapAvailability() =>
        _waterHeatmapValueEdit.Disabled = !_canvas.HeatmapEnabled
            || _controller.Document?.WaterBodies.Count is not > 0;

    private void SetWaterHeatmapValue(long item)
    {
        var value = (WaterHeatmapValue)item;
        _canvas.WaterHeatmapValue = value;
        _waterHeatmapValueEdit.Text = value switch
        {
            WaterHeatmapValue.Surface => "S",
            WaterHeatmapValue.Bed => "B",
            WaterHeatmapValue.CutTop => "C",
            _ => throw new InvalidOperationException("Unknown water heatmap value."),
        };
        CheckWaterHeatmapItem(value);
        SetStatus(value switch
        {
            WaterHeatmapValue.Surface => "Height view: water cells show their surface.",
            WaterHeatmapValue.Bed => "Height view: water cells show the river bed.",
            WaterHeatmapValue.CutTop => "Height view: water cells show the top of the Terrain cut.",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
    }

    private void SetPropLineOffset(double value)
    {
        var offset = checked((int)value);
        _interaction.State.SetPropLineOffset(offset);
        SetStatus($"Prop Line offset set to {offset} authoring px.");
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

    private void SetWaterPointMode(long item)
    {
        var mode = (WaterPointMode)_waterPointModeEdit.GetItemId((int)item);
        _interaction.State.SetWaterPointMode(mode);
        SetStatus(mode == WaterPointMode.Linear
            ? "River: the next point makes its segments straight."
            : "River: drag the next point to pull its handle, or click for an automatic one.");
    }

    private void SetRiverWidth(double value)
    {
        var width = DecimalOf(value);
        _interaction.State.SetRiverWidth(width);
        SetStatus($"River width set to {width:0.###} m.");
    }

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
        SetStatus($"Water level set to {elevation:0.###} m.");
    }

    private void SetWaterDepth(double value)
    {
        var depth = DecimalOf(value);
        _interaction.State.SetWaterChannelDepth(depth);
        UpdateWaterDerivedSpan();
        SetStatus($"Channel depth set to {depth:0.###} m; the bed sits that far below the surface.");
    }

    private void SetWaterClearance(double value)
    {
        var clearance = DecimalOf(value);
        _interaction.State.SetWaterClearanceAbove(clearance);
        UpdateWaterDerivedSpan();
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
        var riverActive = _interaction.Mode == EditorMode.Terrain
            && _interaction.ActiveTool == EditorTool.DrawRiver;
        _toolContextSeparator.Visible = propLineActive || riverActive;
        _propLineOffsetLabel.Visible = propLineActive;
        _propLineOffsetEdit.Visible = propLineActive;
        _waterPointModeLabel.Visible = riverActive;
        _waterPointModeEdit.Visible = riverActive;
        _riverWidthLabel.Visible = riverActive;
        _riverWidthEdit.Visible = riverActive;
        _snapWaterToggle.Visible = riverActive;
        _waterElevationLabel.Visible = riverActive;
        _waterElevationEdit.Visible = riverActive;
        _waterDepthLabel.Visible = riverActive;
        _waterDepthEdit.Visible = riverActive;
        _waterClearanceLabel.Visible = riverActive;
        _waterClearanceEdit.Visible = riverActive;
        _waterDerivedSpanLabel.Visible = riverActive;
        // Height authors Terrain and Props. Water carries its own three, so
        // leaving it in reach here would offer a number that changes nothing.
        _elevationLabel.Visible = !riverActive;
        _elevationEdit.Visible = !riverActive;
    }

    private void SelectTerrainAsset(string assetKey)
    {
        var asset = _controller.Session!.TerrainAssets.Resolve(assetKey);
        _canvas.SelectedTerrainAssetKey = assetKey;
        // The tools follow the Asset. Grass is painted and has no Bezier; a
        // river is drawn and has no Pencil or Fill, and offering them would be
        // offering something that cannot work.
        if (_interaction.Mode == EditorMode.Terrain
            && !EditorToolRegistry.Offers(EditorMode.Terrain, _interaction.ActiveTool, asset.Authoring))
        {
            _canvas.SelectTool(EditorToolRegistry.DefaultTool(EditorMode.Terrain, asset.Authoring));
        }
        UpdateDrawingToolAvailability();
        UpdateToolContextLabel();
        SetStatus($"Selected Terrain '{asset.Name}' ({asset.AssetKey}).");
    }

    /// <summary>
    /// How the chosen Terrain Asset is authored, or null while none is chosen -
    /// then the tool bar narrows nothing.
    /// </summary>
    private TerrainAuthoring? SelectedTerrainAuthoring()
    {
        if (_controller.Session is not { } session
            || _canvas.SelectedTerrainAssetKey is not { } assetKey)
        {
            return null;
        }
        foreach (var asset in session.TerrainAssets.Assets)
        {
            if (string.Equals(asset.AssetKey, assetKey, StringComparison.Ordinal))
                return asset.Authoring;
        }
        return null;
    }

    private void SelectPropAsset(string assetKey)
    {
        var asset = _controller.Session!.PropAssets.Resolve(assetKey);
        _canvas.SelectedPropAssetKey = assetKey;
        SetStatus($"Selected Prop '{asset.Name}' · footprint {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels}).");
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
        SetSettingsItemDisabled(SettingsMenuItem.ExportWorkspace, _controller.Session is null);
        UpdateDrawingToolAvailability();
        UpdateWaterHeatmapAvailability();
        UpdateTemplateControls();
        UpdateMapControls();
        UpdateDocumentState();
        UpdateViewStatus();
    }

    private void UpdateMapControls()
    {
        var scene = _controller.Document;
        var enabled = scene is not null;
        _mapNavigationButton.Disabled = !enabled;
        _mapExtensionCellsEdit.Editable = enabled;
        _extendNorthButton.Disabled = !enabled;
        _extendEastButton.Disabled = !enabled;
        if (scene is null)
        {
            _mapDimensionsLabel.Text = "No Scene loaded";
            _mapExtensionMetricsLabel.Text = string.Empty;
            return;
        }

        // A Scene can only be open while a session is open.
        var metrics = _controller.Session!.Metrics;
        _mapDimensionsLabel.Text =
            $"{scene.SizeCells.Width} × {scene.SizeCells.Height} Cells  ·  "
            + $"{scene.SizeCells.Width * metrics.TerrainCellMeters:0.###} × {scene.SizeCells.Height * metrics.TerrainCellMeters:0.###} m  ·  "
            + $"{metrics.SceneWidthAuthoringPixels(scene)} × {metrics.SceneHeightAuthoringPixels(scene)} px";
        var extensionCells = checked((int)_mapExtensionCellsEdit.Value);
        _mapExtensionMetricsLabel.Text =
            $"= {extensionCells * metrics.TerrainCellMeters:0.###} m · {extensionCells * metrics.AuthoringPixelsPerTerrainCell} px";
    }

    private void UpdateDrawingToolAvailability()
    {
        var templateMode = _interaction.Mode == EditorMode.Templates;
        var instanceActive = _controller.Document?.SceneKind == SceneKind.Instance;
        foreach (var (tool, control) in _drawingToolControlsByTool)
        {
            control.Visible = EditorToolRegistry.Offers(
                _interaction.Mode, tool, SelectedTerrainAuthoring());
            control.Disabled = _controller.Scene is null
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
        ConfigureRiverWidthInput(_riverWidthEdit);
        _interaction.State.SetRiverWidth(DecimalOf(_riverWidthEdit.Value));
        ConfigureElevationInputs(session.Metrics);
        UpdateSceneSizeMetrics();
        RebuildAssetBars();
    }

    private void ConfigureElevationInputs(WorkspaceMetrics metrics)
    {
        ConfigureElevationInput(_elevationEdit, metrics);
        ConfigureElevationInput(_waterElevationEdit, metrics);
        ConfigureElevationInput(_sceneElevationEdit, metrics);

        var authoringElevation = ElevationOf(_elevationEdit, _elevationEdit.Value);
        _canvas.ElevationMeters = authoringElevation;
        _interaction.State.SetWaterElevation(
            ElevationOf(_waterElevationEdit, _waterElevationEdit.Value));
        _ = ElevationOf(_sceneElevationEdit, _sceneElevationEdit.Value);
    }

    private static void ConfigureElevationInput(SpinBox input, WorkspaceMetrics metrics)
    {
        input.Step = (double)metrics.ElevationQuantumMeters;
        input.Editable = true;
    }

    private void SetElevationInputsEditable(bool editable)
    {
        _elevationEdit.Editable = editable;
        _waterElevationEdit.Editable = editable;
        _sceneElevationEdit.Editable = editable;
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
        _controller.SaveRecentSession(_recentSessionPath);
    }

    private void RestoreRecentSession()
    {
        var report = _controller.RestoreRecentSession(_recentSessionPath!);
        if (!report.Succeeded) DiscardRecentSession();
        ShowSession();
        ClearTemplatePreview();
        ShowAuthoringElevation();
        _canvas.ShowScene(_controller.Scene);
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
