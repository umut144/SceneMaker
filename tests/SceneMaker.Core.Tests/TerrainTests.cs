using System.Text.Json.Nodes;
using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class TerrainTests
{
    private static string DisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "terrain_display.json");

    private static string CatalogPath => Path.Combine(
        AppContext.BaseDirectory,
        "world_assets",
        "world_assets.json");

    [Fact]
    public void DisplayDataResolvesGrassAndWaterFromCanonicalWorldAssets()
    {
        var displayJson = JsonNode.Parse(File.ReadAllText(DisplayPath))!.AsObject();
        var entries = displayJson["assets"]!.AsArray();
        Assert.All(entries, entry => Assert.Equal(
            ["asset_id", "color"],
            entry!.AsObject().Select(static property => property.Key).ToArray()));

        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);

        Assert.Equal([1003U, 1006U], catalog.Assets.Select(static asset => asset.AssetId));
        Assert.Equal("grass", catalog.Resolve(1003).Key);
        Assert.Equal("Grass", catalog.Resolve(1003).Name);
        Assert.Equal("#99E550", catalog.Resolve(1003).Color);
        Assert.Equal("water", catalog.Resolve(1006).Key);
        Assert.Equal("Water", catalog.Resolve(1006).Name);
        Assert.Equal("#5B6EE1", catalog.Resolve(1006).Color);
        Assert.Throws<SceneMakerDocumentException>(() => catalog.Resolve(1002));
    }

    [Fact]
    public void DisplayDataRejectsUnknownFieldsAndNonTerrainAssets()
    {
        using var temporary = TemporaryDirectory.Create();
        var unknownFieldPath = Path.Combine(temporary.Path, "unknown.json");
        File.WriteAllText(unknownFieldPath, """
            {
              "schema": "srt.scene_maker_terrain_display",
              "version": 1,
              "assets": [{"asset_id": 1003, "color": "#99E550", "name": "Grass"}]
            }
            """);
        var placementPath = Path.Combine(temporary.Path, "placement.json");
        File.WriteAllText(placementPath, """
            {
              "schema": "srt.scene_maker_terrain_display",
              "version": 1,
              "assets": [{"asset_id": 2, "color": "#99E550"}]
            }
            """);

        Assert.Throws<SceneMakerDocumentException>(() =>
            TerrainDisplayCatalogLoader.Load(unknownFieldPath, CatalogPath));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TerrainDisplayCatalogLoader.Load(placementPath, CatalogPath));
    }

    [Fact]
    public void PaintIsCellBoundedOverwritesAndOrdersCanonically()
    {
        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var scene = SceneDocument.Create("scene.terrain", 4, 3);

        scene = TerrainEditing.Paint(scene, catalog, 3, 2, 1006);
        scene = TerrainEditing.Paint(scene, catalog, 0, 0, 1003);
        scene = TerrainEditing.Paint(scene, catalog, 2, 1, 1003);
        scene = TerrainEditing.Paint(scene, catalog, 2, 1, 1006);

        Assert.Equal(
            [(0, 0, 1003U), (2, 1, 1006U), (3, 2, 1006U)],
            scene.TerrainCells.Select(static cell => (cell.X, cell.Y, cell.AssetId)));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TerrainEditing.Paint(scene, catalog, -1, 0, 1003));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TerrainEditing.Paint(scene, catalog, 4, 0, 1003));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TerrainEditing.Paint(scene, catalog, 0, 0, 1002));
    }

    [Fact]
    public void FillUsesFourWayConnectivityAndOtherTerrainAsBoundary()
    {
        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var scene = SceneDocument.Create("scene.fill", 5, 4);
        for (var y = 0; y < scene.SizeCells.Height; y++)
            scene = TerrainEditing.Paint(scene, catalog, 2, y, 1006);

        var filled = TerrainEditing.Fill(scene, catalog, 0, 0, 1003);

        Assert.All(
            filled.TerrainCells.Where(static cell => cell.X < 2),
            static cell => Assert.Equal(1003U, cell.AssetId));
        Assert.Equal(8, filled.TerrainCells.Count(static cell => cell.AssetId == 1003));
        Assert.Equal(4, filled.TerrainCells.Count(static cell => cell.AssetId == 1006));
        Assert.DoesNotContain(filled.TerrainCells, static cell => cell.X > 2);

        var replaced = TerrainEditing.Fill(filled, catalog, 2, 1, 1003);
        Assert.Equal(12, replaced.TerrainCells.Count(static cell => cell.AssetId == 1003));
        Assert.Same(replaced, TerrainEditing.Fill(replaced, catalog, 2, 1, 1003));
    }

    [Fact]
    public void FillDoesNotCrossDiagonalContactAndHandlesLargeEmptyRegionsIteratively()
    {
        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var diagonal = SceneDocument.Create("scene.diagonal", 3, 3);
        diagonal = TerrainEditing.Paint(diagonal, catalog, 1, 0, 1006);
        diagonal = TerrainEditing.Paint(diagonal, catalog, 0, 1, 1006);

        var isolated = TerrainEditing.Fill(diagonal, catalog, 0, 0, 1003);
        Assert.Single(isolated.TerrainCells, static cell => cell.AssetId == 1003);

        var large = TerrainEditing.Fill(
            SceneDocument.Create("scene.large_fill", 200, 200),
            catalog,
            0,
            0,
            1003);
        Assert.Equal(40_000, large.TerrainCells.Count);
        Assert.Equal((0, 0), (large.TerrainCells[0].X, large.TerrainCells[0].Y));
        Assert.Equal((199, 199), (large.TerrainCells[^1].X, large.TerrainCells[^1].Y));
    }

    [Fact]
    public void TerrainEraseCreatesAnExplicitEmptyCellWithoutChangingOtherTerrain()
    {
        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var scene = TerrainEditing.Fill(
            SceneDocument.Create("scene.erase", 3, 2),
            catalog,
            0,
            0,
            1003);

        var erased = TerrainEditing.Erase(scene, 1, 0);

        Assert.Equal(5, erased.TerrainCells.Count);
        Assert.DoesNotContain(erased.TerrainCells, static cell => cell.X == 1 && cell.Y == 0);
        Assert.Same(erased, TerrainEditing.Erase(erased, 1, 0));
        Assert.Throws<SceneMakerDocumentException>(() => TerrainEditing.Erase(erased, -1, 0));
    }

    [Fact]
    public void CoverageUsesEveryCellIntersectedByNonAlignedAuthoringFootprint()
    {
        var bounds = new PlacementBoundsAuthoringPixels(13, 27, 112, 208);

        var cells = TerrainCoverage.IntersectedCells(bounds);

        Assert.Equal(112, cells.Count);
        Assert.Contains(new TerrainCellCoordinate(0, 1), cells);
        Assert.Contains(new TerrainCellCoordinate(7, 14), cells);
        Assert.DoesNotContain(new TerrainCellCoordinate(8, 14), cells);
        Assert.DoesNotContain(new TerrainCellCoordinate(0, 15), cells);
    }

    [Fact]
    public void ScreenMappingUsesCellGridAfterPanAndZoom()
    {
        var view = new CanvasViewState(panX: 32, panY: 32, zoom: 2);

        Assert.Equal((0, 3), view.ScreenToTerrainCell(32, 32, sceneHeightCells: 4));
        Assert.Equal((1, 3), view.ScreenToTerrainCell(64, 32, sceneHeightCells: 4));
        Assert.Equal((2, 0), view.ScreenToTerrainCell(112, 144, sceneHeightCells: 4));
        Assert.Equal((-1, 4), view.ScreenToTerrainCell(31, 31, sceneHeightCells: 4));
    }

    [Fact]
    public void PaintedSceneSaveLoadSaveIsByteStable()
    {
        using var temporary = TemporaryDirectory.Create();
        var catalog = TerrainDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var workspace = WorkspaceStore.Create(temporary.Path, "terrain_workspace");
        var created = SceneStore.Create(workspace, "scene.paint", 5, 4);
        var painted = created with
        {
            Document = TerrainEditing.Paint(
                TerrainEditing.Paint(created.Document, catalog, 4, 3, 1006),
                catalog,
                1,
                0,
                1003),
        };
        SceneStore.Save(workspace, painted);
        var firstBytes = File.ReadAllBytes(painted.FilePath);

        var loaded = SceneStore.Load(workspace, painted.FilePath);
        TerrainEditing.ValidateAssetReferences(loaded.Document, catalog);
        SceneStore.Save(workspace, loaded);

        Assert.Equal(firstBytes, File.ReadAllBytes(painted.FilePath));
        Assert.Equal(
            [(1, 0, 1003U), (4, 3, 1006U)],
            loaded.Document.TerrainCells.Select(static cell => (cell.X, cell.Y, cell.AssetId)));
    }

    [Fact]
    public void SceneValidationRejectsDuplicateUnorderedAndOutOfBoundsTerrain()
    {
        var duplicate = SceneDocument.Create("scene.invalid", 2, 2) with
        {
            TerrainCells =
            [
                new TerrainCellDocument { X = 0, Y = 0, AssetId = 1003 },
                new TerrainCellDocument { X = 0, Y = 0, AssetId = 1006 },
            ],
        };
        var unordered = duplicate with
        {
            TerrainCells =
            [
                new TerrainCellDocument { X = 1, Y = 1, AssetId = 1003 },
                new TerrainCellDocument { X = 0, Y = 0, AssetId = 1006 },
            ],
        };
        var outside = duplicate with
        {
            TerrainCells = [new TerrainCellDocument { X = 2, Y = 0, AssetId = 1003 }],
        };

        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(duplicate));
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(unordered));
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(outside));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"scene-maker-terrain-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
