using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Whether a body still hangs on something. This is the question the Canvas
/// colours by, and it is deliberately not the question
/// <see cref="WaterEditing.BrokenJunctions"/> answers: deleting one branch has
/// to turn that branch and everything under it red and leave the river they all
/// hung on alone.
/// </summary>
public sealed class WaterAttachmentTests
{
    [Fact]
    public void ARiverThatClaimsNoSourceIsItsOwnSpring()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);

        Assert.True(WaterAttachment.IsAttached(
            scene, workspace.Metrics, Body(scene, "river_0001")));
        Assert.Empty(WaterAttachment.DetachedBodies(scene, workspace.Metrics));
    }

    [Fact]
    public void ABranchHangsOnTheBodyItLeaves()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);

        Assert.True(WaterAttachment.IsAttached(
            scene, workspace.Metrics, Body(scene, "river_0002")));
        Assert.True(WaterAttachment.IsAttached(
            scene, workspace.Metrics, Body(scene, "river_0003")));
    }

    /// <summary>
    /// The case the author hit: delete the branch a second branch hangs on. The
    /// river is untouched and has to stay untouched - it was the whole river
    /// turning red that made the old document-wide answer useless.
    /// </summary>
    [Fact]
    public void DeletingTheFeederLoosensTheBranchAndNothingElse()
    {
        using var workspace = TestWorkspace.Create();
        var without = WaterEditing.Remove(Tree(workspace), "river_0002");

        Assert.True(WaterAttachment.IsAttached(
            without, workspace.Metrics, Body(without, "river_0001")));
        Assert.False(WaterAttachment.IsAttached(
            without, workspace.Metrics, Body(without, "river_0003")));
        Assert.Equal(
            ["river_0003"],
            WaterAttachment.DetachedBodies(without, workspace.Metrics)
                .Select(static body => body.WaterBodyId));
    }

    /// <summary>
    /// A body whose own claim still holds perfectly can still be loose, because
    /// what it hangs on is loose. Nothing local can see that, which is why the
    /// answer is a walk and not a check.
    /// </summary>
    [Fact]
    public void BeingLooseRunsDownTheTree()
    {
        using var workspace = TestWorkspace.Create();
        var without = WaterEditing.Remove(Tree(workspace), "river_0001");

        Assert.DoesNotContain(
            WaterEditing.BrokenJunctions(without, workspace.Metrics),
            entry => string.Equals(entry.WaterBodyId, "river_0003", StringComparison.Ordinal));
        Assert.Equal(
            ["river_0002", "river_0003"],
            WaterAttachment.DetachedBodies(without, workspace.Metrics)
                .Select(static body => body.WaterBodyId));
    }

    /// <summary>
    /// Pulled apart rather than deleted: the partner is still in the Scene, the
    /// two just no longer touch. Same answer, and again only for the body that
    /// came off.
    /// </summary>
    [Fact]
    public void ASourcePulledOffItsParentComesLooseAndTheParentDoesNot()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);
        var branch = Body(scene, "river_0002");
        var pulled = WaterEditing.Reshape(
            scene,
            "river_0002",
            [
                WaterEditing.Point(32, 400, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                branch.Points[1],
            ]);

        Assert.True(WaterAttachment.IsAttached(
            pulled, workspace.Metrics, Body(pulled, "river_0001")));
        Assert.False(WaterAttachment.IsAttached(
            pulled, workspace.Metrics, Body(pulled, "river_0002")));
    }

    /// <summary>
    /// A height that drifts too far is the same break as a source pulled away,
    /// so it loosens the body in the same way - the export refuses the junction
    /// for exactly this reason.
    /// </summary>
    [Fact]
    public void AHeightThatDriftsTooFarLoosensTheBranchToo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);
        var branch = Body(scene, "river_0002");
        var lowered = WaterEditing.Reshape(
            scene,
            "river_0002",
            [branch.Points[0] with { ElevationMeters = 1.0m }, branch.Points[1]]);

        Assert.False(WaterAttachment.IsAttached(
            lowered, workspace.Metrics, Body(lowered, "river_0002")));
    }

    private static WaterBodyDocument Body(SceneDocument scene, string waterBodyId) =>
        scene.WaterBodies.Single(body =>
            string.Equals(body.WaterBodyId, waterBodyId, StringComparison.Ordinal));

    /// <summary>
    /// A river along y = 32, a branch leaving it at x = 96, and a second branch
    /// leaving that one at y = 96 - the shape the author had on screen.
    /// </summary>
    private static SceneDocument Tree(TestWorkspace workspace)
    {
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(96, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(96, 160, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
        scene = WaterEditing.SetJunctions(
            scene,
            "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(96, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
        return WaterEditing.SetJunctions(
            scene,
            "river_0003",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0002" }]);
    }
}
