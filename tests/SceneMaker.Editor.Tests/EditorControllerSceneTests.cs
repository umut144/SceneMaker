using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The Scene half of the editor's lifecycle: creating, opening, editing, undo
/// and saving. All of it used to be reachable only by clicking.
/// </summary>
public sealed class EditorControllerSceneTests
{
    [Fact]
    public void ACreatedInstanceIsOpenAndClean()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);

        var report = controller.CreateInstance("base", 6, 6);

        Assert.True(report.Succeeded);
        Assert.Equal("Created Scene Instance 'base'.", report.Message);
        Assert.Equal("base", controller.Document?.SceneId);
        Assert.False(controller.IsDirty);
        Assert.False(controller.CanUndo);
        Assert.False(controller.CanRedo);
    }

    [Fact]
    public void SceneCreationSnapsItsGroundHeightToTheWorkspaceQuantum()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);

        var report = controller.CreateInstance("base", 6, 6, 1.1m);

        Assert.True(report.Succeeded);
        Assert.Equal(1.125m, controller.Document?.DefaultElevationMeters);
        Assert.Equal(
            1.125m,
            SceneStore.Load(controller.Session!.Workspace, controller.Scene!.FilePath)
                .Document.DefaultElevationMeters);
    }

    [Fact]
    public void ACreatedTemplateLandsInTheTemplatesDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);

        var report = controller.CreateTemplate("grove", 2, 2, 3, 0, 0);

        Assert.True(report.Succeeded);
        Assert.Equal(SceneKind.Template, controller.Document?.SceneKind);
        Assert.Equal(3, controller.Document?.TemplateDefinition?.GroupNumber);
        Assert.True(File.Exists(Path.Combine(
            workspace.RootPath, WorkspaceStore.TemplatesDirectoryName, "grove.scene.json")));
    }

    [Fact]
    public void CreatingASceneThatIsAlreadyThereIsRefusedAndKeepsTheOpenOne()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        var open = controller.Scene;

        var report = controller.CreateInstance("base", 6, 6);

        Assert.False(report.Succeeded);
        Assert.Same(open, controller.Scene);
    }

    [Fact]
    public void AnEditIsUndoableAndLeavesTheSceneDirty()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        var edit = controller.Apply(Paint(workspace, 0, 0));

        Assert.True(edit.Changed);
        Assert.Equal("painted", edit.Report.Message);
        Assert.Empty(edit.Before!.TerrainCells);
        Assert.Single(edit.After!.TerrainCells);
        Assert.True(controller.IsDirty);
        Assert.True(controller.CanUndo);
    }

    [Fact]
    public void OneStrokeCollapsesIntoOneUndoStep()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        controller.Apply(Paint(workspace, 0, 0, stroke: "paint"));
        controller.Apply(Paint(workspace, 1, 0, stroke: "paint"));
        controller.Apply(Paint(workspace, 2, 0, stroke: "paint"));
        Assert.Equal(3, controller.Document?.TerrainCells.Count);

        Assert.True(controller.Undo().Succeeded);

        Assert.Empty(controller.Document!.TerrainCells);
        Assert.False(controller.CanUndo);
    }

    [Fact]
    public void EndingTheStrokeStartsANewUndoStep()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        controller.Apply(Paint(workspace, 0, 0, stroke: "paint"));
        controller.EndEditStroke();
        controller.Apply(Paint(workspace, 1, 0, stroke: "paint"));

        controller.Undo();

        Assert.Single(controller.Document!.TerrainCells);
        Assert.True(controller.CanUndo);
        Assert.True(controller.CanRedo);
    }

    [Fact]
    public void RedoPutsTheEditBack()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.Apply(Paint(workspace, 0, 0));
        controller.Undo();

        var report = controller.Redo();

        Assert.True(report.Succeeded);
        Assert.Contains("Redo.", report.Message, StringComparison.Ordinal);
        Assert.Single(controller.Document!.TerrainCells);
    }

    [Fact]
    public void ThereIsNothingToUndoOnAFreshScene()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        Assert.Equal("Nothing to undo.", controller.Undo().Message);
        Assert.Equal("Nothing to redo.", controller.Redo().Message);
    }

    [Fact]
    public void AnEditThatThrowsIsReportedAndChangesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        var open = controller.Scene;

        var edit = controller.Apply(new ToolOutcome.Edit(
            "Paint", _ => throw new SceneMakerDocumentException("outside the map")));

        Assert.False(edit.Changed);
        Assert.False(edit.Report.Succeeded);
        Assert.Equal("Paint blocked: outside the map", edit.Report.Message);
        Assert.Same(open, controller.Scene);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void AnEditThatChangesNothingSaysSoWithoutTouchingTheHistory()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        var edit = controller.Apply(new ToolOutcome.Edit(
            "Erase", document => document, NoChangeText: "Nothing to erase there."));

        Assert.False(edit.Changed);
        Assert.Equal("Nothing to erase there.", edit.Report.Message);
        Assert.False(controller.CanUndo);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void SavingWritesTheSceneAndClearsTheDirtyMark()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.Apply(Paint(workspace, 0, 0));

        Assert.True(controller.SaveScene().Succeeded);

        Assert.False(controller.IsDirty);
        var stored = SceneStore.Load(controller.Session!.Workspace, controller.Scene!.FilePath);
        Assert.Single(stored.Document.TerrainCells);
    }

    [Fact]
    public void SavingAgainSaysNothingBecauseThereIsNothingToWrite()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        var report = controller.SaveScene();

        Assert.True(report.Succeeded);
        Assert.False(report.HasMessage);
    }

    [Fact]
    public void OpeningAnotherSceneWritesOutThePendingEditFirst()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("first", 6, 6);
        var firstPath = controller.Scene!.FilePath;
        controller.Apply(Paint(workspace, 0, 0));
        controller.CreateInstance("second", 6, 6);

        Assert.True(controller.OpenScene(firstPath).Succeeded);

        Assert.Equal("first", controller.Document?.SceneId);
        Assert.Single(controller.Document!.TerrainCells);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void TheRecentSessionBringsBackTheWorkspaceAndTheScene()
    {
        using var workspace = TestWorkspace.Create();
        var recentPath = Path.Combine(workspace.RootPath, "recent.json");
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.Apply(Paint(workspace, 0, 0));
        controller.SaveScene();
        controller.SaveRecentSession(recentPath);

        var restored = new EditorController();
        var report = restored.RestoreRecentSession(recentPath);

        Assert.True(report.Succeeded);
        Assert.Contains("and Scene 'base'", report.Message, StringComparison.Ordinal);
        Assert.Equal("test_world", restored.Session?.WorkspaceKey);
        Assert.Single(restored.Document!.TerrainCells);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public void ARecentSessionThatNoLongerFitsLeavesTheEditorClosed()
    {
        using var workspace = TestWorkspace.Create();
        var recentPath = Path.Combine(workspace.RootPath, "recent.json");
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.Apply(Paint(workspace, 0, 0));
        controller.SaveScene();
        controller.SaveRecentSession(recentPath);
        // Grass is gone from the Workspace, so the stored Scene names an Asset
        // that is no longer enabled. The Scene has to be closed first, because
        // the controller refuses a profile set that would orphan an open Scene.
        controller.CloseScene();
        Assert.True(
            controller.SaveAssetProfiles([new WorkspaceAssetProfile(
                "sand", "Sand", WorkspaceAssetRole.Terrain,
                "#E5C07B", "sand", TerrainAuthoring.Cells)]).Succeeded);

        var restored = new EditorController();
        var report = restored.RestoreRecentSession(recentPath);

        Assert.False(report.Succeeded);
        Assert.Equal("No compatible recent session was restored.", report.Message);
        Assert.Null(restored.Session);
        Assert.Null(restored.Scene);
    }

    [Fact]
    public void NoRecentSessionIsNotAFailure()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.RestoreRecentSession(
            Path.Combine(workspace.RootPath, "absent.json"));

        Assert.True(report.Succeeded);
        Assert.False(report.HasMessage);
    }

    [Fact]
    public void AStoredSceneIsAdoptedOnlyWhenItIsTheOpenOne()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        var open = controller.Scene!;
        var elsewhere = new LoadedScene(
            Path.Combine(workspace.RootPath, "scenes", "other.scene.json"), open.Document);

        Assert.False(controller.AdoptStoredScene(elsewhere));
        Assert.True(controller.AdoptStoredScene(open with { Document = open.Document }));
    }

    private static EditorController Opened(TestWorkspace workspace)
    {
        var controller = new EditorController();
        Assert.True(controller.OpenWorkspaceAt(workspace.RootPath).Succeeded);
        return controller;
    }

    private static ToolOutcome.Edit Paint(
        TestWorkspace workspace,
        int cellX,
        int cellY,
        string? stroke = null) =>
        new(
            "Paint",
            document => TerrainEditing.Paint(document, workspace.Terrain, cellX, cellY, "grass"),
            StrokeKey: stroke,
            Describe: (_, _) => "painted");
}
