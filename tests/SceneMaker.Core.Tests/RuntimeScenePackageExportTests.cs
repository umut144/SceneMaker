using System.Text.Json.Nodes;
using MMORPG.Simulation.Scenes;
using MMORPG.Simulation.WorldAssets;
using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RuntimeScenePackageExportTests
{
    private static string TerrainDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "terrain_display.json");

    private static string PlacementDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "placement_display.json");

    private static string TransitionDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "transition_display.json");

    private static string CatalogPath => Path.Combine(
        AppContext.BaseDirectory,
        "world_assets",
        "world_assets.json");

    [Fact]
    public void ExporterCreatesCanonicalRoleNeutralPackageFromEveryScene()
    {
        var catalogs = LoadCatalogs();
        var dungeon = TerrainEditing.Paint(
            SceneDocument.Create("dungeon", 20, 20),
            catalogs.Terrain,
            1,
            2,
            1003);
        var start = BuildStartupScene(catalogs);

        var package = RuntimeScenePackageExporter.Create(
            [start, dungeon],
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            catalogs.WorldAssets);
        var json = RuntimeScenePackageLoader.Serialize(package, catalogs.WorldAssets);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(
            ["schema", "version", "coordinate_space", "scenes"],
            root.Select(static property => property.Key));
        Assert.Equal(["dungeon", "world_start"], package.Scenes.Select(static scene => scene.SceneId));
        Assert.DoesNotContain("workspace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("startup_scene", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("color", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("footprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meters", json, StringComparison.OrdinalIgnoreCase);

        var validated = RuntimeScenePackageValidator.Validate(package, catalogs.WorldAssets);
        Assert.Equal("world_start", validated.StartupScene.SceneId);
        var tree = Assert.Single(validated.StartupScene.Placements);
        Assert.Equal(200, tree.Source.PositionAuthoringPx.X);
        Assert.Equal(100, tree.Source.PositionAuthoringPx.Y);
        Assert.Equal(6.25m, tree.AnchorXMeters);
        Assert.Equal(3.125m, tree.AnchorYMeters);
        var portal = Assert.Single(validated.Portals);
        Assert.Equal(32, portal.Source.PositionAuthoringPx.X);
        Assert.Equal(0, portal.Source.PositionAuthoringPx.Y);
    }

    [Fact]
    public void WorkspaceExportIsByteStableAndNeverMutatesEditableScenes()
    {
        using var temporary = TemporaryDirectory.Create();
        var catalogs = LoadCatalogs();
        var workspace = WorkspaceStore.Create(temporary.Path, "runtime_workspace");
        var dungeon = SceneStore.Create(workspace, "dungeon", 20, 20);
        dungeon = dungeon with
        {
            Document = TerrainEditing.Paint(
                dungeon.Document,
                catalogs.Terrain,
                1,
                2,
                1003),
        };
        SceneStore.Save(workspace, dungeon);
        var start = SceneStore.Create(workspace, "world_start", 40, 40) with
        {
            Document = BuildStartupScene(catalogs),
        };
        SceneStore.Save(workspace, start);
        var editableBefore = Directory.EnumerateFiles(
                workspace.ScenesDirectoryPath,
                $"*{SceneStore.FileSuffix}")
            .ToDictionary(
                static path => path,
                File.ReadAllBytes,
                StringComparer.Ordinal);

        var path = RuntimeScenePackageStore.Export(
            workspace,
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            CatalogPath);
        var firstBytes = File.ReadAllBytes(path);
        var loaded = RuntimeScenePackageLoader.Load(path, CatalogPath);
        _ = RuntimeScenePackageStore.Export(
            workspace,
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            CatalogPath);

        Assert.Equal(
            Path.Combine(
                workspace.DirectoryPath,
                RuntimeScenePackageStore.ExportsDirectoryName,
                workspace.Document.WorkspaceId + RuntimeScenePackageStore.FileSuffix),
            path);
        Assert.Equal(firstBytes, File.ReadAllBytes(path));
        Assert.Equal(2, loaded.Scenes.Count);
        Assert.Equal("world_start", loaded.StartupScene.SceneId);
        foreach (var (scenePath, bytes) in editableBefore)
        {
            Assert.Equal(bytes, File.ReadAllBytes(scenePath));
        }
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(path)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));

        var deploymentDirectory = Path.Combine(temporary.Path, "game", "runtime_scenes");
        var deployedPath = RuntimeScenePackageStore.Export(
            workspace,
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            CatalogPath,
            deploymentDirectory);
        Assert.Equal(
            Path.Combine(deploymentDirectory, "runtime_workspace.runtime-scenes.json"),
            deployedPath);
        Assert.Equal(firstBytes, File.ReadAllBytes(deployedPath));
    }

    [Fact]
    public void WorkspaceExportReadsTemplatesFromTheirSeparateDirectory()
    {
        using var temporary = TemporaryDirectory.Create();
        var catalogs = LoadCatalogs();
        var workspace = WorkspaceStore.Create(temporary.Path, "template_workspace");
        var start = SceneStore.Create(workspace, "world_start", 40, 40) with
        {
            Document = BuildStartupScene(catalogs),
        };
        SceneStore.Save(workspace, start);
        _ = SceneStore.Create(
            workspace,
            "forest_patch",
            4,
            4,
            SceneKind.Template,
            templateGroupNumber: 1);

        var path = RuntimeScenePackageStore.Export(
            workspace,
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            CatalogPath);
        var loaded = RuntimeScenePackageLoader.Load(path, CatalogPath);

        Assert.Single(loaded.Scenes);
        Assert.Single(loaded.Templates);
        Assert.Equal("forest_patch", loaded.Templates.Single().Key);
    }

    [Fact]
    public void ExportRejectsMissingOrMultiplePortalBearingScenes()
    {
        var catalogs = LoadCatalogs();
        var withoutPortal = SceneDocument.Create("dungeon", 20, 20);
        var secondPortalScene = TransitionEditing.Place(
            TerrainEditing.Fill(
                SceneDocument.Create("second_start", 20, 20),
                catalogs.Terrain,
                0,
                0,
                1003),
            catalogs.Placements,
            catalogs.Transitions,
            32,
            0,
            503);

        var missing = Assert.Throws<SceneMakerDocumentException>(() =>
            RuntimeScenePackageExporter.Create(
                [withoutPortal],
                catalogs.Terrain,
                catalogs.Placements,
                catalogs.Transitions,
                catalogs.WorldAssets));
        var multiple = Assert.Throws<SceneMakerDocumentException>(() =>
            RuntimeScenePackageExporter.Create(
                [BuildStartupScene(catalogs), secondPortalScene],
                catalogs.Terrain,
                catalogs.Placements,
                catalogs.Transitions,
                catalogs.WorldAssets));

        Assert.Contains("none was found", missing.Message);
        Assert.Contains("multiple Scenes", multiple.Message);
    }

    [Fact]
    public void ExportRejectsCanonicalAssetsNotEnabledInSceneMaker()
    {
        var catalogs = LoadCatalogs();
        var unsupported = BuildStartupScene(catalogs) with
        {
            TerrainCells =
            [
                new TerrainCellDocument { X = 0, Y = 0, AssetId = 1002 },
            ],
        };

        var error = Assert.Throws<SceneMakerDocumentException>(() =>
            RuntimeScenePackageExporter.Create(
                [unsupported],
                catalogs.Terrain,
                catalogs.Placements,
                catalogs.Transitions,
                catalogs.WorldAssets));

        Assert.Contains("not enabled in SceneMaker", error.Message);
    }

    [Fact]
    public void VersionThreeExportPreservesTemplatesAndTemplateAnchors()
    {
        var catalogs = LoadCatalogs();
        var template = SceneDocument.Create(
            "forest_patch",
            4,
            4,
            SceneKind.Template);
        var instance = TemplateEditing.PlaceAnchor(
            BuildStartupScene(catalogs),
            160,
            160,
            1);

        var package = RuntimeScenePackageExporter.Create(
            [instance, template],
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            catalogs.WorldAssets);

        Assert.Equal(RuntimeScenePackageContract.Version, package.Version);
        var exportedInstance = package.Scenes.Single(scene => scene.SceneId == "world_start");
        var exportedTemplate = package.Scenes.Single(scene => scene.SceneId == "forest_patch");
        Assert.Equal(RuntimeSceneKind.Instance, exportedInstance.SceneKind);
        Assert.Single(exportedInstance.TemplateAnchors);
        Assert.Null(exportedInstance.TemplateDefinition);
        Assert.Equal(RuntimeSceneKind.Template, exportedTemplate.SceneKind);
        Assert.Equal(1, exportedTemplate.TemplateDefinition!.GroupNumber);
        Assert.Empty(exportedTemplate.TemplateAnchors);
    }

    [Fact]
    public void ValidatorCollectsCoverageIssuesAndFailedExportPreservesLastValidPackage()
    {
        using var temporary = TemporaryDirectory.Create();
        var catalogs = LoadCatalogs();
        var workspace = WorkspaceStore.Create(temporary.Path, "coverage_workspace");
        var loaded = SceneStore.Create(workspace, "world_start", 40, 40) with
        {
            Document = BuildStartupScene(catalogs),
        };
        SceneStore.Save(workspace, loaded);
        var exportPath = RuntimeScenePackageStore.Export(
            workspace,
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions,
            CatalogPath);
        var validBytes = File.ReadAllBytes(exportPath);

        var uncovered = TerrainEditing.Erase(loaded.Document, 0, 0);
        uncovered = TerrainEditing.Erase(uncovered, 9, 6);
        loaded = loaded with { Document = uncovered };
        SceneStore.Save(workspace, loaded);
        var issues = RuntimeScenePackageExportValidator.Validate(
            [uncovered],
            catalogs.Terrain,
            catalogs.Placements,
            catalogs.Transitions);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, static issue =>
            issue.Message.Contains("Placement 'tree_0001'", StringComparison.Ordinal)
            && issue.Message.Contains("y=6: x=9", StringComparison.Ordinal));
        Assert.Contains(issues, static issue =>
            issue.Message.Contains("Transition 'portal_0001'", StringComparison.Ordinal)
            && issue.Message.Contains("y=0: x=0", StringComparison.Ordinal));
        var error = Assert.Throws<SceneMakerDocumentException>(() =>
            RuntimeScenePackageStore.Export(
                workspace,
                catalogs.Terrain,
                catalogs.Placements,
                catalogs.Transitions,
                CatalogPath));
        Assert.Contains("tree_0001", error.Message);
        Assert.Contains("portal_0001", error.Message);
        Assert.Equal(validBytes, File.ReadAllBytes(exportPath));
    }

    private static SceneDocument BuildStartupScene(Catalogs catalogs)
    {
        var scene = TerrainEditing.Fill(
            SceneDocument.Create("world_start", 40, 40),
            catalogs.Terrain,
            0,
            0,
            1003);
        scene = PlacementEditing.Place(
            scene,
            catalogs.Placements,
            catalogs.Transitions,
            200,
            100,
            2);
        return TransitionEditing.Place(
            scene,
            catalogs.Placements,
            catalogs.Transitions,
            32,
            0,
            503);
    }

    private static Catalogs LoadCatalogs() => new(
        TerrainDisplayCatalogLoader.Load(TerrainDisplayPath, CatalogPath),
        PlacementDisplayCatalogLoader.Load(PlacementDisplayPath, CatalogPath),
        TransitionDisplayCatalogLoader.Load(TransitionDisplayPath, CatalogPath),
        WorldAssetCatalogLoader.Load(CatalogPath));

    private sealed record Catalogs(
        TerrainDisplayCatalog Terrain,
        PlacementDisplayCatalog Placements,
        TransitionDisplayCatalog Transitions,
        LoadedWorldAssetCatalog WorldAssets);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"scene-maker-runtime-package-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
