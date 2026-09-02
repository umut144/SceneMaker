using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Closed chains: the ring the flattening produces, which way round it goes,
/// and whether it is a contour at all. No filling, no cells - what a ring
/// encloses is a question for the slice that answers it.
/// </summary>
public sealed class ClosedChainTests
{
    [Fact]
    public void ASquareOfFourLinearPointsFlattensToFourPointsAndFourSegments()
    {
        var ring = BezierChain.FlattenClosed(Square());

        Assert.Equal(
            [Point(0, 0), Point(64, 0), Point(64, 64), Point(0, 64)],
            ring.Points);
        Assert.Equal([0.0, 64.0, 128.0, 192.0], ring.Stations);
        Assert.Equal([0.0, 64.0, 128.0, 192.0], ring.AnchorStations);
        Assert.Equal(4, BezierChain.Segments(ring).Count);
    }

    [Fact]
    public void TotalLengthIncludesTheWayBackToTheFirstPoint()
    {
        var ring = BezierChain.FlattenClosed(Square());

        // The last station only reaches the last point. The way home is a
        // segment like any other, and it is the difference between these two.
        Assert.Equal(192.0, ring.Stations[^1]);
        Assert.Equal(256.0, ring.TotalLength);
        var segments = BezierChain.Segments(ring);
        Assert.Equal(192.0, segments[^1].StartStation);
        Assert.Equal(64.0, Math.Sqrt(segments[^1].LengthSquared));
    }

    [Fact]
    public void TheFirstPointIsNotRepeatedAtTheEnd()
    {
        var straight = BezierChain.FlattenClosed(Square());
        var curved = BezierChain.FlattenClosed(CurvedClosingEdge());

        Assert.NotEqual(straight.Points[0], straight.Points[^1]);
        Assert.NotEqual(curved.Points[0], curved.Points[^1]);
        Assert.Single(straight.Points, point => point == Point(0, 0));
        Assert.Single(curved.Points, point => point == Point(0, 0));
    }

    [Fact]
    public void TheClosingEdgeUsesTheLastPointsOutgoingHandleAndTheFirstPointsIncomingOne()
    {
        var ring = BezierChain.FlattenClosed(CurvedClosingEdge());

        // The closing edge runs from (128, 128) to (0, 0) over the control
        // points (64, 128) and (0, 64), and the curve's own midpoint,
        // (p0 + 3p1 + 3p2 + p3) / 8, is therefore exactly (40, 88). The first
        // subdivision produces that point and nothing rounds it away. Reaching
        // for the first point's outgoing handle and the last point's incoming
        // one - both zero here - would draw a straight line instead, and this
        // point would not be on it.
        var index = ring.Points.ToList().IndexOf(Point(40, 88));
        Assert.True(index >= 0, "The closing edge did not pass through (40, 88).");

        // And it belongs to the closing edge: past the last authored point,
        // short of the way home.
        Assert.True(ring.Stations[index] > ring.AnchorStations[2]);
        Assert.True(ring.Stations[index] < ring.TotalLength);
    }

    [Fact]
    public void AnchorStationsStartAtZeroAndStayBelowTheTotalLength()
    {
        var ring = BezierChain.FlattenClosed(CurvedClosingEdge());

        Assert.Equal(0.0, ring.AnchorStations[0]);
        for (var index = 1; index < ring.AnchorStations.Count; index++)
            Assert.True(ring.AnchorStations[index] > ring.AnchorStations[index - 1]);
        Assert.All(ring.AnchorStations, station => Assert.True(station < ring.TotalLength));
    }

    [Fact]
    public void AClosedChainNeedsAtLeastTwoAuthoredPoints()
    {
        Assert.Throws<SceneMakerDocumentException>(
            () => BezierChain.FlattenClosed([Authored(0, 0)]));
    }

    [Fact]
    public void AStationPastTheEndComesBackRoundInsteadOfBeingClamped()
    {
        Assert.Equal(4.0, BezierChain.WrapStation(260.0, 256.0));
        Assert.Equal(252.0, BezierChain.WrapStation(-4.0, 256.0));
        Assert.Equal(0.0, BezierChain.WrapStation(0.0, 256.0));
        // The total length is the same place as zero, and one place gets one
        // answer. An open chain would clamp both of these to an end instead.
        Assert.Equal(0.0, BezierChain.WrapStation(256.0, 256.0));
        Assert.Equal(4.0, BezierChain.WrapStation(1028.0, 256.0));
    }

    [Fact]
    public void ACounterClockwiseSquareHasAPositiveArea()
    {
        var ring = BezierChain.FlattenClosed(Square());

        // SceneMaker authors in scene_local_bottom_left_y_up, so this winding
        // is the counter-clockwise one and its shoelace area is positive.
        Assert.Equal(4096.0, ClosedChainGeometry.SignedArea(ring));
        Assert.Equal(ChainOrientation.CounterClockwise, ClosedChainGeometry.Orientation(ring));
    }

    [Fact]
    public void TheSameSquareWoundTheOtherWayIsClockwiseAndKeepsItsPointOrder()
    {
        var ring = BezierChain.FlattenClosed(
            [Authored(0, 0), Authored(0, 64), Authored(64, 64), Authored(64, 0)]);

        Assert.Equal(-4096.0, ClosedChainGeometry.SignedArea(ring));
        Assert.Equal(ChainOrientation.Clockwise, ClosedChainGeometry.Orientation(ring));
        // Derived, never applied: nothing here turns the author's ring around.
        Assert.Equal(
            [Point(0, 0), Point(0, 64), Point(64, 64), Point(64, 0)],
            ring.Points);
    }

    [Fact]
    public void ASimpleSquareHasNoDefect()
    {
        Assert.Null(ClosedChainGeometry.Validate(BezierChain.FlattenClosed(Square())));
    }

    [Fact]
    public void ATriangleIsSimpleEvenThoughEveryPairOfItsSegmentsIsAdjacent()
    {
        // Three segments have three pairs and all three are neighbours, so the
        // non-adjacent test never runs. That is not a hole: a triangle with an
        // area cannot cross itself.
        var ring = BezierChain.FlattenClosed(
            [Authored(0, 0), Authored(64, 0), Authored(32, 64)]);

        Assert.Null(ClosedChainGeometry.Validate(ring));
    }

    [Fact]
    public void AFigureEightIsRejectedWhereItCrosses()
    {
        // Deliberately lopsided. A symmetric figure eight has two lobes of
        // opposite sign and no net area at all, and would be caught one check
        // earlier for the wrong reason.
        var ring = BezierChain.FlattenClosed(
            [Authored(0, 0), Authored(64, 64), Authored(64, 0), Authored(0, 96)]);

        var defect = Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring));

        Assert.Equal(ClosedChainDefectKind.SelfIntersecting, defect.Kind);
        Assert.Equal(0, defect.SegmentA);
        Assert.Equal(2, defect.SegmentB);
    }

    [Fact]
    public void TwoSegmentsThatOnlyTouchAreRejectedToo()
    {
        // A pinch: the last point comes back down onto the first edge and stops
        // exactly on it. The ring still encloses 2048 square pixels, so it
        // passes the area check and is caught only here.
        var ring = BezierChain.FlattenClosed(Pinch(0.0));

        var defect = Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring));

        Assert.Equal(ClosedChainDefectKind.SelfIntersecting, defect.Kind);
        Assert.Equal(0, defect.SegmentA);
        Assert.Equal(3, defect.SegmentB);
    }

    [Fact]
    public void ANearTouchIsRejectedAndAClearGapIsNot()
    {
        // The same pinch, stopping short of the edge. Half the tolerance is
        // still a defect and a whole pixel is not, which is what makes the
        // tolerance a stated rule rather than an accident of arithmetic: a
        // contour's edges have to stay apart, and by how much is written down.
        var tooClose = BezierChain.FlattenClosed(
            Pinch(ClosedChainGeometry.SimplicityToleranceAuthoringPixels / 2.0));
        var clear = BezierChain.FlattenClosed(Pinch(1.0));

        Assert.Equal(
            ClosedChainDefectKind.SelfIntersecting,
            Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(tooClose)).Kind);
        Assert.Null(ClosedChainGeometry.Validate(clear));
    }

    [Fact]
    public void ASegmentThatFoldsBackAlongItsNeighbourIsRejected()
    {
        // Neighbours may share their corner and nothing else. These two lie on
        // the same line and run back over one another, which is more.
        var ring = BezierChain.FlattenClosed(
            [Authored(0, 0), Authored(64, 0), Authored(32, 0), Authored(32, 32)]);

        var defect = Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring));

        Assert.Equal(ClosedChainDefectKind.SelfIntersecting, defect.Kind);
        Assert.Equal(0, defect.SegmentA);
        Assert.Equal(1, defect.SegmentB);
    }

    [Fact]
    public void AContourThatFoldsOntoOneOfItsOwnLinesIsRejected()
    {
        // A ring that comes back along an edge it has already drawn. Only the
        // kind is promised here: to lie along one of its own lines it has to
        // reach that line twice, so it touches itself in more than one place and
        // which pair is found first is not something to write down.
        var ring = BezierChain.FlattenClosed(
        [
            Authored(0, 0), Authored(64, 0), Authored(64, 32),
            Authored(16, 32), Authored(16, 0), Authored(48, 0),
        ]);

        Assert.Equal(
            ClosedChainDefectKind.SelfIntersecting,
            Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring)).Kind);
    }

    [Fact]
    public void ARingWithNoAreaIsRejectedBeforeItsPairsAreEvenLookedAt()
    {
        // Three points on a line. Every pair of its segments is a neighbour, so
        // nothing but the area can catch it - which is why the area is asked
        // first.
        var ring = BezierChain.FlattenClosed(
            [Authored(0, 0), Authored(32, 0), Authored(64, 0)]);

        var defect = Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring));

        Assert.Equal(ClosedChainDefectKind.ZeroArea, defect.Kind);
        Assert.Null(defect.SegmentA);
        Assert.Null(defect.SegmentB);
    }

    [Fact]
    public void ALoopThatCollapsesToTwoPointsIsRejectedForTooFewPoints()
    {
        // There and back again: the closing edge retraces the first, and its
        // repeat of the starting point is dropped. Two points are left, which is
        // a line and not a contour.
        var ring = BezierChain.FlattenClosed([Authored(0, 0), Authored(64, 0)]);

        Assert.Equal(2, ring.Points.Count);
        Assert.Equal(
            ClosedChainDefectKind.TooFewPoints,
            Assert.IsType<ClosedChainDefect>(ClosedChainGeometry.Validate(ring)).Kind);
    }

    private static BezierChainPoint[] Square() =>
        [Authored(0, 0), Authored(64, 0), Authored(64, 64), Authored(0, 64)];

    /// <summary>
    /// A triangle whose closing edge is the only curved one: the last point
    /// leaves west, the first arrives from the north.
    /// </summary>
    private static BezierChainPoint[] CurvedClosingEdge() =>
    [
        new(0, 0, HandleInX: 0, HandleInY: 64, HandleOutX: 0, HandleOutY: 0),
        Authored(128, 0),
        new(128, 128, HandleInX: 0, HandleInY: 0, HandleOutX: -64, HandleOutY: 0),
    ];

    /// <summary>
    /// A rectangle whose last point reaches back down towards its first edge and
    /// stops <paramref name="gap"/> authoring pixels above it.
    /// </summary>
    private static BezierChainPoint[] Pinch(double gap) =>
    [
        Authored(0, 0), Authored(64, 0), Authored(64, 64), Authored(32, 64),
        Authored(32, gap),
    ];

    private static BezierChainPoint Authored(double x, double y) => new(x, y, 0, 0, 0, 0);

    private static ChainPoint Point(double x, double y) => new(x, y);
}
