using SceneMaker.Core;
using System.Globalization;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class StandaloneWorkspaceTests
{
    [Fact]
    public void PolyToolsCatalogAndWorkspaceDeriveEffectiveSpatialAssets()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01");
        WriteConfig(directory.Path, "game01", 0.5m, 32m, 192m, """
            { "asset_key": "grass", "color": "#99E550" },
            { "asset_key": "tree", "color": "#2E7D32" },
            { "asset_key": "portal", "color": "#8E6CFF", "authoring_role": "transition" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var tree = PlacementDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");
        var portal = TransitionDisplayCatalogLoader.Load(catalog, workspace).Resolve("portal");

        Assert.Equal(66, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(65, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(33, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
        Assert.Equal(32, portal.FootprintWidthAuthoringPixels);
        Assert.Equal(64, portal.FootprintHeightAuthoringPixels);
        Assert.Equal(16, portal.AnchorXAuthoringPixels);
    }

    [Fact]
    public void SceneDocumentsPersistPolyToolsAssetKeysAndRespectDerivedFootprints()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01");
        WriteConfig(directory.Path, "game01", 0.5m, 32m, 192m, """
            { "asset_key": "grass", "color": "#99E550" },
            { "asset_key": "tree", "color": "#2E7D32" },
            { "asset_key": "portal", "color": "#8E6CFF", "authoring_role": "transition" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var terrain = TerrainDisplayCatalogLoader.Load(catalog, workspace);
        var placements = PlacementDisplayCatalogLoader.Load(catalog, workspace);
        var transitions = TransitionDisplayCatalogLoader.Load(catalog, workspace);
        var scene = SceneDocument.Create("test", 20, 20);

        scene = TerrainEditing.Paint(scene, terrain, 0, 0, "grass");
        scene = PlacementEditing.Place(scene, placements, transitions, 33, 0, "tree");
        scene = TransitionEditing.Place(scene, placements, transitions, 160, 32, "portal");
        var serialized = DocumentJson.Serialize(scene);
        var restored = DocumentJson.DeserializeScene(serialized);

        Assert.Contains("\"asset_key\": \"tree\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("prop.tree", serialized, StringComparison.Ordinal);
        Assert.Equal("grass", restored.TerrainCells.Single().AssetKey);
        Assert.Equal("tree", restored.Placements.Single().AssetKey);
        Assert.Equal("portal", restored.Transitions.Single().AssetKey);
    }

    [Fact]
    public void PolyToolsPivotAndComponentHierarchyDetermineAuthoringBounds()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01", treeComponents: """
            {
              "component_id": "root",
              "parent_component_id": null,
              "local_transform": {
                "position": [2.0, 1.0],
                "rotation_radians": 0.0,
                "scale": [1.0, 1.0]
              },
              "mesh": null,
              "contour_stroke_mesh": null
            },
            {
              "component_id": "child",
              "parent_component_id": "root",
              "local_transform": {
                "position": [1.0, 2.0],
                "rotation_radians": 0.0,
                "scale": [2.0, 1.0]
              },
              "mesh": {
                "vertices": [[0.0, 0.0], [1.0, 1.0]],
                "indices": [0, 1, 1]
              },
              "contour_stroke_mesh": null
            }
        """, treePivot: "[4.0, 3.0]");
        WriteConfig(directory.Path, "game01", 1m, 10m, 40m, """
            { "asset_key": "tree", "color": "#2E7D32" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var tree = PlacementDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        Assert.Equal(20, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(10, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(10, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
    }

    [Fact]
    public void WorkspaceMetricsDriveGridAndSpatialConversions()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game02");
        WriteConfig(directory.Path, "game02", 0.25m, 20m, 100m, """
            { "asset_key": "tree", "color": "#2E7D32" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path, catalog);

        Assert.Equal(5, workspace.Metrics.AuthoringPixelsPerTerrainCell);
        Assert.Equal(0.75m, PlacementEditing.PositionMeters(15, workspace.Metrics));
        Assert.Equal(75, PlacementEditing.PositionGamePixels(15, workspace.Metrics));
        Assert.Equal(
            [new TerrainCellCoordinate(1, 0)],
            TerrainCoverage.IntersectedCells(
                new PlacementBoundsAuthoringPixels(5, 0, 5, 5),
                workspace.Metrics));
    }

    [Fact]
    public void WorkspaceProfilesSaveOnlySceneMakerOwnedOverrides()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game03");
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var configuration = WorkspaceConfigurationStore.Create(
            "game03",
            new WorkspaceGridConfiguration(1m, 10m, 40m),
            [
                new WorkspaceAssetProfile("grass", "#99E550"),
                new WorkspaceAssetProfile("portal", "#8E6CFF", AuthoringAssetRole.Transition),
            ],
            catalog);

        WorkspaceConfigurationStore.Save(directory.Path, configuration);
        var json = File.ReadAllText(Path.Combine(directory.Path, "config.json"));
        var restored = WorkspaceConfigurationStore.Load(directory.Path, catalog);

        Assert.Equal(AuthoringAssetRole.Transition, restored.ResolveAssetProfile("portal").AuthoringRole);
        Assert.DoesNotContain("footprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("anchor", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceLoadRequiresSynchronizedPolyToolsImport()
    {
        using var parent = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(parent.Path, "game04");
        WorkspaceConfigurationStore.CreateDefault(workspace.DirectoryPath, workspace.WorkspaceKey);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(workspace.DirectoryPath));

        Assert.Contains("synchronized PolyTools catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportEmbedsDerivedSpatialSnapshotButNotEditorColors()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game05");
        WriteConfig(directory.Path, "game05", 0.5m, 32m, 128m, """
            { "asset_key": "grass", "color": "#99E550" },
            { "asset_key": "tree", "color": "#2E7D32" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var configuration = WorkspaceConfigurationStore.Load(directory.Path, catalog);
        var terrain = TerrainDisplayCatalogLoader.Load(catalog, configuration);
        var placements = PlacementDisplayCatalogLoader.Load(catalog, configuration);
        var transitions = TransitionDisplayCatalogLoader.Load(catalog, configuration);
        var scene = TerrainEditing.Paint(
            SceneDocument.Create("field", 1, 1), terrain, 0, 0, "grass");
        var workspace = new LoadedWorkspace(directory.Path, "game05");
        var path = SceneExport.Write(
            workspace,
            new LoadedScene(Path.Combine(directory.Path, "scenes", "field.scene.json"), scene),
            configuration,
            terrain,
            placements,
            transitions);
        var json = File.ReadAllText(path);

        Assert.Contains("\"asset_key\": \"grass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"width\": 2.0625", json, StringComparison.Ordinal);
        Assert.DoesNotContain("#99E550", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportRejectsOutdatedPolyToolsManifestSchema()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game06", manifestSchema: 13);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains("schema_version 14 or 15", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportAcceptsCurrentPolyToolsManifestSchema()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game07", manifestSchema: 15);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);

        Assert.Contains(catalog.Assets, asset => asset.AssetKey == "tree");
    }

    private static void WriteConfig(
        string directory,
        string workspaceKey,
        decimal terrainCellMeters,
        decimal authoringPixelsPerMeter,
        decimal gamePixelsPerMeter,
        string assets)
    {
        File.WriteAllText(Path.Combine(directory, WorkspaceConfigurationStore.FileName), $$"""
        {
          "format": "scene_maker_workspace",
          "version": 2,
          "workspace_key": "{{workspaceKey}}",
          "grid": {
            "terrain_cell_meters": {{terrainCellMeters.ToString(CultureInfo.InvariantCulture)}},
            "authoring_pixels_per_meter": {{authoringPixelsPerMeter.ToString(CultureInfo.InvariantCulture)}},
            "game_pixels_per_meter": {{gamePixelsPerMeter.ToString(CultureInfo.InvariantCulture)}}
          },
          "assets": [
            {{assets}}
          ]
        }
        """);
    }

    private static void WritePolyToolsImport(
        string workspaceDirectory,
        string worldKey,
        string? treeComponents = null,
        string treePivot = "[0.0, 0.0]",
        int manifestSchema = 14)
    {
        var importDirectory = Path.Combine(
            workspaceDirectory,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(Path.Combine(importDirectory, "catalog.json"), $$"""
        {
          "schema_version": 1,
          "world_key": "{{worldKey}}",
          "world_name": "Test World",
          "assets": [
            {
              "asset_key": "grass",
              "display_name": "Grass",
              "asset_type": "terrain",
              "runtime_package": "PolyToolsRuntimeExports/grass/manifest.json"
            },
            {
              "asset_key": "portal",
              "display_name": "Portal",
              "asset_type": "props",
              "runtime_package": "PolyToolsRuntimeExports/portal/manifest.json"
            },
            {
              "asset_key": "tree",
              "display_name": "Tree",
              "asset_type": "props",
              "runtime_package": "PolyToolsRuntimeExports/tree/manifest.json"
            }
          ]
        }
        """);

        WriteManifest(
            importDirectory,
            "grass",
            "terrain",
            manifestSchema,
            "[0.5, 0.5]",
            BasicComponent("[[0.0, 0.0], [1.0, 1.0]]"));
        WriteManifest(
            importDirectory,
            "portal",
            "props",
            manifestSchema,
            "[0.0, 0.0]",
            BasicComponent("[[-0.5, 0.0], [0.5, 2.0]]"));
        WriteManifest(
            importDirectory,
            "tree",
            "props",
            manifestSchema,
            treePivot,
            treeComponents ?? BasicComponent("[[-1.01, 0.01], [1.02, 2.03]]"));
    }

    private static string BasicComponent(string vertices) => $$"""
        {
          "component_id": "body",
          "parent_component_id": null,
          "local_transform": {
            "position": [0.0, 0.0],
            "rotation_radians": 0.0,
            "scale": [1.0, 1.0]
          },
          "mesh": {
            "vertices": {{vertices}},
            "indices": [0, 1, 1]
          },
          "contour_stroke_mesh": null
        }
        """;

    private static void WriteManifest(
        string importDirectory,
        string assetKey,
        string assetType,
        int schema,
        string assetPivot,
        string components)
    {
        var directory = Path.Combine(
            importDirectory, "PolyToolsRuntimeExports", assetKey);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
        {
          "schema_version": {{schema}},
          "asset_key": "{{assetKey}}",
          "display_name": "{{assetKey}}",
          "asset_type": "{{assetType}}",
          "asset_pivot": {{assetPivot}},
          "components": [
            {{components}}
          ]
        }
        """);
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
