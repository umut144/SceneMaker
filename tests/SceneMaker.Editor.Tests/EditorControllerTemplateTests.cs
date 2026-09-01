using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// Template Previews, the Template listing and the export. The preview is
/// transient by design, so most of what matters here is when it disappears.
/// </summary>
public sealed class EditorControllerTemplateTests
{
    [Fact]
    public void TheTemplateListingHoldsOnlyTemplates()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.CreateTemplate("grove", 2, 2, 1, 0, 0);
        controller.CreateTemplate("rocks", 2, 2, 2, 0, 0);

        Assert.True(controller.ReloadTemplates().Succeeded);

        Assert.Equal(
            ["grove", "rocks"],
            controller.Templates.Select(scene => scene.Document.SceneId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ChangingATemplateGroupRewritesItAndUpdatesTheListing()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateTemplate("grove", 2, 2, 1, 0, 0);
        var path = controller.Scene!.FilePath;
        controller.ReloadTemplates();

        var report = controller.SetTemplateGroup(path, 7);

        Assert.True(report.Succeeded);
        Assert.Equal("Assigned Scene Template 'grove' to group 7.", report.Message);
        Assert.Equal(7, controller.Templates.Single().Document.TemplateDefinition?.GroupNumber);
        var stored = SceneStore.Load(controller.Session!.Workspace, path);
        Assert.Equal(7, stored.Document.TemplateDefinition?.GroupNumber);
    }

    [Fact]
    public void ChangingTheGroupOfTheOpenTemplateBringsTheEditorAlong()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateTemplate("grove", 2, 2, 1, 0, 0);

        controller.SetTemplateGroup(controller.Scene!.FilePath, 4);

        Assert.Equal(4, controller.Document?.TemplateDefinition?.GroupNumber);
        Assert.False(controller.IsDirty);
        Assert.False(controller.CanUndo);
    }

    [Fact]
    public void APreviewNeedsAnOpenInstance()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateTemplate("grove", 2, 2, 1, 0, 0);

        var report = controller.GenerateTemplatePreview(seed: 1);

        Assert.False(report.Succeeded);
        Assert.Null(controller.TemplatePreview);
    }

    [Fact]
    public void APreviewWithoutAnchorsSaysSo()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);

        var report = controller.GenerateTemplatePreview(seed: 1);

        Assert.True(report.Succeeded);
        Assert.Contains("no Template Anchors", report.Message, StringComparison.Ordinal);
        Assert.NotNull(controller.TemplatePreview);
    }

    [Fact]
    public void AnyEditThrowsThePreviewAway()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.GenerateTemplatePreview(seed: 1);
        Assert.NotNull(controller.TemplatePreview);

        controller.Apply(Paint(workspace));

        Assert.Null(controller.TemplatePreview);
    }

    [Fact]
    public void UndoThrowsThePreviewAway()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        controller.Apply(Paint(workspace));
        controller.GenerateTemplatePreview(seed: 1);

        controller.Undo();

        Assert.Null(controller.TemplatePreview);
    }

    [Fact]
    public void ExportingWritesTheSnapshotBesideTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 1, 1);
        controller.Apply(Paint(workspace));

        var report = controller.ExportScene();

        Assert.True(report.Succeeded);
        Assert.True(File.Exists(Path.Combine(
            workspace.RootPath, SceneExport.DirectoryName, "base" + SceneExport.FileSuffix)));
        // Exporting writes the Scene out first, so the two never disagree.
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void ExportingWithoutASceneIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);

        var report = controller.ExportScene();

        Assert.False(report.Succeeded);
        Assert.Equal("Load a Scene before exporting.", report.Message);
    }

    [Fact]
    public void ExportingASceneWithUncoveredPropsIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var controller = Opened(workspace);
        controller.CreateInstance("base", 6, 6);
        // A Prop on a cell that has no Terrain under it.
        controller.Apply(new ToolOutcome.Edit(
            "Place",
            document => PropEditing.Place(document, workspace.Props, 0, 0, "stone")));

        var report = controller.ExportScene();

        Assert.False(report.Succeeded);
        Assert.Contains("Terrain is missing", report.Message, StringComparison.Ordinal);
    }

    private static EditorController Opened(TestWorkspace workspace)
    {
        var controller = new EditorController();
        Assert.True(controller.OpenWorkspaceAt(workspace.RootPath).Succeeded);
        return controller;
    }

    private static ToolOutcome.Edit Paint(TestWorkspace workspace) =>
        new("Paint", document => TerrainEditing.Paint(document, workspace.Terrain, 0, 0, "grass"));
}
