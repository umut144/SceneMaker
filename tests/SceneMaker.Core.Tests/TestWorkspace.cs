namespace SceneMaker.Core.Tests;

/// <summary>
/// Shared fixture for Core tests. Writes a synchronized PolyTools import and a
/// Workspace configuration into a temporary directory, then loads every derived
/// catalog from it.
///
/// The Workspace uses 1 m Terrain cells at 32 authoring pixels per meter, so one
/// Terrain cell is exactly 32 authoring pixels. Every asset is authored as a
/// 1 x 1 m box whose pivot sits in its lower left corner, which derives a
/// 32 x 32 authoring-pixel footprint with anchor (0, 0). One spatial instance
/// therefore covers exactly one Terrain cell, which keeps coordinate assertions
/// readable: an anchor at (64, 64) occupies Terrain cell (2, 2).
///
/// Assets: <c>grass</c> and <c>sand</c> are PolyTools terrain, <c>stone</c> is a
/// PolyTools prop, and <c>portal</c> is a PolyTools prop that this Workspace
/// overrides into the Transition role.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public const int AuthoringPixelsPerCell = 32;

    private TestWorkspace(
        string rootPath,
        PolyToolsCatalog catalog,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrain,
        PlacementDisplayCatalog placements,
        TransitionDisplayCatalog transitions)
    {
        RootPath = rootPath;
        Catalog = catalog;
        Configuration = configuration;
        Terrain = terrain;
        Placements = placements;
        Transitions = transitions;
    }

    public string RootPath { get; }
    public PolyToolsCatalog Catalog { get; }
    public WorkspaceConfiguration Configuration { get; }
    public TerrainDisplayCatalog Terrain { get; }
    public PlacementDisplayCatalog Placements { get; }
    public TransitionDisplayCatalog Transitions { get; }
    public WorkspaceMetrics Metrics => Configuration.Metrics;

    public static TestWorkspace Create(string worldKey = "test_world")
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"scene-maker-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        WriteImport(rootPath, worldKey);
        WriteConfiguration(rootPath, worldKey);
        var catalog = PolyToolsCatalogImporter.Load(rootPath);
        var configuration = WorkspaceConfigurationStore.Load(rootPath, catalog);
        return new TestWorkspace(
            rootPath,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(catalog, configuration),
            PlacementDisplayCatalogLoader.Load(catalog, configuration),
            TransitionDisplayCatalogLoader.Load(catalog, configuration));
    }

    public void Dispose() => Directory.Delete(RootPath, recursive: true);

    private static void WriteConfiguration(string rootPath, string worldKey)
    {
        File.WriteAllText(
            Path.Combine(rootPath, WorkspaceConfigurationStore.FileName),
            $$"""
            {
              "format": "scene_maker_workspace",
              "version": 2,
              "workspace_key": "{{worldKey}}",
              "grid": {
                "terrain_cell_meters": 1.0,
                "authoring_pixels_per_meter": 32,
                "game_pixels_per_meter": 192
              },
              "assets": [
                { "asset_key": "grass", "color": "#99E550" },
                { "asset_key": "portal", "color": "#8E6CFF", "authoring_role": "transition" },
                { "asset_key": "sand", "color": "#E5C07B" },
                { "asset_key": "stone", "color": "#808080" }
              ]
            }
            """);
    }

    private static void WriteImport(string rootPath, string worldKey)
    {
        var importDirectory = Path.Combine(
            rootPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(
            Path.Combine(importDirectory, PolyToolsCatalogImporter.CatalogFileName),
            $$"""
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
                  "asset_key": "sand",
                  "display_name": "Sand",
                  "asset_type": "terrain",
                  "runtime_package": "PolyToolsRuntimeExports/sand/manifest.json"
                },
                {
                  "asset_key": "stone",
                  "display_name": "Stone",
                  "asset_type": "props",
                  "runtime_package": "PolyToolsRuntimeExports/stone/manifest.json"
                }
              ]
            }
            """);

        WriteManifest(importDirectory, "grass", "terrain");
        WriteManifest(importDirectory, "portal", "props");
        WriteManifest(importDirectory, "sand", "terrain");
        WriteManifest(importDirectory, "stone", "props");
    }

    private static void WriteManifest(string importDirectory, string assetKey, string assetType)
    {
        var directory = Path.Combine(importDirectory, "PolyToolsRuntimeExports", assetKey);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""
            {
              "schema_version": 15,
              "asset_key": "{{assetKey}}",
              "display_name": "{{assetKey}}",
              "asset_type": "{{assetType}}",
              "asset_pivot": [0.0, 0.0],
              "components": [
                {
                  "component_id": "body",
                  "parent_component_id": null,
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  },
                  "mesh": {
                    "vertices": [[0.0, 0.0], [1.0, 1.0]],
                    "indices": [0, 1, 1]
                  },
                  "contour_stroke_mesh": null
                }
              ]
            }
            """);
    }
}
