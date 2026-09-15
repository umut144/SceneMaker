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
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" },
            { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF", "polytools_asset_id": "asset_portal" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var props = PropDisplayCatalogLoader.Load(catalog, workspace);
        var tree = props.Resolve("tree");
        var portal = props.Resolve("portal");

        Assert.Equal(66, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(65, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(33, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
        Assert.Equal(32, portal.FootprintWidthAuthoringPixels);
        Assert.Equal(64, portal.FootprintHeightAuthoringPixels);
        Assert.Equal(16, portal.AnchorXAuthoringPixels);
    }

    /// <summary>
    /// A scale is a Workspace fact about the Asset, not a PolyTools one: the
    /// model stays a 1m x 1m authoring, and Workspace config schema 17 grows
    /// the footprint and collision box before either becomes authoring
    /// pixels. Only "tree" declares one here, so "portal" is the control -
    /// unscaled and unaffected, exactly as an Asset nobody touched stays
    /// today.
    /// </summary>
    [Fact]
    public void AnAssetScaleGrowsItsFootprintAndCollisionAroundThePivot()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01");
        WriteConfig(directory.Path, "game01", 0.5m, 32m, 192m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree", "scale": 2 },
            { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF", "polytools_asset_id": "asset_portal" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var props = PropDisplayCatalogLoader.Load(catalog, workspace);
        var tree = props.Resolve("tree");
        var portal = props.Resolve("portal");

        Assert.Equal(2m, workspace.ResolveAssetProfile("tree").Scale);
        Assert.Equal(131, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(130, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(65, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
        Assert.NotNull(tree.Collision);
        Assert.Equal(131, tree.Collision!.WidthAuthoringPixels);
        Assert.Equal(130, tree.Collision!.HeightAuthoringPixels);

        Assert.Null(portal.Category);
        Assert.Equal(32, portal.FootprintWidthAuthoringPixels);
        Assert.Equal(64, portal.FootprintHeightAuthoringPixels);
    }

    /// <summary>
    /// The scenario in the room: two Totems that clear each other at their
    /// PolyTools size stop clearing once the Asset is scaled up - no
    /// Placement moves and nothing is re-saved, because a Placement stores
    /// only asset_key and an anchor, never a baked footprint. The same
    /// export-time check that always refused a collision (PropEditing.
    /// ValidateAssetReferences) is what catches it; scaling adds no
    /// mechanism of its own.
    /// </summary>
    [Fact]
    public void ScalingAPlacementAssetCanMakeAlreadyPlacedPropsCollide()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "scale01",
            regions: """
                [
                  {
                    "region_id": "collision_0001",
                    "name": "trunk",
                    "role": "collision",
                    "geometry_source": "authored",
                    "source_component_id": "body",
                    "vertices": [[-0.1, 0.0], [0.1, 0.0], [0.1, 0.2]],
                    "indices": [0, 1, 2]
                  }
                ]
                """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path, ["tree"]);
        var grid = new WorkspaceGridConfiguration(1m, 10m, 40m, 0.5m, 0.125m, 0.25m);
        var unscaledWorkspace = WorkspaceConfigurationStore.Create(
            "scale01", grid,
            [new WorkspaceAssetProfile(
                "tree", "Tree", WorkspaceAssetRole.Placement, "#2E7D32",
                PolyToolsAssetId: "asset_tree")]);
        var unscaledProps = PropDisplayCatalogLoader.Load(catalog, unscaledWorkspace);

        // Four authoring pixels apart: the 0.1m-radius trunks (2px wide at
        // 10px/m) clear each other with one pixel to spare.
        // Wide enough that the scaled-up visible footprint (as opposed to
        // the narrow trunk collision box under test) still fits the Scene;
        // that is not the question this test asks.
        var scene = PropEditing.Place(
            SceneDocument.CreateInstance("scale", 60, 60), unscaledProps, 150, 150, "tree");
        scene = PropEditing.Place(scene, unscaledProps, 154, 150, "tree");
        PropEditing.ValidateAssetReferences(scene, unscaledProps);

        var scaledWorkspace = WorkspaceConfigurationStore.Create(
            "scale01", grid,
            [new WorkspaceAssetProfile(
                "tree", "Tree", WorkspaceAssetRole.Placement, "#2E7D32",
                PolyToolsAssetId: "asset_tree", Scale: 5m)]);
        var scaledProps = PropDisplayCatalogLoader.Load(catalog, scaledWorkspace);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PropEditing.ValidateAssetReferences(scene, scaledProps));

        Assert.Contains("collides with something already in the Scene", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneDocumentsPersistPolyToolsAssetKeysAndRespectDerivedFootprints()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01");
        WriteConfig(directory.Path, "game01", 0.5m, 32m, 192m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" },
            { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF", "polytools_asset_id": "asset_portal" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var terrain = TerrainDisplayCatalogLoader.Load(workspace);
        var props = PropDisplayCatalogLoader.Load(catalog, workspace);
        var scene = SceneDocument.CreateInstance("test", 20, 20);

        scene = TerrainEditing.Paint(scene, terrain, 0, 0, "grass");
        scene = PropEditing.Place(scene, props, 33, 0, "tree");
        scene = PropEditing.Place(scene, props, 160, 32, "portal");
        var serialized = DocumentJson.Serialize(scene);
        var restored = DocumentJson.DeserializeScene(serialized);

        Assert.Contains("\"asset_key\": \"tree\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("prop.tree", serialized, StringComparison.Ordinal);
        Assert.Equal("grass", restored.TerrainCells.Single().AssetKey);
        Assert.Equal(
            new[] { "portal", "tree" },
            restored.Props.Select(static prop => prop.AssetKey).ToArray());
    }

    [Fact]
    public void TerrainLinesCanBePaintedAndErased()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "line01");
        WriteConfig(directory.Path, "line01", 1m, 32m, 192m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" }
        """);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var terrain = TerrainDisplayCatalogLoader.Load(workspace);
        var scene = TerrainEditing.PaintLine(
            SceneDocument.CreateInstance("line", 5, 5),
            terrain,
            0,
            0,
            4,
            4,
            "grass");

        Assert.Equal(5, scene.TerrainCells.Count);

        scene = TerrainEditing.EraseLine(scene, 1, 1, 3, 3);

        Assert.Equal(2, scene.TerrainCells.Count);
        Assert.Contains(scene.TerrainCells, cell => cell.X == 0 && cell.Y == 0);
        Assert.Contains(scene.TerrainCells, cell => cell.X == 4 && cell.Y == 4);
    }

    [Fact]
    public void PropLineOffsetAddsSpaceBetweenFootprints()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "offset01");
        WriteConfig(directory.Path, "offset01", 1m, 32m, 192m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var asset = PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        var withoutOffset = PropEditing.LineAnchors(asset, 100, 100, 300, 100);
        var withOffset = PropEditing.LineAnchors(
            asset,
            100,
            100,
            300,
            100,
            offsetAuthoringPixels: 10);

        Assert.Equal([100, 166, 232, 298], withoutOffset.Select(static anchor => anchor.X));
        Assert.Equal([100, 176, 252], withOffset.Select(static anchor => anchor.X));
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
        """, treePivot: "[4.0, 3.0]", treeCollisionComponentId: "child");
        WriteConfig(directory.Path, "game01", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var tree = PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        Assert.Equal(20, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(10, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(10, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
    }

    /// <summary>
    /// The future narrow adapter may change where Placement geometry comes
    /// from, but not what qualifies as geometry: without a visible point there
    /// is no footprint or anchor for SceneMaker to place.
    /// </summary>
    [Fact]
    public void APlacementWithoutVisibleGeometryIsRejected()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "geometry01",
            treeComponents: """
                {
                  "component_id": "body",
                  "parent_component_id": null,
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  },
                  "mesh": null,
                  "contour_stroke_mesh": null
                }
            """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains(
            "PolyTools asset 'tree' has no visible geometry",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceMetricsDriveGridAndSpatialConversions()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game02");
        WriteConfig(directory.Path, "game02", 0.25m, 20m, 100m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);

        Assert.Equal(5, workspace.Metrics.AuthoringPixelsPerTerrainCell);
        Assert.Equal(0.75m, PropEditing.PositionMeters(15, workspace.Metrics));
        Assert.Equal(75, PropEditing.PositionGamePixels(15, workspace.Metrics));
        Assert.Equal(
            [new TerrainCellCoordinate(1, 0)],
            TerrainCoverage.IntersectedCells(
                new PropBoundsAuthoringPixels(5, 0, 5, 5),
                workspace.Metrics));
    }

    [Fact]
    public void WorkspaceProfilesOwnAuthoringIdentityButNotPlacementGeometry()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game03");
        var configuration = WorkspaceConfigurationStore.Create(
            "game03",
            new WorkspaceGridConfiguration(1m, 10m, 40m, 0.5m, 0.125m, 0.25m),
            [
                new WorkspaceAssetProfile(
                    "grass", "Grass", WorkspaceAssetRole.Terrain,
                    "#99E550", "land", TerrainAuthoring.Cells),
                new WorkspaceAssetProfile(
                    "portal", "Portal", WorkspaceAssetRole.Placement, "#8E6CFF",
                    PolyToolsAssetId: "asset_portal"),
            ]);

        WorkspaceConfigurationStore.Save(directory.Path, configuration);
        var json = File.ReadAllText(Path.Combine(directory.Path, "config.json"));
        var restored = WorkspaceConfigurationStore.Load(directory.Path);

        Assert.Equal("#8E6CFF", restored.ResolveAssetProfile("portal").Color);
        Assert.Equal("Portal", restored.ResolveAssetProfile("portal").DisplayName);
        Assert.Equal(
            WorkspaceAssetRole.Placement,
            restored.ResolveAssetProfile("portal").Role);
        Assert.Equal(0.125m, restored.Metrics.ElevationQuantumMeters);
        Assert.Equal(
            "asset_portal",
            restored.ResolveAssetProfile("portal").PolyToolsAssetId);
        Assert.Contains("\"elevation_quantum_meters\": 0.125", json, StringComparison.Ordinal);
        // PolyTools is one word on the wire. The naming policy would write
        // "poly_tools_asset_id", which every other reader of this file - the
        // fixtures, the checked-in Workspaces, the sync preflight - refuses.
        Assert.Contains(
            "\"polytools_asset_id\": \"asset_portal\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("poly_tools_asset_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("footprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("anchor", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authoring_role", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceAssetsMayShareACategoryAndStandAloneWhenTheyDoNot()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game05");
        var configuration = WorkspaceConfigurationStore.Create(
            "game05",
            new WorkspaceGridConfiguration(1m, 10m, 40m, 0.5m, 0.125m, 0.25m),
            [
                new WorkspaceAssetProfile(
                    "tree", "Tree", WorkspaceAssetRole.Placement, "#2E7D32",
                    PolyToolsAssetId: "asset_tree"),
                new WorkspaceAssetProfile(
                    "totem_of_life", "Totem Of Life", WorkspaceAssetRole.Placement,
                    "#8E6CFF", PolyToolsAssetId: "asset_totem_of_life",
                    Category: "totems"),
                new WorkspaceAssetProfile(
                    "totem_of_mana", "Totem Of Mana", WorkspaceAssetRole.Placement,
                    "#4C8EDA", PolyToolsAssetId: "asset_totem_of_mana",
                    Category: "totems"),
            ]);

        WorkspaceConfigurationStore.Save(directory.Path, configuration);
        var json = File.ReadAllText(Path.Combine(directory.Path, "config.json"));
        var restored = WorkspaceConfigurationStore.Load(directory.Path);

        Assert.Null(restored.ResolveAssetProfile("tree").Category);
        Assert.Equal("totems", restored.ResolveAssetProfile("totem_of_life").Category);
        Assert.Equal("totems", restored.ResolveAssetProfile("totem_of_mana").Category);
        Assert.Contains("\"category\": \"totems\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceRejectsACategoryThatIsNotALowerSnakeCaseToken()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game06");
        WriteConfig(directory.Path, "game06", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree", "category": "Totems" }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains("Category", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceConfigurationPersistsAssetScale()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game07");
        var configuration = WorkspaceConfigurationStore.Create(
            "game07",
            new WorkspaceGridConfiguration(1m, 10m, 40m, 0.5m, 0.125m, 0.25m),
            [
                new WorkspaceAssetProfile(
                    "tree", "Tree", WorkspaceAssetRole.Placement, "#2E7D32",
                    PolyToolsAssetId: "asset_tree"),
                new WorkspaceAssetProfile(
                    "totem_of_life", "Totem Of Life", WorkspaceAssetRole.Placement,
                    "#8E6CFF", PolyToolsAssetId: "asset_totem_of_life", Scale: 5m),
            ]);

        WorkspaceConfigurationStore.Save(directory.Path, configuration);
        var json = File.ReadAllText(Path.Combine(directory.Path, "config.json"));
        var restored = WorkspaceConfigurationStore.Load(directory.Path);

        Assert.Null(restored.ResolveAssetProfile("tree").Scale);
        Assert.Equal(5m, restored.ResolveAssetProfile("totem_of_life").Scale);
        Assert.Contains("\"scale\": 5", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceRejectsATerrainAssetThatDeclaresAScale()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game08");
        WriteConfig(directory.Path, "game08", 1m, 10m, 40m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells", "scale": 2 }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains("must not declare a scale", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void WorkspaceRejectsANonPositiveScale(string scaleJson)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game09");
        WriteConfig(directory.Path, "game09", 1m, 10m, 40m, $$"""
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree", "scale": {{scaleJson}} }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains("scale must be positive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceLoadRequiresSynchronizedPolyToolsImport()
    {
        using var parent = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(parent.Path, "game04", 0.125m);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(workspace.DirectoryPath));

        Assert.Contains("synchronized PolyTools catalog", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("-0.125")]
    public void WorkspaceRejectsMissingOrNonPositiveElevationQuantum(
        string? elevationQuantumJson)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "quantum01");
        WriteConfig(
            directory.Path,
            "quantum01",
            1m,
            32m,
            192m,
            """{ "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" }""",
            elevationQuantumJson: elevationQuantumJson);
        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains("positive", exception.Message, StringComparison.Ordinal);
        Assert.Contains("elevation_quantum_meters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceRejectsTheReplacedConfigurationSchema()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "quantum02");
        WriteConfig(
            directory.Path,
            "quantum02",
            1m,
            32m,
            192m,
            """{ "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" }""",
            version: WorkspaceConfigurationStore.Version - 1);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains(
            FormattableString.Invariant($"version {WorkspaceConfigurationStore.Version}"),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceMetricsSnapElevationsSymmetricallyToTheirQuantum()
    {
        var metrics = new WorkspaceMetrics(
            new WorkspaceGridConfiguration(1m, 32m, 192m, 0.5m, 0.125m, 0.25m));

        Assert.True(metrics.IsElevationAligned(1.125m));
        Assert.False(metrics.IsElevationAligned(1.1m));
        Assert.Equal(1.125m, metrics.SnapElevation(1.1m));
        Assert.Equal(-1.125m, metrics.SnapElevation(-1.1m));
        Assert.Equal(0.125m, metrics.SnapElevation(0.0625m));
        Assert.Equal(-0.125m, metrics.SnapElevation(-0.0625m));
    }

    [Fact]
    public void CreatingAWorkspaceWritesEveryDirectoryAndTheDefaultConfiguration()
    {
        using var parent = TemporaryDirectory.Create();

        var workspace = WorkspaceStore.Create(parent.Path, "game04b", 0.125m);

        // A fresh World has no Game yet - GameStore.Create is the separate,
        // later step that adds one.
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "sandbox")));
        Assert.True(Directory.Exists(Path.Combine(
            workspace.DirectoryPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName)));
        Assert.True(File.Exists(
            Path.Combine(workspace.DirectoryPath, WorkspaceConfigurationStore.FileName)));
    }

    [Fact]
    public void CreatingAGameWritesItsScenesAndTemplatesDirectories()
    {
        using var parent = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(parent.Path, "game04d", 0.125m);

        var game = GameStore.Create(workspace, "sandbox");

        Assert.True(Directory.Exists(
            Path.Combine(game.DirectoryPath, GameStore.ScenesDirectoryName)));
        Assert.True(Directory.Exists(
            Path.Combine(game.DirectoryPath, GameStore.TemplatesDirectoryName)));
    }

    [Fact]
    public void CreatingAWorkspaceOverAnExistingFileFailsWithoutTouchingIt()
    {
        using var parent = TemporaryDirectory.Create();
        var occupied = Path.Combine(parent.Path, "game04c");
        File.WriteAllText(occupied, "not a Workspace");

        Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceStore.Create(parent.Path, "game04c", 0.125m));

        Assert.Equal("not a Workspace", File.ReadAllText(occupied));
    }

    [Fact]
    public void ExportEmbedsDerivedSpatialSnapshotButNotEditorColors()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game05");
        WriteConfig(directory.Path, "game05", 0.5m, 32m, 128m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var configuration = WorkspaceConfigurationStore.Load(directory.Path);
        var terrain = TerrainDisplayCatalogLoader.Load(configuration);
        var props = PropDisplayCatalogLoader.Load(catalog, configuration);
        var scene = TerrainEditing.Paint(
            SceneDocument.CreateInstance("field", 1, 1), terrain, 0, 0, "grass");
        // A plain LoadedGame whose DirectoryPath resolves back to directory.Path,
        // without going through GameStore - this fixture writes its own
        // scenes/ directly and never claims to be a Game GameStore would load.
        var game = new LoadedGame(
            new LoadedWorkspace(Path.GetDirectoryName(directory.Path)!, "game05"),
            Path.GetFileName(directory.Path));
        var written = SceneExport.Write(
            game,
            new LoadedScene(Path.Combine(directory.Path, "scenes", "field.scene.json"), scene),
            configuration,
            terrain,
            props);
        var json = File.ReadAllText(written.Path);

        Assert.Contains("\"asset_key\": \"grass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"width\": 2.0625", json, StringComparison.Ordinal);
        Assert.DoesNotContain("#99E550", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both directions are refused, and the offsets are taken from the current
    /// schema rather than written out: a pinned number here turns every
    /// PolyTools bump into an edit in a test about rejection, and one of those
    /// numbers silently became the accepted schema once already.
    /// </summary>
    [Theory]
    [InlineData(-3)]
    [InlineData(-1)]
    [InlineData(1)]
    public void ImportRejectsReplacedPolyToolsManifestSchemas(int offset)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "game06",
            manifestSchema: PolyToolsCatalogImporter.ManifestSchemaVersion + offset);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains(
            FormattableString.Invariant(
                $"schema_version {PolyToolsCatalogImporter.ManifestSchemaVersion}"),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ImportAcceptsCurrentPolyToolsManifestSchema()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game07");

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);

        Assert.Contains(catalog.Assets, asset => asset.AssetKey == "tree");
    }

    [Fact]
    public void NarrowImportIncludesTransitiveGeometryReferencesButOnlyExposesTheRoot()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game07_refs", treeComponents: """
            {
              "component_id": "grass_reference",
              "parent_component_id": null,
              "kind": "asset_reference",
              "source_asset_key": "grass",
              "local_transform": {
                "position": [2.0, 0.0],
                "rotation_radians": 0.0,
                "scale": [1.0, 1.0]
              },
              "mesh": null,
              "contour_stroke_mesh": null
            }
        """, treeCollisionComponentId: "grass_reference");

        var catalog = PolyToolsCatalogImporter.Load(directory.Path, ["tree"]);
        var tree = Assert.Single(catalog.Assets);

        Assert.Equal("tree", tree.AssetKey);
        Assert.Equal(1.5m, tree.BoundsMeters.MinimumX);
        Assert.Equal(-0.5m, tree.BoundsMeters.MinimumY);
        Assert.Equal(2.5m, tree.BoundsMeters.MaximumX);
        Assert.Equal(0.5m, tree.BoundsMeters.MaximumY);
    }

    [Fact]
    public void ImportValidatesBothRegionGeometryVariantsWithoutChangingVisibleBounds()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game08", regions: """
            [
              {
                "region_id": "region_authored",
                "name": "attack_region",
                "role": "attack",
                "geometry_source": "authored",
                "source_component_id": "body",
                "vertices": [[-100.0, -100.0], [100.0, -100.0], [0.0, 100.0]],
                "indices": [0, 1, 2]
              },
              {
                "region_id": "region_component",
                "name": "collision_region",
                "role": "collision",
                "geometry_source": "component",
                "source_component_id": "body"
              }
            ]
        """);

        var tree = PolyToolsCatalogImporter.Load(directory.Path).Resolve("tree");

        Assert.Equal(-1.01m, tree.BoundsMeters.MinimumX);
        Assert.Equal(0.01m, tree.BoundsMeters.MinimumY);
        Assert.Equal(1.02m, tree.BoundsMeters.MaximumX);
        Assert.Equal(2.03m, tree.BoundsMeters.MaximumY);
    }

    [Fact]
    public void ImportAcceptsARegionNameWhoseSnakeCaseSegmentStartsWithADigit()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game08_digits", regions: """
            [{
              "region_id": "region_component",
              "name": "collision_region_2",
              "role": "collision",
              "geometry_source": "component",
              "source_component_id": "body"
            }]
        """);

        var tree = PolyToolsCatalogImporter.Load(directory.Path).Resolve("tree");

        Assert.Equal("tree", tree.AssetKey);
    }

    [Theory]
    [MemberData(nameof(InvalidRegions))]
    public void ImportRejectsInvalidRegionVariants(string regions, string expectedMessage)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game09", regions: regions);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidRegions => new()
    {
        {
            "null",
            "requires array regions"
        },
        {
            """
            [{
              "region_id": "region_component",
              "name": "collision_region",
              "role": "collision",
              "geometry_source": "component",
              "source_component_id": "body",
              "vertices": [[0, 0], [1, 0], [0, 1]],
              "indices": [0, 1, 2]
            }]
            """,
            "must not contain vertices or indices"
        },
        {
            """
            [{
              "region_id": "region_authored",
              "name": "hurt_region",
              "role": "hurt",
              "geometry_source": "authored",
              "source_component_id": "body"
            }]
            """,
            "requires array vertices"
        },
        {
            """
            [{
              "region_id": "region_missing",
              "name": "collision_region",
              "role": "collision",
              "geometry_source": "component",
              "source_component_id": "missing"
            }]
            """,
            "references missing Component 'missing'"
        },
        {
            """
            [{
              "region_id": "region_unknown",
              "name": "collision_region",
              "role": "defence",
              "geometry_source": "component",
              "source_component_id": "body"
            }]
            """,
            "unsupported role 'defence'"
        },
    };

    [Fact]
    public void ImportRejectsComponentBoundRegionWithoutAnOrdinaryClosedGeometrySource()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "game10",
            treeComponents: """
                {
                  "component_id": "body",
                  "parent_component_id": null,
                  "kind": "asset_reference",
                  "source_asset_key": "grass",
                  "local_transform": {
                    "position": [0.0, 0.0],
                    "rotation_radians": 0.0,
                    "scale": [1.0, 1.0]
                  },
                  "mesh": null,
                  "contour_stroke_mesh": null
                }
            """,
            regions: """
                [{
                  "region_id": "region_component",
                  "name": "collision_region",
                  "role": "collision",
                  "geometry_source": "component",
                  "source_component_id": "body"
                }]
            """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains(
            "requires an ordinary source Component with closed geometry",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A Workspace config. <paramref name="waterCellMeters"/> defaults to the
    /// Terrain cell, the coarsest water grid the metrics allow, because most of
    /// these tests are about Terrain and Props and only need water to be valid.
    /// </summary>

    /// <summary>
    /// What a Placement is drawn as and what it occupies are two different
    /// boxes, and only the second decides whether something fits beside it.
    /// The model says which, by authoring collision Regions - more than one
    /// where it needs them, as an Ankh does - and their union is the answer.
    /// </summary>
    [Fact]
    public void APlacementOccupiesItsCollisionRegionsRatherThanItsFootprint()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "collide01",
            regions: """
                [
                  {
                    "region_id": "collision_0001",
                    "name": "trunk",
                    "role": "collision",
                    "geometry_source": "authored",
                    "source_component_id": "body",
                    "vertices": [[-0.2, 0.0], [0.2, 0.0], [0.2, 0.5]],
                    "indices": [0, 1, 2]
                  },
                  {
                    "region_id": "collision_0002",
                    "name": "buttress",
                    "role": "collision",
                    "geometry_source": "authored",
                    "source_component_id": "body",
                    "vertices": [[-0.4, 0.0], [0.1, 0.0], [0.1, 0.2]],
                    "indices": [0, 1, 2]
                  },
                  {
                    "region_id": "hurt_0001",
                    "name": "hurt_region",
                    "role": "hurt",
                    "geometry_source": "authored",
                    "source_component_id": "body",
                    "vertices": [[-9.0, 0.0], [9.0, 0.0], [9.0, 9.0]],
                    "indices": [0, 1, 2]
                  }
                ]
                """);
        WriteConfig(directory.Path, "collide01", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var tree = PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        // The crown still reaches from -1.01 to 1.02 m; the trunk does not.
        Assert.Equal(22, tree.FootprintWidthAuthoringPixels);

        // The union of both collision Regions, -0.4..0.2 by 0..0.5 m, rounded
        // outward. The hurt Region is not a collision Region and stays out of
        // it, however far it reaches.
        Assert.Equal(-4, tree.Collision!.OffsetXAuthoringPixels);
        Assert.Equal(0, tree.Collision!.OffsetYAuthoringPixels);
        Assert.Equal(6, tree.Collision!.WidthAuthoringPixels);
        Assert.Equal(5, tree.Collision!.HeightAuthoringPixels);
    }

    /// <summary>
    /// Two trees may stand close enough that their crowns overlap. Their
    /// trunks may not share a place. Before this rule the visible footprint
    /// decided both, which refused an arrangement the world is full of.
    /// </summary>
    [Fact]
    public void OverlappingFootprintsAreAllowedWhileOverlappingCollisionIsNot()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "collide02",
            regions: """
                [
                  {
                    "region_id": "collision_0001",
                    "name": "trunk",
                    "role": "collision",
                    "geometry_source": "authored",
                    "source_component_id": "body",
                    "vertices": [[-0.1, 0.0], [0.1, 0.0], [0.1, 0.2]],
                    "indices": [0, 1, 2]
                  }
                ]
                """);
        WriteConfig(directory.Path, "collide02", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var props = PropDisplayCatalogLoader.Load(catalog, workspace);
        var scene = PropEditing.Place(
            SceneDocument.CreateInstance("collide", 20, 20), props, 100, 100, "tree");

        // Five authoring pixels apart: the crowns overlap by far, the trunks
        // clear each other by one pixel.
        var beside = PropEditing.ValidateCandidate(scene, props, 105, 100, "tree");
        var onTop = PropEditing.ValidateCandidate(scene, props, 101, 100, "tree");

        Assert.True(beside.IsValid);
        Assert.False(onTop.IsValid);
        Assert.Contains("collides with", onTop.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refusing such an Asset was tried and taken back. Falling back to the
    /// visible footprint would still be wrong - a model whose collision nobody
    /// has drawn would quietly claim every pixel it is drawn with - but the
    /// third answer was missing: nothing. A bridge plank occupies nothing,
    /// because what takes space there is the bridge, and an Asset that says so
    /// is not an unfinished one.
    /// </summary>
    [Fact]
    public void APlacementWithoutACollisionRegionOccupiesNothing()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "collide03", regions: "[]");
        WriteConfig(directory.Path, "collide03", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var props = PropDisplayCatalogLoader.Load(catalog, workspace);

        Assert.Null(props.Resolve("tree").Collision);

        // And because it occupies nothing, nothing can be in its way - two of
        // them in the same place is a legal arrangement, not a collision.
        var scene = PropEditing.Place(
            SceneDocument.CreateInstance("collide", 20, 20), props, 100, 100, "tree");

        Assert.True(PropEditing.ValidateCandidate(scene, props, 100, 100, "tree").IsValid);
    }

    /// <summary>
    /// A Palette publishes Keys that may substitute for one another, not
    /// geometry. SceneMaker asks PolyTools only for Placements it can put
    /// somewhere, so being handed one is a mistake worth naming rather than a
    /// package that later fails on empty bounds.
    /// </summary>
    [Fact]
    public void APaletteIsRefusedRatherThanTreatedAsAPlaceableAsset()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "palette01", treeCategory: "palette");
        WriteConfig(directory.Path, "palette01", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path, ["tree"]));

        Assert.Contains("is a Palette", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Catalog states how an Asset is composed so a consumer can tell a
    /// Palette from a placeable Asset without opening its package. SceneMaker
    /// reads it for one decision - a Set is not offered as a Placement - and
    /// refuses a value it does not know rather than guessing single.
    /// </summary>
    [Fact]
    public void AnUnknownAssetCategoryIsRefused()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "category01");
        var catalogPath = Path.Combine(
            directory.Path,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName,
            PolyToolsCatalogImporter.CatalogFileName);
        File.WriteAllText(
            catalogPath,
            File.ReadAllText(catalogPath).Replace(
                "\"asset_category\": \"single\"",
                "\"asset_category\": \"bundle\"",
                StringComparison.Ordinal));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path, ["tree"]));

        Assert.Contains("unsupported asset_category", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Set publishes which Assets belong together, not a thing to place: its
    /// members lie centered on their own pivot, so the box it would be placed
    /// by is their overlap and means nothing. Configure the members.
    /// </summary>
    [Fact]
    public void ASetIsRefusedRatherThanPlacedByTheBoxItsMembersOverlapInto()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "set01", treeCategory: "set");
        WriteConfig(directory.Path, "set01", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "polytools_asset_id": "asset_tree" }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path, ["tree"]));

        Assert.Contains("is a Set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACatalogAndItsManifestMustAgreeOnHowAnAssetIsComposed()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "category02");
        var manifestPath = Path.Combine(
            directory.Path,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName,
            "PolyToolsRuntimeExports",
            "tree",
            "manifest.json");
        File.WriteAllText(
            manifestPath,
            File.ReadAllText(manifestPath).Replace(
                "\"asset_category\": \"single\"",
                "\"asset_category\": \"set\"",
                StringComparison.Ordinal));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path, ["tree"]));

        Assert.Contains("does not match its catalog entry", exception.Message, StringComparison.Ordinal);
    }

    private static void WriteConfig(
        string directory,
        string workspaceKey,
        decimal terrainCellMeters,
        decimal authoringPixelsPerMeter,
        decimal gamePixelsPerMeter,
        string assets,
        decimal? waterCellMeters = null,
        int version = WorkspaceConfigurationStore.Version,
        string? elevationQuantumJson = "0.125",
        string? minimumChannelDepthJson = "0.25")
    {
        var waterCell = waterCellMeters ?? terrainCellMeters;
        var quantumProperty = elevationQuantumJson is null
            ? string.Empty
            : $"\"elevation_quantum_meters\": {elevationQuantumJson},";
        var minimumDepthProperty = minimumChannelDepthJson is null
            ? string.Empty
            : $"\"minimum_channel_depth_meters\": {minimumChannelDepthJson},";
        File.WriteAllText(Path.Combine(directory, WorkspaceConfigurationStore.FileName), $$"""
        {
          "format": "scene_maker_workspace",
          "version": {{version}},
          "workspace_key": "{{workspaceKey}}",
          "grid": {
            "terrain_cell_meters": {{terrainCellMeters.ToString(CultureInfo.InvariantCulture)}},
            "authoring_pixels_per_meter": {{authoringPixelsPerMeter.ToString(CultureInfo.InvariantCulture)}},
            "game_pixels_per_meter": {{gamePixelsPerMeter.ToString(CultureInfo.InvariantCulture)}},
            {{quantumProperty}}
            {{minimumDepthProperty}}
            "water_cell_meters": {{waterCell.ToString(CultureInfo.InvariantCulture)}}
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
        int manifestSchema = PolyToolsCatalogImporter.ManifestSchemaVersion,
        string? regions = null,
        string treeCollisionComponentId = "body",
        string treeCategory = "single")
    {
        var importDirectory = Path.Combine(
            workspaceDirectory,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(Path.Combine(importDirectory, "catalog.json"), $$"""
        {
          "schema_version": {{PolyToolsCatalogImporter.CatalogSchemaVersion}},
          "world_key": "{{worldKey}}",
          "world_name": "Test World",
          "retired_assets": [],
          "assets": [
            {
              "asset_key": "grass",
              "asset_id": "asset_grass",
              "display_name": "Grass",
              "asset_type": "terrain",
              "asset_category": "single",
              "previous_keys": [],
              "runtime_package": "PolyToolsRuntimeExports/grass/manifest.json"
            },
            {
              "asset_key": "portal",
              "asset_id": "asset_portal",
              "display_name": "Portal",
              "asset_type": "props",
              "asset_category": "single",
              "previous_keys": [],
              "runtime_package": "PolyToolsRuntimeExports/portal/manifest.json"
            },
            {
              "asset_key": "tree",
              "asset_id": "asset_tree",
              "display_name": "Tree",
              "asset_type": "props",
              "asset_category": "{{treeCategory}}",
              "previous_keys": [],
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
            BasicComponent("[[-0.5, 0.0], [0.5, 2.0]]"),
            CollisionRegion("body", "[[-0.5, 0.0], [0.5, 0.0], [0.5, 2.0]]"));
        WriteManifest(
            importDirectory,
            "tree",
            "props",
            manifestSchema,
            treePivot,
            treeComponents ?? BasicComponent("[[-1.01, 0.01], [1.02, 2.03]]"),

            // A Placement must say what it occupies, so the default covers the
            // whole visible mesh: a fixture that has not thought about
            // collision then behaves exactly as it did before the rule.
            regions ?? CollisionRegion(
                treeCollisionComponentId,
                "[[-1.01, 0.01], [1.02, 0.01], [1.02, 2.03]]"),
            treeCategory);
    }

    private static string CollisionRegion(string sourceComponentId, string vertices) => $$"""
        [
          {
            "region_id": "collision_0001",
            "name": "collision_region",
            "role": "collision",
            "geometry_source": "authored",
            "source_component_id": "{{sourceComponentId}}",
            "vertices": {{vertices}},
            "indices": [0, 1, 2]
          }
        ]
        """;

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
        string components,
        string regions = "[]",
        string category = "single")
    {
        var directory = Path.Combine(
            importDirectory, "PolyToolsRuntimeExports", assetKey);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
        {
          "schema_version": {{schema}},
          "asset_key": "{{assetKey}}",
          "asset_id": "asset_{{assetKey}}",
          "display_name": "{{assetKey}}",
          "asset_type": "{{assetType}}",
          "asset_category": "{{category}}",
          "asset_pivot": {{assetPivot}},
          "components": [
            {{components}}
          ],
          "regions": {{regions}}
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
