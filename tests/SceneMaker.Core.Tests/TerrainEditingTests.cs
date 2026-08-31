using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Pins the invariants that Terrain editing maintains, so that the operations
/// can stop re-validating and re-sorting the whole document on every edit and
/// still be known to produce canonical, valid Scenes.
/// </summary>
public sealed class TerrainEditingTests
{
    [Fact]
    public void PaintKeepsCellsOrderedByYThenX()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        scene = TerrainEditing.Paint(scene, workspace.Terrain, 3, 2, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 0, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "sand");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 1, "grass");

        Assert.Equal(
            new[] { "1:0", "0:1", "2:2", "3:2" },
            scene.TerrainCells.Select(static cell => $"{cell.X}:{cell.Y}").ToArray());
    }

    [Fact]
    public void PaintingTheSameCellReplacesItInsteadOfAddingASecond()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "sand");

        var cell = Assert.Single(scene.TerrainCells);
        Assert.Equal("sand", cell.AssetKey);
    }

    [Fact]
    public void RepaintingACellWithItsOwnAssetLeavesTheContentUnchanged()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 2, 2, "grass");

        var repainted = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "grass");

        Assert.Equal(Signature(scene), Signature(repainted));
    }

    [Fact]
    public void PaintingOutsideTheSceneIsRejected()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 6);

        Assert.Throws<SceneMakerDocumentException>(
            () => TerrainEditing.Paint(scene, workspace.Terrain, 6, 0, "grass"));
        Assert.Throws<SceneMakerDocumentException>(
            () => TerrainEditing.Paint(scene, workspace.Terrain, 0, -1, "grass"));
    }

    [Fact]
    public void PaintingAnAssetThatIsNotTerrainIsRejected()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();

        Assert.Throws<SceneMakerDocumentException>(
            () => TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "stone"));
    }

    [Fact]
    public void ErasingRemovesOnlyTheAddressedCell()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance();
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 0, "grass");

        var erased = TerrainEditing.Erase(scene, 0, 0);

        var cell = Assert.Single(erased.TerrainCells);
        Assert.Equal(1, cell.X);
    }

    /// <summary>
    /// The editor detects "nothing happened" by reference, so an erase that
    /// removes nothing has to hand back the very same document.
    /// </summary>
    [Fact]
    public void ErasingWhereThereIsNoTerrainReturnsTheSameDocument()
    {
        var scene = TestScenes.EmptyInstance();

        Assert.Same(scene, TerrainEditing.Erase(scene, 0, 0));
        Assert.Same(scene, TerrainEditing.EraseLine(scene, 0, 0, 3, 3));
        Assert.Same(scene, TerrainEditing.EraseFill(scene, 0, 0));
    }

    [Fact]
    public void PaintLineCoversExactlyTheCellsOfItsBresenhamLine()
    {
        using var workspace = TestWorkspace.Create();

        var scene = TerrainEditing.PaintLine(
            TestScenes.EmptyInstance(), workspace.Terrain, 0, 0, 4, 2, "grass");

        var expected = TerrainEditing.LineCells(0, 0, 4, 2);
        Assert.Equal(expected.Count, scene.TerrainCells.Count);
        foreach (var cell in expected)
            Assert.Contains(scene.TerrainCells, value => value.X == cell.X && value.Y == cell.Y);
    }

    [Fact]
    public void LineCellsIncludeBothEndpoints()
    {
        Assert.Equal(
            new[] { (2, 3) },
            TerrainEditing.LineCells(2, 3, 2, 3).Select(static c => (c.X, c.Y)).ToArray());
        Assert.Equal(
            new[] { (0, 0), (1, 0), (2, 0), (3, 0) },
            TerrainEditing.LineCells(0, 0, 3, 0).Select(static c => (c.X, c.Y)).ToArray());
    }

    [Fact]
    public void FillSpreadsOverCardinalNeighboursButNotDiagonals()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 4);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 1, "grass");

        var filled = TerrainEditing.Fill(scene, workspace.Terrain, 0, 0, "sand");

        Assert.Equal("sand", AssetAt(filled, 0, 0));
        Assert.Equal("grass", AssetAt(filled, 1, 1));
    }

    [Fact]
    public void FillWithTheAssetThatIsAlreadyThereReturnsTheSameDocument()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 0, 0, "grass");

        Assert.Same(scene, TerrainEditing.Fill(scene, workspace.Terrain, 0, 0, "grass"));
    }

    [Fact]
    public void FillPaintsAConnectedAreaThatHasNoTerrainYet()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 3);

        var filled = TerrainEditing.Fill(scene, workspace.Terrain, 1, 1, "grass");

        Assert.Equal(9, filled.TerrainCells.Count);
    }

    [Fact]
    public void EraseFillRemovesOnlyTheConnectedRegionOfTheSameAsset()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 3);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 0, "grass");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 0, "sand");

        var erased = TerrainEditing.EraseFill(scene, 0, 0);

        var cell = Assert.Single(erased.TerrainCells);
        Assert.Equal(2, cell.X);
        Assert.Equal("sand", cell.AssetKey);
    }

    /// <summary>
    /// Every operation has to leave a document that passes full validation and
    /// survives a serialization round trip, because that is what reaches disk.
    /// </summary>
    [Fact]
    public void ASequenceOfEditsLeavesAValidCanonicalDocument()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.EmptyInstance(sizeCells: 8);

        scene = TerrainEditing.Fill(scene, workspace.Terrain, 4, 4, "grass");
        scene = TerrainEditing.PaintLine(scene, workspace.Terrain, 0, 0, 7, 7, "sand");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 3, 3, "grass");
        scene = TerrainEditing.EraseLine(scene, 0, 7, 7, 0);
        scene = TerrainEditing.Erase(scene, 4, 4);
        scene = TerrainEditing.EraseFill(scene, 0, 0);

        DocumentValidation.Validate(scene);
        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));
        Assert.Equal(Signature(scene), Signature(restored));
    }

    private static string AssetAt(SceneDocument scene, int cellX, int cellY) =>
        scene.TerrainCells.Single(cell => cell.X == cellX && cell.Y == cellY).AssetKey;

    private static string[] Signature(SceneDocument scene) =>
        scene.TerrainCells
            .Select(static cell => $"{cell.X}:{cell.Y}:{cell.AssetKey}")
            .ToArray();
}
