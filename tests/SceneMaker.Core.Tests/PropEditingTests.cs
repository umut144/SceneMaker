using System.Text.Json;
using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// One Prop covers exactly one Terrain cell in this fixture, so anchors 32
/// authoring pixels apart never overlap.
///
/// <para>What a Prop does <em>not</em> need is ground. It carries an absolute
/// elevation and is allowed to stand free, so nothing between placement and the
/// written export asks what is under its footprint. What stays checked is what
/// geometry alone can answer: the footprint is representable, it lies inside the
/// Scene, and it meets no other Prop.</para>
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

    [Fact]
    public void APropCanBePlacedOnASceneWithoutAnyTerrain()
    {
        using var workspace = TestWorkspace.Create();

        var scene = PropEditing.Place(
            TestScenes.EmptyInstance(), workspace.Props, 64, 64, "stone", 4.0m);

        var prop = Assert.Single(scene.Props);
        Assert.Equal("stone_0001", prop.InstanceId);
        Assert.Equal(4.0m, prop.ElevationMeters);
        Assert.Empty(scene.TerrainCells);
        Assert.True(
            PropEditing.ValidateCandidate(
                TestScenes.EmptyInstance(), workspace.Props, 64, 64, "stone").IsValid);
    }

    [Fact]
    public void ASceneWhoseOnlyPropStandsOnNothingExportsWithoutAWarning()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.EmptyInstance(), workspace.Props, 64, 64, "stone", 4.0m);

        var written = Export(workspace, scene);

        Assert.Empty(written.Warnings);
        using var parsed = JsonDocument.Parse(File.ReadAllText(written.Path));
        var exported = parsed.RootElement.GetProperty("scene");
        Assert.Empty(exported.GetProperty("terrain_cells").EnumerateArray());
        Assert.Equal(
            "stone_0001",
            Assert.Single(exported.GetProperty("props").EnumerateArray())
                .GetProperty("instance_id").GetString());
    }

    [Fact]
    public void ErasingThePaintedCellUnderAPropLeavesTheSceneExportable()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 2, 2, "grass");
        scene = PropEditing.Place(scene, workspace.Props, 64, 64, "stone");

        scene = TerrainEditing.Erase(scene, 2, 2);

        Assert.Empty(scene.TerrainCells);
        Assert.Empty(Export(workspace, scene).Warnings);
    }

    [Fact]
    public void RemovingTheElevationRegionUnderAPropLeavesTheSceneExportable()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);
        scene = PropEditing.Place(scene, workspace.Props, 64, 64, "stone", 10.0m);

        scene = ElevationRegionEditing.Remove(scene, "mountain_0001");

        Assert.Empty(scene.ElevationRegions);
        Assert.Empty(Export(workspace, scene).Warnings);
    }

    [Fact]
    public void AFootprintReachingOutsideTheSceneIsStillRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        var validation = PropEditing.ValidateCandidate(
            scene, workspace.Props, -32, 0, "stone");

        Assert.False(validation.IsValid);
        Assert.Contains("outside the Scene bounds", validation.Reason!, StringComparison.Ordinal);
        Assert.Throws<SceneMakerDocumentException>(() =>
            PropEditing.Place(scene, workspace.Props, -32, 0, "stone"));
    }

    [Fact]
    public void APlacementMeetingWhatAnotherOccupiesIsStillRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.EmptyInstance(), workspace.Props, 64, 64, "stone");

        var validation = PropEditing.ValidateCandidate(
            scene, workspace.Props, 64, 64, "stone");

        Assert.False(validation.IsValid);
        Assert.Contains("collides with 'stone_0001'", validation.Reason!, StringComparison.Ordinal);
        Assert.Throws<SceneMakerDocumentException>(() =>
            PropEditing.Place(scene, workspace.Props, 64, 64, "stone"));
    }

    private static SceneExportResult Export(TestWorkspace workspace, SceneDocument scene)
    {
        var session = WorkspaceSession.Load(workspace.RootPath);
        return SceneExport.Write(
            session,
            workspace.Game,
            new LoadedScene(
                Path.Combine(workspace.Game.ScenesDirectoryPath, "base.scene.json"),
                scene));
    }

    /// <summary>A closed square contour, in authoring pixels.</summary>
    private static IReadOnlyList<ElevationRegionPointDocument> Square(
        int left,
        int bottom,
        int right,
        int top) =>
    [
        ElevationRegionEditing.Point(left, bottom),
        ElevationRegionEditing.Point(right, bottom),
        ElevationRegionEditing.Point(right, top),
        ElevationRegionEditing.Point(left, top),
    ];
}
