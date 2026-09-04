using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class OpenChainCorridorTests
{
    [Fact]
    public void WidthIsInterpolatedAtTheProjectedStationWithoutRounding()
    {
        var corridor = Corridor(
            [Point(0, 0), Point(48, 0)],
            [2.499, 2.500],
            widthUnitInAuthoringPixels: 32.0);

        // At the middle the full width is 2.4995, so the exact half width is
        // 39.992 authoring pixels. Rounding the width to a millimetre first
        // would make it 40 and admit the second point too.
        Assert.Equal(24.0, corridor.NearestStation(24, 39.99));
        Assert.Null(corridor.NearestStation(24, 39.996));
    }

    [Fact]
    public void TheTwoEndsAreSquareInsteadOfRound()
    {
        var corridor = Corridor(
            [Point(0, 0), Point(64, 0)],
            [2.0, 2.0],
            widthUnitInAuthoringPixels: 16.0);

        Assert.Equal(0.0, corridor.NearestStation(0, 16));
        Assert.Equal(64.0, corridor.NearestStation(64, 16));
        Assert.Null(corridor.NearestStation(-1, 0));
        Assert.Null(corridor.NearestStation(65, 0));
    }

    [Fact]
    public void TheNearestOfTwoReachingStretchesChoosesTheStation()
    {
        var corridor = Corridor(
            [Point(0, 0), Point(100, 0), Point(100, 20), Point(0, 20)],
            [40.0, 40.0, 40.0, 40.0],
            widthUnitInAuthoringPixels: 1.0);

        Assert.Equal(25.0, corridor.NearestStation(25, 8));
        Assert.Equal(195.0, corridor.NearestStation(25, 12));
    }

    [Fact]
    public void BoundsUseTheLargestHalfWidthAroundTheWholeCenterline()
    {
        var corridor = Corridor(
            [Point(20, 30), Point(80, 70)],
            [2.0, 4.0],
            widthUnitInAuthoringPixels: 10.0);

        Assert.Equal(0.0, corridor.MinX);
        Assert.Equal(100.0, corridor.MaxX);
        Assert.Equal(10.0, corridor.MinY);
        Assert.Equal(90.0, corridor.MaxY);
        Assert.Equal(20.0, corridor.MaximumHalfWidth);
    }

    [Fact]
    public void EveryAuthoredPointNeedsOneWidth()
    {
        var centerline = BezierChain.FlattenOpen([Point(0, 0), Point(64, 0)]);

        var exception = Assert.Throws<ArgumentException>(
            () => OpenChainCorridor.For(centerline, [2.0], 16.0));

        Assert.Contains("one width for every authored chain point", exception.Message);
    }

    private static OpenChainCorridor Corridor(
        IReadOnlyList<BezierChainPoint> points,
        IReadOnlyList<double> widths,
        double widthUnitInAuthoringPixels) =>
        OpenChainCorridor.For(
            BezierChain.FlattenOpen(points),
            widths,
            widthUnitInAuthoringPixels);

    private static BezierChainPoint Point(double x, double y) => new(x, y, 0, 0, 0, 0);
}
