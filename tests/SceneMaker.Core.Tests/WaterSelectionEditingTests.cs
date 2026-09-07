using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The operations a selection tool stands on. They are pure
/// <c>SceneDocument -&gt; SceneDocument</c> like every other editing operation,
/// so what a pointer did to arrive at a new point list stays in the editor and
/// never reaches Core.
/// </summary>
public sealed class WaterSelectionEditingTests
{
    /// <summary>
    /// Moving a point, inserting one, deleting one and retyping its width are
    /// four gestures and one operation. From the document's side a curve is its
    /// points, and nothing else can be said about what changed.
    /// </summary>
    [Fact]
    public void ReshapeReplacesTheWholePointListAndKeepsEverythingElse()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Fixture(workspace);
        var before = scene.WaterBodies[0];

        var moved = WaterEditing.Reshape(
            scene,
            "river_0001",
            [
                before.Points[0],
                WaterEditing.Point(96, 64, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.5m),
                before.Points[1],
            ]);

        var after = moved.WaterBodies[0];
        Assert.Equal("river_0001", after.WaterBodyId);
        Assert.Equal(3, after.Points.Count);
        Assert.Equal(2.5m, after.Points[1].WidthMeters);
        Assert.Equal(before.AssetKey, after.AssetKey);
        Assert.Equal(before.Activation, after.Activation);
        Assert.Equal(before.Junctions, after.Junctions);
        DocumentValidation.Validate(moved);

        // The corridor follows, which is the whole point of reshaping a curve
        // rather than a raster.
        Assert.NotEqual(
            WaterGeometry.Corridor(scene, workspace.Metrics, before).Count,
            WaterGeometry.Corridor(moved, workspace.Metrics, after).Count);
    }

    [Fact]
    public void ACurveCannotBeReshapedBelowTwoPointsOrToAWidthOfNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Fixture(workspace);
        var points = scene.WaterBodies[0].Points;

        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.Reshape(scene, "river_0001", [points[0]]));
        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.Reshape(
                scene,
                "river_0001",
                [points[0], points[1] with { WidthMeters = 0m }]));
        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.Reshape(scene, "river_0404", points));
    }

    [Fact]
    public void ActivationIsCheckedAgainstTheGroupsTheSceneDeclares()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Fixture(workspace) with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "fork_gate",
                    States = ["shut", "flowing"],
                    InitialState = "shut",
                },
            ],
        };

        var switched = WaterEditing.SetActivation(
            scene,
            "river_0001",
            new WaterActivationDocument
            {
                Group = "fork_gate",
                ActiveIn = ["flowing"],
                Inactive = WaterInactive.DryBed,
            });
        Assert.Equal(["flowing"], switched.WaterBodies[0].Activation!.ActiveIn);
        DocumentValidation.Validate(switched);

        // And back out of every group, which is what a body with no activation is.
        Assert.Null(WaterEditing.SetActivation(switched, "river_0001", null).WaterBodies[0].Activation);

        foreach (var refused in new[]
                 {
                     new WaterActivationDocument
                     {
                         Group = "no_such_group", ActiveIn = ["flowing"], Inactive = WaterInactive.DryBed,
                     },
                     new WaterActivationDocument
                     {
                         Group = "fork_gate", ActiveIn = ["flooded"], Inactive = WaterInactive.DryBed,
                     },
                     new WaterActivationDocument
                     {
                         Group = "fork_gate", ActiveIn = [], Inactive = WaterInactive.DryBed,
                     },
                 })
        {
            Assert.Throws<SceneMakerDocumentException>(
                () => WaterEditing.SetActivation(scene, "river_0001", refused));
        }
    }

    [Fact]
    public void JunctionsAreStoredInTheOrderTheDocumentKeepsThemIn()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);

        var stated = WaterEditing.SetJunctions(
            scene,
            "river_0002",
            [
                new WaterJunctionDocument { End = WaterEnd.Mouth, WaterBodyId = "river_0001" },
                new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" },
                new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" },
            ]);

        var junctions = stated.WaterBodies[1].Junctions;
        Assert.Equal([WaterEnd.Source, WaterEnd.Mouth], junctions.Select(static claim => claim.End));
        DocumentValidation.Validate(stated);

        Assert.Throws<SceneMakerDocumentException>(() => WaterEditing.SetJunctions(
            scene, "river_0002", [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0002" }]));
        Assert.Throws<SceneMakerDocumentException>(() => WaterEditing.SetJunctions(
            scene, "river_0002", [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0404" }]));
    }

    /// <summary>
    /// A drag may break a fork - the editor validates at its IO boundaries, so
    /// a document is allowed to be wrong between two edits. What must not
    /// happen is that the author hears about it from an export hours later.
    /// </summary>
    [Fact]
    public void PullingAForkApartIsAllowedAndIsReportedAtOnce()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.SetJunctions(
            Forked(workspace),
            "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);
        Assert.Empty(WaterEditing.BrokenJunctions(scene, workspace.Metrics));

        // Drag the branch's source clear of the river it hangs on.
        var branch = scene.WaterBodies[1];
        var pulled = WaterEditing.Reshape(
            scene,
            "river_0002",
            [
                WaterEditing.Point(96, 400, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                branch.Points[1],
            ]);

        var complaint = Assert.Single(WaterEditing.BrokenJunctions(pulled, workspace.Metrics));
        Assert.Contains("river_0002", complaint, StringComparison.Ordinal);
        Assert.Contains("river_0001", complaint, StringComparison.Ordinal);

        // Still a document the editor is happy to hold: only the export refuses.
        DocumentValidation.Validate(pulled);
    }

    /// <summary>
    /// A height that drifts too far is the same break as a source pulled away:
    /// the two bodies stop being the same water where they meet.
    /// </summary>
    [Fact]
    public void AForkWhoseHeightsDriftApartIsReportedToo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.SetJunctions(
            Forked(workspace),
            "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);

        var branch = scene.WaterBodies[1];
        var lowered = WaterEditing.Reshape(
            scene,
            "river_0002",
            [branch.Points[0] with { ElevationMeters = 1.0m }, branch.Points[1]]);

        Assert.Single(WaterEditing.BrokenJunctions(lowered, workspace.Metrics));
    }

    [Fact]
    public void APointIsGrabbedByWhicheverIsNearer()
    {
        using var workspace = TestWorkspace.Create();
        var body = Fixture(workspace).WaterBodies[0];

        Assert.Equal(0, WaterEditing.FindPointAt(body, 34, 34, 8.0));
        Assert.Equal(1, WaterEditing.FindPointAt(body, 158, 32, 8.0));
        Assert.Null(WaterEditing.FindPointAt(body, 96, 32, 8.0));
    }

    private static SceneDocument Fixture(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

    private static SceneDocument Forked(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            Fixture(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(96, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(96, 160, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
}
