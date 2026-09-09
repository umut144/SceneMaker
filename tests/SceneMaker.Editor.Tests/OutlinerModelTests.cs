using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// What the Outliner shows. It is the other half of the Inspector - which
/// objects are there, against what the chosen one has - and for water it is the
/// only place the junction tree is visible as a tree.
/// </summary>
public sealed class OutlinerModelTests
{
    [Fact]
    public void ABranchHangsUnderTheBodyItLeaves()
    {
        using var workspace = TestWorkspace.Create();
        var entries = OutlinerModel.Build(
            Chained(workspace), workspace.Metrics, EditorMode.River);

        Assert.Equal(
            [("river_0001", 0), ("river_0002", 1), ("river_0003", 2)],
            entries.Select(entry => (entry.ObjectId, entry.Depth)));
        Assert.All(entries, entry => Assert.False(entry.Loose));
    }

    /// <summary>
    /// Delete the middle body and the branch under it has nothing to hang from,
    /// so it goes back to the top of the list and says why. The one that is
    /// still attached is left alone - the same scoping the Canvas colour got.
    /// </summary>
    [Fact]
    public void ABranchWhoseFeederIsGoneIsListedLooseAtTheTop()
    {
        using var workspace = TestWorkspace.Create();
        var without = WaterEditing.Remove(Chained(workspace), "river_0002");
        var entries = OutlinerModel.Build(without, workspace.Metrics, EditorMode.River);

        Assert.Equal(
            [("river_0001", 0), ("river_0003", 0)],
            entries.Select(entry => (entry.ObjectId, entry.Depth)));
        Assert.False(entries[0].Loose);
        Assert.True(entries[1].Loose);
    }

    /// <summary>
    /// A body under a loose one still nests - its own claim is fine - and is
    /// loose all the same, which is exactly what the walk is for.
    /// </summary>
    [Fact]
    public void EverythingUnderALooseBodyIsLooseAndStillNests()
    {
        using var workspace = TestWorkspace.Create();
        var without = WaterEditing.Remove(Chained(workspace), "river_0001");
        var entries = OutlinerModel.Build(without, workspace.Metrics, EditorMode.River);

        Assert.Equal(
            [("river_0002", 0), ("river_0003", 1)],
            entries.Select(entry => (entry.ObjectId, entry.Depth)));
        Assert.All(entries, entry => Assert.True(entry.Loose));
    }

    [Fact]
    public void WhatSwitchesABodyIsWrittenBesideIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Chained(workspace) with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "sluice",
                    States = ["dry", "flowing"],
                    InitialState = "dry",
                },
            ],
        };
        scene = WaterEditing.SetActivation(
            scene,
            "river_0002",
            new WaterActivationDocument
            {
                Group = "sluice",
                ActiveIn = ["flowing"],
                Inactive = WaterInactive.DryBed,
            });

        var entries = OutlinerModel.Build(scene, workspace.Metrics, EditorMode.River);
        Assert.Equal("sluice: flowing", Assert.Single(
            entries, entry => entry.ObjectId == "river_0002").Note);
        Assert.Null(Assert.Single(
            entries, entry => entry.ObjectId == "river_0001").Note);
    }

    /// <summary>
    /// Every other kind is a flat list. The panel is generic over modes on
    /// purpose: only water has a relationship to draw.
    /// </summary>
    [Fact]
    public void EveryOtherModeIsAFlatList()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Chained(workspace);

        Assert.Empty(OutlinerModel.Build(scene, workspace.Metrics, EditorMode.Terrain));
        Assert.All(
            OutlinerModel.Build(scene, workspace.Metrics, EditorMode.Bridge),
            entry => Assert.Equal(0, entry.Depth));
    }

    private static SceneDocument Chained(TestWorkspace workspace)
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
