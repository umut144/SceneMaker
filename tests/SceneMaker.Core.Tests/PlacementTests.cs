using System.Text.Json.Nodes;
using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class PlacementTests
{
    private static string DisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "placement_display.json");

    private static string CatalogPath => Path.Combine(
        AppContext.BaseDirectory,
        "world_assets",
        "world_assets.json");

    private static string TransitionDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "transition_display.json");

    private static string TerrainDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "terrain_display.json");

    private static TransitionDisplayCatalog LoadTransitions() =>
        TransitionDisplayCatalogLoader.Load(TransitionDisplayPath, CatalogPath);

    [Fact]
    public void TreeSpatialContractDerivesAcceptedFootprintAndBottomCenterAnchor()
    {
        var displayJson = JsonNode.Parse(File.ReadAllText(DisplayPath))!.AsObject();
        var entry = displayJson["assets"]!.AsArray().Single()!.AsObject();
        Assert.Equal(["asset_id", "color"], entry.Select(static property => property.Key));

        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var tree = catalog.Resolve(2);

        Assert.Equal("tree", tree.Key);
        Assert.Equal("Tree", tree.Name);
        Assert.Equal(3.5m, tree.WidthMeters);
        Assert.Equal(6.5m, tree.HeightMeters);
        Assert.Equal(1.75m, tree.AnchorXMeters);
        Assert.Equal(0m, tree.AnchorYMeters);
        Assert.Equal(112, tree.FootprintWidthAuthoringPixels);
        Assert.Equal(208, tree.FootprintHeightAuthoringPixels);
        Assert.Equal(56, tree.AnchorXAuthoringPixels);
        Assert.Equal(0, tree.AnchorYAuthoringPixels);
    }

    [Fact]
    public void TreeBandAllowsTouchingFootprintsAndPreservesExactAnchorDelta()
    {
        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var scene = SceneDocument.Create("scene.tree_band", 30, 30);

        scene = PlacementEditing.Place(scene, catalog, transitions, 69, 27, 2);
        scene = PlacementEditing.Place(scene, catalog, transitions, 181, 27, 2);

        Assert.Equal(
            [(69, 27), (181, 27)],
            scene.Placements.Select(static placement =>
                (placement.PositionAuthoringPx.X, placement.PositionAuthoringPx.Y)));
        Assert.Equal(112, scene.Placements[1].PositionAuthoringPx.X
            - scene.Placements[0].PositionAuthoringPx.X);
        Assert.Equal(3.5m, PlacementEditing.PositionMeters(112));
        Assert.Equal(448, PlacementEditing.PositionGamePixels(112));
    }

    [Fact]
    public void LineDrawBuildsHorizontalTreeBandAtExactFootprintIntervals()
    {
        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var scene = PlacementEditing.PlaceLine(
            SceneDocument.Create("scene.line_band", 30, 30),
            catalog,
            transitions,
            56,
            16,
            300,
            16,
            2);

        Assert.Equal(
            [(56, 16), (168, 16), (280, 16)],
            scene.Placements.Select(static placement =>
                (placement.PositionAuthoringPx.X, placement.PositionAuthoringPx.Y)));
    }

    [Fact]
    public void SelectorAndEraserUsePlacementFootprintWithoutMovingItsAnchor()
    {
        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var scene = PlacementEditing.Place(
            SceneDocument.Create("scene.erase", 30, 30),
            catalog,
            transitions,
            69,
            27,
            2);

        var selected = PlacementEditing.FindAt(scene, catalog, 13, 27);
        var untouched = PlacementEditing.EraseAt(scene, catalog, 12, 27);
        var erased = PlacementEditing.EraseAt(scene, catalog, 13, 27);

        Assert.NotNull(selected);
        Assert.Equal((69, 27),
            (selected.PositionAuthoringPx.X, selected.PositionAuthoringPx.Y));
        Assert.Same(scene, untouched);
        Assert.Empty(erased.Placements);
    }

    [Fact]
    public void PlacementRejectsOverlapAndOutOfSceneFootprints()
    {
        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var scene = SceneDocument.Create("scene.tree_bounds", 30, 30);
        scene = PlacementEditing.Place(scene, catalog, transitions, 56, 0, 2);

        Assert.Throws<SceneMakerDocumentException>(() =>
            PlacementEditing.Place(scene, catalog, transitions, 167, 0, 2));
        Assert.Throws<SceneMakerDocumentException>(() =>
            PlacementEditing.Place(scene, catalog, transitions, 55, 0, 2));
        Assert.Throws<SceneMakerDocumentException>(() =>
            PlacementEditing.Place(scene, catalog, transitions, 56, 273, 2));

        var touching = PlacementEditing.ValidateCandidate(scene, catalog, transitions, 168, 0, 2);
        var occupied = PlacementEditing.ValidateCandidate(scene, catalog, transitions, 167, 0, 2);
        var outside = PlacementEditing.ValidateCandidate(scene, catalog, transitions, 55, 0, 2);

        Assert.True(touching.IsValid);
        Assert.False(occupied.IsValid);
        Assert.Contains("overlaps", occupied.Reason);
        Assert.False(outside.IsValid);
        Assert.Contains("outside", outside.Reason);
    }

    [Fact]
    public void MissingTerrainWarnsButDoesNotBlockPlacementAuthoring()
    {
        var placements = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var terrain = TerrainDisplayCatalogLoader.Load(TerrainDisplayPath, CatalogPath);
        var empty = SceneDocument.Create("scene.coverage_warning", 30, 30);

        var warning = PlacementEditing.ValidateCandidate(
            empty,
            placements,
            transitions,
            69,
            27,
            2);
        var authored = PlacementEditing.Place(empty, placements, transitions, 69, 27, 2);
        var covered = TerrainEditing.Fill(authored, terrain, 0, 0, 1006);
        var valid = PlacementEditing.ValidateCandidate(
            covered with { Placements = [] },
            placements,
            transitions,
            69,
            27,
            2);

        Assert.True(warning.IsValid);
        Assert.False(warning.HasCompleteTerrain);
        Assert.Contains("lacks Terrain", warning.Warning);
        Assert.Single(authored.Placements);
        Assert.True(valid.IsValid);
        Assert.True(valid.HasCompleteTerrain);
        Assert.Null(valid.Warning);
    }

    [Fact]
    public void PlacementSaveLoadPreservesNonCellAlignedAnchorExactly()
    {
        var catalog = PlacementDisplayCatalogLoader.Load(DisplayPath, CatalogPath);
        var transitions = LoadTransitions();
        var scene = PlacementEditing.Place(
            SceneDocument.Create("scene.tree_round_trip", 30, 30),
            catalog,
            transitions,
            69,
            27,
            2);

        var serialized = DocumentJson.Serialize(scene);
        var loaded = DocumentJson.DeserializeScene(serialized);
        PlacementEditing.ValidateAssetReferences(loaded, catalog, transitions);

        Assert.Equal(serialized, DocumentJson.Serialize(loaded));
        Assert.Equal(69, loaded.Placements.Single().PositionAuthoringPx.X);
        Assert.Equal(27, loaded.Placements.Single().PositionAuthoringPx.Y);
    }
}
