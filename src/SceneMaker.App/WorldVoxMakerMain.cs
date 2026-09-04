using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SceneMaker.Core;
using SceneMaker.Editor;

namespace SceneMaker.App;

/// <summary>The product-facing voxel authoring shell.</summary>
public sealed partial class WorldVoxMakerMain : Control
{
    private readonly EditorController _controller = new();
    private readonly VoxelToolInteraction _interaction = new();
    private readonly VoxelViewport3D _viewport = new();
    private readonly OptionButton _tool = new();
    private readonly OptionButton _editMode = new();
    private readonly OptionButton _material = new();
    private readonly OptionButton _exportFormat = new();
    private readonly SpinBox _hillBase = Number(-1000, 1000, 0.2, 0);
    private readonly SpinBox _hillTop = Number(-1000, 1000, 0.2, 8);
    private readonly SpinBox _pathWidth = Number(0.2, 1000, 0.2, 4);
    private readonly SpinBox _pathHeight = Number(0.2, 1000, 0.2, 3);
    private readonly SpinBox _pathElevation = Number(-1000, 1000, 0.2, 0);
    private readonly SpinBox _pathGrade = Number(-10, 10, 0.05, 0.25);
    private readonly CheckBox _gradeFollowing = new() { Text = "Grade per segment", ButtonPressed = true };
    private readonly CheckBox _fillToGround = new() { Text = "Fill gap to ground", ButtonPressed = true };
    private readonly GridContainer _hillOptions = new() { Columns = 4 };
    private readonly GridContainer _pathOptions = new() { Columns = 4 };
    private readonly Label _workspace = new() { Text = "No Workspace" };
    private readonly Label _scene = new() { Text = "No Scene" };
    private readonly Label _status = new() { Text = "Open a Workspace and Scene to author voxels." };
    private readonly Label _slice = new() { Text = "Top-down 3D" };
    private readonly Button _save = new() { Text = "Save", Disabled = true };
    private readonly Button _undo = new() { Text = "Undo", Disabled = true };
    private readonly Button _redo = new() { Text = "Redo", Disabled = true };
    private readonly Button _openScene = new() { Text = "Open Scene", Disabled = true };
    private readonly Button _newScene = new() { Text = "New Scene", Disabled = true };
    private readonly Button _export = new() { Text = "Export", Disabled = true };
    private readonly FileDialog _workspaceDialog = new();
    private readonly FileDialog _sceneDialog = new();
    private readonly ConfirmationDialog _newSceneDialog = new();
    private readonly AcceptDialog _errorDialog = new();
    private readonly LineEdit _newSceneId = new() { PlaceholderText = "scene_id" };
    private readonly SpinBox _newSceneWidth = Number(1, 4096, 1, 100);
    private readonly SpinBox _newSceneDepth = Number(1, 4096, 1, 100);
    private readonly Timer _autosave = new() { WaitTime = 1.5, OneShot = true };
    private readonly List<string> _materialKeys = [];

    public override void _Ready()
    {
        BuildInterface();
        BuildDialogs();
        WireViewport();
        UpdateToolOptions();
        GD.Print("WorldVoxMaker voxel authoring tool ready");
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.IsCommandOrControlPressed())
        {
            if (key.Keycode == Key.S) Save();
            else if ((key.Keycode == Key.Z && key.ShiftPressed) || key.Keycode == Key.Y) Redo();
            else if (key.Keycode == Key.Z) Undo();
            else return;
            GetViewport().SetInputAsHandled();
            return;
        }
        var toolKey = key.Keycode switch
        {
            Key.Enter or Key.KpEnter => ToolKey.Enter,
            Key.Escape => ToolKey.Escape,
            _ => (ToolKey?)null,
        };
        if (toolKey is null || BuildContext() is not { } context) return;
        ApplyOutcome(_interaction.KeyPressed(context, toolKey.Value));
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree() => _controller.SaveScene();

    private void BuildInterface()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        var navigation = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        root.AddChild(navigation);
        var openWorkspace = new Button { Text = "Open Workspace" };
        openWorkspace.Pressed += () => _workspaceDialog.PopupCenteredRatio(0.72f);
        navigation.AddChild(openWorkspace);
        _openScene.Pressed += () => _sceneDialog.PopupCenteredRatio(0.72f);
        navigation.AddChild(_openScene);
        _newScene.Pressed += () => _newSceneDialog.PopupCentered(new Vector2I(420, 260));
        navigation.AddChild(_newScene);
        navigation.AddChild(new VSeparator());
        _save.Pressed += Save;
        navigation.AddChild(_save);
        _undo.Pressed += Undo;
        navigation.AddChild(_undo);
        _redo.Pressed += Redo;
        navigation.AddChild(_redo);
        navigation.AddChild(new VSeparator());
        _exportFormat.AddItem("Heightfield (2.5D)", (int)VoxelExportFormat.Heightfield);
        _exportFormat.AddItem("Surface mesh", (int)VoxelExportFormat.SurfaceMesh);
        _exportFormat.AddItem("Compressed voxels", (int)VoxelExportFormat.CompressedVoxels);
        _exportFormat.AddItem("Legacy SceneMaker", 100);
        _exportFormat.Select(2);
        navigation.AddChild(_exportFormat);
        _export.Pressed += Export;
        navigation.AddChild(_export);
        navigation.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        navigation.AddChild(_workspace);
        navigation.AddChild(new VSeparator());
        navigation.AddChild(_scene);

        var tools = new HBoxContainer { CustomMinimumSize = new Vector2(0, 42) };
        root.AddChild(tools);
        tools.AddChild(new Label { Text = "Tool" });
        _tool.AddItem("VoxelTile", (int)VoxelAuthoringTool.Tile);
        _tool.AddItem("VoxelHill", (int)VoxelAuthoringTool.Hill);
        _tool.AddItem("VoxelPath", (int)VoxelAuthoringTool.Path);
        _tool.ItemSelected += _ =>
        {
            _interaction.SelectTool(SelectedTool());
            UpdateToolOptions();
        };
        tools.AddChild(_tool);
        tools.AddChild(new Label { Text = "Operation" });
        _editMode.AddItem("Add", (int)VoxelEditMode.Additive);
        _editMode.AddItem("Subtract", (int)VoxelEditMode.Subtractive);
        tools.AddChild(_editMode);
        tools.AddChild(new Label { Text = "Material" });
        _material.CustomMinimumSize = new Vector2(180, 0);
        tools.AddChild(_material);
        tools.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        tools.AddChild(_slice);

        AddNumberField(_hillOptions, "Base m", _hillBase);
        AddNumberField(_hillOptions, "Top m", _hillTop);
        root.AddChild(_hillOptions);

        AddNumberField(_pathOptions, "Width m", _pathWidth);
        AddNumberField(_pathOptions, "Height m", _pathHeight);
        AddNumberField(_pathOptions, "Elevation m", _pathElevation);
        AddNumberField(_pathOptions, "Grade", _pathGrade);
        _pathOptions.AddChild(_gradeFollowing);
        _pathOptions.AddChild(_fillToGround);
        root.AddChild(_pathOptions);

        root.AddChild(_viewport);
        _status.CustomMinimumSize = new Vector2(0, 30);
        _status.VerticalAlignment = VerticalAlignment.Center;
        root.AddChild(_status);
        AddChild(_autosave);
        _autosave.Timeout += Save;
    }

    private void BuildDialogs()
    {
        _workspaceDialog.Title = "Open WorldVoxMaker Workspace";
        _workspaceDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
        _workspaceDialog.Access = FileDialog.AccessEnum.Filesystem;
        _workspaceDialog.DirSelected += OpenWorkspace;
        AddChild(_workspaceDialog);

        _sceneDialog.Title = "Open WorldVoxMaker Scene";
        _sceneDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        _sceneDialog.Access = FileDialog.AccessEnum.Filesystem;
        _sceneDialog.Filters = ["*.scene.json ; WorldVoxMaker Scene"];
        _sceneDialog.FileSelected += OpenScene;
        AddChild(_sceneDialog);

        _newSceneDialog.Title = "Create voxel Scene";
        var fields = new GridContainer { Columns = 2 };
        fields.AddChild(new Label { Text = "Scene ID" });
        fields.AddChild(_newSceneId);
        fields.AddChild(new Label { Text = "Width (m)" });
        fields.AddChild(_newSceneWidth);
        fields.AddChild(new Label { Text = "Depth (m)" });
        fields.AddChild(_newSceneDepth);
        _newSceneDialog.AddChild(fields);
        _newSceneDialog.Confirmed += CreateScene;
        AddChild(_newSceneDialog);

        _errorDialog.Title = "WorldVoxMaker";
        AddChild(_errorDialog);
    }

    private void WireViewport()
    {
        _viewport.PickPressed += pick => HandlePick(pick, dragging: false);
        _viewport.PickDragged += pick => HandlePick(pick, dragging: true);
        _viewport.PickHovered += HandleHover;
        _viewport.StrokeEnded += () => _controller.EndEditStroke();
    }

    private void OpenWorkspace(string directory)
    {
        var report = _controller.OpenWorkspaceDirectory(directory);
        Report(report);
        if (!report.Succeeded || _controller.Session is not { } session) return;
        _workspace.Text = $"Workspace: {session.WorkspaceKey}";
        _scene.Text = "No Scene";
        _openScene.Disabled = false;
        _newScene.Disabled = false;
        _sceneDialog.CurrentDir = session.Workspace.ScenesDirectoryPath;
        PopulateMaterials(session.Configuration);
        UpdateButtons();
    }

    private void OpenScene(string path)
    {
        var report = _controller.OpenScene(path);
        Report(report);
        if (report.Succeeded) RefreshScene(resetCamera: true);
    }

    private void CreateScene()
    {
        var report = _controller.CreateInstance(
            _newSceneId.Text.Trim(),
            checked((int)_newSceneWidth.Value),
            checked((int)_newSceneDepth.Value),
            0m);
        Report(report);
        if (report.Succeeded) RefreshScene(resetCamera: true);
    }

    private void HandlePick(VoxelPick pick, bool dragging)
    {
        if (BuildContext() is not { } context) return;
        var coordinate = pick.Cell;
        if (context.Settings.Tool == VoxelAuthoringTool.Tile)
        {
            if (context.Settings.EditMode == VoxelEditMode.Subtractive && !pick.HitVoxel)
                return;
            if (context.Settings.EditMode == VoxelEditMode.Additive && pick.HitVoxel)
            {
                coordinate = new VoxelCoordinate(
                    pick.Cell.X + pick.FaceNormal.X,
                    pick.Cell.Y + pick.FaceNormal.Y,
                    pick.Cell.Z + pick.FaceNormal.Z);
            }
        }
        var outcome = dragging
            ? _interaction.PointerDragged(context, coordinate, pick.Position)
            : _interaction.PointerPressed(context, coordinate, pick.Position);
        ApplyOutcome(outcome);
    }

    private void HandleHover(VoxelPick pick)
    {
        if (BuildContext() is not { } context) return;
        var elevation = context.Settings.Tool switch
        {
            VoxelAuthoringTool.Path => _interaction.NextPathElevation(context, pick.Position),
            VoxelAuthoringTool.Hill => context.Settings.HillBaseElevationMeters,
            _ => pick.HitVoxel
                ? (pick.Cell.Y + 0.5m) * context.Metrics.VoxelSizeMeters
                : 0m,
        };
        _viewport.FollowCrossSection(new VoxelPointMeters(
            pick.Position.X,
            elevation,
            pick.Position.Z));
        var current = _viewportCrossSectionLabel(elevation);
        _slice.Text = current;
    }

    private string _viewportCrossSectionLabel(decimal elevation) =>
        $"Smart section · {elevation:0.###} m";

    private VoxelToolContext? BuildContext()
    {
        if (_controller.Session is not { } session || _controller.Document is not { } document)
            return null;
        return new VoxelToolContext(
            document,
            session.Configuration,
            session.Metrics,
            new VoxelToolSettings(
                SelectedTool(),
                (VoxelEditMode)_editMode.GetItemId(_editMode.Selected),
                SelectedMaterial(),
                (decimal)_hillBase.Value,
                (decimal)_hillTop.Value,
                (decimal)_pathWidth.Value,
                (decimal)_pathHeight.Value,
                (decimal)_pathElevation.Value,
                (decimal)_pathGrade.Value,
                _gradeFollowing.ButtonPressed,
                _fillToGround.ButtonPressed
                    ? VoxelPathSupportMode.FillToGround
                    : VoxelPathSupportMode.Floating));
    }

    private void ApplyOutcome(ToolOutcome outcome)
    {
        switch (outcome)
        {
            case ToolOutcome.Idle:
                return;
            case ToolOutcome.Message message:
                _status.Text = message.Text;
                return;
            case ToolOutcome.Edit edit:
                var result = _controller.Apply(edit);
                Report(result.Report);
                if (!result.Changed) return;
                RefreshScene(resetCamera: false);
                _autosave.Start();
                return;
        }
    }

    private void RefreshScene(bool resetCamera)
    {
        if (_controller.Session is not { } session || _controller.Document is not { } document)
            return;
        var grid = VoxelDocumentEditing.ToGrid(document, session.Metrics);
        _viewport.ShowGrid(grid, session.Configuration.AssetProfiles, resetCamera);
        _scene.Text = $"Scene: {document.SceneId} · {grid.Count:N0} voxels";
        UpdateButtons();
    }

    private void Save()
    {
        var report = _controller.SaveScene();
        Report(report);
        UpdateButtons();
    }

    private void Undo()
    {
        Report(_controller.Undo());
        RefreshScene(resetCamera: false);
    }

    private void Redo()
    {
        Report(_controller.Redo());
        RefreshScene(resetCamera: false);
    }

    private void Export()
    {
        if (_controller.Session is not { } session || _controller.Scene is not { } scene) return;
        try
        {
            SceneExportResult result;
            var id = _exportFormat.GetItemId(_exportFormat.Selected);
            if (id == 100)
                result = SceneExport.Write(session, scene);
            else
                result = VoxelSceneExport.Write(session, scene, (VoxelExportFormat)id);
            _status.Text = $"Exported {result.Path}"
                + (result.Warnings.Count == 0 ? string.Empty : $" · {result.Warnings[0]}");
        }
        catch (Exception exception) when (exception is SceneMakerDocumentException or IOException)
        {
            Error(exception.Message);
        }
    }

    private void PopulateMaterials(WorkspaceConfiguration configuration)
    {
        _material.Clear();
        _materialKeys.Clear();
        foreach (var profile in configuration.AssetProfiles
            .Where(static profile => profile.Role == WorkspaceAssetRole.Terrain))
        {
            _material.AddItem(profile.DisplayName);
            _materialKeys.Add(profile.AssetKey);
        }
    }

    private void UpdateToolOptions()
    {
        _hillOptions.Visible = SelectedTool() == VoxelAuthoringTool.Hill;
        _pathOptions.Visible = SelectedTool() == VoxelAuthoringTool.Path;
    }

    private void UpdateButtons()
    {
        var hasScene = _controller.Scene is not null;
        _save.Disabled = !hasScene || _controller.IsDirty != true;
        _undo.Disabled = !_controller.CanUndo;
        _redo.Disabled = !_controller.CanRedo;
        _export.Disabled = !hasScene;
    }

    private void Report(EditorReport report)
    {
        if (report.HasMessage) _status.Text = report.Message;
        if (!report.Succeeded) Error(report.Message);
        UpdateButtons();
    }

    private void Error(string message)
    {
        _errorDialog.DialogText = message;
        _errorDialog.PopupCentered(new Vector2I(560, 180));
    }

    private VoxelAuthoringTool SelectedTool() =>
        (VoxelAuthoringTool)_tool.GetItemId(_tool.Selected);

    private string? SelectedMaterial() =>
        _material.Selected >= 0 && _material.Selected < _materialKeys.Count
            ? _materialKeys[_material.Selected]
            : null;

    private static void AddNumberField(GridContainer fields, string label, SpinBox input)
    {
        fields.AddChild(new Label { Text = label });
        fields.AddChild(input);
    }

    private static SpinBox Number(double minimum, double maximum, double step, double value) =>
        new()
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(110, 0),
        };
}
