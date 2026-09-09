using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// Hiding an object is a way of looking. The same filtered Scene answers what
/// is drawn and what a press can take hold of, and neither of those is allowed
/// to reach the document.
/// </summary>
public sealed class SceneVisibilityTests
{
    [Fact]
    public void HidingNothingHandsBackTheSameScene()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        Assert.Same(scene, SceneVisibility.Without(scene, []));
    }

    [Fact]
    public void AHiddenBodyIsGoneFromTheViewAndStillInTheDocument()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        var seen = SceneVisibility.Without(scene, ["river_0001"]);

        Assert.Empty(seen.WaterBodies);
        Assert.Single(scene.WaterBodies);
        Assert.Equal(scene.TerrainCells.Count, seen.TerrainCells.Count);
        Assert.Equal(scene.SceneId, seen.SceneId);
    }

    [Fact]
    public void AnIdThatNamesNothingChangesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        var seen = SceneVisibility.Without(scene, ["river_0404"]);

        Assert.Single(seen.WaterBodies);
    }

    private static SceneDocument River(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
}
