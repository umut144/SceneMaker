using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `River:Insert Point`. It is a tool of its own because every gesture a press
/// can carry was already taken - point, corridor, open ground, and the same
/// three with the eraser - and `ToolInteraction` sees no modifier keys by
/// design.
/// </summary>
public sealed class ToolInsertRiverPointTests
{
    [Fact]
    public void APressOnTheRiverPutsAPointThereAndAuthorsNoValue()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Inserting();

        // Aimed beside the line, and with tool defaults that must not be used.
        interaction.State.SetRiverWidth(99m);
        interaction.State.SetWaterChannelDepth(9m);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(160, 40), Cell(5, 1)));
        var body = edit.Apply(scene).WaterBodies[0];

        Assert.Equal(3, body.Points.Count);
        Assert.Equal(160, body.Points[1].PositionAuthoringPx.X);
        Assert.Equal(32, body.Points[1].PositionAuthoringPx.Y);
        Assert.Equal(2.0m, body.Points[1].WidthMeters);
        Assert.Equal(0.5m, body.Points[1].ChannelDepthMeters);
        Assert.Contains("3 points", edit.Describe!(scene, edit.Apply(scene)), StringComparison.Ordinal);
    }

    [Fact]
    public void APressAwayFromEveryRiverSaysSoAndChangesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);

        var message = Assert.IsType<ToolOutcome.Message>(
            Inserting().PointerPressed(Context(workspace, scene), Point(160, 400), Cell(5, 12)));
        Assert.Contains("press on a river", message.Text, StringComparison.Ordinal);
    }

    /// <summary>The inverse, on the same tool: what it adds, the eraser takes.</summary>
    [Fact]
    public void TheEraserTakesTheCurvePointUnderIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.InsertPoint(River(workspace), workspace.Metrics, "river_0001", 160, 32);
        var interaction = Inserting();
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(160, 32), Cell(5, 1)));
        Assert.Equal(2, edit.Apply(scene).WaterBodies[0].Points.Count);
    }

    [Fact]
    public void TheEraserKeepsASourceAndAMouth()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Inserting();
        interaction.SetEraserEnabled(true);

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(32, 32), Cell(1, 1)));
        Assert.Contains("source and a mouth", message.Text, StringComparison.Ordinal);

        // And nothing under the pointer is not an edit either.
        var nothing = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(160, 32), Cell(5, 1)));
        Assert.Contains("no curve point", nothing.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A river's curve is invisible unless something is selected, so this tool
    /// used to be a press into a blue band and a hope. The preview shows the
    /// body it would put a point into and where that point would land, and it
    /// finds the body the same way the press does - what lights up is what a
    /// press takes.
    /// </summary>
    [Fact]
    public void ThePreviewShowsTheRiverUnderThePointerAndWhereThePointWouldLand()
    {
        using var workspace = TestWorkspace.Create();
        var scene = River(workspace);
        var interaction = Inserting();
        var context = Context(workspace, scene);

        interaction.PointerMoved(Point(160, 40), Cell(5, 1));
        var near = interaction.WaterInsert(context);
        Assert.Equal("river_0001", near.Body?.WaterBodyId);
        Assert.NotEmpty(near.Centerline);
        var anchor = Assert.NotNull(near.Anchor);
        Assert.Equal(160, anchor.PositionAuthoringPx.X);
        Assert.Equal(32, anchor.PositionAuthoringPx.Y);

        // Far from every river there is nothing to show and nothing to take.
        interaction.PointerMoved(Point(160, 400), Cell(5, 12));
        Assert.Null(interaction.WaterInsert(context).Body);

        // And a tool that is not this one shows nothing, whatever the pointer.
        var selecting = new ToolInteraction();
        selecting.SelectMode(EditorMode.River);
        selecting.PointerMoved(Point(160, 40), Cell(5, 1));
        Assert.Null(selecting.WaterInsert(context).Body);
    }

    private static ToolInteraction Inserting()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.InsertRiverPoint);
        return interaction;
    }

    private static SceneDocument River(TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace, sizeCells: 30),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
                WaterEditing.Point(288, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 2.0m),
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
