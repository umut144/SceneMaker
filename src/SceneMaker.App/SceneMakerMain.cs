using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SceneMaker.Core;

namespace SceneMaker.App;

public sealed partial class SceneMakerMain : Control
{
    private const int CreateWorkspaceMenuId = 10;
    private const int LoadWorkspaceMenuId = 11;
    private const int WorkspaceAssetsMenuId = 12;
    private const int CreateSceneMenuId = 20;
    private const int LoadSceneMenuId = 21;
    private const int ExportSceneMenuId = 22;
    private const int ChunkHelperMenuId = 30;

    private readonly EditorInteractionState _interactionState = new();
    private readonly SceneCanvas _canvas = new();
    private readonly Label _workspaceLabel = new();
    private readonly Label _sceneLabel = new();
    private readonly Label _toolContextLabel = new();
    private readonly VSeparator _toolContextSeparator = new();
    private readonly Label _propLineOffsetLabel = new();
    private readonly SpinBox _propLineOffsetEdit = new();
    private readonly Button _eraserToggle = new();
    private readonly Label _viewLabel = new();
    private readonly Label _statusLabel = new();
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
    private readonly SpinBox _templateInsertionXEdit = new();
    private readonly SpinBox _templateInsertionYEdit = new();
    private readonly CheckBox _templatePivotCenterToggle = new();
    private readonly Label _templateInsertionXMetricsLabel = new();
    private readonly Label _templateInsertionYMetricsLabel = new();

    private LoadedWorkspace? _workspace;
    private LoadedScene? _scene;
    private PolyToolsCatalog? _catalog;
    private WorkspaceConfiguration? _workspaceConfiguration;
    private TerrainDisplayCatalog? _terrainAssets;
    private PropDisplayCatalog? _propAssets;
    private string? _pendingWorkspaceParentDirectory;
    private string? _recentSessionPath;
    private string? _selectedTemplateAnchorId;
    private bool _updatingAnchorGroupEdit;
    private TemplateCompositionResult? _templatePreview;

    private sealed record WorkspaceAssetEditorRow(
        PolyToolsCatalogAsset Asset,
        CheckBox Enabled,
        LineEdit Color);

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
        _anchorGroupEdit.ValueChanged += SetSelectedTemplateAnchorGroup;
        toolColumn.AddChild(_anchorGroupEdit);

        _contextMenuBar.Name = "ContextMenu";
        _contextMenuBar.CustomMinimumSize = new Vector2(0f, 38f);
        _contextMenuBar.AddThemeFontSizeOverride("font_size", 14);
        _toolContextLabel.Name = "ActiveToolLabel";
        _toolContextLabel.VerticalAlignment = VerticalAlignment.Center;
        _contextMenuBar.AddChild(_toolContextLabel);
        _toolContextSeparator.Name = "ToolContextSeparator";
        _contextMenuBar.AddChild(_toolContextSeparator);
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
        _canvas.ConfigureInteractionState(_interactionState);
        if (_terrainAssets is not null) _canvas.ConfigureTerrainAssets(_terrainAssets);
        if (_propAssets is not null) _canvas.ConfigurePropAssets(_propAssets);
        _canvas.ViewChanged += UpdateViewStatus;
        _canvas.TerrainPaintRequested += PaintTerrainCell;
        _canvas.TerrainEraseRequested += EraseTerrainCell;
        _canvas.TerrainFillEraseRequested += EraseTerrainRegion;
        _canvas.TerrainFillRequested += FillTerrainRegion;
        _canvas.TerrainLineRequested += PaintTerrainLine;
        _canvas.TerrainLineEraseRequested += EraseTerrainLine;
        _canvas.PropRequested += PlaceProp;
        _canvas.PropLineRequested += PlacePropLine;
        _canvas.PropLineEraseRequested += ErasePropLine;
        _canvas.PropEraseRequested += EraseProp;
        _canvas.PropSelectRequested += SelectProp;
        _canvas.TemplateAnchorPlaceRequested += PlaceTemplateAnchor;
        _canvas.TemplateAnchorSelectRequested += SelectTemplateAnchor;
        _canvas.TemplateAnchorMoveRequested += MoveTemplateAnchor;
        _canvas.ToolStatusRequested += SetStatus;
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
        button.ButtonPressed = definition.Tool == _interactionState.ActiveTool;
        _drawingToolControlsByTool.Add(definition.Tool, button);
        parent.AddChild(button);
    }

    private void BuildTerrainAssetBar()
    {
        _terrainAssetBar.AddChild(new Label { Text = "Terrain  ›" });
        foreach (var asset in _terrainAssets?.Assets ?? [])
        {
            var button = new Button
            {
                Text = asset.Name,
                ToggleMode = true,
                ButtonGroup = _terrainAssetButtons,
                TooltipText = $"{asset.Name} · {asset.AssetKey}",
                CustomMinimumSize = new Vector2(120f, 0f),
            };
            button.AddThemeColorOverride("font_color", Color.FromHtml(asset.Color));
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

    private void BuildPropAssetBar()
    {
        _propAssetBar.AddChild(new Label { Text = "Props  ›" });
        foreach (var asset in _propAssets?.Assets ?? [])
        {
            var button = new Button
            {
                Text = asset.Name,
                ToggleMode = true,
                ButtonGroup = _propAssetButtons,
                TooltipText = $"{asset.Name} · {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} authoring px · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels})",
                CustomMinimumSize = new Vector2(160f, 0f),
            };
            button.AddThemeColorOverride("font_color", Color.FromHtml(asset.Color));
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
        _templateSearchEdit.TextChanged += _ => RebuildTemplateRows();
        popupContent.AddChild(_templateSearchEdit);

        var filterRow = new HBoxContainer();
        filterRow.AddChild(new Label { Text = "Group filter" });
        _templateGroupFilter.MinValue = 0;
        _templateGroupFilter.MaxValue = int.MaxValue;
        _templateGroupFilter.Step = 1;
        _templateGroupFilter.Value = 0;
        _templateGroupFilter.TooltipText = "0 shows all Template groups";
        _templateGroupFilter.ValueChanged += _ => RebuildTemplateRows();
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
        RebuildTemplateRows();
        _templatesPopup.PopupCentered(new Vector2I(500, 380));
    }

    private void RebuildTemplateRows()
    {
        foreach (var child in _templateRows.GetChildren())
        {
            _templateRows.RemoveChild(child);
            child.QueueFree();
        }

        if (_workspace is null)
        {
            _templateRows.AddChild(new Label { Text = "No Workspace loaded." });
            return;
        }

        try
        {
            var search = _templateSearchEdit.Text.Trim();
            var groupFilter = checked((int)_templateGroupFilter.Value);
            var templates = SceneStore
                .EnumeratePaths(_workspace)
                .Select(path => SceneStore.Load(_workspace, path))
                .Where(static scene => scene.Document.SceneKind == SceneKind.Template)
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
        catch (Exception exception) when (exception is SceneMakerDocumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            _templateRows.AddChild(new Label { Text = exception.Message });
        }
    }

    private void UpdateWorkspaceTemplateGroup(string filePath, int groupNumber)
    {
        if (_workspace is null) return;
        TryDocumentAction(() =>
        {
            var loaded = SceneStore.Load(_workspace, filePath);
            var document = TemplateEditing.SetTemplateGroup(loaded.Document, groupNumber);
            var updated = new LoadedScene(loaded.FilePath, document);
            SceneStore.Save(_workspace, updated);
            ClearTemplatePreview();
            if (_scene?.FilePath == updated.FilePath)
            {
                _scene = updated;
                _canvas.UpdateScene(updated);
                UpdateDocumentStatus();
            }
            SetStatus($"Assigned Scene Template '{document.SceneId}' to group {groupNumber}.");
        });
    }

    private void BuildSettingsMenu()
    {
        var menu = _settingsButton.GetPopup();
        menu.AddSeparator("Workspaces");
        menu.AddItem("Create Workspace", CreateWorkspaceMenuId);
        menu.AddItem("Load Workspace", LoadWorkspaceMenuId);
        menu.AddItem("Workspace Assets", WorkspaceAssetsMenuId);
        menu.AddSeparator("Scenes");
        menu.AddItem("Create Scene", CreateSceneMenuId);
        menu.AddItem("Load Scene", LoadSceneMenuId);
        menu.AddItem("Export Scene", ExportSceneMenuId);
        menu.AddSeparator("Canvas Helpers");
        menu.AddItem("Chunk Helper: not applicable", ChunkHelperMenuId);
        menu.SetItemDisabled(menu.GetItemIndex(ChunkHelperMenuId), true);
        menu.IdPressed += HandleSettingsMenu;
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
        _workspaceDirectoryLoadDialog.DirSelected += LoadWorkspaceFromDirectory;
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
        if (_workspaceConfiguration is null) return string.Empty;
        var integralCells = checked((int)cells);
        var metrics = _workspaceConfiguration.Metrics;
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
        if (_workspaceConfiguration is null) return;
        var metrics = _workspaceConfiguration.Metrics;
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
        if (_workspaceConfiguration is null) return string.Empty;
        var meters = (decimal)authoringPixels * _workspaceConfiguration.Metrics.MetersPerAuthoringPixel;
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
        switch (id)
        {
            case CreateWorkspaceMenuId:
                _workspaceDirectoryDialog.PopupCenteredRatio(0.75f);
                break;
            case LoadWorkspaceMenuId:
                OpenWorkspaceFinder();
                break;
            case WorkspaceAssetsMenuId:
                ShowWorkspaceAssetsDialog();
                break;
            case CreateSceneMenuId:
                if (_workspace is null)
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
            case LoadSceneMenuId:
                if (_workspace is null)
                {
                    ShowError("Create or load a Workspace before loading a Scene.");
                    return;
                }
                _sceneFileDialog.CurrentDir = _workspace.DirectoryPath;
                _sceneFileDialog.PopupCenteredRatio(0.75f);
                break;
            case ExportSceneMenuId:
                ExportCurrentScene();
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
                ["config.json ; SceneMaker Workspace"],
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
        if (paths.Length > 0)
            LoadWorkspaceSelection(paths[0]);
    }

    private void ExportCurrentScene()
    {
        if (_workspace is null || _scene is null || _workspaceConfiguration is null
            || _terrainAssets is null || _propAssets is null)
        {
            ShowError("Load a Scene before exporting.");
            return;
        }
        TryDocumentAction(() =>
        {
            var path = SceneExport.Write(
                _workspace,
                _scene,
                _workspaceConfiguration,
                _terrainAssets,
                _propAssets);
            SetStatus($"Exported Scene snapshot to '{path}'.");
        });
    }

    private void ShowWorkspaceAssetsDialog()
    {
        if (_workspaceConfiguration is null || _catalog is null) return;
        foreach (var child in _workspaceAssetRows.GetChildren())
        {
            _workspaceAssetRows.RemoveChild(child);
            child.QueueFree();
        }
        _workspaceAssetEditorRows.Clear();
        _workspaceAssetRows.AddChild(new Label
        {
            Text = "Assets come from the synchronized PolyTools catalog. SceneMaker owns only enablement and authoring color; whether an Asset is Terrain or a Prop is PolyTools data.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        foreach (var asset in _catalog.Assets)
        {
            var profile = _workspaceConfiguration.AssetProfiles
                .SingleOrDefault(value => value.AssetKey == asset.AssetKey);
            var row = new GridContainer { Columns = 3 };
            var enabled = new CheckBox { Text = asset.AssetKey, ButtonPressed = profile is not null };
            enabled.CustomMinimumSize = new Vector2(180f, 0f);
            var color = NewAssetField(profile?.Color ?? string.Empty, "#RRGGBB");
            row.AddChild(enabled);
            row.AddChild(new Label { Text = asset.AssetType.ToString() });
            row.AddChild(color);
            _workspaceAssetRows.AddChild(row);
            _workspaceAssetEditorRows.Add(asset.AssetKey,
                new WorkspaceAssetEditorRow(asset, enabled, color));
        }
        _workspaceAssetsDialog.PopupCentered(new Vector2I(760, 520));
    }

    private static LineEdit NewAssetField(string value, string placeholder) => new()
    {
        Text = value,
        PlaceholderText = placeholder,
        CustomMinimumSize = new Vector2(100f, 0f),
    };

    private void SaveWorkspaceAssets()
    {
        if (_workspace is null || _workspaceConfiguration is null || _catalog is null) return;
        TryDocumentAction(() =>
        {
            List<WorkspaceAssetProfile> profiles = [];
            foreach (var row in _workspaceAssetEditorRows.Values)
            {
                if (!row.Enabled.ButtonPressed) continue;
                profiles.Add(new WorkspaceAssetProfile(
                    row.Asset.AssetKey,
                    row.Color.Text.Trim()));
            }
            var candidate = _workspaceConfiguration.WithAssetProfiles(profiles, _catalog);
            var terrain = TerrainDisplayCatalogLoader.Load(_catalog, candidate);
            var props = PropDisplayCatalogLoader.Load(_catalog, candidate);
            if (_scene is not null)
            {
                DocumentValidation.ValidateGrid(_scene.Document, candidate.Metrics);
                TerrainEditing.ValidateAssetReferences(_scene.Document, terrain);
                PropEditing.ValidateAssetReferences(_scene.Document, props);
            }
            WorkspaceConfigurationStore.Save(_workspace.DirectoryPath, candidate);
            LoadWorkspaceAssets();
            UpdateDocumentStatus();
            SetStatus("Saved Workspace asset profiles.");
        });
    }

    private void CreateWorkspace()
    {
        if (_pendingWorkspaceParentDirectory is null)
        {
            ShowError("Choose a parent directory for the Workspace first.");
            return;
        }

        TryDocumentAction(() =>
        {
            _workspace = WorkspaceStore.Create(
                _pendingWorkspaceParentDirectory,
                _workspaceIdEdit.Text.Trim());
            WorkspaceConfigurationStore.CreateDefault(
                _workspace.DirectoryPath,
                _workspace.WorkspaceKey);
            _catalog = null;
            _workspaceConfiguration = null;
            _terrainAssets = null;
            _propAssets = null;
            _scene = null;
            _selectedTemplateAnchorId = null;
            _templatePreview = null;
            _canvas.ShowScene(null);
            SaveRecentSession();
            UpdateDocumentStatus();
            SetStatus(
                $"Created Workspace '{_workspace.WorkspaceKey}'. Synchronize its PolyTools import before editing.");
        });
    }

    private void LoadWorkspaceFromDirectory(string directory)
    {
        var workspaceDirectory = ResolveFileSystemPath(directory);
        var configPath = Path.Combine(workspaceDirectory, WorkspaceConfigurationStore.FileName);
        if (!File.Exists(configPath))
        {
            SetStatus($"Workspace load blocked: no {WorkspaceConfigurationStore.FileName} in the selected folder.");
            return;
        }

        LoadWorkspace(configPath);
    }

    private void LoadWorkspaceSelection(string selectedPath)
    {
        var path = ResolveFileSystemPath(selectedPath);
        if (Directory.Exists(path))
        {
            LoadWorkspaceFromDirectory(path);
            return;
        }

        if (File.Exists(path))
        {
            LoadWorkspace(path);
            return;
        }

        SetStatus("Workspace load blocked: selected path does not exist.");
    }

    private void LoadWorkspace(string configPath)
    {
        var previousWorkspace = _workspace;
        var previousCatalog = _catalog;
        var previousConfiguration = _workspaceConfiguration;
        var previousTerrainAssets = _terrainAssets;
        var previousPropAssets = _propAssets;
        try
        {
            var fullConfigPath = ResolveFileSystemPath(configPath);
            if (!string.Equals(Path.GetFileName(fullConfigPath), WorkspaceConfigurationStore.FileName, StringComparison.Ordinal))
            {
                SetStatus("Workspace load blocked: select a workspace config.json file.");
                return;
            }
            if (!File.Exists(fullConfigPath))
            {
                SetStatus("Workspace load blocked: config.json was not found.");
                return;
            }
            var workspaceDirectory = Path.GetDirectoryName(fullConfigPath)
                ?? throw new SceneMakerDocumentException("Workspace config requires a parent directory.");
            var catalog = PolyToolsCatalogImporter.Load(workspaceDirectory);
            var loadedWorkspace = WorkspaceStore.Load(workspaceDirectory, catalog);
            _catalog = catalog;
            _workspace = loadedWorkspace;
            LoadWorkspaceAssets();
            _scene = null;
            _selectedTemplateAnchorId = null;
            _templatePreview = null;
            _canvas.ShowScene(null);
            SaveRecentSession();
            UpdateDocumentStatus();
            SetStatus($"Loaded Workspace '{_workspace.WorkspaceKey}'.");
        }
        catch (Exception exception) when (exception is SceneMakerDocumentException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or OverflowException)
        {
            _workspace = previousWorkspace;
            _catalog = previousCatalog;
            _workspaceConfiguration = previousConfiguration;
            _terrainAssets = previousTerrainAssets;
            _propAssets = previousPropAssets;
            SetStatus($"Workspace load blocked: {exception.Message}");
        }
    }

    private void CreateScene()
    {
        if (_workspace is null) return;
        TryDocumentAction(() =>
        {
            _scene = SceneStore.Create(
                _workspace,
                _sceneIdEdit.Text.Trim(),
                checked((int)_sceneWidthEdit.Value),
                checked((int)_sceneHeightEdit.Value),
                SelectedSceneKind(),
                checked((int)_templateGroupEdit.Value),
                checked((int)_templateInsertionXEdit.Value),
                checked((int)_templateInsertionYEdit.Value));
            _canvas.ShowScene(_scene);
            _selectedTemplateAnchorId = null;
            _templatePreview = null;
            SaveRecentSession();
            UpdateDocumentStatus();
            SetStatus($"Created {_scene.Document.SceneKind switch
            {
                SceneKind.Instance => "Scene Instance",
                SceneKind.Template => "Scene Template",
                _ => throw new ArgumentOutOfRangeException(),
            }} '{_scene.Document.SceneId}'.");
        });
    }

    private void LoadScene(string filePath)
    {
        if (_workspace is null) return;
        TryDocumentAction(() =>
        {
            _scene = SceneStore.Load(_workspace, ResolveFileSystemPath(filePath));
            DocumentValidation.ValidateGrid(_scene.Document, _workspaceConfiguration!.Metrics);
            TerrainEditing.ValidateAssetReferences(_scene.Document, _terrainAssets!);
            PropEditing.ValidateAssetReferences(_scene.Document, _propAssets!);
            _canvas.ShowScene(_scene);
            _templatePreview = null;
            _selectedTemplateAnchorId = null;
            SaveRecentSession();
            UpdateDocumentStatus();
            SetStatus($"Loaded Scene '{_scene.Document.SceneId}'.");
        });
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
        if (_scene is null || _workspace is null) return;
        TryDocumentAction(() =>
        {
            var cells = checked((int)_mapExtensionCellsEdit.Value);
            var document = north
                ? MapEditing.ExtendNorth(_scene.Document, cells)
                : MapEditing.ExtendEast(_scene.Document, cells);
            SaveUpdatedScene(document);
            UpdateDocumentStatus();
            var direction = north ? "north" : "east";
            SetStatus($"Extended Map {direction} by {cells} Cells without moving authored data.");
        });
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

    private void SetPropLineOffset(double value)
    {
        var offset = checked((int)value);
        _interactionState.SetPropLineOffset(offset);
        SetStatus($"Prop Line offset set to {offset} authoring px.");
    }

    private void UpdateToolContextLabel()
    {
        _toolContextLabel.Text =
            $"{EditorToolRegistry.ModeDisplayName(_interactionState.Mode)}:"
            + EditorToolRegistry.Resolve(_interactionState.ActiveTool).DisplayName;
        var propLineActive = _interactionState.Mode == EditorMode.Props
            && _interactionState.ActiveTool == EditorTool.Line;
        _toolContextSeparator.Visible = propLineActive;
        _propLineOffsetLabel.Visible = propLineActive;
        _propLineOffsetEdit.Visible = propLineActive;
    }

    private void SelectTerrainAsset(string assetKey)
    {
        var asset = _terrainAssets!.Resolve(assetKey);
        _canvas.SelectedTerrainAssetKey = assetKey;
        SetStatus($"Selected Terrain '{asset.Name}' ({asset.AssetKey}).");
    }

    private void SelectPropAsset(string assetKey)
    {
        var asset = _propAssets!.Resolve(assetKey);
        _canvas.SelectedPropAssetKey = assetKey;
        SetStatus($"Selected Prop '{asset.Name}' · footprint {asset.FootprintWidthAuthoringPixels} × {asset.FootprintHeightAuthoringPixels} · anchor ({asset.AnchorXAuthoringPixels}, {asset.AnchorYAuthoringPixels}).");
    }

    private void BeginTemplateAnchorPlacement()
    {
        if (_scene?.Document.SceneKind != SceneKind.Instance)
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
        if (_workspace is null || _scene?.Document.SceneKind != SceneKind.Instance) return;
        TryDocumentAction(() =>
        {
            var scenes = SceneStore
                .EnumeratePaths(_workspace)
                .Select(path => SceneStore.Load(_workspace, path).Document)
                .ToList();
            var seed = unchecked((ulong)Random.Shared.NextInt64());
            _templatePreview = TemplateComposition.Compose(
                _scene.Document,
                scenes,
                _propAssets!,
                seed);
            _canvas.ShowTemplatePreview(
                _templatePreview.ComposedScene,
                _templatePreview.EffectiveTerrainMasks);
            var selections = _templatePreview.Selections.Count == 0
                ? "no Template Anchors"
                : string.Join(
                    ", ",
                    _templatePreview.Selections.Select(selection =>
                        $"{selection.AnchorId} → {selection.TemplateSceneId}"));
            SetStatus($"Generated transient Template Preview: {selections}.");
        });
    }

    private void ClearTemplatePreview()
    {
        _templatePreview = null;
        _canvas.ShowTemplatePreview(null);
    }

    private void PlaceTemplateAnchor(int authoringX, int authoringY)
    {
        if (_scene is null || _workspace is null) return;
        TryEditAction("Place Anchor", () =>
        {
            var before = _scene.Document.TemplateAnchors
                .Select(static anchor => anchor.AnchorId)
                .ToHashSet(StringComparer.Ordinal);
            var document = TemplateEditing.PlaceAnchor(
                _scene.Document,
                _workspaceConfiguration!.Metrics,
                authoringX,
                authoringY,
                checked((int)_anchorGroupEdit.Value));
            var added = document.TemplateAnchors.Single(anchor => !before.Contains(anchor.AnchorId));
            SaveUpdatedScene(document);
            SelectTemplateAnchorById(added.AnchorId);
            SetStatus(
                $"Placed Template Anchor '{added.AnchorId}' for group {added.GroupNumber} at ({added.PositionAuthoringPx.X}, {added.PositionAuthoringPx.Y}).");
        });
    }

    private void SelectTemplateAnchor(int authoringX, int authoringY)
    {
        if (_scene?.Document.SceneKind != SceneKind.Instance) return;
        var anchor = TemplateEditing.FindAnchorAt(
            _scene.Document,
            authoringX,
            authoringY);
        SelectTemplateAnchorById(anchor?.AnchorId);
        SetStatus(anchor is null
            ? "No Template Anchor selected."
            : $"Selected '{anchor.AnchorId}' · group {anchor.GroupNumber} · position ({anchor.PositionAuthoringPx.X}, {anchor.PositionAuthoringPx.Y}).");
    }

    private void SelectTemplateAnchorById(string? anchorId)
    {
        _selectedTemplateAnchorId = anchorId;
        _canvas.SelectTemplateAnchor(anchorId);
        if (anchorId is not null && _scene is not null)
        {
            var anchor = _scene.Document.TemplateAnchors.Single(value => value.AnchorId == anchorId);
            _updatingAnchorGroupEdit = true;
            _anchorGroupEdit.Value = anchor.GroupNumber;
            _updatingAnchorGroupEdit = false;
        }
        UpdateTemplateControls();
    }

    private void MoveTemplateAnchor(string anchorId, int authoringX, int authoringY)
    {
        if (_scene is null || _workspace is null) return;
        TryEditAction("Move Anchor", () =>
        {
            var document = TemplateEditing.MoveAnchor(
                _scene.Document,
                _workspaceConfiguration!.Metrics,
                anchorId,
                authoringX,
                authoringY);
            SaveUpdatedScene(document);
            SelectTemplateAnchorById(anchorId);
            var moved = document.TemplateAnchors.Single(anchor => anchor.AnchorId == anchorId);
            SetStatus(
                $"Moved Template Anchor '{anchorId}' to ({moved.PositionAuthoringPx.X}, {moved.PositionAuthoringPx.Y}).");
        });
    }

    private void SetSelectedTemplateAnchorGroup(double value)
    {
        if (_updatingAnchorGroupEdit
            || _scene is null
            || _workspace is null
            || _selectedTemplateAnchorId is null)
            return;
        TryEditAction("Assign Anchor Group", () =>
        {
            var groupNumber = checked((int)value);
            var document = TemplateEditing.SetAnchorGroup(
                _scene.Document,
                _selectedTemplateAnchorId,
                groupNumber);
            SaveUpdatedScene(document);
            SetStatus(
                $"Assigned Template Anchor '{_selectedTemplateAnchorId}' to group {groupNumber}.");
        });
    }

    private void PaintTerrainCell(int cellX, int cellY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedTerrainAssetKey is not { } assetKey)
            return;

        TryDocumentAction(() =>
        {
            var document = TerrainEditing.Paint(
                _scene.Document,
                _terrainAssets!,
                cellX,
                cellY,
                assetKey);
            var updated = new LoadedScene(_scene.FilePath, document);
            SceneStore.Save(_workspace, updated);
            _scene = updated;
            _canvas.UpdateScene(updated);
            SaveRecentSession();
            var asset = _terrainAssets!.Resolve(assetKey);
            SetStatus($"Painted {asset.Name} at Terrain cell ({cellX}, {cellY}).");
        });
    }

    private void EraseTerrainCell(int cellX, int cellY)
    {
        if (_workspace is null || _scene is null) return;
        TryDocumentAction(() =>
        {
            var document = TerrainEditing.Erase(_scene.Document, cellX, cellY);
            if (ReferenceEquals(document, _scene.Document)) return;
            SaveUpdatedScene(document);
            SetStatus($"Erased Terrain at cell ({cellX}, {cellY}); uncovered spatial instances are export warnings.");
        });
    }

    private void FillTerrainRegion(int cellX, int cellY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedTerrainAssetKey is not { } assetKey)
            return;
        TryDocumentAction(() =>
        {
            var document = TerrainEditing.Fill(
                _scene.Document,
                _terrainAssets!,
                cellX,
                cellY,
                assetKey);
            if (ReferenceEquals(document, _scene.Document))
            {
                SetStatus("Terrain Fill made no change because source and target Terrain are identical.");
                return;
            }
            SaveUpdatedScene(document);
            var asset = _terrainAssets!.Resolve(assetKey);
            SetStatus($"Filled the connected region at ({cellX}, {cellY}) with {asset.Name}.");
        });
    }

    private void EraseTerrainRegion(int cellX, int cellY)
    {
        if (_workspace is null || _scene is null) return;
        TryDocumentAction(() =>
        {
            var document = TerrainEditing.EraseFill(_scene.Document, cellX, cellY);
            if (ReferenceEquals(document, _scene.Document))
            {
                SetStatus("Terrain Eraser Fill made no change because the region is already empty.");
                return;
            }
            SaveUpdatedScene(document);
            SetStatus($"Erased the connected Terrain region at ({cellX}, {cellY}).");
        });
    }

    private void PaintTerrainLine(int startX, int startY, int endX, int endY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedTerrainAssetKey is not { } assetKey)
            return;
        TryDocumentAction(() =>
        {
            var document = TerrainEditing.PaintLine(
                _scene.Document,
                _terrainAssets!,
                startX,
                startY,
                endX,
                endY,
                assetKey);
            SaveUpdatedScene(document);
            _canvas.CompleteLinePlacement();
            var count = TerrainEditing.LineCells(startX, startY, endX, endY).Count;
            var asset = _terrainAssets!.Resolve(assetKey);
            SetStatus($"Line Draw painted {count} Terrain cells with {asset.Name}.");
        });
    }

    private void EraseTerrainLine(int startX, int startY, int endX, int endY)
    {
        if (_workspace is null || _scene is null) return;
        TryDocumentAction(() =>
        {
            var beforeCount = _scene.Document.TerrainCells.Count;
            var document = TerrainEditing.EraseLine(
                _scene.Document,
                startX,
                startY,
                endX,
                endY);
            _canvas.CompleteLinePlacement();
            if (ReferenceEquals(document, _scene.Document))
            {
                SetStatus("Line Eraser made no change because the selected Terrain cells are empty.");
                return;
            }
            SaveUpdatedScene(document);
            var erased = beforeCount - document.TerrainCells.Count;
            SetStatus($"Line Eraser removed {erased} Terrain cell{(erased == 1 ? string.Empty : "s")}.");
        });
    }

    private void PlaceProp(int authoringX, int authoringY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedPropAssetKey is not { } assetKey)
            return;

        TryEditAction("Pencil Draw", () =>
        {
            var document = PropEditing.Place(
                _scene.Document,
                _propAssets!,
                authoringX,
                authoringY,
                assetKey);
            SaveUpdatedScene(document);
            var asset = _propAssets!.Resolve(assetKey);
            SetStatus($"Placed {asset.Name} anchor at ({authoringX}, {authoringY}) authoring px.");
        });
    }

    private void PlacePropLine(int startX, int startY, int endX, int endY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedPropAssetKey is not { } assetKey)
            return;

        TryEditAction("Line Draw", () =>
        {
            var beforeCount = _scene.Document.Props.Count;
            var document = PropEditing.PlaceLine(
                _scene.Document,
                _propAssets!,
                startX,
                startY,
                endX,
                endY,
                assetKey,
                _interactionState.PropLineOffsetAuthoringPixels);
            SaveUpdatedScene(document);
            _canvas.CompleteLinePlacement();
            var added = document.Props.Count - beforeCount;
            SetStatus($"Line Draw placed {added} Prop{(added == 1 ? string.Empty : "s")} with exact non-overlapping footprints.");
        });
    }

    private void ErasePropLine(int startX, int startY, int endX, int endY)
    {
        if (_workspace is null || _scene is null || _canvas.SelectedPropAssetKey is not { } assetKey)
            return;

        TryEditAction("Line Eraser", () =>
        {
            var asset = _propAssets!.Resolve(assetKey);
            var document = _scene.Document;
            var beforeCount = document.Props.Count;
            foreach (var anchor in PropEditing.LineAnchors(
                         asset,
                         startX,
                         startY,
                         endX,
                         endY,
                         _interactionState.PropLineOffsetAuthoringPixels))
            {
                document = PropEditing.EraseAt(
                    document,
                    _propAssets!,
                    anchor.X,
                    anchor.Y);
            }
            SaveUpdatedScene(document);
            _canvas.CompleteLinePlacement();
            var erased = beforeCount - document.Props.Count;
            SetStatus($"Line Eraser removed {erased} Prop{(erased == 1 ? string.Empty : "s")}.");
        });
    }

    private void EraseProp(int authoringX, int authoringY)
    {
        if (_workspace is null || _scene is null) return;
        TryEditAction("Eraser", () =>
        {
            var document = PropEditing.EraseAt(
                _scene.Document,
                _propAssets!,
                authoringX,
                authoringY);
            if (ReferenceEquals(document, _scene.Document)) return;
            SaveUpdatedScene(document);
            _canvas.SelectProp(null);
            SetStatus($"Erased Prop at ({authoringX}, {authoringY}) authoring px.");
        });
    }

    private void SelectProp(int authoringX, int authoringY)
    {
        if (_scene is null) return;
        var prop = PropEditing.FindAt(
            _scene.Document,
            _propAssets!,
            authoringX,
            authoringY);
        _canvas.SelectProp(prop?.InstanceId);
        SetStatus(prop is null
            ? "No Prop selected."
            : $"Selected '{prop.InstanceId}' · anchor ({prop.PositionAuthoringPx.X}, {prop.PositionAuthoringPx.Y}).");
    }

    private void SaveUpdatedScene(SceneDocument document)
    {
        var updated = new LoadedScene(_scene!.FilePath, document);
        SceneStore.Save(_workspace!, updated);
        _scene = updated;
        ClearTemplatePreview();
        _canvas.UpdateScene(updated);
        SaveRecentSession();
    }

    private void UpdateDocumentStatus()
    {
        _workspaceLabel.Text = _workspace is null
            ? "Workspace: none"
            : $"Workspace: {_workspace.WorkspaceKey}";
        _sceneLabel.Text = _scene is null
            ? "Scene: none"
            : $"Scene: {_scene.Document.SceneId}  ·  {(_scene.Document.SceneKind == SceneKind.Instance ? "Instance" : "Template")}  ·  {_scene.Document.SizeCells.Width} × {_scene.Document.SizeCells.Height} cells";

        var menu = _settingsButton.GetPopup();
        var sceneActionsAvailable = _workspace is not null;
        menu.SetItemDisabled(menu.GetItemIndex(WorkspaceAssetsMenuId), !sceneActionsAvailable);
        menu.SetItemDisabled(menu.GetItemIndex(CreateSceneMenuId), !sceneActionsAvailable);
        menu.SetItemDisabled(menu.GetItemIndex(LoadSceneMenuId), !sceneActionsAvailable);
        menu.SetItemDisabled(menu.GetItemIndex(ExportSceneMenuId), _scene is null);
        UpdateDrawingToolAvailability();
        UpdateTemplateControls();
        UpdateMapControls();
        UpdateViewStatus();
    }

    private void UpdateMapControls()
    {
        var scene = _scene?.Document;
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

        _mapDimensionsLabel.Text =
            $"{scene.SizeCells.Width} × {scene.SizeCells.Height} Cells  ·  "
            + $"{scene.SizeCells.Width * _workspaceConfiguration!.Metrics.TerrainCellMeters:0.###} × {scene.SizeCells.Height * _workspaceConfiguration.Metrics.TerrainCellMeters:0.###} m  ·  "
            + $"{_workspaceConfiguration.Metrics.SceneWidthAuthoringPixels(scene)} × {_workspaceConfiguration.Metrics.SceneHeightAuthoringPixels(scene)} px";
        var extensionCells = checked((int)_mapExtensionCellsEdit.Value);
        _mapExtensionMetricsLabel.Text =
            $"= {extensionCells * _workspaceConfiguration!.Metrics.TerrainCellMeters:0.###} m · {extensionCells * _workspaceConfiguration.Metrics.AuthoringPixelsPerTerrainCell} px";
    }

    private void UpdateDrawingToolAvailability()
    {
        var templateMode = _interactionState.Mode == EditorMode.Templates;
        var instanceActive = _scene?.Document.SceneKind == SceneKind.Instance;
        foreach (var (tool, control) in _drawingToolControlsByTool)
        {
            control.Visible = EditorToolRegistry.Supports(_interactionState.Mode, tool);
            control.Disabled = _scene is null
                || templateMode && !instanceActive;
            control.ButtonPressed = control.Visible && tool == _interactionState.ActiveTool;
        }
        _anchorGroupEdit.Visible = templateMode && instanceActive;
    }

    private void UpdateTemplateControls()
    {
        var workspaceActive = _workspace is not null;
        var instanceActive = _scene?.Document.SceneKind == SceneKind.Instance;
        _placeTemplateAnchorButton.Disabled = !instanceActive;
        _templatesButton.Disabled = !workspaceActive;
        _anchorGroupEdit.Editable = instanceActive;
        _regeneratePreviewButton.Disabled = !instanceActive
            || _scene!.Document.TemplateAnchors.Count == 0;
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

    private void TryEditAction(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is SceneMakerDocumentException
                                          or OverflowException)
        {
            SetStatus($"{operation} blocked: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
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

    private void LoadWorkspaceAssets()
    {
        if (_workspace is null || _catalog is null)
            throw new InvalidOperationException("Workspace and synchronized PolyTools catalog are required.");
        _workspaceConfiguration = WorkspaceConfigurationStore.Load(
            _workspace.DirectoryPath, _catalog);
        if (!string.Equals(
                _workspaceConfiguration.WorkspaceKey,
                _workspace.WorkspaceKey,
                StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Workspace config key '{_workspaceConfiguration.WorkspaceKey}' must match workspace '{_workspace.WorkspaceKey}'.");
        }
        _terrainAssets = TerrainDisplayCatalogLoader.Load(_catalog, _workspaceConfiguration);
        _propAssets = PropDisplayCatalogLoader.Load(_catalog, _workspaceConfiguration);
        _canvas.ConfigureMetrics(_workspaceConfiguration.Metrics);
        _canvas.ConfigureTerrainAssets(_terrainAssets);
        _canvas.ConfigurePropAssets(_propAssets);
        UpdateSceneSizeMetrics();
        RebuildAssetBars();
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
        if (_recentSessionPath is null || _workspace is null) return;
        RecentSessionStore.Save(_recentSessionPath, _workspace, _scene);
    }

    private void RestoreRecentSession()
    {
        try
        {
            var recent = RecentSessionStore.Load(_recentSessionPath!);
            if (recent is null) return;
            _catalog = PolyToolsCatalogImporter.Load(recent.WorkspaceDirectoryPath);
            _workspace = WorkspaceStore.Load(recent.WorkspaceDirectoryPath, _catalog);
            LoadWorkspaceAssets();
            _templatePreview = null;
            _scene = recent.SceneRelativePath is null
                ? null
                : SceneStore.Load(
                    _workspace,
                    Path.Combine(_workspace.DirectoryPath, recent.SceneRelativePath));
            if (_scene is not null)
            {
                DocumentValidation.ValidateGrid(_scene.Document, _workspaceConfiguration!.Metrics);
                TerrainEditing.ValidateAssetReferences(_scene.Document, _terrainAssets!);
                PropEditing.ValidateAssetReferences(_scene.Document, _propAssets!);
            }
            _canvas.ShowScene(_scene);
            SetStatus(_scene is null
                ? $"Restored Workspace '{_workspace.WorkspaceKey}'."
                : $"Restored Workspace '{_workspace.WorkspaceKey}' and Scene '{_scene.Document.SceneId}'.");
        }
        catch (Exception exception) when (exception is SceneMakerDocumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            DiscardRecentSession();
            _workspace = null;
            _scene = null;
            _canvas.ShowScene(null);
            SetStatus("No compatible recent session was restored.");
        }
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
        if (_workspace is not null) return _workspace.DirectoryPath;
        var defaultDirectory = ProjectSettings.GlobalizePath("res://workspaces");
        return Directory.Exists(defaultDirectory)
            ? defaultDirectory
            : ProjectSettings.GlobalizePath("res://");
    }
}
