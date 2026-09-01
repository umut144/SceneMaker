using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// One Prop covers exactly one Terrain cell in this fixture, so anchors 32
/// authoring pixels apart never overlap.
/// </summary>
public sealed class PropEditingTests
{
    [Fact]
    public void PlacedPropsCountUpPerAsset()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "portal");
        scene = PropEditing.Place(scene, workspace.Props, 64, 0, "stone");

        Assert.Equal(
            ["portal_0001", "stone_0001", "stone_0002"],
            scene.Props.Select(prop => prop.InstanceId));
    }

    [Fact]
    public void ErasingAPropFreesItsInstanceIdForTheNextOne()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 64, 0, "stone");

        scene = PropEditing.EraseAt(scene, workspace.Props, 32, 0);
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "stone");

        Assert.Equal(
            ["stone_0001", "stone_0002", "stone_0003"],
            scene.Props.Select(prop => prop.InstanceId));
    }

    [Fact]
    public void PropsStayInCanonicalOrderWhereverTheyArePlaced()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        scene = PropEditing.Place(scene, workspace.Props, 96, 96, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 32, 32, "portal");

        Assert.Equal(
            scene.Props.Select(prop => prop.InstanceId).Order(StringComparer.Ordinal),
            scene.Props.Select(prop => prop.InstanceId));
    }
}
