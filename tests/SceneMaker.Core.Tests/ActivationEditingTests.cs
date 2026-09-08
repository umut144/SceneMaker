using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Declaring the states a Scene can be in. Until now a group existed only if
/// somebody wrote it into the document by hand, which meant a branch could be
/// drawn but not made switchable.
/// </summary>
public sealed class ActivationEditingTests
{
    [Fact]
    public void AGroupIsDeclaredInCanonicalOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ActivationEditing.AddGroup(
            River(workspace), "mill_gate", ["dry", "flowing"], "dry");
        scene = ActivationEditing.AddGroup(scene, "east_gate", ["shut", "open"], "shut");

        Assert.Equal(
            ["east_gate", "mill_gate"],
            scene.ActivationGroups.Select(static group => group.Group));
        // The authored order of the states is kept: initial_state names the one
        // it starts in rather than being the first of them.
        Assert.Equal(["shut", "open"], scene.ActivationGroups[0].States);
        DocumentValidation.Validate(scene);
    }

    [Fact]
    public void AGroupThatSwitchesNothingOrContradictsItselfIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        foreach (var refused in new (string Group, string[] States, string Initial)[]
                 {
                     ("mill_gate", ["dry"], "dry"),                       // one state switches nothing
                     ("mill_gate", ["dry", "dry"], "dry"),                // the same state twice
                     ("mill_gate", ["dry", "flowing"], "flooded"),        // an initial state it does not have
                     ("Mill Gate", ["dry", "flowing"], "dry"),            // not a stable id
                 })
        {
            Assert.Throws<SceneMakerDocumentException>(
                () => ActivationEditing.AddGroup(scene, refused.Group, refused.States, refused.Initial));
        }

        var declared = ActivationEditing.AddGroup(scene, "mill_gate", ["dry", "flowing"], "dry");
        Assert.Throws<SceneMakerDocumentException>(
            () => ActivationEditing.AddGroup(declared, "mill_gate", ["a", "b"], "a"));
    }

    /// <summary>
    /// Removing a group takes the activation of its members with it. Leaving
    /// them pointing at a group that is gone would be a document no reader can
    /// resolve, and clearing them quietly is the kind of edit this project keeps
    /// out - so it happens here, in one operation.
    /// </summary>
    [Fact]
    public void RemovingAGroupLeavesItsMembersExistingInEveryState()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ActivationEditing.AddGroup(
            River(workspace), "mill_gate", ["dry", "flowing"], "dry");
        scene = WaterEditing.SetActivation(
            scene,
            "river_0001",
            new WaterActivationDocument
            {
                Group = "mill_gate", ActiveIn = ["flowing"], Inactive = WaterInactive.DryBed,
            });
        Assert.Single(ActivationEditing.Members(scene, "mill_gate"));

        var without = ActivationEditing.RemoveGroup(scene, "mill_gate");

        Assert.Empty(without.ActivationGroups);
        Assert.Null(without.WaterBodies[0].Activation);
        Assert.True(WaterActivation.IsActive(without, without.WaterBodies[0]));
        DocumentValidation.Validate(without);

        // A group the Scene never had is not an error, it is nothing to do.
        Assert.Same(without, ActivationEditing.RemoveGroup(without, "mill_gate"));
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
