using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The height the tools author at travels in the ToolContext, next to the
/// selected Asset. It starts at the Scene's own ground height and the context
/// bar moves it from there.
/// </summary>
public sealed class ToolElevationTests
{
    [Fact]
    public void ThePencilPaintsAtTheHeightTheContextCarries()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);

        var outcome = interaction.PointerPressed(
            Context(workspace, scene, elevation: 2.5m), Point(64, 64), Cell(2, 2));

        var painted = Assert.IsType<ToolOutcome.Edit>(outcome).Apply(scene);
        Assert.Equal(2.5m, Assert.Single(painted.TerrainCells).ElevationMeters);
    }

    [Fact]
    public void TheFillToolRewritesAWholeRegionToOneHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace, sizeCells: 3);
        var interaction = At(EditorMode.Terrain, EditorTool.Fill);

        var outcome = interaction.PointerPressed(
            Context(workspace, scene, elevation: 0.0m, terrain: "sand"), Point(0, 0), Cell(0, 0));

        var filled = Assert.IsType<ToolOutcome.Edit>(outcome).Apply(scene);
        Assert.Equal(9, filled.TerrainCells.Count);
        Assert.All(filled.TerrainCells, cell => Assert.Equal(0.0m, cell.ElevationMeters));
    }

    [Fact]
    public void APlacedPropTakesTheSameHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var interaction = At(EditorMode.Props, EditorTool.Pencil);

        var outcome = interaction.PointerPressed(
            Context(workspace, scene, elevation: 1.1m), Point(64, 64), Cell(2, 2));

        var placed = Assert.IsType<ToolOutcome.Edit>(outcome).Apply(scene);
        Assert.Equal(1.1m, Assert.Single(placed.Props).ElevationMeters);
    }

    [Fact]
    public void ADragKeepsTheHeightAcrossTheWholeStroke()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        var interaction = At(EditorMode.Terrain, EditorTool.Pencil);
        var context = Context(workspace, scene, elevation: 4.0m);

        var first = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(context, Point(0, 0), Cell(0, 0))).Apply(scene);
        interaction.SceneChanged(scene, first);
        var second = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerDragged(
                Context(workspace, first, elevation: 4.0m), Point(32, 0), Cell(1, 0))).Apply(first);

        Assert.Equal(2, second.TerrainCells.Count);
        Assert.All(second.TerrainCells, cell => Assert.Equal(4.0m, cell.ElevationMeters));
    }

    private static ToolInteraction At(EditorMode mode, EditorTool tool)
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(mode);
        interaction.SelectTool(tool);
        return interaction;
    }

    private static ToolContext Context(
        TestWorkspace workspace,
        SceneDocument scene,
        decimal elevation,
        string terrain = "grass") => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: terrain,
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: elevation);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
