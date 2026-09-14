using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The tool state machine that used to live inside the canvas. One Prop covers
/// exactly one Terrain cell in this fixture, so the anchor (64, 64) is cell (2, 2).
/// </summary>
public sealed class ToolInteractionTests
{
    [Fact]
    public void ThePencilTurnsAPointerPressIntoOneTerrainEdit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);

        var outcome = interaction.PointerPressed(Context(workspace, scene), Point(64, 64), Cell(2, 2));

        var edit = Assert.IsType<ToolOutcome.Edit>(outcome);
        var painted = edit.Apply(scene);
        Assert.Equal("grass", Assert.Single(painted.TerrainCells).AssetKey);
        Assert.Equal(ToolInteraction.TerrainPaintStroke, edit.StrokeKey);
    }

    [Fact]
    public void DraggingThePencilKeepsTheSameStrokeKeySoUndoStaysOneStep()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);
        var context = Context(workspace, scene);

        var pressed = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(0, 0), Cell(0, 0)));
        var dragged = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerDragged(context, Point(32, 0), Cell(1, 0)));

        Assert.Equal(pressed.StrokeKey, dragged.StrokeKey);
    }

    [Fact]
    public void ATerrainLineIsDraggedAndAppliedOnRelease()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Line);
        var context = Context(workspace, scene);

        Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(context, Point(0, 0), Cell(0, 0)));
        Assert.Equal(new TerrainCellCoordinate(0, 0), interaction.TerrainLineStart);
        interaction.PointerDragged(context, Point(96, 0), Cell(3, 0));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));

        Assert.Equal(4, edit.Apply(scene).TerrainCells.Count);
        Assert.Null(interaction.TerrainLineStart);
    }

    [Fact]
    public void APencilWithABrushWidthPaintsTheSquareAroundTheCell()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);
        var context = Context(workspace, scene, terrainBrushWidthCells: 3);

        var outcome = interaction.PointerPressed(context, Point(64, 64), Cell(2, 2));

        var edit = Assert.IsType<ToolOutcome.Edit>(outcome);
        var painted = edit.Apply(scene);
        Assert.Equal(9, painted.TerrainCells.Count);
        for (var y = 1; y <= 3; y++)
            for (var x = 1; x <= 3; x++)
                Assert.Contains(painted.TerrainCells, cell => cell.X == x && cell.Y == y);
    }

    [Fact]
    public void ATerrainLineWithABrushWidthThickensTheWholeStroke()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 8);
        var interaction = At(EditorMode.Terrain, EditorTool.Line);
        var context = Context(workspace, scene, terrainBrushWidthCells: 3);

        interaction.PointerPressed(context, Point(64, 64), Cell(2, 2));
        interaction.PointerDragged(context, Point(160, 64), Cell(5, 2));

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var painted = edit.Apply(scene);
        Assert.Equal(18, painted.TerrainCells.Count);
        for (var y = 1; y <= 3; y++)
            for (var x = 1; x <= 6; x++)
                Assert.Contains(painted.TerrainCells, cell => cell.X == x && cell.Y == y);
    }

    [Fact]
    public void APropLineFixesStartAndEndAndOnlyEnterAppliesIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Line);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(0, 0), Cell(0, 0));
        Assert.Equal(new AuthoringPoint(0, 0), interaction.PropLineStart);
        interaction.PointerPressed(context, Point(128, 0), Cell(4, 0));
        Assert.Equal(new AuthoringPoint(128, 0), interaction.PropLineEnd);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));

        Assert.Equal(5, edit.Apply(scene).Props.Count);
        Assert.Null(interaction.PropLineStart);
    }

    [Fact]
    public void EscapeReleasesTheEndPointFirstAndThenTheStartPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Line);
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(0, 0), Cell(0, 0));
        interaction.PointerPressed(context, Point(128, 0), Cell(4, 0));

        interaction.KeyPressed(context, ToolKey.Escape);
        Assert.Null(interaction.PropLineEnd);
        Assert.NotNull(interaction.PropLineStart);

        interaction.KeyPressed(context, ToolKey.Escape);
        Assert.Null(interaction.PropLineStart);
    }

    [Fact]
    public void EnterWithoutAFixedLineOnlyExplainsWhatIsMissing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Line);

        var outcome = interaction.KeyPressed(Context(workspace, scene), ToolKey.Enter);

        var message = Assert.IsType<ToolOutcome.Message>(outcome);
        Assert.Contains("choose a start point", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysOutsideThePropLineToolAreNotHandled()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);

        Assert.IsType<ToolOutcome.Idle>(
            interaction.KeyPressed(Context(workspace, scene), ToolKey.Enter));
    }

    [Fact]
    public void APropThatCannotBePlacedProducesAMessageRatherThanAnEdit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 64, 64, "stone");
        var interaction = At(EditorMode.Props, EditorTool.Pencil);

        var outcome = interaction.PointerPressed(Context(workspace, scene), Point(64, 64), Cell(2, 2));

        var message = Assert.IsType<ToolOutcome.Message>(outcome);
        Assert.Contains("Pencil Draw blocked", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSelectorRecordsAndClearsThePropSelection()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 64, 64, "stone");
        var interaction = At(EditorMode.Props, EditorTool.Selector);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(70, 70), Cell(2, 2));
        Assert.Equal("stone_0001", interaction.SelectedPropInstanceId);

        interaction.PointerPressed(context, Point(160, 160), Cell(5, 5));
        Assert.Null(interaction.SelectedPropInstanceId);
    }

    [Fact]
    public void ChangingModeForgetsAHalfFinishedLine()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Line);
        interaction.PointerPressed(Context(workspace, scene), Point(0, 0), Cell(0, 0));

        interaction.SelectMode(EditorMode.Terrain);

        Assert.Null(interaction.PropLineStart);
    }

    [Fact]
    public void ANewlyAppearedAnchorBecomesTheSelectedOne()
    {
        using var workspace = TestWorkspace.Create();
        var before = TestScenes.Instance(workspace);
        var after = TemplateEditing.PlaceAnchor(before, workspace.Metrics, 64, 64, 1);
        var interaction = At(EditorMode.Templates, EditorTool.Selector);

        interaction.SceneChanged(before, after);

        Assert.Equal("template_anchor_001", interaction.SelectedTemplateAnchorId);
    }

    [Fact]
    public void ASelectionThatNoLongerExistsIsDropped()
    {
        using var workspace = TestWorkspace.Create();
        var scene = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 64, 64, "stone");
        var interaction = At(EditorMode.Props, EditorTool.Selector);
        interaction.SelectProp("stone_0001");

        interaction.SceneChanged(scene, PropEditing.EraseAt(scene, workspace.Props, 64, 64));

        Assert.Null(interaction.SelectedPropInstanceId);
    }

    [Fact]
    public void PlacingATemplateAnchorDescribesTheAnchorThatAppeared()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Templates, EditorTool.AnchorPlace);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(64, 64), Cell(2, 2)));
        var placed = edit.Apply(scene);

        Assert.Contains(
            "template_anchor_001",
            edit.Describe!(scene, placed),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnchorIsDraggedOnTheWorldGridAndMovedOnRelease()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TemplateEditing.PlaceAnchor(
            TestScenes.Instance(workspace), workspace.Metrics, 64, 64, 1);
        var interaction = At(EditorMode.Templates, EditorTool.AnchorMove);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(64, 64), Cell(2, 2));
        Assert.Equal("template_anchor_001", interaction.DraggedAnchorId);
        interaction.PointerDragged(context, Point(100, 100), Cell(3, 3));
        Assert.Equal(new AuthoringPoint(96, 96), interaction.DraggedAnchorPosition);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var moved = edit.Apply(scene);

        Assert.Equal(96, Assert.Single(moved.TemplateAnchors).PositionAuthoringPx.X);
        Assert.Null(interaction.DraggedAnchorId);
    }

    [Fact]
    public void FillReportsWhenItWouldChangeNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Terrain, EditorTool.Fill);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(64, 64), Cell(2, 2)));

        Assert.Same(scene, edit.Apply(scene));
        Assert.NotNull(edit.NoChangeText);
    }

    [Fact]
    public void UndoTakesBackAFixedPropLineStartBeforeItTouchesTheHistory()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Line);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(64, 64), Cell(2, 2));
        Assert.True(interaction.HasUnfinishedDraft);

        Assert.NotNull(interaction.UndoDraftStep());

        Assert.False(interaction.HasUnfinishedDraft);
        Assert.Null(interaction.UndoDraftStep());
    }

    [Fact]
    public void AToolWithoutADraftLeavesUndoToTheHistory()
    {
        using var workspace = TestWorkspace.Create();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);

        Assert.False(interaction.HasUnfinishedDraft);
        Assert.Null(interaction.UndoDraftStep());
    }

    /// <summary>
    /// Bare ground is not a defect: a Prop holds an absolute height, so the
    /// pencil authors it and says so plainly rather than attaching a warning.
    /// </summary>
    [Fact]
    public void APropIsPlacedOnASceneWithoutTerrainWithoutAWarning()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Props, EditorTool.Pencil);

        var outcome = interaction.PointerPressed(Context(workspace, scene), Point(64, 64), Cell(2, 2));

        var edit = Assert.IsType<ToolOutcome.Edit>(outcome);
        var after = edit.Apply(scene);
        Assert.Equal("stone_0001", Assert.Single(after.Props).InstanceId);
        Assert.Equal(
            "Placed Stone anchor at (64, 64) authoring px.",
            edit.Describe!(scene, after));
    }

    private static ToolInteraction At(EditorMode mode, EditorTool tool)
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(mode);
        interaction.SelectTool(tool);
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace, SceneDocument scene, int terrainBrushWidthCells = 1) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: "grass",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: scene.DefaultElevationMeters,
        TerrainBrushWidthCells: terrainBrushWidthCells);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
