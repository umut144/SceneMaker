using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The Workspace half of the editor's lifecycle, which used to be reachable
/// only by clicking through the Godot application.
/// </summary>
public sealed class EditorControllerWorkspaceTests
{
    [Fact]
    public void OpeningAWorkspaceDirectoryOpensItsSession()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(workspace.RootPath);

        Assert.True(report.Succeeded);
        Assert.Contains("test_world", report.Message, StringComparison.Ordinal);
        Assert.Equal("test_world", controller.Session?.WorkspaceKey);
    }

    [Fact]
    public void OpeningTheConfigFileOpensTheWorkspaceAroundIt()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(
            Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName));

        Assert.True(report.Succeeded);
        Assert.Equal("test_world", controller.Session?.WorkspaceKey);
    }

    [Fact]
    public void OpeningSomeOtherFileIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var other = Path.Combine(workspace.RootPath, "README.md");
        File.WriteAllText(other, "not a Workspace");
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(other);

        Assert.False(report.Succeeded);
        Assert.Contains("config.json file", report.Message, StringComparison.Ordinal);
        Assert.Null(controller.Session);
    }

    [Fact]
    public void OpeningADirectoryWithoutAConfigIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(
            Path.Combine(workspace.RootPath, WorkspaceStore.ScenesDirectoryName));

        Assert.False(report.Succeeded);
        Assert.Contains("no config.json", report.Message, StringComparison.Ordinal);
        Assert.Null(controller.Session);
    }

    [Fact]
    public void OpeningAPathThatIsNotThereIsRefused()
    {
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

        Assert.False(report.Succeeded);
        Assert.Contains("does not exist", report.Message, StringComparison.Ordinal);
    }

    /// <summary>This is the guarantee the whole session type exists for.</summary>
    [Fact]
    public void AFailedOpenLeavesTheWorkspaceThatIsAlreadyOpenUntouched()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);
        var opened = controller.Session;
        File.Delete(Path.Combine(
            workspace.RootPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName,
            PolyToolsCatalogImporter.CatalogFileName));

        var report = controller.OpenWorkspaceAt(workspace.RootPath);

        Assert.False(report.Succeeded);
        Assert.Same(opened, controller.Session);
    }

    [Fact]
    public void CreatingAWorkspaceDoesNotOpenItButPointsTheDialogsAtIt()
    {
        using var parent = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.CreateWorkspace(parent.RootPath, "fresh_world");

        Assert.True(report.Succeeded);
        Assert.Contains("Synchronize its PolyTools import", report.Message, StringComparison.Ordinal);
        Assert.Null(controller.Session);
        Assert.Equal(
            Path.Combine(parent.RootPath, "fresh_world"),
            controller.LastWorkspaceDirectory);
        Assert.True(File.Exists(Path.Combine(
            parent.RootPath, "fresh_world", WorkspaceConfigurationStore.FileName)));
    }

    [Fact]
    public void CreatingAWorkspaceOverAnExistingOneFailsAndKeepsTheOpenOne()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);
        var opened = controller.Session;

        var report = controller.CreateWorkspace(
            Path.GetDirectoryName(workspace.RootPath)!, "test_world");

        Assert.False(report.Succeeded);
        Assert.Same(opened, controller.Session);
    }

    [Fact]
    public void NarrowingTheAssetProfilesPersistsThemAndAdoptsTheNewSession()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);

        var report = controller.SaveAssetProfiles(
            [new WorkspaceAssetProfile("grass", "#99E550"), new WorkspaceAssetProfile("stone", "#808080")],
            openScene: null);

        Assert.True(report.Succeeded);
        Assert.Equal(
            ["grass"],
            controller.Session!.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        var reopened = WorkspaceSession.Load(workspace.RootPath);
        Assert.Equal(
            ["grass", "stone"],
            reopened.Configuration.AssetProfiles.Select(profile => profile.AssetKey));
    }

    [Fact]
    public void AProfileSetThatWouldOrphanTheOpenSceneIsRejectedAndWritesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);
        var opened = controller.Session;
        var configPath = Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName);
        var stored = File.ReadAllText(configPath);
        var scene = TestScenes.Instance(workspace);

        // The Scene is covered in grass, so dropping grass would orphan it.
        var report = controller.SaveAssetProfiles(
            [new WorkspaceAssetProfile("sand", "#E5C07B")],
            scene);

        Assert.False(report.Succeeded);
        Assert.Same(opened, controller.Session);
        Assert.Equal(stored, File.ReadAllText(configPath));
    }

    [Fact]
    public void ClosingTheWorkspaceLeavesTheDialogsPointingAtIt()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);

        controller.CloseWorkspace();

        Assert.Null(controller.Session);
        Assert.Equal(Path.GetFullPath(workspace.RootPath), controller.LastWorkspaceDirectory);
    }
}
