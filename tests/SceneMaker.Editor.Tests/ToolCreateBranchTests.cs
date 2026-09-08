using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `River:Create Branch`. Drawing a river whose first point happens to lie in
/// another river's corridor already produced the shape of a branch - what it
/// did not produce was the statement that it is one, and that statement is what
/// a consumer splits its flow at and what the export checks the two heights
/// against.
/// </summary>
public sealed class ToolCreateBranchTests
{
    [Fact]
    public void TheFirstPointSnapsToTheRiverAndTakesItsSurfaceThere()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Branching();

        // Aimed beside the centerline, not at it.
        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(100, 40), Cell(3, 1)));

        Assert.Contains("river_0001", message.Text, StringComparison.Ordinal);
        Assert.Contains("station 2 m", message.Text, StringComparison.Ordinal);
        Assert.Contains("2 m", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFinishedBranchCarriesTheJunctionInTheSameEditThatPlacesIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Branching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(100, 40), Cell(3, 1));
        interaction.PointerReleased(context);
        interaction.PointerPressed(context, Point(96, 160), Cell(3, 5));
        interaction.PointerReleased(context);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));
        var after = edit.Apply(scene);
        var branch = Assert.Single(after.WaterBodies, body => body.WaterBodyId == "river_0002");

        var junction = Assert.Single(branch.Junctions);
        Assert.Equal(WaterEnd.Source, junction.End);
        Assert.Equal("river_0001", junction.WaterBodyId);

        // The source sits on the river, at the river's height, which is what the
        // export checks and what makes the two the same water where they meet.
        Assert.Equal(96, branch.Points[0].PositionAuthoringPx.X);
        Assert.Equal(32, branch.Points[0].PositionAuthoringPx.Y);
        Assert.Equal(
            after.WaterBodies[0].Points[0].ElevationMeters,
            branch.Points[0].ElevationMeters);
        Assert.Empty(WaterEditing.BrokenJunctions(after, workspace.Metrics));
        DocumentValidation.ValidateGrid(after, workspace.Metrics);

        Assert.Contains(
            "branch of 'river_0001'",
            edit.Describe!(scene, after),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The one thing this tool refuses. A branch that starts anywhere else is
    /// exactly the river-that-touches-a-river the tool exists to stop producing.
    /// </summary>
    [Fact]
    public void AFirstPointAwayFromEveryRiverIsRefusedAndStartsNoDraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Branching();

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(96, 400), Cell(3, 12)));

        Assert.Contains("start on a river", message.Text, StringComparison.Ordinal);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    /// <summary>
    /// Every point after the first is an ordinary river point: a branch is an
    /// ordinary river in every respect but where it starts.
    /// </summary>
    [Fact]
    public void PointsAfterTheFirstAreNotSnappedToAnything()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Branching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(100, 40), Cell(3, 1));
        interaction.PointerReleased(context);
        interaction.PointerPressed(context, Point(100, 164), Cell(3, 5));
        interaction.PointerReleased(context);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));
        var branch = edit.Apply(scene).WaterBodies[1];

        // Snapped to the water grid like any drawn point, and nowhere near a river.
        Assert.Equal(96, branch.Points[1].PositionAuthoringPx.X);
        Assert.Equal(160, branch.Points[1].PositionAuthoringPx.Y);
    }

    [Fact]
    public void ADiscardedBranchDoesNotHandItsParentToTheNextOne()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Branching();
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(100, 40), Cell(3, 1));
        interaction.PointerReleased(context);
        interaction.KeyPressed(context, ToolKey.Escape);
        interaction.SelectTool(EditorTool.DrawRiver);
        interaction.SelectTool(EditorTool.CreateBranch);

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Enter));
        Assert.Contains("start on a river", message.Text, StringComparison.Ordinal);
    }

    private static ToolInteraction Branching()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.CreateBranch);
        interaction.State.SetWaterChannelDepth(0.5m);
        interaction.State.SetWaterClearanceAbove(5.0m);
        interaction.State.SetRiverWidth(2.0m);
        return interaction;
    }

    private static SceneDocument River(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
            ],
            "river");

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
