using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `River:Select River`. Rivers were the one curve area with no selector, so a
/// body that had been drawn could only be redrawn - and a body authored by hand
/// could not be touched at all.
/// </summary>
public sealed class ToolRiverSelectionTests
{
    [Fact]
    public void PressingACorridorSelectsThatBodyAndSaysWhatItIs()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(96, 100), Cell(3, 3)));

        Assert.Equal("river_0002", interaction.SelectedWaterBodyId);
        Assert.Contains("river_0002", message.Text, StringComparison.Ordinal);
        Assert.Contains("2 points", message.Text, StringComparison.Ordinal);
        // A branch says what it hangs on and when it is there.
        Assert.Contains("Meets 'river_0001'", message.Text, StringComparison.Ordinal);
        Assert.Contains("Always there", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PressingOpenGroundClearsTheSelection()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(600, 600), Cell(18, 18)));

        Assert.Null(interaction.SelectedWaterBodyId);
        Assert.Equal("River selection cleared.", message.Text);
    }

    /// <summary>
    /// A point is grabbed before a corridor, because at a fork every point of
    /// interest lies inside two corridors at once.
    /// </summary>
    [Fact]
    public void DraggingAPointOfTheSelectedBodyMovesThatPointAlone()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));   // select the branch
        interaction.PointerPressed(context, Point(96, 160), Cell(3, 5));   // grab its mouth
        interaction.PointerDragged(context, Point(132, 190), Cell(4, 5));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var moved = edit.Apply(scene).WaterBodies[1];

        // Snapped to the water grid, which is the grid a curve point must sit on.
        Assert.Equal(128, moved.Points[1].PositionAuthoringPx.X);
        Assert.Equal(192, moved.Points[1].PositionAuthoringPx.Y);
        Assert.Equal(96, moved.Points[0].PositionAuthoringPx.X);
        Assert.Equal(32, moved.Points[0].PositionAuthoringPx.Y);
        DocumentValidation.Validate(edit.Apply(scene));
    }

    [Fact]
    public void PressingASelectedBodyAgainCarriesTheWholeCurve()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.PointerDragged(context, Point(128, 100), Cell(4, 3));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var moved = edit.Apply(scene).WaterBodies[1];

        Assert.Equal([128, 128], moved.Points.Select(p => p.PositionAuthoringPx.X));
        Assert.Equal([32, 160], moved.Points.Select(p => p.PositionAuthoringPx.Y));
    }

    /// <summary>
    /// The move is taken, and the broken fork is said in the same breath. The
    /// editor validates at its IO boundaries, so an intermediate document is
    /// allowed to be wrong - what is not allowed is finding out from an export.
    /// </summary>
    [Fact]
    public void PullingABranchOffItsParentIsTakenAndSaidOutLoud()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.PointerPressed(context, Point(96, 32), Cell(3, 1));    // grab the source
        interaction.PointerDragged(context, Point(96, 400), Cell(3, 12));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var after = edit.Apply(scene);
        var said = edit.Describe!(scene, after);

        Assert.Contains("no longer touches", said, StringComparison.Ordinal);
        Assert.Contains("river_0001", said, StringComparison.Ordinal);
        Assert.Equal(400, after.WaterBodies[1].Points[0].PositionAuthoringPx.Y);
    }

    [Fact]
    public void TheContextBarValuesEditTheSelectedPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.PointerPressed(context, Point(96, 160), Cell(3, 5));
        Assert.Equal(1, interaction.SelectedWaterPointIndex);
        interaction.PointerReleased(context);

        interaction.State.SetRiverWidth(2.5m);
        interaction.State.SetWaterChannelDepth(0.75m);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.ReshapeSelectedWaterPoint(Context(workspace, scene)));
        var point = edit.Apply(scene).WaterBodies[1].Points[1];

        Assert.Equal(2.5m, point.WidthMeters);
        Assert.Equal(0.75m, point.ChannelDepthMeters);
    }

    [Fact]
    public void ActivationIsSetOnTheSelectedBodyAndTakenOffAgain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace) with
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
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.SetSelectedWaterActivation(
            context,
            new WaterActivationDocument
            {
                Group = "fork_gate",
                ActiveIn = ["flowing"],
                Inactive = WaterInactive.DryBed,
            }));
        var switched = edit.Apply(scene);
        Assert.Equal(["flowing"], switched.WaterBodies[1].Activation!.ActiveIn);
        Assert.Contains("a dry bed", edit.Describe!(scene, switched), StringComparison.Ordinal);

        var cleared = Assert.IsType<ToolOutcome.Edit>(
            interaction.SetSelectedWaterActivation(Context(workspace, switched), null));
        Assert.Null(cleared.Apply(switched).WaterBodies[1].Activation);
    }

    [Fact]
    public void EscapeClearsTheSelectionAndNothingElseDoes()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        interaction.PointerPressed(Context(workspace, scene), Point(96, 100), Cell(3, 3));

        // A tool or mode switch keeps a selection, as every other selector does.
        interaction.SelectTool(EditorTool.DrawRiver);
        interaction.SelectTool(EditorTool.SelectRiver);
        Assert.Equal("river_0002", interaction.SelectedWaterBodyId);

        Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(Context(workspace, scene), ToolKey.Escape));
        Assert.Null(interaction.SelectedWaterBodyId);
    }

    [Fact]
    public void ASelectionOfABodyThatIsGoneIsDropped()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        interaction.PointerPressed(Context(workspace, scene), Point(96, 100), Cell(3, 3));

        var without = WaterEditing.Remove(scene, "river_0002");
        interaction.SceneChanged(scene, without);

        Assert.Null(interaction.SelectedWaterBodyId);
        Assert.Null(interaction.SelectedWaterPointIndex);
    }

    /// <summary>
    /// The eraser follows the same order a plain press does - points before
    /// corridors - so what it takes away is what a press would have grabbed.
    /// </summary>
    [Fact]
    public void TheEraserTakesThePointUnderItAndTheBodyEverywhereElse()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.Reshape(
            Forked(workspace),
            "river_0002",
            [
                WaterEditing.Point(96, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(96, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(96, 160, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ]);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(96, 96), Cell(3, 3)));
        Assert.Equal(2, edit.Apply(scene).WaterBodies[1].Points.Count);

        // Away from any point it is still the body that goes.
        var whole = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(96, 130), Cell(3, 4)));
        Assert.Single(whole.Apply(scene).WaterBodies);
    }

    [Fact]
    public void ACurveWithOnlyASourceAndAMouthKeepsBothPoints()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Forked(workspace);
        var interaction = Selecting();
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(96, 100), Cell(3, 3));
        interaction.SetEraserEnabled(true);

        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(96, 160), Cell(3, 5)));
        Assert.Contains("source and a mouth", message.Text, StringComparison.Ordinal);
    }

    private static ToolInteraction Selecting()
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.SelectRiver);
        return interaction;
    }

    /// <summary>A river along y = 32 and a branch leaving it at x = 96.</summary>
    private static SceneDocument Forked(TestWorkspace workspace)
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
        return WaterEditing.SetJunctions(
            scene,
            "river_0002",
            [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }]);
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
