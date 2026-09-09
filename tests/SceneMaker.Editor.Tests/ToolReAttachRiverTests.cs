using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `River:Re-Attach`. A junction could be torn by dragging a point and made
/// only by drawing a new branch, so a body that came off its river could be
/// deleted and redrawn and nothing else. This is the other half: two presses,
/// an end and the river it should meet.
/// </summary>
public sealed class ToolReAttachRiverTests
{
    [Fact]
    public void TwoPressesMoveTheEndAndAuthorTheClaim()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Loose(workspace);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        // The end it carries, then the river that end should meet.
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
        Assert.Null(interaction.ReAttachEnd);
    }

    [Fact]
    public void OnlyASourceOrAMouthCanBeCarried()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.InsertPoint(
            Loose(workspace), workspace.Metrics, "river_0002", 96, 480);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

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

        interaction.PointerPressed(context, Point(96, 400), Cell(3, 12));
        var refused = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(160, 40), Cell(5, 1)));

        Assert.Contains("feeding itself", refused.Text, StringComparison.Ordinal);
        // Still carried, so the author can aim somewhere else without starting over.
        Assert.Equal(("river_0002", WaterEnd.Source), interaction.ReAttachEnd);
    }

    [Fact]
    public void EscapeGivesTheCarriedEndBack()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Loose(workspace);
        var interaction = ReAttaching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(96, 400), Cell(3, 12));
        var back = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Escape));

        Assert.Contains("stays where it is", back.Text, StringComparison.Ordinal);
        Assert.Null(interaction.ReAttachEnd);
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
