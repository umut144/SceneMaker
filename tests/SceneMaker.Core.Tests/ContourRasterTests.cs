using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Filling a closed contour into Terrain cells.
///
/// <para>The fixture's Scene is 6 x 6 cells of 32 authoring pixels, so a cell
/// centre sits at 32n + 16 - which is what makes every expectation here
/// something one can count out by hand rather than record from a run.</para>
/// </summary>
public sealed class ContourRasterTests
{
    [Fact]
    public void ASquareContourFillsTheCellsItsOutlineEncloses()
    {
        var cells = Fill(Ring((32, 32), (160, 32), (160, 160), (32, 160)));

        // The outline runs between the centres at 16 and 176 and encloses the
        // four in between, on both axes.
        Assert.Equal(Block(1, 4, 1, 4), cells);
    }

    [Fact]
    public void TheSameSquareWoundClockwiseFillsExactlyTheSameCells()
    {
        // Even-odd parity does not read the winding, and nothing here derives
        // one to act on. The same outline is the same shape either way round.
        var counterClockwise = Fill(Ring((32, 32), (160, 32), (160, 160), (32, 160)));
        var clockwise = Fill(Ring((32, 32), (32, 160), (160, 160), (160, 32)));

        Assert.Equal(counterClockwise, clockwise);
    }

    [Fact]
    public void ACentreOnTheOutlineCountsAsInside()
    {
        // Edges at 16 and 112 run exactly through cell centres. Those cells are
        // in - the same way a water cell whose centre sits exactly on the edge
        // of a corridor is in.
        var cells = Fill(Ring((16, 16), (112, 16), (112, 112), (16, 112)));

        Assert.Equal(Block(0, 3, 0, 3), cells);
    }

    [Fact]
    public void ACentreWithinTheToleranceOfTheOutlineIsStillInside()
    {
        // The same outline nudged out by half the tolerance. The centres at 16
        // now sit outside it, and still count.
        var nudge = ClosedChainGeometry.SimplicityToleranceAuthoringPixels / 2.0;
        var cells = Fill(Ring(
            (16 + nudge, 16 + nudge),
            (112 + nudge, 16 + nudge),
            (112 + nudge, 112 + nudge),
            (16 + nudge, 112 + nudge)));

        Assert.Equal(Block(0, 3, 0, 3), cells);
    }

    [Fact]
    public void ACentreAClearPixelOutsideIsNot()
    {
        // A whole authoring pixel is not within the tolerance, and the outermost
        // row and column fall away. With the test above, that is what makes the
        // tolerance a stated rule and not an accident: how far apart is written
        // down, and it decides.
        var cells = Fill(Ring((17, 17), (113, 17), (113, 113), (17, 113)));

        Assert.Equal(Block(1, 3, 1, 3), cells);
    }

    [Fact]
    public void ADiagonalEdgeIsFilledByTheCentresItPassesThrough()
    {
        var cells = Fill(Ring((0, 0), (192, 0), (0, 192)));

        // The hypotenuse runs through every centre whose coordinates sum to 192,
        // and those are in. What is left is every cell with column + row <= 5.
        List<TerrainCellCoordinate> expected = [];
        for (var y = 0; y <= 5; y++)
        {
            for (var x = 0; x + y <= 5; x++) expected.Add(new TerrainCellCoordinate(x, y));
        }
        Assert.Equal(expected, cells);
    }

    [Fact]
    public void AConcaveContourIsFilledWhereItActuallyReaches()
    {
        // An L. No centre lands on an edge, so this is parity alone - and parity
        // is what makes a concave outline fill its own shape rather than its
        // bounding box.
        var cells = Fill(Ring(
            (0, 0), (192, 0), (192, 64), (64, 64), (64, 192), (0, 192)));

        Assert.Equal([.. Block(0, 5, 0, 1), .. Block(0, 1, 2, 5)], cells);
    }

    [Fact]
    public void AContourSmallerThanACellStillCoversTheOneItSitsOn()
    {
        var cells = Fill(Ring((40, 40), (56, 40), (56, 56), (40, 56)));

        Assert.Equal(new TerrainCellCoordinate(1, 1), Assert.Single(cells));
    }

    [Fact]
    public void AContourThatMissesEveryCentreCoversNothing()
    {
        // Between two centres and touching neither. Empty is the right answer,
        // not a failure: the rule is about centres.
        Assert.Empty(Fill(Ring((20, 20), (28, 20), (28, 28), (20, 28))));
    }

    [Fact]
    public void CellsOutsideTheSceneAreDroppedRatherThanRefused()
    {
        var cells = Fill(Ring((96, 96), (300, 96), (300, 300), (96, 300)));

        Assert.Equal(Block(3, 5, 3, 5), cells);
    }

    [Fact]
    public void TheCellsComeBackInCanonicalOrderWithoutRepeats()
    {
        var cells = Fill(Ring((0, 0), (192, 0), (192, 64), (64, 64), (64, 192), (0, 192)));

        Assert.Equal(cells.OrderBy(static cell => cell.Y).ThenBy(static cell => cell.X), cells);
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void ContainsAndTheRasterNeverDisagree()
    {
        // One predicate. What a pointer would pick and what the Scene rasters
        // are the same question asked twice, and they answer the same way for
        // every cell of the map - inside and out.
        var contour = Ring((0, 0), (192, 0), (192, 64), (64, 64), (64, 192), (0, 192));
        var cells = Fill(contour);

        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                Assert.Equal(
                    cells.Contains(new TerrainCellCoordinate(x, y)),
                    ContourRaster.Contains(contour, x * 32 + 16, y * 32 + 16));
            }
        }
    }

    [Fact]
    public void ACurvedContourFollowsItsCurveAndNotItsControlPolygon()
    {
        // A lens: two anchors with handles bulging 96 pixels apart. A cubic
        // reaches three quarters of the way to its handles, so the curve tops
        // out at y = 168 while its control points stand at y = 192 - and the
        // centres at 16 and 176 lie between the two. Filling the control
        // polygon would take rows 0 and 5; filling the curve does not.
        var cells = Fill(BezierChain.FlattenClosed(
        [
            new BezierChainPoint(32, 96, 0, -96, 0, 96),
            new BezierChainPoint(160, 96, 0, 96, 0, -96),
        ]));

        Assert.Equal([1, 2, 3, 4], cells.Select(static cell => cell.Y).Distinct().Order());
        Assert.Equal(
            [
                .. Block(2, 3, 1, 1),
                .. Block(1, 4, 2, 3),
                .. Block(2, 3, 4, 4),
            ],
            cells);
    }

    [Fact]
    public void ADefectiveContourIsRefusedRatherThanFilled()
    {
        // Even-odd agrees with the winding rule only for a simple ring, so the
        // fill is only allowed to see one.
        using var workspace = TestWorkspace.Create();

        Assert.Throws<SceneMakerDocumentException>(() => ContourRaster.TerrainCells(
            TestScenes.EmptyInstance(), workspace.Metrics, FigureEight()));
    }

    [Fact]
    public void ContainsRefusesADefectiveContourToo()
    {
        // The same precondition, or the predicate and the raster would stop
        // being the same question.
        Assert.Throws<SceneMakerDocumentException>(
            () => ContourRaster.Contains(FigureEight(), 32, 32));
    }

    private static IReadOnlyList<TerrainCellCoordinate> Fill(FlattenedClosedChain ring)
    {
        using var workspace = TestWorkspace.Create();
        return ContourRaster.TerrainCells(
            TestScenes.EmptyInstance(), workspace.Metrics, ring);
    }

    /// <summary>A contour of straight edges through the given corners.</summary>
    private static FlattenedClosedChain Ring(params (double X, double Y)[] corners) =>
        BezierChain.FlattenClosed(
            [.. corners.Select(corner => new BezierChainPoint(corner.X, corner.Y, 0, 0, 0, 0))]);

    private static FlattenedClosedChain FigureEight() =>
        Ring((0, 0), (64, 64), (64, 0), (0, 96));

    /// <summary>
    /// Every cell of a rectangle of them, in the order the raster produces:
    /// row by row, and each row left to right.
    /// </summary>
    private static List<TerrainCellCoordinate> Block(
        int firstX,
        int lastX,
        int firstY,
        int lastY)
    {
        List<TerrainCellCoordinate> cells = [];
        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++) cells.Add(new TerrainCellCoordinate(x, y));
        }
        return cells;
    }
}
