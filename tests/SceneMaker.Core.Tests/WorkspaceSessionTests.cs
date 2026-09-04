using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The session is what makes a half-opened Workspace unrepresentable: it either
/// loads completely or throws, and it never mutates itself in place.
/// </summary>
public sealed class WorkspaceSessionTests
{
    [Fact]
    public void LoadingAWorkspaceDerivesEveryCatalogFromTheSameConfiguration()
    {
        using var workspace = TestWorkspace.Create();

        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.Equal("test_world", session.WorkspaceKey);
        Assert.Equal(Path.GetFullPath(workspace.RootPath), session.DirectoryPath);
        Assert.Equal(
            ["grass", "river", "sand"],
            session.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(
            ["portal", "stone"],
            session.PropAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal("Water", session.TerrainAssets.Resolve("river").Name);
        Assert.Equal("River", session.Catalog.Resolve("river").Name);
        Assert.Equal(5, session.Catalog.Assets.Count);
        Assert.Equal(session.Configuration.Metrics, session.Metrics);
    }

    [Fact]
    public void SceneMakerCanAuthorTerrainThatHasNoPolyToolsAsset()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);

        var derived = session.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "bog", "Bog", WorkspaceAssetRole.Terrain,
                "#516B3A", "mud", TerrainAuthoring.Cells)]);

        Assert.Equal("Bog", derived.TerrainAssets.Resolve("bog").Name);
        Assert.Empty(derived.PropAssets.Assets);
    }

    [Fact]
    public void SceneMakerRoleDoesNotComeFromThePolyToolsAssetType()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);

        var derived = session.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "portal", "Portal Floor", WorkspaceAssetRole.Terrain,
                "#8E6CFF", "land", TerrainAuthoring.Cells)]);

        Assert.Equal("Portal Floor", derived.TerrainAssets.Resolve("portal").Name);
        Assert.Empty(derived.PropAssets.Assets);
    }

    [Fact]
    public void LoadingReportsAMissingPolyToolsImportInsteadOfOpeningHalfAWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(
            Path.Combine(workspace.RootPath, PolyToolsCatalogImporter.ImportDirectoryName),
            recursive: true);

        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceSession.Load(workspace.RootPath));
    }

    [Fact]
    public void LoadingReportsAMissingConfiguration()
    {
        using var workspace = TestWorkspace.Create();
        File.Delete(Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName));

        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceSession.Load(workspace.RootPath));
    }

    [Fact]
    public void LoadingReportsAWorkspaceWithoutItsScenesDirectory()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(Path.Combine(workspace.RootPath, WorkspaceStore.ScenesDirectoryName));

        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceSession.Load(workspace.RootPath));
    }

    [Fact]
    public void NarrowingTheAssetProfilesDerivesANewSessionAndLeavesTheOldOneIntact()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);

        var narrowed = session.WithAssetProfiles(
            [
                new WorkspaceAssetProfile(
                    "grass", "Grass", WorkspaceAssetRole.Terrain,
                    "#99E550", "land", TerrainAuthoring.Cells),
                new WorkspaceAssetProfile(
                    "stone", "Stone", WorkspaceAssetRole.Placement, "#808080"),
            ]);

        Assert.Equal(["grass"], narrowed.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["stone"], narrowed.PropAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["grass", "river", "sand"], session.TerrainAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["portal", "stone"], session.PropAssets.Assets.Select(asset => asset.AssetKey));
    }

    [Fact]
    public void DerivingAssetProfilesWritesNothingToTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var configPath = Path.Combine(workspace.RootPath, WorkspaceConfigurationStore.FileName);
        var stored = File.ReadAllText(configPath);
        var session = WorkspaceSession.Load(workspace.RootPath);

        _ = session.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "grass", "Grass", WorkspaceAssetRole.Terrain,
                "#99E550", "land", TerrainAuthoring.Cells)]);

        Assert.Equal(stored, File.ReadAllText(configPath));
    }

    [Fact]
    public void DerivingRejectsAPlacementWhoseGeometryTheCatalogDoesNotKnow()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.Throws<SceneMakerDocumentException>(() =>
            session.WithAssetProfiles([new WorkspaceAssetProfile(
                "nowhere", "Nowhere", WorkspaceAssetRole.Placement, "#FFFFFF")]));
    }
}
