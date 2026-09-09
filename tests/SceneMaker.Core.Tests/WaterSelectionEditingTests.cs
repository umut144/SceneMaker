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
        Assert.Equal(before.Switch, after.Switch);
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

    /// <summary>
    /// A body may only name a switch the Scene declares, and taking it off one
    /// is what "always there" is written as.
    /// </summary>
    [Fact]
    public void ASwitchIsCheckedAgainstTheOnesTheSceneDeclares()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SwitchEditing.AddSwitch(Fixture(workspace), "fork_gate", initiallyOn: false);

        var switched = WaterEditing.SetSwitch(scene, "river_0001", "fork_gate");
        Assert.Equal("fork_gate", switched.WaterBodies[0].Switch);
        DocumentValidation.Validate(switched);

        // And back off every switch, which is what a body that is always there is.
        Assert.Null(WaterEditing.SetSwitch(switched, "river_0001", null).WaterBodies[0].Switch);

        Assert.Throws<SceneMakerDocumentException>(
            () => WaterEditing.SetSwitch(scene, "river_0001", "no_such_switch"));
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
        // Which body states the fork it cannot keep is part of the answer: the
        // Canvas colours one river by it, not the Scene.
        Assert.Equal("river_0002", complaint.WaterBodyId);
        Assert.Equal("river_0001", complaint.PartnerWaterBodyId);
        Assert.Equal(WaterEnd.Source, complaint.End);
        Assert.Contains("river_0001", complaint.Message, StringComparison.Ordinal);

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

    /// <summary>
    /// What a branch tool aims at. The author points at a line; a curve point
    /// belongs on the water grid; so the exact nearest point is found first and
    /// the grid position around it that is nearest to the curve and still inside
    /// the corridor is what comes back.
    /// </summary>
    [Fact]
    public void TheNearestAnchorSitsOnTheGridInsideTheCorridorAndCarriesItsBodysSurface()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.Reshape(
            Fixture(workspace),
            "river_0001",
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ]);

        var anchor = Assert.NotNull(
            WaterGeometry.NearestCenterlineAnchor(scene, workspace.Metrics, 100, 40, 32.0));

        Assert.Equal("river_0001", anchor.WaterBodyId);
        Assert.Equal(96, anchor.PositionAuthoringPx.X);
        Assert.Equal(32, anchor.PositionAuthoringPx.Y);
        Assert.Equal(0, anchor.PositionAuthoringPx.X % workspace.Metrics.AuthoringPixelsPerWaterCell);
        Assert.Equal(0, anchor.PositionAuthoringPx.Y % workspace.Metrics.AuthoringPixelsPerWaterCell);
        Assert.Equal(2.0m, anchor.StationMeters);

        // The height is the parent's there, which is what makes the two the same
        // water where they meet.
        Assert.Equal(2.0m, anchor.SurfaceMeters);
    }

    [Fact]
    public void AnchorsAreOnlyOfferedWithinReachOfARiver()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Fixture(workspace);

        Assert.Null(WaterGeometry.NearestCenterlineAnchor(scene, workspace.Metrics, 96, 400, 32.0));
        Assert.Null(WaterGeometry.NearestCenterlineAnchor(
            TestScenes.Instance(workspace), workspace.Metrics, 96, 32, 32.0));
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
