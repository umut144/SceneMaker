using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Declaring the switches a Scene has. Until these existed a switch was only
/// there if somebody wrote it into the document by hand, which meant a branch
/// could be drawn but not made switchable.
/// </summary>
public sealed class SwitchEditingTests
{
    [Fact]
    public void ASwitchIsDeclaredInCanonicalOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SwitchEditing.AddSwitch(River(workspace), "mill_gate", initiallyOn: false);
        scene = SwitchEditing.AddSwitch(scene, "east_gate", initiallyOn: true);

        Assert.Equal(
            ["east_gate", "mill_gate"],
            scene.Switches.Select(static declared => declared.Switch));
        Assert.True(scene.Switches[0].InitiallyOn);
        Assert.False(scene.Switches[1].InitiallyOn);
        DocumentValidation.Validate(scene);
    }

    [Fact]
    public void ANameThatIsNotAnIdOrIsAlreadyTakenIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        Assert.Throws<SceneMakerDocumentException>(
            () => SwitchEditing.AddSwitch(scene, "Mill Gate", initiallyOn: false));

        var declared = SwitchEditing.AddSwitch(scene, "mill_gate", initiallyOn: false);
        Assert.Throws<SceneMakerDocumentException>(
            () => SwitchEditing.AddSwitch(declared, "mill_gate", initiallyOn: true));
    }

    /// <summary>
    /// Where the Scene starts belongs to the switch, not to any body on it: two
    /// bodies on one switch must not be able to disagree about where the map
    /// opens.
    /// </summary>
    [Fact]
    public void WhereTheSceneStartsBelongsToTheSwitch()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SwitchEditing.AddSwitch(River(workspace), "mill_gate", initiallyOn: false);
        scene = WaterEditing.SetSwitch(scene, "river_0001", "mill_gate");
        Assert.False(WaterActivation.IsActive(scene, scene.WaterBodies[0]));

        var opened = SwitchEditing.SetInitiallyOn(scene, "mill_gate", initiallyOn: true);

        Assert.True(opened.Switches[0].InitiallyOn);
        Assert.True(WaterActivation.IsActive(opened, opened.WaterBodies[0]));
        Assert.Throws<SceneMakerDocumentException>(
            () => SwitchEditing.SetInitiallyOn(opened, "no_such_gate", initiallyOn: true));
        DocumentValidation.Validate(opened);
    }

    /// <summary>
    /// Removing a switch takes its members off it. Leaving them pointing at a
    /// switch that is gone would be a document no reader can resolve, and
    /// clearing them quietly is the kind of edit this project keeps out - so it
    /// happens here, in one operation.
    /// </summary>
    [Fact]
    public void RemovingASwitchLeavesItsMembersAlwaysThere()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SwitchEditing.AddSwitch(River(workspace), "mill_gate", initiallyOn: false);
        scene = WaterEditing.SetSwitch(scene, "river_0001", "mill_gate");
        Assert.Single(SwitchEditing.Members(scene, "mill_gate"));

        var without = SwitchEditing.RemoveSwitch(scene, "mill_gate");

        Assert.Empty(without.Switches);
        Assert.Null(without.WaterBodies[0].Switch);
        Assert.True(WaterActivation.IsActive(without, without.WaterBodies[0]));
        DocumentValidation.Validate(without);

        // A switch the Scene never had is not an error, it is nothing to do.
        Assert.Same(without, SwitchEditing.RemoveSwitch(without, "mill_gate"));
    }

    /// <summary>A body may only name a switch the Scene declares.</summary>
    [Fact]
    public void ABodyCannotNameASwitchTheSceneDoesNotDeclare()
    {
        using var workspace = TestWorkspace.Create();
        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.SetSwitch(River(workspace), "river_0001", "mill_gate"));
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
