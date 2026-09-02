using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Recorded rasters, kept so that a refactoring underneath them has something
/// to be measured against.
///
/// <para>The other water tests state a rule and check that the corridor obeys
/// it, which is what they should do. These three state nothing: they hold the
/// cells this code produces today, in full, so that moving the flattening and
/// the station arithmetic into a shared geometry layer can be shown to change
/// nothing rather than asserted to. A golden that fails does not mean the new
/// value is wrong - it means somebody has to look.</para>
///
/// <para>Each covers a place where a change would be invisible to the rules the
/// other tests check: a curve whose flattening decides how many segments the
/// corridor is made of, a width that lands a hair inside a cell centre, and a
/// section that depends on where along the curve a cell projects.</para>
/// </summary>
public sealed class WaterRasterGoldenTests
{
    [Fact]
    public void AHookWithABendKeepsExactlyTheCellsItHasToday()
    {
        using var workspace = TestWorkspace.Create();
        // A hook: down the west edge, east along the south, then a curve back up
        // to the north-west, ending over the half of the map its own source sits
        // in. The bend is what makes the flattening subdivide, and the return
        // leg is what an end cap reaching beyond its own segment used to cut
        // away. Both are decided by the arithmetic this file guards.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                Point(32, 32),
                Point(32, 160),
                Point(
                    160, 160,
                    WaterPointMode.Aligned,
                    handleIn: new AuthoringPixelOffset { X = -48, Y = 0 },
                    handleOut: new AuthoringPixelOffset { X = 0, Y = -48 }),
                Point(96, 96),
            ],
            "river");

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        // Row by row, in the corridor's canonical order: 44 cells.
        (int X, int Y)[] golden =
        [
            (1, 2), (2, 2),
            (1, 3), (2, 3),
            (1, 4), (2, 4),
            (1, 5), (2, 5), (6, 5),
            (1, 6), (2, 6), (6, 6), (7, 6), (8, 6),
            (1, 7), (2, 7), (7, 7), (8, 7), (9, 7),
            (1, 8), (2, 8), (8, 8), (9, 8), (10, 8),
            (1, 9), (2, 9), (3, 9), (4, 9), (5, 9), (6, 9), (7, 9), (8, 9), (9, 9), (10, 9),
            (1, 10), (2, 10), (3, 10), (4, 10), (5, 10), (6, 10), (7, 10), (8, 10), (9, 10), (10, 10),
        ];
        Assert.Equal(golden, cells.Select(static cell => (cell.X, cell.Y)));

        // The section is constant along this curve, so every cell must carry the
        // same span. That is not a height test - it is the check that every cell
        // was sampled at all, whatever station it found.
        Assert.All(cells, cell =>
        {
            Assert.Equal(0.5m, cell.BedMeters);
            Assert.Equal(1.0m, cell.SurfaceMeters);
            Assert.Equal(6.0m, cell.CutTopMeters);
        });
    }

    [Fact]
    public void TheWidthBoundaryKeepsExactlyTheCellsItHasToday()
    {
        using var workspace = TestWorkspace.Create();
        // The same river as the non-rounding regression test, recorded with its
        // coordinates rather than its count: half of 2.5 m is exactly the
        // distance from the centerline to the cell centres of rows 3 and 8, so
        // an interpolated width that drifts by a millionth of a metre either
        // admits two whole rows or does not.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, widthMeters: 2.499m),
                WaterEditing.Point(48, 96, WaterPointMode.Linear, widthMeters: 2.500m),
            ],
            "river");

        var cells = WaterGeometry.Corridor(
            scene, workspace.Metrics, Assert.Single(scene.WaterBodies));

        (int X, int Y)[] golden =
        [
            (0, 4), (1, 4), (2, 4),
            (0, 5), (1, 5), (2, 5),
            (0, 6), (1, 6), (2, 6),
            (0, 7), (1, 7), (2, 7),
        ];
        Assert.Equal(golden, cells.Select(static cell => (cell.X, cell.Y)));
    }

    [Fact]
    public void AFallingRiverKeepsExactlyTheSectionItHasToday()
    {
        using var workspace = TestWorkspace.Create();
        // Straight from the west edge, 4 m falling to 2 m over 160 authoring
        // pixels. The cell centres sit at 16n + 8, so the surface at column n is
        // 4 - (16n + 8) / 80 - a whole tenth of a metre every time, which is
        // what makes this list something one can check by hand rather than only
        // by running it.
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, 4.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        decimal[] golden = [3.9m, 3.7m, 3.5m, 3.3m, 3.1m, 2.9m, 2.7m, 2.5m, 2.3m, 2.1m];
        Assert.Equal([5, 6], cells.Select(static cell => cell.Y).Distinct().Order());
        Assert.Equal(
            Enumerable.Range(0, golden.Length),
            cells.Select(static cell => cell.X).Distinct().Order());
        // Both rows of a column project onto the same station, so both carry the
        // same section.
        Assert.All(cells, cell =>
        {
            Assert.Equal(golden[cell.X], cell.SurfaceMeters);
            Assert.Equal(golden[cell.X] - 0.5m, cell.BedMeters);
            Assert.Equal(golden[cell.X] + 5.0m, cell.CutTopMeters);
        });
    }

    /// <summary>
    /// A point of a one-metre river sitting at ground level: half a metre deep
    /// with five metres of headroom, the fixture's usual section.
    /// </summary>
    private static WaterCurvePointDocument Point(
        int x,
        int y,
        WaterPointMode mode = WaterPointMode.Linear,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) =>
        WaterEditing.Point(
            x,
            y,
            mode,
            elevationMeters: 1.0m,
            channelDepthMeters: 0.5m,
            clearanceAboveMeters: 5.0m,
            widthMeters: 1.0m,
            handleIn: handleIn,
            handleOut: handleOut);
}
