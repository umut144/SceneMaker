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
    public void ErasingAPropNeverFreesItsInstanceIdForALaterOne()
    {
        // world01 keeps a per-map design file that names a Placement by its
        // instance_id and outlives that Placement, so a later Placement taking
        // the same number would make the reference silently resolve to the
        // wrong thing. Erasing must never move the counter back down, even
        // when the erased Placement was the asset's only living one.
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 64, 0, "stone");

        scene = PropEditing.EraseAt(scene, workspace.Props, 32, 0);
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "stone");

        Assert.Equal(
            ["stone_0001", "stone_0003", "stone_0004"],
            scene.Props.Select(prop => prop.InstanceId));
    }

    [Fact]
    public void ErasingEveryLivingPropOfAnAssetStillNeverReusesItsNumbers()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");

        scene = PropEditing.EraseAt(scene, workspace.Props, 0, 0);
        Assert.Empty(scene.Props);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");

        Assert.Equal("stone_0002", Assert.Single(scene.Props).InstanceId);
    }

    /// <summary>
    /// The counter is what carries the non-reuse guarantee across a save and a
    /// later load, not just within one in-memory session - requirement (2) of
    /// the world01 request: the same counter has to survive save/load and
    /// re-export so a Prop keeps its instance_id for its whole life.
    /// </summary>
    [Fact]
    public void TheAllocationCounterSurvivesAJsonRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone");
        scene = PropEditing.Place(scene, workspace.Props, 32, 0, "stone");
        scene = PropEditing.EraseAt(scene, workspace.Props, 32, 0);

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));
        restored = PropEditing.Place(restored, workspace.Props, 32, 0, "stone");

        Assert.Equal(
            ["stone_0001", "stone_0003"],
            restored.Props.Select(prop => prop.InstanceId));
    }

    /// <summary>
    /// A document where a counter has fallen behind a number a Prop already
    /// carries could still open and export today and only start handing out a
    /// reused instance_id on some later Placement. Validation refuses it
    /// outright instead, so the failure is loud at load time.
    /// </summary>
    [Fact]
    public void ASceneWhoseCounterHasFallenBehindASpentNumberIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.EmptyInstance(), workspace.Props, 0, 0, "stone");
        var corrupted = scene with
        {
            PropInstanceCounters =
            [
                new PropInstanceCounterDocument { AssetKey = "stone", NextIndex = 1 },
            ],
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => DocumentJson.Serialize(corrupted));

        Assert.Contains("has not caught up to", exception.Message, StringComparison.Ordinal);
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
