using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The chain arithmetic on its own, with no river around it. These are the
/// properties every curve in SceneMaker rests on, so they are stated here once
/// rather than inferred from a raster somewhere above.
/// </summary>
public sealed class BezierChainTests
{
    [Fact]
    public void AStraightStretchStaysOneSegment()
    {
        var chain = BezierChain.FlattenOpen([Point(0, 0), Point(160, 0)]);

        // Nothing bends, so there is nothing to subdivide: the two authored
        // points are the whole polyline.
        Assert.Equal([new ChainPoint(0, 0), new ChainPoint(160, 0)], chain.Points);
        Assert.Equal([0.0, 160.0], chain.Stations);
        Assert.Equal([0.0, 160.0], chain.AnchorStations);
    }

    [Fact]
    public void AnchorStationsMeasureArcLengthRatherThanPointIndex()
    {
        // Deliberately uneven: 32 pixels to the middle point, 128 from there on.
        var chain = BezierChain.FlattenOpen([Point(0, 96), Point(32, 96), Point(160, 96)]);

        Assert.Equal([0.0, 32.0, 160.0], chain.AnchorStations);
    }

    [Fact]
    public void ACollapsedStretchLeavesItsTwoAnchorsOnTheSameStation()
    {
        // Two points in the same place have no segment between them. The
        // duplicate is dropped from the polyline and both anchors keep the one
        // station that is left, which is what callers interpolating over these
        // stations already answer for. Tidying it away here would move that
        // decision somewhere nobody is looking.
        var chain = BezierChain.FlattenOpen([Point(0, 0), Point(0, 0), Point(160, 0)]);

        Assert.Equal(2, chain.Points.Count);
        Assert.Equal([0.0, 0.0, 160.0], chain.AnchorStations);
    }

    [Fact]
    public void ABendIsSubdividedAndItsStationsKeepClimbing()
    {
        var chain = BezierChain.FlattenOpen(
        [
            Point(0, 0, handleOutY: 128),
            Point(160, 0, handleInY: 128),
        ]);

        Assert.True(chain.Points.Count > 2, "A bent segment has to be flattened into several.");
        for (var index = 1; index < chain.Stations.Count; index++)
            Assert.True(chain.Stations[index] > chain.Stations[index - 1]);
        Assert.Equal([0.0, chain.Stations[^1]], chain.AnchorStations);
    }

    [Fact]
    public void SegmentsCarryTheStationTheyStartAtAndTheRowsTheyReach()
    {
        var chain = BezierChain.FlattenOpen([Point(0, 32), Point(0, 160)]);

        var segment = Assert.Single(BezierChain.Segments(chain));
        Assert.Equal(0.0, segment.StartStation);
        Assert.Equal(0.0, segment.DeltaX);
        Assert.Equal(128.0, segment.DeltaY);
        Assert.Equal(128.0 * 128.0, segment.LengthSquared);
        Assert.Equal(32.0, segment.MinY);
        Assert.Equal(160.0, segment.MaxY);
    }

    [Fact]
    public void ThereIsOneSegmentBetweenEveryPairOfFlattenedPoints()
    {
        var chain = BezierChain.FlattenOpen(
        [
            Point(0, 0, handleOutY: 128),
            Point(160, 0, handleInY: 128),
        ]);

        var segments = BezierChain.Segments(chain);

        Assert.Equal(chain.Points.Count - 1, segments.Count);
        for (var index = 0; index < segments.Count; index++)
            Assert.Equal(chain.Stations[index], segments[index].StartStation);
    }

    [Fact]
    public void AProjectionPastEitherEndIsClampedToTheSegment()
    {
        var segment = Assert.Single(
            BezierChain.Segments(BezierChain.FlattenOpen([Point(0, 0), Point(100, 0)])));

        var before = segment.ProjectTo(-50, 0);
        Assert.Equal(0.0, before.T);
        Assert.Equal(0.0, before.Station);
        Assert.Equal(2500.0, before.DistanceSquared);

        var beyond = segment.ProjectTo(150, 0);
        Assert.Equal(1.0, beyond.T);
        Assert.Equal(100.0, beyond.Station);
        Assert.Equal(2500.0, beyond.DistanceSquared);

        var beside = segment.ProjectTo(25, 8);
        Assert.Equal(0.25, beside.T);
        Assert.Equal(25.0, beside.Station);
        Assert.Equal(64.0, beside.DistanceSquared);
    }

    [Fact]
    public void ASegmentOfNoLengthProjectsToItsOwnStartAtAFractionOfZero()
    {
        // A caller mixing two values by this fraction has to land exactly on the
        // first of them; anything but zero would move a width or a height at a
        // place where there is no direction to move it along.
        var segment = new ChainSegment(
            StartX: 40,
            StartY: 40,
            DeltaX: 0,
            DeltaY: 0,
            LengthSquared: 0,
            MinY: 40,
            MaxY: 40,
            StartStation: 17);

        var projection = segment.ProjectTo(43, 44);

        Assert.Equal(0.0, projection.T);
        Assert.Equal(17.0, projection.Station);
        Assert.Equal(25.0, projection.DistanceSquared);
    }

    [Fact]
    public void AChainNeedsTwoPointsBeforeItIsAnything()
    {
        Assert.Throws<SceneMakerDocumentException>(
            () => BezierChain.FlattenOpen([Point(0, 0)]));
    }

    private static BezierChainPoint Point(
        double x,
        double y,
        double handleInX = 0,
        double handleInY = 0,
        double handleOutX = 0,
        double handleOutY = 0) =>
        new(x, y, handleInX, handleInY, handleOutX, handleOutY);
}
