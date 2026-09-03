using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Behaviour of <see cref="TemplateComposition"/>: deterministic Template
/// selection, Terrain mask semantics and the composition rules.
///
/// A Template replaces everything inside its Terrain mask, so every Prop of the
/// base Scene whose footprint intersects the mask is dropped. Before Placements
/// and Transitions were merged into Prop, a Transition under the mask was
/// rejected instead; that exception no longer exists.
/// </summary>
public sealed class TemplateCompositionTests
{
    [Fact]
    public void ComposeIsDeterministicForTheSameSeed()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.Instance(workspace), workspace, 64, 64);
        var templates = new[]
        {
            TestScenes.Template(workspace, "tpl_a", 1),
            TestScenes.Template(workspace, "tpl_b", 1),
        };

        var first = Compose(baseScene, templates, workspace, seed: 42UL);
        var second = Compose(baseScene, templates, workspace, seed: 42UL);

        Assert.Equal(first.Selections, second.Selections);
        Assert.Equal(
            TerrainSignature(first.ComposedScene),
            TerrainSignature(second.ComposedScene));
    }

    [Fact]
    public void SeedSelectsDifferentTemplatesFromTheSamePool()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.Instance(workspace), workspace, 64, 64);
        var templates = new[]
        {
            TestScenes.Template(workspace, "tpl_a", 1),
            TestScenes.Template(workspace, "tpl_b", 1),
        };

        HashSet<string> selected = new(StringComparer.Ordinal);
        for (var seed = 0UL; seed < 50UL; seed++)
        {
            selected.Add(Compose(baseScene, templates, workspace, seed)
                .Selections
                .Single()
                .TemplateSceneId);
        }

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void AnchorsOfOneGroupSelectDistinctTemplates()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = TestScenes.Instance(workspace);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        baseScene = WithAnchor(baseScene, workspace, 128, 128);
        var templates = new[]
        {
            TestScenes.Template(workspace, "tpl_a", 1),
            TestScenes.Template(workspace, "tpl_b", 1),
        };

        var selections = Compose(baseScene, templates, workspace, seed: 7UL).Selections;

        Assert.Equal(2, selections.Count);
        Assert.Equal(
            new[] { "template_anchor_001", "template_anchor_002" },
            selections.Select(static selection => selection.AnchorId).ToArray());
        Assert.Equal(
            new[] { "tpl_a", "tpl_b" },
            selections.Select(static selection => selection.TemplateSceneId).Order().ToArray());
    }

    [Fact]
    public void AnAnchorWithNoTemplateLeftStaysEmpty()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = TestScenes.Instance(workspace);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        baseScene = WithAnchor(baseScene, workspace, 128, 128);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var selections = Compose(baseScene, templates, workspace, seed: 1UL).Selections;

        // One Anchor is filled, the other is not. In the game an Anchor is an
        // event slot the server decides about, so an empty one is ordinary.
        Assert.Equal("tpl_a", Assert.Single(selections).TemplateSceneId);
    }

    [Fact]
    public void AGroupWithoutAnyTemplateFillsNothing()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(
            TestScenes.Instance(workspace), workspace, 64, 64, groupNumber: 2);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var composed = Compose(baseScene, templates, workspace, seed: 1UL);

        Assert.Empty(composed.Selections);
        Assert.Empty(composed.EffectiveTerrainMasks);
        // The base Scene comes through untouched rather than being refused.
        Assert.Equal(baseScene.TerrainCells.Count, composed.ComposedScene.TerrainCells.Count);
    }

    [Fact]
    public void TemplateTerrainOverwritesBaseTerrainInsideItsMask()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.Instance(workspace), workspace, 64, 64);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var composed = Compose(baseScene, templates, workspace, seed: 3UL).ComposedScene;

        Assert.Equal("sand", TerrainAt(composed, 2, 2));
        Assert.Equal("sand", TerrainAt(composed, 3, 2));
        Assert.Equal("sand", TerrainAt(composed, 2, 3));
        Assert.Equal("sand", TerrainAt(composed, 3, 3));
        Assert.Equal("grass", TerrainAt(composed, 1, 1));
        Assert.Equal("grass", TerrainAt(composed, 4, 4));
        Assert.Equal(36, composed.TerrainCells.Count);
    }

    [Fact]
    public void PropsUnderTheTemplateMaskAreRemoved()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = TestScenes.Instance(workspace);
        baseScene = Place(baseScene, workspace, 64, 64);
        baseScene = Place(baseScene, workspace, 0, 0);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var composed = Compose(baseScene, templates, workspace, seed: 5UL).ComposedScene;

        Assert.Equal(2, baseScene.Props.Count);
        var remaining = Assert.Single(composed.Props);
        Assert.Equal("stone_0002", remaining.InstanceId);
        Assert.Equal(0, remaining.PositionAuthoringPx.X);
    }

    [Fact]
    public void ComposedInstanceIdsAreDerivedFromAnchorAndTemplate()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.Instance(workspace), workspace, 64, 64);
        var template = Place(TestScenes.Template(workspace, "tpl_a", 1), workspace, 0, 0);

        var composed = Compose(
            baseScene, new[] { template }, workspace, seed: 11UL).ComposedScene;

        var prop = Assert.Single(composed.Props);
        Assert.Equal("template_anchor_001.tpl_a.stone_0001", prop.InstanceId);
        Assert.Equal(64, prop.PositionAuthoringPx.X);
        Assert.Equal(64, prop.PositionAuthoringPx.Y);
    }

    [Fact]
    public void EffectiveTerrainMasksExcludeCellsClaimedByALaterTemplate()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = TestScenes.Instance(workspace);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        baseScene = WithAnchor(baseScene, workspace, 96, 96);
        var templates = new[]
        {
            TestScenes.Template(workspace, "tpl_a", 1),
            TestScenes.Template(workspace, "tpl_b", 1),
        };

        var masks = Compose(baseScene, templates, workspace, seed: 13UL).EffectiveTerrainMasks;

        Assert.Equal(2, masks.Count);
        Assert.Equal("template_anchor_001", masks[0].AnchorId);
        Assert.Equal("template_anchor_002", masks[1].AnchorId);
        Assert.Equal(3, masks[0].Cells.Count);
        Assert.Equal(4, masks[1].Cells.Count);
        Assert.DoesNotContain(masks[0].Cells, cell => cell.X == 3 && cell.Y == 3);
        Assert.Contains(masks[1].Cells, cell => cell.X == 3 && cell.Y == 3);
    }

    [Fact]
    public void TemplateReachingOutsideTheSceneIsRejected()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.Instance(workspace), workspace, 160, 160);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => Compose(baseScene, templates, workspace, seed: 2UL));

        Assert.Contains(
            "lies outside Scene Instance",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OnlySceneInstancesCanReceiveTemplates()
    {
        using var workspace = TestWorkspace.Create();
        var template = TestScenes.Template(workspace, "tpl_a", 1);

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => Compose(template, new[] { template }, workspace, seed: 0UL));

        Assert.Contains("Only a Scene Instance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneWithoutAnchorsKeepsItsAuthoredContent()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = Place(TestScenes.Instance(workspace), workspace, 0, 0);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var result = Compose(baseScene, templates, workspace, seed: 17UL);

        Assert.Empty(result.Selections);
        Assert.Empty(result.EffectiveTerrainMasks);
        Assert.Equal(TerrainSignature(baseScene), TerrainSignature(result.ComposedScene));
        Assert.Equal("stone_0001", Assert.Single(result.ComposedScene.Props).InstanceId);
    }

    [Fact]
    public void ComposeDoesNotMutateItsSources()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = TestScenes.Instance(workspace);
        baseScene = Place(baseScene, workspace, 64, 64);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        var template = TestScenes.Template(workspace, "tpl_a", 1);
        var baseTerrain = TerrainSignature(baseScene);

        _ = Compose(baseScene, new[] { template }, workspace, seed: 23UL);

        Assert.Equal(baseTerrain, TerrainSignature(baseScene));
        Assert.Equal("stone_0001", Assert.Single(baseScene.Props).InstanceId);
        Assert.Equal(4, template.TerrainCells.Count);
    }

    /// <summary>
    /// Composition moves Terrain cells and Props; it does not ask whether a Prop
    /// has ground. A Prop standing free in the base Scene therefore survives
    /// composition instead of failing it.
    /// </summary>
    [Fact]
    public void ComposingKeepsAPropThatStandsOnNoTerrain()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = WithAnchor(TestScenes.EmptyInstance(), workspace, 64, 64);
        baseScene = PropEditing.Place(baseScene, workspace.Props, 0, 0, "stone", 4.0m);

        var composed = Compose(
            baseScene,
            [TestScenes.Template(workspace, "tpl_a", 1)],
            workspace,
            seed: 7UL).ComposedScene;

        var prop = Assert.Single(
            composed.Props,
            candidate => candidate.InstanceId == "stone_0001");
        Assert.Equal(4.0m, prop.ElevationMeters);
        // The Template brought the only Terrain there is, and none of it is
        // under that Prop.
        Assert.Equal(4, composed.TerrainCells.Count);
        Assert.DoesNotContain(composed.TerrainCells, cell => cell is { X: 0, Y: 0 });
    }

    /// <summary>
    /// The base Scene is folded before the Templates replace anything, and the
    /// bodies do not survive into the composed Scene. A Template therefore
    /// brings its own heights into a hole a mountain made and is not lifted
    /// again afterwards - the replacement is the last word on the cells it
    /// covers.
    /// </summary>
    [Fact]
    public void TemplateCellsAreNotRaisedAgainByTheBaseScenesMountains()
    {
        using var workspace = TestWorkspace.Create();
        var baseScene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            [
                MountainEditing.Point(32, 32),
                MountainEditing.Point(160, 32),
                MountainEditing.Point(160, 160),
                MountainEditing.Point(32, 160),
            ],
            10.0m);
        baseScene = WithAnchor(baseScene, workspace, 64, 64);
        var templates = new[] { TestScenes.Template(workspace, "tpl_a", 1) };

        var composed = Compose(baseScene, templates, workspace, seed: 3UL).ComposedScene;

        Assert.Empty(composed.MountainBodies);
        // Inside the mask: the Template's own sand at the Template's own height.
        Assert.All(
            composed.TerrainCells.Where(static cell =>
                cell.X is 2 or 3 && cell.Y is 2 or 3),
            static cell =>
            {
                Assert.Equal("sand", cell.AssetKey);
                Assert.Equal(1.0m, cell.ElevationMeters);
            });
        // Outside it, the fold that happened first still stands.
        Assert.Equal(
            10.0m,
            composed.TerrainCells.Single(static cell => cell is { X: 1, Y: 1 }).ElevationMeters);
        Assert.Equal(12, composed.TerrainCells.Count(static cell => cell.ElevationMeters == 10.0m));
        Assert.Equal(36, composed.TerrainCells.Count);
    }

    private static TemplateCompositionResult Compose(
        SceneDocument baseScene,
        IEnumerable<SceneDocument> workspaceScenes,
        TestWorkspace workspace,
        ulong seed) =>
        TemplateComposition.Compose(
            baseScene,
            workspaceScenes,
            workspace.Props,
            seed);

    private static SceneDocument WithAnchor(
        SceneDocument scene,
        TestWorkspace workspace,
        int authoringX,
        int authoringY,
        int groupNumber = 1) =>
        TemplateEditing.PlaceAnchor(
            scene, workspace.Metrics, authoringX, authoringY, groupNumber);

    private static SceneDocument Place(
        SceneDocument scene,
        TestWorkspace workspace,
        int authoringX,
        int authoringY,
        string assetKey = "stone") =>
        PropEditing.Place(
            scene,
            workspace.Props,
            authoringX,
            authoringY,
            assetKey);

    private static string TerrainAt(SceneDocument scene, int cellX, int cellY) =>
        scene.TerrainCells.Single(cell => cell.X == cellX && cell.Y == cellY).AssetKey;

    private static string[] TerrainSignature(SceneDocument scene) =>
        scene.TerrainCells
            .Select(static cell => $"{cell.X}:{cell.Y}:{cell.AssetKey}")
            .ToArray();
}
