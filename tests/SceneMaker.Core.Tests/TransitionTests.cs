using System.Text.Json.Nodes;
using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class TransitionTests
{
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

    private static string TerrainDisplayPath => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "terrain_display.json");

    [Fact]
    public void PortalSpatialContractDerivesAcceptedFootprintAndBottomCenterAnchor()
    {
        var displayJson = JsonNode.Parse(File.ReadAllText(TransitionDisplayPath))!.AsObject();
        var entry = displayJson["assets"]!.AsArray().Single()!.AsObject();
        Assert.Equal(["asset_id", "color"], entry.Select(static property => property.Key));

        var portal = LoadTransitions().Resolve(503);

        Assert.Equal("portal", portal.Key);
        Assert.Equal("Portal", portal.Name);
        Assert.Equal(2m, portal.WidthMeters);
        Assert.Equal(3m, portal.HeightMeters);
        Assert.Equal(1m, portal.AnchorXMeters);
        Assert.Equal(0m, portal.AnchorYMeters);
        Assert.Equal(64, portal.FootprintWidthAuthoringPixels);
        Assert.Equal(96, portal.FootprintHeightAuthoringPixels);
        Assert.Equal(32, portal.AnchorXAuthoringPixels);
        Assert.Equal(0, portal.AnchorYAuthoringPixels);
    }

    [Fact]
    public void PortalsAllowTouchingButRejectEverySpatialOverlap()
    {
        var placements = LoadPlacements();
        var transitions = LoadTransitions();
        var scene = PlacementEditing.Place(
            SceneDocument.Create("scene.portal_overlap", 30, 30),
            placements,
            transitions,
            56,
            0,
            2);

        scene = TransitionEditing.Place(scene, placements, transitions, 144, 0, 503);
        scene = TransitionEditing.Place(scene, placements, transitions, 208, 0, 503);

        Assert.False(TransitionEditing.ValidateCandidate(
            scene,
            placements,
            transitions,
            143,
            0,
            503).IsValid);
        Assert.False(TransitionEditing.ValidateCandidate(
            scene,
            placements,
            transitions,
            144,
            1,
            503).IsValid);
        Assert.False(PlacementEditing.ValidateCandidate(
            scene,
            placements,
            transitions,
            232,
            0,
            2).IsValid);

        PlacementEditing.ValidateAssetReferences(scene, placements, transitions);
        TransitionEditing.ValidateAssetReferences(scene, placements, transitions);
        Assert.Equal(2, scene.Transitions.Count);
    }

    [Fact]
    public void PortalRejectsOutOfBoundsAndPreservesExactAnchorOnSaveLoad()
    {
        var placements = LoadPlacements();
        var transitions = LoadTransitions();
        var empty = SceneDocument.Create("scene.portal_bounds", 20, 20);

        Assert.False(TransitionEditing.ValidateCandidate(
            empty,
            placements,
            transitions,
            31,
            0,
            503).IsValid);
        Assert.False(TransitionEditing.ValidateCandidate(
            empty,
            placements,
            transitions,
            32,
            225,
            503).IsValid);

        var scene = TransitionEditing.Place(empty, placements, transitions, 53, 27, 503);
        var json = DocumentJson.Serialize(scene);
        var loaded = DocumentJson.DeserializeScene(json);
        TransitionEditing.ValidateAssetReferences(loaded, placements, transitions);

        Assert.Equal(json, DocumentJson.Serialize(loaded));
        var portal = Assert.Single(loaded.Transitions);
        Assert.Equal("portal_0001", portal.InstanceId);
        Assert.Equal((53, 27),
            (portal.PositionAuthoringPx.X, portal.PositionAuthoringPx.Y));
    }

    [Fact]
    public void MissingTerrainWarnsButDoesNotBlockTransitionAuthoring()
    {
        var placements = LoadPlacements();
        var transitions = LoadTransitions();
        var terrain = TerrainDisplayCatalogLoader.Load(TerrainDisplayPath, CatalogPath);
        var empty = SceneDocument.Create("scene.portal_coverage", 20, 20);

        var warning = TransitionEditing.ValidateCandidate(
            empty,
            placements,
            transitions,
            32,
            0,
            503);
        var authored = TransitionEditing.Place(empty, placements, transitions, 32, 0, 503);
        var covered = TerrainEditing.Fill(authored, terrain, 0, 0, 1003);
        var valid = TransitionEditing.ValidateCandidate(
            covered with { Transitions = [] },
            placements,
            transitions,
            32,
            0,
            503);

        Assert.True(warning.IsValid);
        Assert.False(warning.HasCompleteTerrain);
        Assert.Contains("lacks Terrain", warning.Warning);
        Assert.Single(authored.Transitions);
        Assert.True(valid.HasCompleteTerrain);
    }

    [Fact]
    public void SelectorEraserAndLineUsePortalFootprints()
    {
        var placements = LoadPlacements();
        var transitions = LoadTransitions();
        var scene = TransitionEditing.PlaceLine(
            SceneDocument.Create("scene.portal_tools", 30, 30),
            placements,
            transitions,
            32,
            0,
            170,
            0,
            503);

        Assert.Equal(
            [(32, 0), (96, 0), (160, 0)],
            scene.Transitions.Select(static transition =>
                (transition.PositionAuthoringPx.X, transition.PositionAuthoringPx.Y)));
        Assert.NotNull(TransitionEditing.FindAt(scene, transitions, 0, 0));

        var erased = TransitionEditing.EraseAt(scene, transitions, 0, 0);
        Assert.Equal(2, erased.Transitions.Count);
    }

    private static PlacementDisplayCatalog LoadPlacements() =>
        PlacementDisplayCatalogLoader.Load(PlacementDisplayPath, CatalogPath);

    private static TransitionDisplayCatalog LoadTransitions() =>
        TransitionDisplayCatalogLoader.Load(TransitionDisplayPath, CatalogPath);
}
