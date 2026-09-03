using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Pins how a rectangle maps onto Terrain cells. No Prop rule rests on this any
/// more — a Prop needs no ground — but the mapping itself still answers the
/// Template mask, and the set and formatting helpers still serve the water
/// export warning, so it stays pinned here.
/// </summary>
public sealed class TerrainCoverageTests
{
    [Fact]
    public void AFootprintOnOneCellIntersectsExactlyThatCell()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Equal(
            new[] { (2, 2) },
            Cells(new PropBoundsAuthoringPixels(64, 64, 32, 32), workspace));
    }

    [Fact]
    public void AFootprintSpanningCellBordersCoversEveryCellItTouches()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Equal(
            new[] { (2, 2), (3, 2) },
            Cells(new PropBoundsAuthoringPixels(64, 64, 64, 32), workspace));

        Assert.Equal(
            new[] { (1, 1), (2, 1), (1, 2), (2, 2) },
            Cells(new PropBoundsAuthoringPixels(48, 48, 32, 32), workspace));
    }

    /// <summary>
    /// Negative coordinates have to floor rather than truncate towards zero: a
    /// box from -16 to +16 straddles cell -1 and cell 0 on both axes.
    /// </summary>
    [Fact]
    public void NegativeCoordinatesFloorInsteadOfTruncating()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Equal(
            new[] { (-1, -1), (0, -1), (-1, 0), (0, 0) },
            Cells(new PropBoundsAuthoringPixels(-16, -16, 32, 32), workspace));
    }

    [Fact]
    public void AnEmptyFootprintIsRejected()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TerrainCoverage.IntersectedCells(
                new PropBoundsAuthoringPixels(0, 0, 0, 32), workspace.Metrics));
    }

    [Fact]
    public void MissingCellsNamesOnlyTheCellsTheSceneDoesNotCover()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 2, 2, "grass");
        var bounds = new PropBoundsAuthoringPixels(64, 64, 64, 32);

        var missing = TerrainCoverage.MissingCells(scene, bounds, workspace.Metrics);

        Assert.Equal(
            new[] { (3, 2) },
            missing.Select(static cell => (cell.X, cell.Y)).ToArray());
        Assert.False(TerrainCoverage.IsComplete(scene, bounds, workspace.Metrics));
        Assert.True(TerrainCoverage.IsComplete(
            scene, new PropBoundsAuthoringPixels(64, 64, 32, 32), workspace.Metrics));
    }

    [Fact]
    public void FormatMissingCellsCollapsesContiguousRunsPerRow()
    {
        var cells = new[]
        {
            new TerrainCellCoordinate(1, 0),
            new TerrainCellCoordinate(2, 0),
            new TerrainCellCoordinate(3, 0),
            new TerrainCellCoordinate(5, 0),
            new TerrainCellCoordinate(0, 1),
        };

        Assert.Equal("y=0: x=1..3,5; y=1: x=0", TerrainCoverage.FormatMissingCells(cells));
    }

    [Fact]
    public void FormatMissingCellsReportsNoneForAnEmptyList()
    {
        Assert.Equal("none", TerrainCoverage.FormatMissingCells(Array.Empty<TerrainCellCoordinate>()));
    }

    private static (int X, int Y)[] Cells(
        PropBoundsAuthoringPixels bounds,
        TestWorkspace workspace) =>
        TerrainCoverage.IntersectedCells(bounds, workspace.Metrics)
            .Select(static cell => (cell.X, cell.Y))
            .ToArray();
}
