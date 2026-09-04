using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class VoxelToolInteractionTests
{
    [Fact]
    public void TileProducesOneUndoableVoxelDocumentEdit()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = new VoxelToolInteraction();

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.PointerPressed(
            Context(workspace, scene, VoxelAuthoringTool.Tile),
            new VoxelCoordinate(2, 0, 3),
            new VoxelPointMeters(2.5m, 0m, 3.5m)));
        var changed = edit.Apply(scene);

        Assert.Equal(VoxelToolInteraction.TileStroke, edit.StrokeKey);
        Assert.Contains(changed.VoxelCells, cell => cell is { X: 2, Y: 0, Z: 3 });
    }

    [Fact]
    public void HillCommitsAllDraftPointsAsOneInheritedVolume()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Ground(sceneSize: 6);
        var interaction = new VoxelToolInteraction();
        var context = Context(workspace, scene, VoxelAuthoringTool.Hill) with
        {
            Settings = Settings(VoxelAuthoringTool.Hill) with
            {
                HillTopElevationMeters = 3m,
            },
        };
        interaction.PointerPressed(context, default, new VoxelPointMeters(0m, 0m, 0m));
        interaction.PointerPressed(context, default, new VoxelPointMeters(3m, 0m, 0m));
        interaction.PointerPressed(context, default, new VoxelPointMeters(3m, 0m, 3m));
        interaction.PointerPressed(context, default, new VoxelPointMeters(0m, 0m, 3m));

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.KeyPressed(context, ToolKey.Enter));
        var changed = edit.Apply(scene);

        Assert.Empty(interaction.HillDraft);
        Assert.Contains(changed.VoxelCells, cell => cell is { X: 1, Y: 2, Z: 1 });
    }

    [Fact]
    public void GradeFollowingPathRaisesEachNewControlPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 40);
        var interaction = new VoxelToolInteraction();
        var context = Context(workspace, scene, VoxelAuthoringTool.Path) with
        {
            Settings = Settings(VoxelAuthoringTool.Path) with
            {
                GradeFollowing = true,
                PathGrade = 0.25m,
            },
        };

        interaction.PointerPressed(context, default, new VoxelPointMeters(0m, 0m, 0m));
        interaction.PointerPressed(context, default, new VoxelPointMeters(32m, 0m, 0m));

        Assert.Equal(8m, interaction.PathDraft[1].ElevationMeters);
    }

    [Fact]
    public void EscapeRemovesOnlyLastDraftPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = new VoxelToolInteraction();
        var context = Context(workspace, scene, VoxelAuthoringTool.Path);
        interaction.PointerPressed(context, default, new VoxelPointMeters(0m, 0m, 0m));
        interaction.PointerPressed(context, default, new VoxelPointMeters(2m, 0m, 0m));

        interaction.KeyPressed(context, ToolKey.Escape);

        Assert.Single(interaction.PathDraft);
    }

    [Fact]
    public void DraggingAPathPointPullsAlignedBezierHandles()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = new VoxelToolInteraction();
        var context = Context(workspace, scene, VoxelAuthoringTool.Path);
        interaction.PointerPressed(context, default, new VoxelPointMeters(2m, 0m, 2m));

        interaction.PointerDragged(
            context, default, new VoxelPointMeters(4m, 0m, 3m));

        var point = Assert.Single(interaction.PathDraft);
        Assert.Equal(new VoxelPlanOffsetMeters(2m, 1m), point.HandleOut);
        Assert.Equal(new VoxelPlanOffsetMeters(-2m, -1m), point.HandleIn);
    }

    private static VoxelToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        VoxelAuthoringTool tool) =>
        new(scene, workspace.Configuration, workspace.Metrics, Settings(tool));

    private static VoxelToolSettings Settings(VoxelAuthoringTool tool) =>
        new(
            tool,
            VoxelEditMode.Additive,
            "grass",
            HillBaseElevationMeters: 0m,
            HillTopElevationMeters: 2m,
            PathWidthMeters: 2m,
            PathHeightMeters: 2m,
            PathElevationMeters: 0m,
            PathGrade: 0m,
            GradeFollowing: false,
            VoxelPathSupportMode.Floating);

    private static SceneDocument Ground(int sceneSize)
    {
        var scene = TestScenes.EmptyInstance(sizeCells: sceneSize);
        var cells = new List<VoxelCellDocument>();
        for (var z = 0; z < sceneSize; z++)
        for (var x = 0; x < sceneSize; x++)
            cells.Add(new VoxelCellDocument { X = x, Y = 0, Z = z, AssetKey = "grass" });
        return scene with { VoxelCells = cells };
    }
}
