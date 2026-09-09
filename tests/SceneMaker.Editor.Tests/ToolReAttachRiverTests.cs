using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `River:Re-Attach`. A junction could be torn by dragging a point and made
/// only by drawing a new branch, so a body that came off its river could be
/// deleted and redrawn and nothing else. This is the other half: three presses,
/// each of which narrows what the next one can hit - a river, then one of its
/// two ends, then the river that end should meet.
/// </summary>
public sealed class ToolReAttachRiverTests
{
    [Fact]
    public void ThreePressesMoveTheEndAndAuthorTheClaim()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Loose(workspace);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        // The river, then the end it carries, then the river that end meets.
        Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 470), Cell(3, 14)));
        Assert.Equal("river_0002", interaction.ReAttachBodyId);
        Assert.Null(interaction.ReAttachEnd);

        Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 400), Cell(3, 12)));
        Assert.Equal(("river_0002", WaterEnd.Source), interaction.ReAttachEnd);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(160, 40), Cell(5, 1)));
        var after = edit.Apply(scene);
        var branch = after.WaterBodies[1];

        // On the river, at the river's height there, and said so.
        Assert.Equal(160, branch.Points[0].PositionAuthoringPx.X);
        Assert.Equal(32, branch.Points[0].PositionAuthoringPx.Y);
        Assert.Equal(after.WaterBodies[0].Points[0].ElevationMeters, branch.Points[0].ElevationMeters);
        var claim = Assert.Single(branch.Junctions);
        Assert.Equal(WaterEnd.Source, claim.End);
        Assert.Equal("river_0001", claim.WaterBodyId);

        // The moved point loses its handles; the one beside it keeps its own.
        Assert.Equal(AuthoringPixelOffset.Zero, branch.Points[0].HandleInAuthoringPx);
        Assert.Equal(AuthoringPixelOffset.Zero, branch.Points[0].HandleOutAuthoringPx);
        Assert.Equal(scene.WaterBodies[1].Points[1], branch.Points[1]);

        Assert.Empty(WaterEditing.BrokenJunctions(after, workspace.Metrics));
        Assert.True(WaterAttachment.IsAttached(after, workspace.Metrics, branch));
        DocumentValidation.ValidateGrid(after, workspace.Metrics);

        // The tool is back at its first phase, ready for another river.
        Assert.Null(interaction.ReAttachBodyId);
        Assert.Null(interaction.ReAttachEnd);
    }

    /// <summary>
    /// At a fork the branch's source sits on its parent's centerline, so both a
    /// point and a line are under the pointer at once. Choosing the river first
    /// takes the other body out of the question entirely: the same press means
    /// the branch's source or nothing at all, depending only on what the author
    /// already chose.
    /// </summary>
    [Fact]
    public void OnceARiverIsChosenNothingElseCanAnswer()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var context = Context(workspace, scene);

        // The branch chosen well away from the fork, then its source pressed at
        // the fork, where its parent's line runs through the same place.
        var branch = ReAttaching();
        branch.PointerPressed(context, Point(96, 160), Cell(3, 5));
        Assert.Equal("river_0002", branch.ReAttachBodyId);
        branch.PointerPressed(context, Point(96, 32), Cell(3, 1));
        Assert.Equal(("river_0002", WaterEnd.Source), branch.ReAttachEnd);

        // The parent chosen instead, and the very same press: it holds no end
        // there, so nothing is carried and the message says what is wanted.
        var parent = ReAttaching();
        parent.PointerPressed(context, Point(224, 32), Cell(7, 1));
        Assert.Equal("river_0001", parent.ReAttachBodyId);
        var said = Assert.IsType<ToolOutcome.Message>(
            parent.PointerPressed(context, Point(96, 32), Cell(3, 1)));
        Assert.Null(parent.ReAttachEnd);
        Assert.Contains("source or the mouth", said.Text, StringComparison.Ordinal);

        // Its own mouth still answers, because that is an end of the river the
        // author chose.
        parent.PointerPressed(context, Point(286, 32), Cell(8, 1));
        Assert.Equal(("river_0001", WaterEnd.Mouth), parent.ReAttachEnd);
    }

    /// <summary>A river along y = 32 and a branch leaving it at x = 96.</summary>
    private static SceneDocument Forked(TestWorkspace workspace)
    {
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 40),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(288, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(96, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(96, 224, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");
        return WaterEditing.SetJunctions(
            scene,
            "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);
    }

    [Fact]
    public void OnlyASourceOrAMouthCanBeCarried()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.InsertPoint(
            Loose(workspace), workspace.Metrics, "river_0002", 96, 480);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(96, 440), Cell(3, 13));
        Assert.Equal("river_0002", interaction.ReAttachBodyId);

        var middle = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 480), Cell(3, 15)));
        Assert.Contains("in between", middle.Text, StringComparison.Ordinal);
        Assert.Null(interaction.ReAttachEnd);

        // The mouth is an end like the source, and carries the other claim.
        Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 544), Cell(3, 17)));
        Assert.Equal(("river_0002", WaterEnd.Mouth), interaction.ReAttachEnd);
    }

    /// <summary>
    /// A ring is refused at the gesture rather than stored and complained about.
    /// Unlike a fork pulled apart it is not a step on the way to anything, and
    /// every walk over the junctions would have to guard against it forever.
    /// </summary>
    [Fact]
    public void ARiverCannotBeMadeToFeedItself()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.SetJunctions(
            Loose(workspace),
            "river_0001",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0002" }]);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(96, 470), Cell(3, 14));
        interaction.PointerPressed(context, Point(96, 400), Cell(3, 12));
        var refused = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(160, 40), Cell(5, 1)));

        Assert.Contains("feeding itself", refused.Text, StringComparison.Ordinal);
        // Still carried, so the author can aim somewhere else without starting over.
        Assert.Equal(("river_0002", WaterEnd.Source), interaction.ReAttachEnd);
    }

    /// <summary>
    /// Escape undoes one decision, not the whole gesture: the end goes back
    /// first and the river second, so an author who took the wrong end keeps
    /// the river they took it from.
    /// </summary>
    [Fact]
    public void EscapeStepsBackOnePhaseAtATime()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Loose(workspace);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(96, 470), Cell(3, 14));
        interaction.PointerPressed(context, Point(96, 400), Cell(3, 12));

        var endBack = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Contains("stays where it is", endBack.Text, StringComparison.Ordinal);
        Assert.Null(interaction.ReAttachEnd);
        Assert.Equal("river_0002", interaction.ReAttachBodyId);

        var riverBack = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Contains("let go", riverBack.Text, StringComparison.Ordinal);
        Assert.Null(interaction.ReAttachBodyId);

        // Nothing held, nothing to take back.
        Assert.IsType<ToolOutcome.Idle>(interaction.KeyPressed(context, ToolKey.Escape));
    }

    private static ToolInteraction ReAttaching()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.ReAttachRiver);
        return interaction;
    }

    /// <summary>A river along y = 32, and a second one well away from it.</summary>
    private static SceneDocument Loose(TestWorkspace workspace)
    {
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 40),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(288, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");
        return WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(96, 400, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(96, 544, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");
    }

    private static ToolContext Context(TestWorkspace workspace, SceneDocument scene) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: "river",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: 1m,
        PointerHitRadiusAuthoringPixels: 8.0);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
