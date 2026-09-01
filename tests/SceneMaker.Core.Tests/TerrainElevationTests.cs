using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Height is authored per cell and per Prop, because one grass Asset covers
/// valley floor and hill alike. The Scene carries the height a new cell takes
/// when the author names none.
/// </summary>
public sealed class TerrainElevationTests
{
    [Fact]
    public void ANewSceneStartsAtGroundLevel()
    {
        var scene = SceneDocument.CreateInstance("base", 4, 4);

        Assert.Equal(SceneDocument.GroundElevationMeters, scene.DefaultElevationMeters);
    }

    [Fact]
    public void PaintingWithoutAHeightTakesTheScenesDefault()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 4, 4, defaultElevationMeters: 2.5m);

        var painted = TerrainEditing.Paint(scene, workspace.Terrain, 1, 1, "grass");

        Assert.Equal(2.5m, Assert.Single(painted.TerrainCells).ElevationMeters);
    }

    [Fact]
    public void PaintingWithAHeightOverridesTheDefault()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 4, 4);

        var painted = TerrainEditing.Paint(scene, workspace.Terrain, 1, 1, "grass", 0.0m);

        Assert.Equal(0.0m, Assert.Single(painted.TerrainCells).ElevationMeters);
    }

    [Fact]
    public void RepaintingACellReplacesItsHeightToo()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            SceneDocument.CreateInstance("base", 4, 4), workspace.Terrain, 1, 1, "grass", 3.0m);

        var repainted = TerrainEditing.Paint(scene, workspace.Terrain, 1, 1, "sand", 0.5m);

        var cell = Assert.Single(repainted.TerrainCells);
        Assert.Equal("sand", cell.AssetKey);
        Assert.Equal(0.5m, cell.ElevationMeters);
    }

    [Fact]
    public void ALineTakesOneHeightForEveryCell()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 8, 8);

        var painted = TerrainEditing.PaintLine(
            scene, workspace.Terrain, 0, 0, 3, 0, "grass", 2.0m);

        Assert.Equal(4, painted.TerrainCells.Count);
        Assert.All(painted.TerrainCells, cell => Assert.Equal(2.0m, cell.ElevationMeters));
    }

    [Fact]
    public void FillingRewritesTheHeightOfEveryCellItReaches()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 2, 2);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass", 1.0m);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 0, "grass", 1.0m);

        var filled = TerrainEditing.Fill(scene, workspace.Terrain, 0, 0, "sand", 0.0m);

        Assert.Equal(2, filled.TerrainCells.Count);
        Assert.All(filled.TerrainCells, cell =>
        {
            Assert.Equal("sand", cell.AssetKey);
            Assert.Equal(0.0m, cell.ElevationMeters);
        });
    }

    [Fact]
    public void FillingLeavesTheHeightOfCellsItDoesNotReach()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 4, 1);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass", 1.0m);
        // A different Asset breaks the region, so cell 2 keeps its own height.
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 0, "sand", 4.0m);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 0, "grass", 9.0m);

        var filled = TerrainEditing.Fill(scene, workspace.Terrain, 0, 0, "sand", 0.0m);

        Assert.Equal(4.0m, filled.TerrainCells.Single(cell => cell.X == 1).ElevationMeters);
        Assert.Equal(9.0m, filled.TerrainCells.Single(cell => cell.X == 2).ElevationMeters);
        Assert.Equal(0.0m, filled.TerrainCells.Single(cell => cell.X == 0).ElevationMeters);
    }

    [Fact]
    public void APropTakesItsOwnHeightSoABridgeCanStandOverWater()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);

        scene = PropEditing.Place(scene, workspace.Props, 32, 32, "stone", 1.1m);

        Assert.Equal(1.1m, Assert.Single(scene.Props).ElevationMeters);
    }

    [Fact]
    public void APropWithoutAHeightTakesTheScenesDefault()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace, sizeCells: 4);

        scene = PropEditing.Place(scene, workspace.Props, 32, 32, "stone");

        Assert.Equal(
            scene.DefaultElevationMeters,
            Assert.Single(scene.Props).ElevationMeters);
    }

    [Fact]
    public void HeightsSurviveAWriteAndReadRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneDocument.CreateInstance("base", 4, 4, defaultElevationMeters: 0.5m);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass", 2.25m);
        scene = PropEditing.Place(scene, workspace.Props, 0, 0, "stone", 2.25m);

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));

        Assert.Equal(0.5m, restored.DefaultElevationMeters);
        Assert.Equal(2.25m, Assert.Single(restored.TerrainCells).ElevationMeters);
        Assert.Equal(2.25m, Assert.Single(restored.Props).ElevationMeters);
    }
}
