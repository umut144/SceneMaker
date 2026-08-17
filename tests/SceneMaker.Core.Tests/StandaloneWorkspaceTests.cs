using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class StandaloneWorkspaceTests
{
    [Fact]
    public void CatalogAndWorkspaceProfileResolveEffectiveSpatialAssets()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        File.WriteAllText(Path.Combine(directory.Path, WorkspaceConfigurationStore.FileName), """
        {
          "format": "scene_maker_workspace",
          "version": 1,
          "workspace_key": "game01",
          "grid": {
            "terrain_cell_meters": 0.5,
            "authoring_pixels_per_meter": 32,
            "game_pixels_per_meter": 128
          },
          "assets": [
            { "asset_key": "terrain.grass", "color": "#99E550" },
            { "asset_key": "terrain.water", "color": "#5B6EE1" },
            {
              "asset_key": "prop.tree",
              "color": "#2E7D32",
              "footprint_meters": { "width": 3.5, "height": 6.5 },
              "anchor_meters": { "x": 1.75, "y": 0.0 }
            },
            {
              "asset_key": "transition.portal",
              "color": "#8E6CFF",
              "footprint_meters": { "width": 2.0, "height": 3.0 },
              "anchor_meters": { "x": 1.0, "y": 0.0 }
            }
          ]
        }
        """);

        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var tree = PlacementDisplayCatalogLoader.Load(catalog, workspace).Resolve("prop.tree");
        var portal = TransitionDisplayCatalogLoader.Load(catalog, workspace)
            .Resolve("transition.portal");

        Assert.Equal(112, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(208, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(56, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
        Assert.Equal(64, portal.FootprintWidthAuthoringPixels);
        Assert.Equal(96, portal.FootprintHeightAuthoringPixels);
        Assert.Equal(32, portal.AnchorXAuthoringPixels);
    }

    [Fact]
    public void SceneDocumentsPersistStableAssetKeysAndRespectFootprints()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        File.WriteAllText(Path.Combine(directory.Path, WorkspaceConfigurationStore.FileName), """
        {
          "format": "scene_maker_workspace",
          "version": 1,
          "workspace_key": "game01",
          "grid": {
            "terrain_cell_meters": 0.5,
            "authoring_pixels_per_meter": 32,
            "game_pixels_per_meter": 128
          },
          "assets": [
            { "asset_key": "terrain.grass", "color": "#99E550" },
            {
              "asset_key": "prop.tree",
              "color": "#2E7D32",
              "footprint_meters": { "width": 3.5, "height": 6.5 },
              "anchor_meters": { "x": 1.75, "y": 0.0 }
            },
            {
              "asset_key": "transition.portal",
              "color": "#8E6CFF",
              "footprint_meters": { "width": 2.0, "height": 3.0 },
              "anchor_meters": { "x": 1.0, "y": 0.0 }
            }
          ]
        }
        """);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var terrain = TerrainDisplayCatalogLoader.Load(catalog, workspace);
        var placements = PlacementDisplayCatalogLoader.Load(catalog, workspace);
        var transitions = TransitionDisplayCatalogLoader.Load(catalog, workspace);
        var scene = SceneDocument.Create("test", 20, 20);

        scene = TerrainEditing.Paint(scene, terrain, 0, 0, "terrain.grass");
        scene = PlacementEditing.Place(scene, placements, transitions, 56, 0, "prop.tree");
        scene = TransitionEditing.Place(scene, placements, transitions, 200, 0, "transition.portal");
        var serialized = DocumentJson.Serialize(scene);
        var restored = DocumentJson.DeserializeScene(serialized);

        Assert.Contains("\"asset_key\": \"prop.tree\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_id", serialized, StringComparison.Ordinal);
        Assert.Equal("terrain.grass", restored.TerrainCells.Single().AssetKey);
        Assert.Equal("prop.tree", restored.Placements.Single().AssetKey);
        Assert.Equal("transition.portal", restored.Transitions.Single().AssetKey);
    }

    [Fact]
    public void WorkspaceConfigurationRejectsSpatialProfileWithFractionalAnchorPixel()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        File.WriteAllText(Path.Combine(directory.Path, WorkspaceConfigurationStore.FileName), """
        {
          "format": "scene_maker_workspace",
          "version": 1,
          "workspace_key": "game01",
          "grid": {
            "terrain_cell_meters": 0.5,
            "authoring_pixels_per_meter": 32,
            "game_pixels_per_meter": 128
          },
          "assets": [
            {
              "asset_key": "prop.tree",
              "color": "#2E7D32",
              "footprint_meters": { "width": 3.5, "height": 6.5 },
              "anchor_meters": { "x": 1.71, "y": 0.0 }
            }
          ]
        }
        """);

        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PlacementDisplayCatalogLoader.Load(catalog, workspace));

        Assert.Contains("whole authoring pixel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceMetricsDriveGridAndSpatialConversions()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        File.WriteAllText(Path.Combine(directory.Path, WorkspaceConfigurationStore.FileName), """
        {
          "format": "scene_maker_workspace",
          "version": 1,
          "workspace_key": "game02",
          "grid": {
            "terrain_cell_meters": 0.25,
            "authoring_pixels_per_meter": 20,
            "game_pixels_per_meter": 100
          },
          "assets": [
            { "asset_key": "terrain.grass", "color": "#99E550" },
            {
              "asset_key": "prop.tree",
              "color": "#2E7D32",
              "footprint_meters": { "width": 1.5, "height": 2.0 },
              "anchor_meters": { "x": 0.5, "y": 0.0 }
            }
          ]
        }
        """);

        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var tree = PlacementDisplayCatalogLoader.Load(catalog, workspace).Resolve("prop.tree");

        Assert.Equal(5, workspace.Metrics.AuthoringPixelsPerTerrainCell);
        Assert.Equal(30, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(40, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(10, tree.AnchorXAuthoringPixels);
        Assert.Equal(0.75m, PlacementEditing.PositionMeters(15, workspace.Metrics));
        Assert.Equal(75, PlacementEditing.PositionGamePixels(15, workspace.Metrics));
        Assert.Equal(
            [new TerrainCellCoordinate(1, 0)],
            TerrainCoverage.IntersectedCells(
                new PlacementBoundsAuthoringPixels(5, 0, 5, 5),
                workspace.Metrics));
    }

    [Fact]
    public void WorkspaceProfilesCanBeChangedAndSavedWithoutExternalCatalogs()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        var configuration = WorkspaceConfigurationStore.Create(
            "game03",
            new WorkspaceGridConfiguration(1m, 10m, 40m),
            [
                new WorkspaceAssetProfile("terrain.grass", "#99E550", null, null, null, null),
                new WorkspaceAssetProfile("prop.tree", "#2E7D32", 2m, 3m, 1m, 0m),
            ],
            catalog);

        WorkspaceConfigurationStore.Save(directory.Path, configuration);
        var restored = WorkspaceConfigurationStore.Load(directory.Path, catalog);

        Assert.Equal(10, restored.Metrics.AuthoringPixelsPerTerrainCell);
        Assert.Equal(2m, restored.ResolveAssetProfile("prop.tree").FootprintWidthMeters);
        Assert.Equal(1m, restored.ResolveAssetProfile("prop.tree").AnchorXMeters);
    }

    [Fact]
    public void WorkspaceDirectoryUsesConfigAsItsOnlyManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = SceneMakerCatalogLoader.Load(WriteCatalog(directory.Path));
        var workspace = WorkspaceStore.Create(directory.Path, "game04");
        WorkspaceConfigurationStore.CreateDefault(workspace.DirectoryPath, workspace.WorkspaceKey);

        var loaded = WorkspaceStore.Load(workspace.DirectoryPath, catalog);

        Assert.Equal("game04", loaded.WorkspaceKey);
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, "workspace.json")));
        Assert.True(File.Exists(Path.Combine(workspace.DirectoryPath, "config.json")));
    }

    private static string WriteCatalog(string directory) 
    {
        var path = Path.Combine(directory, "catalog.json");
        File.WriteAllText(path, """
        {
          "format": "scene_maker_catalog",
          "version": 1,
          "assets": [
            { "asset_key": "terrain.grass", "category": "terrain", "name": "Grass" },
            { "asset_key": "terrain.water", "category": "terrain", "name": "Water" },
            { "asset_key": "prop.tree", "category": "prop", "name": "Tree" },
            { "asset_key": "transition.portal", "category": "transition", "name": "Portal" }
          ]
        }
        """);
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"scene-maker-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
