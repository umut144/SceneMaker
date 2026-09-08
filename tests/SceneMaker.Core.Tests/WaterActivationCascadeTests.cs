using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A branch is fed by the body it leaves, so switching a river off takes
/// everything hanging under it with it. The tree this runs down is the
/// junctions - nothing about it is authored twice.
/// </summary>
public sealed class WaterActivationCascadeTests
{
    /// <summary>
    /// The trunk, a branch of it, and a branch of that: three levels, which is
    /// where a rule about parents stops being the same as a rule about one
    /// parent.
    /// </summary>
    private static SceneDocument Tree(TestWorkspace workspace)
    {
        var scene = TestScenes.Instance(workspace, sizeCells: 30);
        foreach (var (fromX, fromY, toX, toY) in new[]
                 {
                     (32, 32, 288, 32),    // river_0001, the trunk
                     (96, 32, 96, 160),    // river_0002, a branch of it
                     (96, 96, 224, 96),    // river_0003, a branch of that
                 })
        {
            scene = WaterEditing.PlaceRiver(
                scene,
                workspace.Terrain,
                [
                    WaterEditing.Point(fromX, fromY, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                    WaterEditing.Point(toX, toY, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                ],
                "river");
        }
        scene = scene with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "lower_fork", States = ["shut", "flowing"], InitialState = "shut",
                },
                new ActivationGroupDocument
                {
                    Group = "upper_fork", States = ["shut", "flowing"], InitialState = "flowing",
                },
            ],
        };
        scene = WaterEditing.SetJunctions(
            scene, "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);
        scene = WaterEditing.SetJunctions(
            scene, "river_0003",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0002" }]);
        scene = WaterEditing.SetActivation(
            scene, "river_0002",
            new WaterActivationDocument
            {
                Group = "upper_fork", ActiveIn = ["flowing"], Inactive = WaterInactive.DryBed,
            });
        return WaterEditing.SetActivation(
            scene, "river_0003",
            new WaterActivationDocument
            {
                Group = "lower_fork", ActiveIn = ["flowing"], Inactive = WaterInactive.DryBed,
            });
    }

    [Fact]
    public void ABranchOfABranchNeedsBothOfThemFlowing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);
        DocumentValidation.Validate(scene);

        // As the Scene starts: upper_fork flowing, lower_fork shut.
        Assert.Equal(
            ["river_0001", "river_0002"],
            WaterActivation.ActiveBodies(scene).Select(static body => body.WaterBodyId));

        // Its own group opened, and it is there because its feeder still is.
        Assert.Equal(
            ["river_0001", "river_0002", "river_0003"],
            WaterActivation
                .ActiveBodies(scene, new Dictionary<string, string> { ["lower_fork"] = "flowing" })
                .Select(static body => body.WaterBodyId));
    }

    /// <summary>
    /// The point of the rule: a branch whose feeder is gone is gone, however
    /// its own group stands. Nothing repeats the parent's states on the child.
    /// </summary>
    [Fact]
    public void ShuttingABranchTakesEverythingUnderItWithIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);

        var states = new Dictionary<string, string>
        {
            ["upper_fork"] = "shut",
            ["lower_fork"] = "flowing",
        };

        Assert.Equal(
            ["river_0001"],
            WaterActivation.ActiveBodies(scene, states).Select(static body => body.WaterBodyId));
        Assert.False(WaterActivation.IsActive(scene, scene.WaterBodies[2], states));
    }

    /// <summary>
    /// Two rivers running together feed the one below: it has water as soon as
    /// either of them does, because that is what water does.
    /// </summary>
    [Fact]
    public void ABodyFedByTwoIsThereAsSoonAsEitherIs()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);
        scene = WaterEditing.SetJunctions(
            scene, "river_0003",
            [
                new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" },
                new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0002" },
            ]);
        scene = WaterEditing.SetActivation(scene, "river_0003", null);

        Assert.True(WaterActivation.IsActive(
            scene,
            scene.WaterBodies[2],
            new Dictionary<string, string> { ["upper_fork"] = "shut" }));
    }

    [Fact]
    public void ABodyFedAroundTheJunctionsByItselfIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Tree(workspace);
        var ringed = WaterEditing.SetJunctions(
            scene, "river_0001",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0003" }]);

        var error = Assert.Throws<SceneMakerDocumentException>(
            () => DocumentValidation.Validate(ringed));
        Assert.Contains("by itself", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A body that names no group is there whenever what feeds it is, which is
    /// how a river split at a fork keeps behaving like one river.
    /// </summary>
    [Fact]
    public void ABodyWithNoActivationStillFollowsItsFeeder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.SetActivation(Tree(workspace), "river_0003", null);

        Assert.False(WaterActivation.IsActive(
            scene,
            scene.WaterBodies[2],
            new Dictionary<string, string> { ["upper_fork"] = "shut" }));
    }
}
