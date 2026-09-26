using SceneMaker.Core;

namespace SceneMaker.TestSupport;

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
/// The water grid is half a Terrain cell: 0.5 m, or 16 authoring pixels, the
/// same proportion world01 authors rivers at.
///
/// Assets: SceneMaker authors <c>grass</c>, <c>river</c> and <c>sand</c> as
/// Terrain with the surfaces <c>land</c>, <c>water</c> and <c>sand</c>;
/// <c>stone</c> and <c>portal</c> are Placements and consume PolyTools geometry.
/// Only <c>river</c> is authored as a curve; the other two are painted as cells.
/// A second curve Asset, <c>lava</c>, is added only when <see cref="Create"/> is
/// asked for it, so an area that draws curves offers a real choice.
///
/// The Workspace directory is named after its key and carries the empty
/// <c>scenes</c> and <c>templates</c> directories every Workspace requires.
/// </summary>
public sealed class TestWorkspace : IDisposable
{
    public const int AuthoringPixelsPerCell = 32;
    public const int AuthoringPixelsPerWaterCell = 16;

    private readonly string _containerPath;

    private TestWorkspace(
        string containerPath,
        string rootPath,
        PolyToolsCatalog catalog,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrain,
        PropDisplayCatalog props)
    {
        _containerPath = containerPath;
        RootPath = rootPath;
        Catalog = catalog;
        Configuration = configuration;
        Terrain = terrain;
        Props = props;
    }

    public string RootPath { get; }
    public PolyToolsCatalog Catalog { get; }
    public WorkspaceConfiguration Configuration { get; }
    public TerrainDisplayCatalog Terrain { get; }
    public PropDisplayCatalog Props { get; }
    public WorkspaceMetrics Metrics => Configuration.Metrics;

    /// <summary>The Workspace this fixture wrote, for callers that need a <see cref="LoadedWorkspace"/>.</summary>
    public LoadedWorkspace Workspace => new(RootPath, Configuration.WorkspaceKey);

    /// <summary>
    /// The shared fixture. <paramref name="secondCurveAsset"/> adds a second
    /// curve-authored Terrain Asset, <c>lava</c>, for the tests that need a
    /// curve area to offer a real choice; it is off by default so that the
    /// Workspace every other test reasons about stays the one described above.
    /// </summary>
    public static TestWorkspace Create(
        string worldKey = "test_world",
        bool secondCurveAsset = false)
    {
        var containerPath = Path.Combine(
            Path.GetTempPath(),
            $"scene-maker-tests-{Guid.NewGuid():N}");
        // WorkspaceStore.Load requires the directory to be named after the key.
        var rootPath = Path.Combine(containerPath, worldKey);
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, WorkspaceStore.ScenesDirectoryName));
        Directory.CreateDirectory(Path.Combine(rootPath, WorkspaceStore.TemplatesDirectoryName));
        WriteImport(rootPath, worldKey, secondCurveAsset);
        WriteConfiguration(rootPath, worldKey, secondCurveAsset);
        var catalog = PolyToolsCatalogImporter.Load(rootPath);
        var configuration = WorkspaceConfigurationStore.Load(rootPath);
        return new TestWorkspace(
            containerPath,
            rootPath,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(configuration),
            PropDisplayCatalogLoader.Load(catalog, configuration));
    }

    public void Dispose() => Directory.Delete(_containerPath, recursive: true);

    private static void WriteConfiguration(
        string rootPath,
        string worldKey,
        bool secondCurveAsset)
    {
        // Appended rather than spliced into the middle: both catalogs sort by
        // asset key when they load, so where the entry sits in the file changes
        // nothing, and a suffix keeps the JSON below readable.
        var lava = secondCurveAsset
            ? ",\n    { \"asset_key\": \"lava\", \"display_name\": \"Lava\", "
                + "\"role\": \"terrain\", \"color\": \"#FF6A3C\", "
                + "\"surface\": \"lava\", \"authoring\": \"curve\" }"
            : string.Empty;
        File.WriteAllText(
            Path.Combine(rootPath, WorkspaceConfigurationStore.FileName),
            $$"""
            {
              "format": "scene_maker_workspace",
              "version": {{WorkspaceConfigurationStore.Version}},
              "workspace_key": "{{worldKey}}",
              "grid": {
                "terrain_cell_meters": 1.0,
                "authoring_pixels_per_meter": 32,
                "game_pixels_per_meter": 192,
                "water_cell_meters": 0.5,
                "elevation_quantum_meters": 0.125,
                "minimum_channel_depth_meters": 0.25
              },
              "bridge_set": "bridge",
              "assets": [
                { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
                { "asset_key": "leaf", "display_name": "Leaf", "role": "placement", "color": "#4CAF50", "polytools_asset_id": "asset_leaf" },
                { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF", "polytools_asset_id": "asset_portal" },
                { "asset_key": "river", "display_name": "Water", "role": "terrain", "color": "#3C7DD9", "surface": "water", "authoring": "curve" },
                { "asset_key": "sand", "display_name": "Sand", "role": "terrain", "color": "#E5C07B", "surface": "sand", "authoring": "cells" },
                { "asset_key": "stone", "display_name": "Stone", "role": "placement", "color": "#808080", "polytools_asset_id": "asset_stone" }{{lava}}
              ]
            }
            """);
    }

    private static void WriteImport(string rootPath, string worldKey, bool secondCurveAsset)
    {
        var lava = secondCurveAsset
            ? ",\n    {\n"
                + "      \"asset_key\": \"lava\",\n"
                + "      \"display_name\": \"Lava\",\n"
                + "      \"asset_type\": \"terrain\",\n"
                + "      \"asset_id\": \"asset_lava\",\n"
                + "      \"asset_category\": \"single\",\n"
                + "      \"runtime_package\": \"PolyToolsRuntimeExports/lava/manifest.json\"\n"
                + "    }"
            : string.Empty;
        var importDirectory = Path.Combine(
            rootPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(
            Path.Combine(importDirectory, PolyToolsCatalogImporter.CatalogFileName),
            $$"""
            {
              "schema_version": {{PolyToolsCatalogImporter.CatalogSchemaVersion}},
              "world_key": "{{worldKey}}",
              "world_name": "Test World",
              "retired_assets": [],
              "assets": [
                {
                  "asset_key": "bridge",
                  "display_name": "Bridge",
                  "asset_type": "props",
                  "asset_id": "asset_bridge",
                  "asset_category": "set",
                  "runtime_package": "PolyToolsRuntimeExports/bridge/manifest.json"
                },
                {
                  "asset_key": "grass",
                  "display_name": "Grass",
                  "asset_type": "terrain",
                  "asset_id": "asset_grass",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/grass/manifest.json"
                },
                {
                  "asset_key": "leaf",
                  "display_name": "Leaf",
                  "asset_type": "props",
                  "asset_id": "asset_leaf",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/leaf/manifest.json"
                },
                {
                  "asset_key": "portal",
                  "display_name": "Portal",
                  "asset_type": "props",
                  "asset_id": "asset_portal",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/portal/manifest.json"
                },
                {
                  "asset_key": "river",
                  "display_name": "River",
                  "asset_type": "terrain",
                  "asset_id": "asset_river",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/river/manifest.json"
                },
                {
                  "asset_key": "sand",
                  "display_name": "Sand",
                  "asset_type": "terrain",
                  "asset_id": "asset_sand",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/sand/manifest.json"
                },
                {
                  "asset_key": "stone",
                  "display_name": "Stone",
                  "asset_type": "props",
                  "asset_id": "asset_stone",
                  "asset_category": "single",
                  "runtime_package": "PolyToolsRuntimeExports/stone/manifest.json"
                }{{lava}}
              ]
            }
            """);

        WriteBridgeSetManifest(importDirectory);
        WriteManifest(importDirectory, "grass", "terrain");
        if (secondCurveAsset) WriteManifest(importDirectory, "lava", "terrain");

        // Leaf authors no collision Region at all - a footprint with nothing
        // occupied, the same shape a bridge plank has in a real Workspace -
        // so a test can place it overlapping another Placement's footprint
        // without also tripping the separate, narrower collision check.
        WriteManifest(importDirectory, "leaf", "props", withCollision: false);
        WriteManifest(importDirectory, "portal", "props");
        WriteManifest(importDirectory, "river", "terrain");
        WriteManifest(importDirectory, "sand", "terrain");

        // Stone carries a named part inside its own bounds, so a Workspace can
        // point an anchor_component at it without changing the Asset's
        // footprint - the case a bridge's post is.
        WriteManifest(importDirectory, "stone", "props", withNamedPart: true);
    }

    /// <summary>
    /// The Set a bridge is built from: portal in the plank role, stone in the
    /// post role. The roles are what SceneMaker reads; that the Assets are
    /// called something else entirely is the point, since a name follows a
    /// rename and a role does not.
    /// </summary>
    private static void WriteBridgeSetManifest(string importDirectory)
    {
        var directory = Path.Combine(importDirectory, "PolyToolsRuntimeExports", "bridge");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""
            {
              "schema_version": {{PolyToolsCatalogImporter.ManifestSchemaVersion}},
              "asset_key": "bridge",
              "asset_id": "asset_bridge",
              "display_name": "Bridge",
              "asset_type": "props",
              "asset_category": "set",
              "asset_pivot": [0.0, 0.0],
              "components": [
                {
                  "component_id": "member_plank",
                  "kind": "asset_reference",
                  "name": "plank",
                  "role": "{{BridgeKit.PlankRole}}",
                  "source_asset_key": "portal",
                  "source_asset_id": "asset_portal",
                  "parent_component_id": null,
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  }
                },
                {
                  "component_id": "member_post",
                  "kind": "asset_reference",
                  "name": "post",
                  "role": "{{BridgeKit.AnchorRole}}",
                  "source_asset_key": "stone",
                  "source_asset_id": "asset_stone",
                  "parent_component_id": null,
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  }
                }
              ],
              "regions": []
            }
            """);
    }

    private static void WriteManifest(
        string importDirectory,
        string assetKey,
        string assetType,
        bool withNamedPart = false,
        bool withCollision = true)
    {
        var directory = Path.Combine(importDirectory, "PolyToolsRuntimeExports", assetKey);
        Directory.CreateDirectory(directory);
        // The named part answers for itself: a bridge sets posts, and a post
        // has to say what it occupies where it stands.
        var partRegion = withNamedPart
            ? """
            ,
                {
                  "region_id": "collision_0002",
                  "name": "post_collision",
                  "role": "collision",
                  "geometry_source": "authored",
                  "source_component_id": "post",
                  "vertices": [[0.25, 0.5], [0.75, 0.5], [0.75, 1.0]],
                  "indices": [0, 1, 2]
                }
            """
            : string.Empty;
        var part = withNamedPart
            ? """
            ,
                {
                  "component_id": "post",
                  "name": "post",
                  "parent_component_id": "body",
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  },
                  "mesh": {
                    "vertices": [[0.25, 0.5], [0.75, 1.0]],
                    "indices": [0, 1, 1]
                  },
                  "contour_stroke_mesh": null
                }
            """
            : string.Empty;
        // An Asset with no collision Region occupies nothing - a bridge plank
        // is the real case this stands in for - so the array holding the
        // named part's own region is empty rather than defaulted to a box.
        var regions = withCollision
            ? """
                {
                  "region_id": "collision_0001",
                  "name": "collision_region",
                  "role": "collision",
                  "geometry_source": "authored",
                  "source_component_id": "body",
                  "vertices": [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0]],
                  "indices": [0, 1, 2]
                }
            """ + partRegion
            : string.Empty;
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""
            {
              "schema_version": {{PolyToolsCatalogImporter.ManifestSchemaVersion}},
              "asset_key": "{{assetKey}}",
              "asset_id": "asset_{{assetKey}}",
              "display_name": "{{assetKey}}",
              "asset_type": "{{assetType}}",
              "asset_category": "single",
              "asset_pivot": [0.0, 0.0],
              "components": [
                {
                  "component_id": "body",
                  "name": "body",
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
                }{{part}}
              ],
              "regions": [{{regions}}]
            }
            """);
    }
}
