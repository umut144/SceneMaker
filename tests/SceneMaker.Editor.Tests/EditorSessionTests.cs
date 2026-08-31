using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The session is what makes a half-opened Workspace unrepresentable: it either
/// loads completely or throws, and it never mutates itself in place.
/// </summary>
public sealed class EditorSessionTests
{
    [Fact]
    public void LoadingAWorkspaceDerivesEveryCatalogFromTheSameConfiguration()
    {
        using var workspace = TestWorkspace.Create();

        var session = EditorSession.Load(workspace.RootPath);

        Assert.Equal("test_world", session.WorkspaceKey);
        Assert.Equal(Path.GetFullPath(workspace.RootPath), session.DirectoryPath);
        Assert.Equal(
            ["grass", "sand"],
            session.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(
            ["portal", "stone"],
            session.PropAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(4, session.Catalog.Assets.Count);
        Assert.Equal(session.Configuration.Metrics, session.Metrics);
    }

    [Fact]
    public void LoadingReportsAMissingPolyToolsImportInsteadOfOpeningHalfAWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(
            Path.Combine(workspace.RootPath, PolyToolsCatalogImporter.ImportDirectoryName),
            recursive: true);

        Assert.Throws<SceneMakerDocumentException>(() => EditorSession.Load(workspace.RootPath));
    }

    [Fact]
    public void LoadingReportsAMissingConfiguration()
    {
        using var workspace = TestWorkspace.Create();
        File.Delete(Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName));

        Assert.Throws<SceneMakerDocumentException>(() => EditorSession.Load(workspace.RootPath));
    }

    [Fact]
    public void LoadingReportsAWorkspaceWithoutItsScenesDirectory()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(Path.Combine(workspace.RootPath, WorkspaceStore.ScenesDirectoryName));

        Assert.Throws<SceneMakerDocumentException>(() => EditorSession.Load(workspace.RootPath));
    }

    [Fact]
    public void NarrowingTheAssetProfilesDerivesANewSessionAndLeavesTheOldOneIntact()
    {
        using var workspace = TestWorkspace.Create();
        var session = EditorSession.Load(workspace.RootPath);

        var narrowed = session.WithAssetProfiles(
            [new WorkspaceAssetProfile("grass", "#99E550"), new WorkspaceAssetProfile("stone", "#808080")]);

        Assert.Equal(["grass"], narrowed.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["stone"], narrowed.PropAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["grass", "sand"], session.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["portal", "stone"], session.PropAssets.Assets.Select(asset => asset.AssetKey));
    }

    [Fact]
    public void DerivingAssetProfilesWritesNothingToTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var configPath = Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName);
        var stored = File.ReadAllText(configPath);
        var session = EditorSession.Load(workspace.RootPath);

        _ = session.WithAssetProfiles([new WorkspaceAssetProfile("grass", "#99E550")]);

        Assert.Equal(stored, File.ReadAllText(configPath));
    }

    [Fact]
    public void DerivingRejectsAnAssetTheCatalogDoesNotKnow()
    {
        using var workspace = TestWorkspace.Create();
        var session = EditorSession.Load(workspace.RootPath);

        Assert.Throws<SceneMakerDocumentException>(() =>
            session.WithAssetProfiles([new WorkspaceAssetProfile("nowhere", "#FFFFFF")]));
    }
}
