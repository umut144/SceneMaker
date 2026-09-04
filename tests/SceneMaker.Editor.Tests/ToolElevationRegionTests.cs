using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// `Draw ElevationRegion` on the canvas. Every anchor here sits on the 32-pixel
/// Terrain grid, because a fixture that builds a Scene the IO boundary would
/// reject is a trap for the next persistence test rather than a shortcut.
/// </summary>
public sealed class ToolElevationRegionTests
{
    [Fact]
    public void ThreePointsAndEnterAuthorOneClosedElevationRegion()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene, elevation: 4.0m);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));

        var body = Assert.Single(edit.Apply(scene).ElevationRegions);
        Assert.Equal("mountain_0001", body.ElevationRegionId);
        Assert.Equal(4.0m, body.ElevationMeters);
        Assert.Equal(3, body.Points.Count);
        Assert.Empty(interaction.ElevationRegionDraft);
        Assert.Null(edit.StrokeKey);
    }

    [Fact]
    public void PointsSnapToTerrainGridButHandlesDoNot()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);
        interaction.State.SetElevationRegionPointMode(ElevationRegionPointMode.Aligned);

        interaction.PointerPressed(context, Point(20, 20), Cell(0, 0));
        interaction.PointerDragged(context, Point(21, 61), Cell(0, 1));
        interaction.PointerReleased(context);

        var placed = Assert.Single(interaction.ElevationRegionDraft);
        Assert.Equal(32, placed.X);
        Assert.Equal(32, placed.Y);
        Assert.Equal(new AuthoringPixelOffset { X = -11, Y = 29 }, placed.DraggedHandleOut);
    }

    [Fact]
    public void AlignedClicksResolveCyclicHandlesAcrossTheClosingEdge()
    {
        var points = ElevationRegionEditing.ResolveContour(
        [
            new ElevationRegionDraftPoint(0, 0, ElevationRegionPointMode.Aligned),
            new ElevationRegionDraftPoint(96, 0, ElevationRegionPointMode.Aligned),
            new ElevationRegionDraftPoint(96, 96, ElevationRegionPointMode.Aligned),
        ]);

        Assert.False(points[0].HandleInAuthoringPx.IsZero());
        Assert.False(points[0].HandleOutAuthoringPx.IsZero());
        Assert.False(points[^1].HandleInAuthoringPx.IsZero());
        Assert.False(points[^1].HandleOutAuthoringPx.IsZero());
    }

    /// <summary>
    /// Not enough points yet is not a refusal. The draft is drawn, nothing is
    /// filled, and the preview says what is still missing instead of claiming
    /// the contour is wrong.
    /// </summary>
    [Fact]
    public void ADraftBelowThreePointsIsIncomplete()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        var preview = interaction.ElevationRegionPreview(context);

        Assert.Equal(ElevationRegionDraftKind.Incomplete, preview.Kind);
        Assert.Equal(ToolPreviewBuilder.IncompleteElevationRegionDraft, preview.Explanation);
        Assert.Empty(preview.RaisedCells);
        Assert.Empty(preview.Outline);
        Assert.Equal(2, preview.Points.Count);
    }

    [Fact]
    public void AClosedContourOverPaintedTerrainIsReady()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);
        var preview = interaction.ElevationRegionPreview(context);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Null(preview.Explanation);
        Assert.NotEmpty(preview.RaisedCells);
        Assert.NotEmpty(preview.Outline);
    }

    /// <summary>
    /// The preview shows the difference the commit would make, in the material
    /// that is already there. A contour over sand and grass previews sand and
    /// grass - the hill brings no colour of its own.
    /// </summary>
    [Fact]
    public void ThePreviewCarriesThePaintedMaterialOfEveryRaisedCell()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.Instance(workspace), workspace.Terrain, 2, 2, "sand");
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 160, 160);
        Place(interaction, context, 32, 160);
        var preview = interaction.ElevationRegionPreview(context);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Equal(16, preview.RaisedCells.Count);
        var sand = Assert.Single(preview.RaisedCells, static cell => cell.AssetKey == "sand");
        Assert.Equal(new TerrainCellCoordinate(2, 2), new TerrainCellCoordinate(sand.X, sand.Y));
        Assert.Equal(15, preview.RaisedCells.Count(static cell => cell.AssetKey == "grass"));
        Assert.All(preview.RaisedCells, static cell => Assert.Equal(4.0m, cell.ElevationMeters));
    }

    /// <summary>
    /// A contour over ground nobody painted is authorable and stays Ready. It
    /// lifts nothing yet, and the preview says exactly that instead of showing
    /// an empty fill and leaving the author to guess.
    /// </summary>
    [Fact]
    public void AContourOverUnpaintedGroundIsReadyAndSaysItRaisesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        interaction.PointerPressed(context, Point(96, 160), Cell(3, 5));
        var closed = Assert.IsType<ToolOutcome.Message>(interaction.PointerReleased(context));
        var preview = interaction.ElevationRegionPreview(context);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Empty(preview.RaisedCells);
        Assert.Equal(ToolPreviewBuilder.ValidButRaisesNoTerrain, preview.Explanation);
        Assert.NotEmpty(preview.Outline);
        // Said before Enter, on the input path that was already there.
        Assert.Contains(ToolPreviewBuilder.RaisesNoTerrain, closed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And Enter takes it. The body is saved, and the message says both things:
    /// what was authored, and that it does nothing yet.
    /// </summary>
    [Fact]
    public void EnterAuthorsAContourThatRaisesNothingAndSaysBoth()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));
        var after = edit.Apply(scene);

        Assert.Single(after.ElevationRegions);
        Assert.Equal(
            "Authored mountain_0001 from 3 points · top 4 m; "
                + ToolPreviewBuilder.RaisesNoTerrain,
            edit.Describe!(scene, after));
    }

    [Fact]
    public void ASelfTouchingContourIsBlockedAndStaysADraft()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        // Asymmetric on purpose: a symmetric bow tie encloses no net area, and
        // the contour rule reaches ZeroArea before it ever looks for a crossing.
        // This is the sequence the Core tests use to pin self-contact.
        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 160);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 32, 192);

        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Enter));
        Assert.Equal(4, interaction.ElevationRegionDraft.Count);
        var preview = interaction.ElevationRegionPreview(context);
        Assert.Equal(ElevationRegionDraftKind.Blocked, preview.Kind);
        Assert.Contains("contact with itself", preview.Explanation!, StringComparison.Ordinal);
        Assert.Empty(preview.RaisedCells);
        Assert.NotEmpty(preview.Outline);
    }

    /// <summary>
    /// A body already sitting there at the same height is no longer a reason to
    /// refuse: the two agree about the only thing a hill says.
    /// </summary>
    [Fact]
    public void AnEqualTopOverAnExistingBodyIsReady()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            4.0m);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);
        var preview = interaction.ElevationRegionPreview(context);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        // Ready, and honest about lifting nothing the other body was not
        // already holding at that height.
        Assert.Empty(preview.RaisedCells);
        Assert.Equal(ToolPreviewBuilder.ValidButRaisesNoTerrain, preview.Explanation);
    }

    [Fact]
    public void ThePreviewAndEnterGiveTheSameReason()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 160);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 32, 192);
        var preview = interaction.ElevationRegionPreview(context);
        var message = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Enter));

        Assert.Equal($"Hill blocked: {preview.Explanation}", message.Text);
    }

    [Fact]
    public void EscapeAndDraftUndoStepBackOnePointAtATime()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Assert.True(interaction.HasUnfinishedDraft);
        Assert.NotNull(interaction.UndoDraftStep());
        Assert.Single(interaction.ElevationRegionDraft);
        Assert.IsType<ToolOutcome.Message>(interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Empty(interaction.ElevationRegionDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    [Fact]
    public void EraserRemovesTheTopmostWholeElevationRegion()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);
        var interaction = ElevationRegion();
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene), Point(80, 80), Cell(2, 2)));
        var erased = edit.Apply(scene);

        Assert.Single(erased.ElevationRegions);
        Assert.Equal("mountain_0001", erased.ElevationRegions[0].ElevationRegionId);
    }

    /// <summary>
    /// The pointer sits in a cell the contour fills but outside the contour
    /// itself - the raster asks about the cell's centre, and picking now asks
    /// the same question instead of testing the exact position.
    /// </summary>
    [Fact]
    public void TheEraserPicksByCellRatherThanByPointerPosition()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Triangle(32, 32, 128),
            2.0m);
        var interaction = ElevationRegion();
        interaction.SetEraserEnabled(true);

        Assert.Contains(
            Cell(2, 3),
            ElevationRegionGeometry.TerrainCells(scene, workspace.Metrics, scene.ElevationRegions[0]));
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene), Point(74, 120), Cell(2, 3)));

        Assert.Empty(edit.Apply(scene).ElevationRegions);
    }

    [Fact]
    public void TheEraserPreviewAndTheClickChooseTheSameBody()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);
        var interaction = ElevationRegion();
        interaction.SetEraserEnabled(true);

        var preview = ToolPreviewBuilder.BuildElevationRegionEraser(
            scene, workspace.Metrics, interaction.ActiveTool, eraserEnabled: true, Cell(2, 2));
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene), Point(80, 80), Cell(2, 2)));
        var removed = scene.ElevationRegions
            .Select(static body => body.ElevationRegionId)
            .Except(edit.Apply(scene).ElevationRegions.Select(static body => body.ElevationRegionId));

        Assert.Equal("mountain_0002", preview.ElevationRegionId);
        Assert.Equal("mountain_0002", Assert.Single(removed));
        Assert.Equal(
            ElevationRegionGeometry.CellsRaisedBy(scene, workspace.Metrics, scene.ElevationRegions[^1]),
            preview.LoweredCells);
        Assert.NotEmpty(preview.LoweredCells);
    }

    /// <summary>
    /// Covering a cell and holding it up are two different things, and the
    /// eraser preview shows the second. Here half the contour lies over ground
    /// nobody painted: those cells cannot drop, so they are not highlighted -
    /// while the body itself is still picked, and the canvas still has its
    /// outline to draw.
    /// </summary>
    [Fact]
    public void TheEraserPreviewShowsWhatDropsRatherThanWhatIsCovered()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 2, 2, "grass");
        scene = ElevationRegionEditing.Place(
            scene, workspace.Metrics, Square(32, 32, 160, 160), 4.0m);

        var preview = ToolPreviewBuilder.BuildElevationRegionEraser(
            scene, workspace.Metrics, EditorTool.DrawElevationRegion, eraserEnabled: true, Cell(2, 2));

        Assert.Equal(
            16,
            ElevationRegionGeometry.TerrainCells(
                scene, workspace.Metrics, scene.ElevationRegions[0]).Count);
        Assert.Equal("mountain_0001", preview.ElevationRegionId);
        var lowered = Assert.Single(preview.LoweredCells);
        Assert.Equal(
            new TerrainCellCoordinate(2, 2),
            new TerrainCellCoordinate(lowered.X, lowered.Y));
    }

    [Fact]
    public void WithoutAElevationRegionUnderThePointerTheEraserPreviewIsEmpty()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);

        var preview = ToolPreviewBuilder.BuildElevationRegionEraser(
            scene, workspace.Metrics, EditorTool.DrawElevationRegion, eraserEnabled: true, Cell(0, 0));

        Assert.Null(preview.ElevationRegionId);
        Assert.Empty(preview.LoweredCells);
    }

    [Fact]
    public void SelectElevationRegionPicksTheTopmostBodyByVisibleCell()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);

        var selected = Assert.IsType<ToolOutcome.Message>(interaction.PointerPressed(
            Context(workspace, scene), Point(80, 80), Cell(2, 2)));

        Assert.Equal("mountain_0002", interaction.SelectedElevationRegionId);
        Assert.Contains("top 4 m", selected.Text, StringComparison.Ordinal);
        Assert.Null(interaction.DraggedElevationRegionPointIndex);
    }

    [Fact]
    public void DraggingASelectedPointReshapesOneElevationRegionOnTheTerrainGrid()
    {
        using var workspace = TestWorkspace.Create();
        var points = Square(32, 32, 160, 160).ToList();
        points[1] = ElevationRegionEditing.Point(
            160,
            32,
            ElevationRegionPointMode.Aligned,
            new AuthoringPixelOffset { X = -1, Y = 0 },
            new AuthoringPixelOffset { X = 1, Y = 0 });
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace), workspace.Metrics, points, 4m);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(161, 33), Cell(5, 1));
        interaction.PointerDragged(context, Point(181, 47), Cell(5, 1));
        var preview = interaction.ElevationRegionSelectionPreview(context);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var reshaped = edit.Apply(scene);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Equal(192, preview.Body!.Points[1].PositionAuthoringPx.X);
        Assert.Equal(32, preview.Body.Points[1].PositionAuthoringPx.Y);
        var body = Assert.Single(reshaped.ElevationRegions);
        Assert.Equal("mountain_0001", body.ElevationRegionId);
        Assert.Equal(4m, body.ElevationMeters);
        Assert.Equal(points[1].Mode, body.Points[1].Mode);
        Assert.Equal(points[1].HandleInAuthoringPx, body.Points[1].HandleInAuthoringPx);
        Assert.Equal(points[1].HandleOutAuthoringPx, body.Points[1].HandleOutAuthoringPx);
        Assert.Null(interaction.DraggedElevationRegionPointIndex);
    }

    [Fact]
    public void ASelectedAlignedHandleCanBeDraggedWithoutSnapping()
    {
        using var workspace = TestWorkspace.Create();
        var points = ElevationRegionEditing.ResolveContour(
        [
            new ElevationRegionDraftPoint(32, 32, ElevationRegionPointMode.Aligned),
            new ElevationRegionDraftPoint(160, 32, ElevationRegionPointMode.Aligned),
            new ElevationRegionDraftPoint(160, 160, ElevationRegionPointMode.Aligned),
            new ElevationRegionDraftPoint(32, 160, ElevationRegionPointMode.Aligned),
        ]);
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace), workspace.Metrics, points, 4m);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);
        var point = scene.ElevationRegions[0].Points[1];
        var anchor = point.PositionAuthoringPx;
        var handle = point.HandleOutAuthoringPx;

        interaction.PointerPressed(context, Point(anchor.X, anchor.Y), Cell(5, 1));
        interaction.PointerReleased(context);
        interaction.PointerPressed(
            context,
            Point(anchor.X + handle.X, anchor.Y + handle.Y),
            Cell((anchor.X + handle.X) / 32, (anchor.Y + handle.Y) / 32));
        Assert.Equal(ElevationRegionHandleSide.Out, interaction.DraggedElevationRegionHandleSide);
        interaction.PointerDragged(
            context,
            Point(anchor.X + handle.X / 2, anchor.Y + handle.Y / 2),
            Cell(5, 1));
        var preview = interaction.ElevationRegionSelectionPreview(context);
        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerReleased(context));
        var changed = edit.Apply(scene).ElevationRegions[0].Points[1];

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Equal(handle.X / 2, changed.HandleOutAuthoringPx.X);
        Assert.Equal(handle.Y / 2, changed.HandleOutAuthoringPx.Y);
        Assert.Equal(point.HandleInAuthoringPx, changed.HandleInAuthoringPx);
        Assert.Equal(ElevationRegionPointMode.Aligned, changed.Mode);
    }

    [Fact]
    public void ThePointFieldChangesTheSelectedStoredPointModeAsOneEdit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            4m);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(32, 32), Cell(1, 1));
        interaction.PointerReleased(context);

        var align = Assert.IsType<ToolOutcome.Edit>(
            interaction.SetSelectedElevationRegionPointMode(context, ElevationRegionPointMode.Aligned));
        var aligned = align.Apply(scene);
        interaction.SceneChanged(scene, aligned);

        Assert.Equal(0, interaction.SelectedElevationRegionPointIndex);
        Assert.Equal(ElevationRegionPointMode.Aligned, aligned.ElevationRegions[0].Points[0].Mode);
        Assert.False(aligned.ElevationRegions[0].Points[0].HandleInAuthoringPx.IsZero());
        Assert.False(aligned.ElevationRegions[0].Points[0].HandleOutAuthoringPx.IsZero());

        var makeLinear = Assert.IsType<ToolOutcome.Edit>(
            interaction.SetSelectedElevationRegionPointMode(
                Context(workspace, aligned), ElevationRegionPointMode.Linear));
        var linear = makeLinear.Apply(aligned).ElevationRegions[0].Points[0];
        Assert.Equal(ElevationRegionPointMode.Linear, linear.Mode);
        Assert.True(linear.HandleInAuthoringPx.IsZero());
        Assert.True(linear.HandleOutAuthoringPx.IsZero());
    }

    [Fact]
    public void TheHeightFieldChangesTheSelectedElevationRegionWithoutChangingItsShape()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            4m);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(80, 80), Cell(2, 2));
        var before = scene.ElevationRegions[0];

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.SetSelectedElevationRegionElevation(context, 6.125m));
        var after = Assert.Single(edit.Apply(scene).ElevationRegions);

        Assert.Equal("mountain_0001", interaction.SelectedElevationRegionId);
        Assert.Equal(before.ElevationRegionId, after.ElevationRegionId);
        Assert.Equal(6.125m, after.ElevationMeters);
        Assert.Same(before.Points, after.Points);
    }

    [Fact]
    public void AnInvalidPointMoveTurnsThePreviewBlockedAndIsNotCommitted()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            4m);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(160, 32), Cell(5, 1));
        interaction.PointerDragged(context, Point(32, 192), Cell(1, 6));
        var preview = interaction.ElevationRegionSelectionPreview(context);
        var refused = Assert.IsType<ToolOutcome.Message>(interaction.PointerReleased(context));

        Assert.Equal(ElevationRegionDraftKind.Blocked, preview.Kind);
        Assert.Contains("contact with itself", preview.Explanation!, StringComparison.Ordinal);
        Assert.Contains("Move Hill Point blocked", refused.Text, StringComparison.Ordinal);
        Assert.Equal(160, scene.ElevationRegions[0].Points[1].PositionAuthoringPx.X);
        Assert.Equal(
            ElevationRegionDraftKind.Ready,
            interaction.ElevationRegionSelectionPreview(context).Kind);
    }

    [Fact]
    public void EmptySpaceClearsElevationRegionSelectionAndEscapeDoesToo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);

        interaction.PointerPressed(context, Point(80, 80), Cell(2, 2));
        interaction.PointerPressed(context, Point(1, 1), Cell(0, 0));
        Assert.Null(interaction.SelectedElevationRegionId);

        interaction.PointerPressed(context, Point(80, 80), Cell(2, 2));
        var cleared = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(context, ToolKey.Escape));
        Assert.Equal("Hill selection cleared.", cleared.Text);
        Assert.Null(interaction.SelectedElevationRegionId);
    }

    [Fact]
    public void RemovingTheSelectedElevationRegionClearsSelection()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Nested(workspace);
        var interaction = ElevationRegion(EditorTool.SelectElevationRegion);
        var context = Context(workspace, scene);
        interaction.PointerPressed(context, Point(80, 80), Cell(2, 2));
        var after = ElevationRegionEditing.Remove(scene, "mountain_0002");

        interaction.SceneChanged(scene, after);

        Assert.Null(interaction.SelectedElevationRegionId);
        Assert.Null(interaction.DraggedElevationRegionPointIndex);
    }

    [Fact]
    public void LeavingTheAreaDiscardsTheDraftAndSaysSo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);
        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);

        var outcome = interaction.SelectMode(EditorMode.Terrain);

        var message = Assert.IsType<ToolOutcome.Message>(outcome);
        Assert.Equal("The unfinished hill contour of 2 points was discarded.", message.Text);
        Assert.Empty(interaction.ElevationRegionDraft);
    }

    /// <summary>
    /// ElevationRegion offers no Asset, so `Draw ElevationRegion` may not need one. The whole
    /// path runs with nothing selected: placing points, the preview, and Enter.
    /// </summary>
    [Fact]
    public void DrawElevationRegionNeedsNoSelectedAsset()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);

        Assert.Null(context.SelectedTerrainAssetKey);
        Place(interaction, context, 32, 32);
        Place(interaction, context, 160, 32);
        Place(interaction, context, 96, 160);

        Assert.Equal(ElevationRegionDraftKind.Ready, interaction.ElevationRegionPreview(context).Kind);
        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));
        Assert.Single(edit.Apply(scene).ElevationRegions);
    }

    [Fact]
    public void EnablingTheEraserDiscardsTheDraftAndDisablingItStartsNone()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = ElevationRegion();
        var context = Context(workspace, scene);
        Place(interaction, context, 32, 32);

        var enabled = interaction.SetEraserEnabled(true);
        var disabled = interaction.SetEraserEnabled(false);

        Assert.Equal(
            "The unfinished hill contour of 1 point was discarded.",
            Assert.IsType<ToolOutcome.Message>(enabled).Text);
        Assert.IsType<ToolOutcome.Idle>(disabled);
        Assert.Empty(interaction.ElevationRegionDraft);
        Assert.False(interaction.HasUnfinishedDraft);
    }

    [Fact]
    public void LeavingTheAreaWithoutADraftSaysNothingExtra()
    {
        var interaction = ElevationRegion();

        Assert.IsType<ToolOutcome.Idle>(interaction.SelectMode(EditorMode.Terrain));
        Assert.IsType<ToolOutcome.Idle>(interaction.SetEraserEnabled(true));
    }

    /// <summary>
    /// One control in the context bar, two independent session values. A river
    /// and a hill never share a point mode, only the widget that shows one.
    /// </summary>
    [Fact]
    public void RiverAndElevationRegionPointModesStayIndependent()
    {
        var interaction = ElevationRegion();

        interaction.State.SetElevationRegionPointMode(ElevationRegionPointMode.Aligned);

        Assert.Equal(ElevationRegionPointMode.Aligned, interaction.State.ElevationRegionPointMode);
        Assert.Equal(WaterPointMode.Linear, interaction.State.WaterPointMode);

        interaction.State.SetWaterPointMode(WaterPointMode.Aligned);
        interaction.State.SetElevationRegionPointMode(ElevationRegionPointMode.Linear);

        Assert.Equal(WaterPointMode.Aligned, interaction.State.WaterPointMode);
        Assert.Equal(ElevationRegionPointMode.Linear, interaction.State.ElevationRegionPointMode);
    }

    [Fact]
    public void PreviewUsesTheSameFilledCellsAsTheAuthoredBody()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var draft = new[]
        {
            new ElevationRegionDraftPoint(32, 32, ElevationRegionPointMode.Linear),
            new ElevationRegionDraftPoint(160, 32, ElevationRegionPointMode.Linear),
            new ElevationRegionDraftPoint(96, 160, ElevationRegionPointMode.Linear),
        };
        var preview = ToolPreviewBuilder.BuildElevationRegionDraft(
            scene,
            workspace.Metrics,
            EditorTool.DrawElevationRegion,
            draft,
            pending: null,
            elevationMeters: 4m);
        var authored = ElevationRegionEditing.Place(
            scene,
            workspace.Metrics,
            ElevationRegionEditing.ResolveContour(draft),
            4m);

        Assert.Equal(ElevationRegionDraftKind.Ready, preview.Kind);
        Assert.Equal(
            ElevationRegionGeometry.CellsRaisedBy(
                authored, workspace.Metrics, authored.ElevationRegions[^1]),
            preview.RaisedCells);
    }

    /// <summary>The fixtures above are Scenes the IO boundary would accept.</summary>
    [Fact]
    public void TheTestElevationRegionsSitOnTheTerrainGrid()
    {
        using var workspace = TestWorkspace.Create();

        DocumentValidation.ValidateGrid(Nested(workspace), workspace.Metrics);
    }

    /// <summary>A small hill inside a larger, lower one.</summary>
    private static SceneDocument Nested(TestWorkspace workspace)
    {
        var lower = ElevationRegionEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Triangle(32, 32, 128),
            2m);
        return ElevationRegionEditing.Place(lower, workspace.Metrics, Triangle(64, 64, 64), 4m);
    }

    /// <summary>
    /// An isosceles triangle whose three anchors land on the Terrain grid.
    /// <paramref name="size"/> is therefore a multiple of two Terrain cells.
    /// </summary>
    private static IReadOnlyList<ElevationRegionPointDocument> Triangle(int x, int y, int size) =>
    [
        ElevationRegionEditing.Point(x, y),
        ElevationRegionEditing.Point(x + size, y),
        ElevationRegionEditing.Point(x + (size / 2), y + size),
    ];

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

    private static ToolInteraction ElevationRegion(EditorTool tool = EditorTool.DrawElevationRegion)
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.ElevationRegion);
        interaction.SelectTool(tool);
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        decimal elevation = 4m) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: null,
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: elevation);

    private static void Place(ToolInteraction interaction, ToolContext context, int x, int y)
    {
        interaction.PointerPressed(context, Point(x, y), Cell(x / 32, y / 32));
        interaction.PointerReleased(context);
    }

    private static AuthoringPoint Point(int x, int y) => new(x, y);
    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
