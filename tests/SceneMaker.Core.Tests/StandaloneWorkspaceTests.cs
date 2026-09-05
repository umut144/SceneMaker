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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" },
            { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF" }
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

    [Fact]
    public void SceneDocumentsPersistPolyToolsAssetKeysAndRespectDerivedFootprints()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game01");
        WriteConfig(directory.Path, "game01", 0.5m, 32m, 192m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" },
            { "asset_key": "portal", "display_name": "Portal", "role": "placement", "color": "#8E6CFF" }
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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
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
            new WorkspaceGridConfiguration(1m, 10m, 40m, 0.5m, 0.125m),
            [
                new WorkspaceAssetProfile(
                    "grass", "Grass", WorkspaceAssetRole.Terrain,
                    "#99E550", "land", TerrainAuthoring.Cells),
                new WorkspaceAssetProfile(
                    "portal", "Portal", WorkspaceAssetRole.Placement, "#8E6CFF"),
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
        Assert.Contains("\"elevation_quantum_meters\": 0.125", json, StringComparison.Ordinal);
        Assert.DoesNotContain("footprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("anchor", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authoring_role", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceLoadRequiresSynchronizedPolyToolsImport()
    {
        using var parent = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(parent.Path, "game04");

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
            version: 8);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains("version 9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceMetricsSnapElevationsSymmetricallyToTheirQuantum()
    {
        var metrics = new WorkspaceMetrics(
            new WorkspaceGridConfiguration(1m, 32m, 192m, 0.5m, 0.125m));

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

        var workspace = WorkspaceStore.Create(parent.Path, "game04b");

        Assert.True(Directory.Exists(
            Path.Combine(workspace.DirectoryPath, WorkspaceStore.ScenesDirectoryName)));
        Assert.True(Directory.Exists(
            Path.Combine(workspace.DirectoryPath, WorkspaceStore.TemplatesDirectoryName)));
        Assert.True(Directory.Exists(Path.Combine(
            workspace.DirectoryPath,
            PolyToolsCatalogImporter.ImportDirectoryName,
            PolyToolsCatalogImporter.PolyToolsDirectoryName)));
        Assert.True(File.Exists(
            Path.Combine(workspace.DirectoryPath, WorkspaceConfigurationStore.FileName)));
    }

    [Fact]
    public void CreatingAWorkspaceOverAnExistingFileFailsWithoutTouchingIt()
    {
        using var parent = TemporaryDirectory.Create();
        var occupied = Path.Combine(parent.Path, "game04c");
        File.WriteAllText(occupied, "not a Workspace");

        Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceStore.Create(parent.Path, "game04c"));

        Assert.Equal("not a Workspace", File.ReadAllText(occupied));
    }

    [Fact]
    public void ExportEmbedsDerivedSpatialSnapshotButNotEditorColors()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game05");
        WriteConfig(directory.Path, "game05", 0.5m, 32m, 128m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells" },
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var configuration = WorkspaceConfigurationStore.Load(directory.Path);
        var terrain = TerrainDisplayCatalogLoader.Load(configuration);
        var props = PropDisplayCatalogLoader.Load(catalog, configuration);
        var scene = TerrainEditing.Paint(
            SceneDocument.CreateInstance("field", 1, 1), terrain, 0, 0, "grass");
        var workspace = new LoadedWorkspace(directory.Path, "game05");
        var written = SceneExport.Write(
            workspace,
            new LoadedScene(Path.Combine(directory.Path, "scenes", "field.scene.json"), scene),
            configuration,
            terrain,
            props);
        var json = File.ReadAllText(written.Path);

        Assert.Contains("\"asset_key\": \"grass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"width\": 2.0625", json, StringComparison.Ordinal);
        Assert.DoesNotContain("#99E550", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(17)]
    public void ImportRejectsReplacedPolyToolsManifestSchemas(int schema)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "game06", manifestSchema: schema);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PolyToolsCatalogImporter.Load(directory.Path));

        Assert.Contains("schema_version 16", exception.Message, StringComparison.Ordinal);
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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var tree = PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        // The crown still reaches from -1.01 to 1.02 m; the trunk does not.
        Assert.Equal(22, tree.FootprintWidthAuthoringPixels);

        // The union of both collision Regions, -0.4..0.2 by 0..0.5 m, rounded
        // outward. The hurt Region is not a collision Region and stays out of
        // it, however far it reaches.
        Assert.Equal(-4, tree.Collision.OffsetXAuthoringPixels);
        Assert.Equal(0, tree.Collision.OffsetYAuthoringPixels);
        Assert.Equal(6, tree.Collision.WidthAuthoringPixels);
        Assert.Equal(5, tree.Collision.HeightAuthoringPixels);
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
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
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
    /// Falling back to the visible footprint would be a hidden default, and
    /// the wrong one: a model whose collision nobody has drawn yet would
    /// quietly claim every pixel it is drawn with. Naming the Asset and
    /// stopping is the honest answer, and adding the Region in PolyTools is
    /// the fix.
    /// </summary>
    [Fact]
    public void APlacementWithoutACollisionRegionIsRefused()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "collide03", regions: "[]");
        WriteConfig(directory.Path, "collide03", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PropDisplayCatalogLoader.Load(catalog, workspace));

        Assert.Contains(
            "has no PolyTools Region with the collision role",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A bridge sets posts, not whole bridges. The Workspace therefore names
    /// which Component of a Placement it means, and the import answers with
    /// that part's own box measured from the Placement's anchor - including
    /// everything hanging under it, because a part is what it covers together
    /// with its children.
    /// </summary>
    [Fact]
    public void AWorkspaceNamesTheComponentAPlacementOffersAsItsOwnPart()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(
            directory.Path,
            "parts01",
            treeComponents: NamedParts,
            regions: NamedPartsRegions);
        WriteConfig(directory.Path, "parts01", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "anchor_component": "post" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);
        var tree = PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree");

        // The whole Asset is unchanged: naming a part is a question about it,
        // not an edit to it.
        Assert.Equal(40, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(40, tree.FootprintHeightAuthoringPixels);

        var post = Assert.IsType<PropAnchorComponent>(tree.AnchorComponent);
        Assert.Equal("post", post.Name);
        Assert.Equal(10, post.OffsetXAuthoringPixels);
        Assert.Equal(10, post.OffsetYAuthoringPixels);
        Assert.Equal(10, post.WidthAuthoringPixels);

        // 10..30 rather than 10..20: the cap hanging under the post belongs to
        // it, so the part reaches as high as its children do.
        Assert.Equal(20, post.HeightAuthoringPixels);
    }

    [Fact]
    public void APlacementWithoutANamedComponentOffersNoPart()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "parts02", treeComponents: NamedParts);
        WriteConfig(directory.Path, "parts02", 1m, 10m, 40m, """
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32" }
        """);

        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);

        Assert.Null(PropDisplayCatalogLoader.Load(catalog, workspace).Resolve("tree").AnchorComponent);
    }

    /// <summary>
    /// Someone authored that name on purpose. Drawing the wrong part, or none,
    /// is worse than a load that says which name went missing - which is also
    /// what makes a rename in PolyTools a visible event rather than a silent
    /// change of what a bridge sets at its corners.
    /// </summary>
    [Theory]
    [InlineData("mast", "which its PolyTools geometry does not contain")]
    [InlineData("body", "which its PolyTools geometry contains 2 times")]
    public void AnAnchorComponentThatDoesNotNameExactlyOnePartIsRefused(
        string anchorComponent,
        string expectedMessage)
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "parts03", treeComponents: NamedParts);
        WriteConfig(directory.Path, "parts03", 1m, 10m, 40m, $$"""
            { "asset_key": "tree", "display_name": "Tree", "role": "placement", "color": "#2E7D32", "anchor_component": "{{anchorComponent}}" }
        """);
        var catalog = PolyToolsCatalogImporter.Load(directory.Path);
        var workspace = WorkspaceConfigurationStore.Load(directory.Path);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            PropDisplayCatalogLoader.Load(catalog, workspace));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TerrainMayNotNameAnAnchorComponent()
    {
        using var directory = TemporaryDirectory.Create();
        WritePolyToolsImport(directory.Path, "parts04");
        WriteConfig(directory.Path, "parts04", 1m, 10m, 40m, """
            { "asset_key": "grass", "display_name": "Grass", "role": "terrain", "color": "#99E550", "surface": "land", "authoring": "cells", "anchor_component": "post" }
        """);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceConfigurationStore.Load(directory.Path));

        Assert.Contains(
            "must not declare an anchor_component",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One Region for the whole Asset and one for the post, so the part can
    /// answer for itself rather than for everything the Asset is made of.
    /// </summary>
    private const string NamedPartsRegions = """
        [
          {
            "region_id": "collision_0001",
            "name": "body_collision",
            "role": "collision",
            "geometry_source": "authored",
            "source_component_id": "body",
            "vertices": [[0.0, 0.0], [4.0, 0.0], [4.0, 4.0]],
            "indices": [0, 1, 2]
          },
          {
            "region_id": "collision_0002",
            "name": "post_collision",
            "role": "collision",
            "geometry_source": "authored",
            "source_component_id": "post",
            "vertices": [[1.0, 1.0], [2.0, 1.0], [2.0, 2.0]],
            "indices": [0, 1, 2]
          }
        ]
        """;

    /// <summary>
    /// A body at 0..4 m, a post at 1..2 m inside it, and a cap under the post
    /// reaching to 3 m. Two Components share the name "body" so an ambiguous
    /// reference has something to be ambiguous about.
    /// </summary>
    private const string NamedParts = """
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
            "vertices": [[0.0, 0.0], [4.0, 4.0]],
            "indices": [0, 1, 1]
          },
          "contour_stroke_mesh": null
        },
        {
          "component_id": "body_two",
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
        },
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
            "vertices": [[1.0, 1.0], [2.0, 2.0]],
            "indices": [0, 1, 1]
          },
          "contour_stroke_mesh": null
        },
        {
          "component_id": "post_cap",
          "name": "post_cap",
          "parent_component_id": "post",
          "local_transform": {
            "position": [0.0, 0.0],
            "rotation_radians": 0.0,
            "scale": [1.0, 1.0]
          },
          "mesh": {
            "vertices": [[1.5, 2.0], [2.0, 3.0]],
            "indices": [0, 1, 1]
          },
          "contour_stroke_mesh": null
        }
        """;

    private static void WriteConfig(
        string directory,
        string workspaceKey,
        decimal terrainCellMeters,
        decimal authoringPixelsPerMeter,
        decimal gamePixelsPerMeter,
        string assets,
        decimal? waterCellMeters = null,
        int version = WorkspaceConfigurationStore.Version,
        string? elevationQuantumJson = "0.125")
    {
        var waterCell = waterCellMeters ?? terrainCellMeters;
        var quantumProperty = elevationQuantumJson is null
            ? string.Empty
            : $"\"elevation_quantum_meters\": {elevationQuantumJson},";
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
        string treeCollisionComponentId = "body")
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
                "[[-1.01, 0.01], [1.02, 0.01], [1.02, 2.03]]"));
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
        string regions = "[]")
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
