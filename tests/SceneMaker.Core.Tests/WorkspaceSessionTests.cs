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
        Assert.Equal(
            ["portal", "stone"],
            session.Catalog.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(session.Configuration.Metrics, session.Metrics);
    }

    [Fact]
    public void AnUnrequestedBrokenManifestCannotBlockTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var grassManifest = Path.Combine(
            workspace.RootPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName,
            "PolyToolsRuntimeExports",
            "grass",
            "manifest.json");
        File.WriteAllText(
            grassManifest,
            File.ReadAllText(grassManifest).Replace(
                "\"schema_version\": 16",
                "\"schema_version\": 15",
                StringComparison.Ordinal));

        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.Equal(["portal", "stone"], session.PropAssets.Assets.Select(asset => asset.AssetKey));
        Assert.Equal(["grass", "river", "sand"], session.TerrainAssets.Assets.Select(asset => asset.AssetKey));
    }

    [Fact]
    public void AWorkspaceWithoutPlacementsNeedsNoPolyToolsImport()
    {
        using var workspace = TestWorkspace.Create();
        var terrainOnly = workspace.Configuration.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "bog", "Bog", WorkspaceAssetRole.Terrain,
                "#516B3A", "mud", TerrainAuthoring.Cells)]);
        WorkspaceConfigurationStore.Save(workspace.RootPath, terrainOnly);
        Directory.Delete(
            Path.Combine(workspace.RootPath, PolyToolsCatalogImporter.ImportDirectoryName),
            recursive: true);

        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.Equal("Bog", session.TerrainAssets.Resolve("bog").Name);
        Assert.Empty(session.PropAssets.Assets);
        Assert.Empty(session.Catalog.Assets);
    }

    [Fact]
    public void APlacementCanUseGeometryFromAnyPolyToolsAssetType()
    {
        using var workspace = TestWorkspace.Create();
        var grassPlacement = workspace.Configuration.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "grass", "Grass Token", WorkspaceAssetRole.Placement, "#99E550",
                PolyToolsAssetId: "asset_grass")]);
        WorkspaceConfigurationStore.Save(workspace.RootPath, grassPlacement);

        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.Equal("Grass Token", session.PropAssets.Resolve("grass").Name);
        Assert.Equal(["grass"], session.Catalog.Assets.Select(asset => asset.AssetKey));
    }

    /// <summary>
    /// Narrowing the enabled Assets is an ordinary edit. If it could take the
    /// bridge kit's plank away and shut the Workspace with it, an author could
    /// edit their world closed and have no way back in - so the kit goes away
    /// and says why, and the Workspace still opens.
    /// </summary>
    [Fact]
    public void NarrowingTheAssetsCostsTheKitRatherThanTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var withoutThePlank = workspace.Configuration.WithAssetProfiles(
            [new WorkspaceAssetProfile(
                "grass", "Grass Token", WorkspaceAssetRole.Placement, "#99E550",
                PolyToolsAssetId: "asset_grass")]);
        WorkspaceConfigurationStore.Save(workspace.RootPath, withoutThePlank);

        var session = WorkspaceSession.Load(workspace.RootPath);

        var unavailable = Assert.IsType<BridgeKitResolution.Unavailable>(session.BridgeKit);
        Assert.Contains("the role 'plank'", unavailable.Reason, StringComparison.Ordinal);
        Assert.Contains("enables under no name", unavailable.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same holds for what PolyTools publishes: a Set SceneMaker cannot read
    /// as a kit is their problem to fix and no reason to keep the author out of
    /// a world full of Terrain that has nothing to do with bridges.
    /// </summary>
    [Fact]
    public void APolyToolsSetThatIsNoKitCostsTheKitRatherThanTheWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        File.Delete(Path.Combine(
            workspace.RootPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName,
            "PolyToolsRuntimeExports",
            "bridge",
            "manifest.json"));

        var session = WorkspaceSession.Load(workspace.RootPath);

        Assert.NotEmpty(session.PropAssets.Assets);
        Assert.IsType<BridgeKitResolution.Unavailable>(session.BridgeKit);
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
    public void LoadingAWorkspaceDoesNotRequireAnyGame()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(workspace.Game.DirectoryPath, recursive: true);

        // A World is ordinary with no Game yet - loading it is not an error.
        var session = WorkspaceSession.Load(workspace.RootPath);
        Assert.Equal("test_world", session.WorkspaceKey);
    }

    [Fact]
    public void LoadingAGameReportsItsMissingScenesDirectory()
    {
        using var workspace = TestWorkspace.Create();
        Directory.Delete(Path.Combine(workspace.Game.DirectoryPath, GameStore.ScenesDirectoryName));

        Assert.Throws<SceneMakerDocumentException>(
            () => GameStore.Load(workspace.Workspace, TestWorkspace.DefaultGameKey));
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
                    "stone", "Stone", WorkspaceAssetRole.Placement, "#808080",
                    PolyToolsAssetId: "asset_stone"),
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
                "nowhere", "Nowhere", WorkspaceAssetRole.Placement, "#FFFFFF",
                PolyToolsAssetId: "asset_nowhere")]));
    }
}
