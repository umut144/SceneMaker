using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class TemplateCompositionTests
{
    private static string TerrainDisplayPath => Path.Combine(
        AppContext.BaseDirectory, "config", "terrain_display.json");
    private static string PlacementDisplayPath => Path.Combine(
        AppContext.BaseDirectory, "config", "placement_display.json");
    private static string TransitionDisplayPath => Path.Combine(
        AppContext.BaseDirectory, "config", "transition_display.json");
    private static string CatalogPath => Path.Combine(
        AppContext.BaseDirectory, "world_assets", "world_assets.json");

    [Fact]
    public void SelectionIsSeededAndDrawsWithoutReplacementPerGroup()
    {
        var catalogs = LoadCatalogs();
        var baseScene = TemplateEditing.PlaceAnchor(
            TemplateEditing.PlaceAnchor(FilledInstance("world", 40, 40, catalogs), 0, 0, 1),
            160,
            0,
            1);
        var templates = new[]
        {
            Template("template.a", 1, 0, 1003),
            Template("template.b", 1, 1, 1006),
            Template("template.c", 1, 2, 1003),
        };

        var first = TemplateComposition.Compose(
            baseScene, templates, catalogs.Placements, catalogs.Transitions, 982_451_653UL);
        var again = TemplateComposition.Compose(
            baseScene, templates, catalogs.Placements, catalogs.Transitions, 982_451_653UL);

        Assert.Equal(first.Selections, again.Selections);
        Assert.Equal(2, first.Selections.Count);
        Assert.Equal(2, first.Selections.Select(static selection => selection.TemplateSceneId).Distinct().Count());
        Assert.Equal(
            ["template_anchor_001", "template_anchor_002"],
            first.Selections.Select(static selection => selection.AnchorId));
        Assert.Equal(first.Selections.Select(static selection => selection.AnchorId),
            first.EffectiveTerrainMasks.Select(static mask => mask.AnchorId));
        Assert.All(first.EffectiveTerrainMasks, static mask => Assert.NotEmpty(mask.Cells));
    }

    [Fact]
    public void TemplateTerrainIsTheExactNonDestructiveReplacementMask()
    {
        var catalogs = LoadCatalogs();
        var baseScene = FilledInstance("world", 30, 30, catalogs);
        baseScene = PlacementEditing.Place(
            baseScene, catalogs.Placements, catalogs.Transitions, 56, 0, 2);
        baseScene = TemplateEditing.PlaceAnchor(baseScene, 0, 0, 1);

        var preservingTemplate = Template("small_patch", 1, 20, 1006);
        var preserved = TemplateComposition.Compose(
            baseScene,
            [preservingTemplate],
            catalogs.Placements,
            catalogs.Transitions,
            1UL);
        Assert.Single(preserved.ComposedScene.Placements);
        Assert.Equal(1003u, TerrainAt(preserved.ComposedScene, 0, 0));
        Assert.Equal(1006u, TerrainAt(preserved.ComposedScene, 20, 0));

        var replacingTemplate = Template("wide_patch", 1, 0, 1006);
        var replaced = TemplateComposition.Compose(
            baseScene,
            [replacingTemplate],
            catalogs.Placements,
            catalogs.Transitions,
            1UL);
        Assert.Empty(replaced.ComposedScene.Placements);
        Assert.Single(baseScene.Placements);
        Assert.Equal(1003u, TerrainAt(baseScene, 0, 0));
        Assert.Equal(1006u, TerrainAt(replaced.ComposedScene, 0, 0));
    }

    [Fact]
    public void TemplateContentIsTranslatedAtAnchorAndCanReplaceMaskedBaseContent()
    {
        var catalogs = LoadCatalogs();
        var baseScene = FilledInstance("world", 40, 40, catalogs);
        baseScene = PlacementEditing.Place(
            baseScene, catalogs.Placements, catalogs.Transitions, 56, 0, 2);
        baseScene = TemplateEditing.PlaceAnchor(baseScene, 0, 0, 1);

        var template = FilledTemplate("tree_patch", 1, 30, 30, catalogs);
        template = PlacementEditing.Place(
            template,
            catalogs.Placements,
            catalogs.Transitions,
            168,
            0,
            2);
        var result = TemplateComposition.Compose(
            baseScene,
            [template],
            catalogs.Placements,
            catalogs.Transitions,
            7UL);

        var tree = Assert.Single(result.ComposedScene.Placements);
        Assert.Equal("template_anchor_001.tree_patch.tree_0001", tree.InstanceId);
        Assert.Equal((168, 0), (tree.PositionAuthoringPx.X, tree.PositionAuthoringPx.Y));
        Assert.Single(baseScene.Placements);
    }

    [Fact]
    public void LaterAnchorIdWinsWhenSelectedTemplateMasksOverlap()
    {
        var catalogs = LoadCatalogs();
        var baseScene = TemplateEditing.PlaceAnchor(
            TemplateEditing.PlaceAnchor(FilledInstance("world", 10, 10, catalogs), 0, 0, 1),
            0,
            0,
            1);
        var result = TemplateComposition.Compose(
            baseScene,
            [
                Template("grass_patch", 1, 0, 1003),
                Template("water_patch", 1, 0, 1006),
            ],
            catalogs.Placements,
            catalogs.Transitions,
            42UL);

        var laterTemplate = result.Selections.Single(selection =>
            selection.AnchorId == "template_anchor_002").TemplateSceneId;
        var expected = laterTemplate == "grass_patch" ? 1003u : 1006u;
        Assert.Equal(expected, TerrainAt(result.ComposedScene, 0, 0));
        Assert.Empty(result.EffectiveTerrainMasks.Single(mask =>
            mask.AnchorId == "template_anchor_001").Cells);
        Assert.Single(result.EffectiveTerrainMasks.Single(mask =>
            mask.AnchorId == "template_anchor_002").Cells);
    }

    [Fact]
    public void ComposerRejectsInsufficientPoolsOutOfBoundsTerrainAndPortalOverwrite()
    {
        var catalogs = LoadCatalogs();
        var insufficient = TemplateEditing.PlaceAnchor(
            TemplateEditing.PlaceAnchor(FilledInstance("world", 40, 40, catalogs), 0, 0, 1),
            16,
            0,
            1);
        var oneTemplate = Template("only_patch", 1, 0, 1003);
        var poolError = Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateComposition.Compose(
                insufficient,
                [oneTemplate],
                catalogs.Placements,
                catalogs.Transitions,
                1UL));
        Assert.Contains("without replacement", poolError.Message);

        var outOfBounds = TemplateEditing.PlaceAnchor(
            FilledInstance("small_world", 2, 2, catalogs), 32, 0, 1);
        var terrainError = Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateComposition.Compose(
                outOfBounds,
                [Template("outside_patch", 1, 1, 1003)],
                catalogs.Placements,
                catalogs.Transitions,
                1UL));
        Assert.Contains("outside Scene Instance", terrainError.Message);

        var portalWorld = TransitionEditing.Place(
            FilledInstance("portal_world", 20, 20, catalogs),
            catalogs.Placements,
            catalogs.Transitions,
            32,
            0,
            503);
        portalWorld = TemplateEditing.PlaceAnchor(portalWorld, 0, 0, 1);
        var portalError = Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateComposition.Compose(
                portalWorld,
                [Template("portal_mask", 1, 0, 1003)],
                catalogs.Placements,
                catalogs.Transitions,
                1UL));
        Assert.Contains("overwrite Portal", portalError.Message);
    }

    private static SceneDocument FilledInstance(
        string sceneId,
        int width,
        int height,
        Catalogs catalogs) => TerrainEditing.Fill(
        SceneDocument.Create(sceneId, width, height),
        catalogs.Terrain,
        0,
        0,
        1003);

    private static SceneDocument FilledTemplate(
        string sceneId,
        int groupNumber,
        int width,
        int height,
        Catalogs catalogs) => TerrainEditing.Fill(
        SceneDocument.Create(sceneId, width, height, SceneKind.Template, groupNumber),
        catalogs.Terrain,
        0,
        0,
        1003);

    private static SceneDocument Template(
        string sceneId,
        int groupNumber,
        int terrainX,
        uint assetId) => SceneDocument.Create(
        sceneId,
        30,
        30,
        SceneKind.Template,
        groupNumber) with
    {
        TerrainCells =
        [
            new TerrainCellDocument { X = terrainX, Y = 0, AssetId = assetId },
        ],
    };

    private static uint TerrainAt(SceneDocument scene, int x, int y) => scene.TerrainCells
        .Single(cell => cell.X == x && cell.Y == y)
        .AssetId;

    private static Catalogs LoadCatalogs() => new(
        TerrainDisplayCatalogLoader.Load(TerrainDisplayPath, CatalogPath),
        PlacementDisplayCatalogLoader.Load(PlacementDisplayPath, CatalogPath),
        TransitionDisplayCatalogLoader.Load(TransitionDisplayPath, CatalogPath));

    private sealed record Catalogs(
        TerrainDisplayCatalog Terrain,
        PlacementDisplayCatalog Placements,
        TransitionDisplayCatalog Transitions);
}
