using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The whole way from the pointer to the file and back, with only Godot's input
/// plumbing left out. Drawing a river, applying it, saving it and reading it
/// back is what an author actually does, and every step of it used to be
/// covered separately and nowhere together.
/// </summary>
public sealed class RiverPersistenceTests
{
    [Fact]
    public void ADrawnRiverIsAppliedSavedAndReadBack()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        Assert.True(controller.OpenWorkspaceAt(workspace.RootPath).Succeeded);
        Assert.True(controller.CreateInstance("base", 6, 6).Succeeded);
        Assert.False(controller.IsDirty);

        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.DrawRiver);
        var context = Context(workspace, controller);

        interaction.PointerPressed(context, new AuthoringPoint(32, 32), new TerrainCellCoordinate(1, 1));
        interaction.PointerReleased(context);
        interaction.PointerPressed(context, new AuthoringPoint(160, 96), new TerrainCellCoordinate(5, 3));
        interaction.PointerReleased(context);

        var edit = Assert.IsType<ToolOutcome.Edit>(interaction.KeyPressed(context, ToolKey.Enter));
        var applied = controller.Apply(edit);

        Assert.True(applied.Changed);
        Assert.Single(controller.Document!.WaterBodies);
        // The Scene has to be dirty afterwards, or saving says it saved and
        // writes nothing at all.
        Assert.True(controller.IsDirty);

        Assert.True(controller.SaveScene().Succeeded);
        Assert.False(controller.IsDirty);

        var stored = SceneStore.Load(
            controller.Session!.Workspace,
            Path.Combine(controller.Session!.Workspace.ScenesDirectoryPath, "base.scene.json"));
        var body = Assert.Single(stored.Document.WaterBodies);
        Assert.Equal("river_0001", body.WaterBodyId);
        Assert.Equal(WaterKind.River, body.WaterKind);
        Assert.Equal(2, body.Points.Count);
        Assert.Equal(32, body.Points[0].PositionAuthoringPx.X);
        Assert.Equal(96, body.Points[1].PositionAuthoringPx.Y);
    }

    [Fact]
    public void AnUnfinishedRiverChangesNothingAndLeavesTheSceneClean()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        Assert.True(controller.OpenWorkspaceAt(workspace.RootPath).Succeeded);
        Assert.True(controller.CreateInstance("base", 6, 6).Succeeded);

        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.River);
        interaction.SelectTool(EditorTool.DrawRiver);
        var context = Context(workspace, controller);

        interaction.PointerPressed(context, new AuthoringPoint(32, 32), new TerrainCellCoordinate(1, 1));
        interaction.PointerReleased(context);

        // A draft is not an edit. Nothing is authored until Enter, so a Scene
        // with a river half drawn into it is a Scene with nothing to save - and
        // the interface has to say that rather than claim it saved something.
        Assert.Empty(controller.Document!.WaterBodies);
        Assert.False(controller.IsDirty);
        Assert.True(interaction.HasUnfinishedDraft);
    }

    private static ToolContext Context(TestWorkspace workspace, EditorController controller) => new(
        controller.Document!,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: "river",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: 0.0m);
}
