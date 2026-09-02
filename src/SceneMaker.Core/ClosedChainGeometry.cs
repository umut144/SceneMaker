namespace SceneMaker.Core;

/// <summary>
/// Which way round a closed chain was drawn.
///
/// <para>SceneMaker's coordinate space is <c>scene_local_bottom_left_y_up</c>,
/// so a positive shoelace area means counter-clockwise. In a y-down space the
/// same sign means the opposite, which is why this is written down rather than
/// left to whoever reads the sign next.</para>
/// </summary>
public enum ChainOrientation
{
    CounterClockwise,
    Clockwise,
}

/// <summary>Why a closed chain cannot be used as a contour.</summary>
public enum ClosedChainDefectKind
{
    /// <summary>Fewer than three distinct points survived the flattening.</summary>
    TooFewPoints,

    /// <summary>The ring encloses nothing, or a sliver thinner than the tolerance.</summary>
    ZeroArea,

    /// <summary>Two of its edges meet where they must not.</summary>
    SelfIntersecting,
}

/// <summary>
/// What is wrong with a closed chain, and where.
///
/// <para>The two segment indices are carried so that a canvas can later mark
/// the offending stretch. Finding them again means running the whole pairwise
/// scan a second time from somewhere else, so they are kept the first
/// time.</para>
/// </summary>
public sealed record ClosedChainDefect(
    ClosedChainDefectKind Kind,
    int? SegmentA,
    int? SegmentB);

/// <summary>
/// Questions about a flattened ring: which way round it goes, how much it
/// encloses, and whether it is a contour at all.
///
/// <para>Nothing here changes the ring. In particular the orientation is
/// derived and never applied: authored points keep the order and the handles
/// their author gave them, and a consumer that needs a particular winding asks
/// and decides for itself. Reversing a ring behind the author's back would make
/// the document say something they did not draw.</para>
///
/// <para>None of this is exact arithmetic. After recursive de Casteljau
/// subdivision the flattened coordinates are ordinary doubles, and the products
/// in an orientation determinant are rounded before they are subtracted - so a
/// bare <c>cross == 0</c> is not a reliable test for "collinear", and a bare
/// equality is not a reliable test for "touching". The rule here is therefore
/// geometric rather than algebraic, and stated in a unit the author can see:
/// non-adjacent edges must stay at least
/// <see cref="SimplicityToleranceAuthoringPixels"/> apart, and adjacent edges
/// may share their common corner and nothing else. A near touch is a defect for
/// the same reason a touch is: nothing downstream could tell them apart. An
/// adaptive exact predicate would be a different and much larger piece of work,
/// and would change this rule rather than implement it.</para>
/// </summary>
public static class ClosedChainGeometry
{
    /// <summary>
    /// How far apart a contour's own edges have to stay, in authoring pixels.
    ///
    /// <para>A four-thousandth of a pixel: some seven orders of magnitude above
    /// the rounding error of the doubles involved, and far below anything the
    /// authoring grid or any raster derived from it could resolve - a water cell
    /// is sixteen pixels. Small on purpose. It is here to make the test decide
    /// the same way every time, not to express a view about how thin a mountain
    /// may be; that is an authoring-quality question, it belongs with the rule
    /// that fills a contour, and it does not exist yet.</para>
    ///
    /// <para>A power of two, so the constant itself is exact.</para>
    /// </summary>
    public const double SimplicityToleranceAuthoringPixels = 1.0 / 4096.0;

    /// <summary>
    /// Twice the area the ring encloses, halved: positive counter-clockwise,
    /// negative clockwise, in square authoring pixels.
    ///
    /// <para>Summed in ring order from the first point, never sorted, so the
    /// same ring always gives the same number. The terms are taken relative to
    /// the first point - the same value mathematically, but the products stay
    /// the size of the loop instead of the size of the Scene.</para>
    /// </summary>
    public static double SignedArea(FlattenedClosedChain closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        var points = closed.Points;
        if (points.Count < 3) return 0.0;

        var origin = points[0];
        var sum = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var a = points[index];
            var b = points[(index + 1) % points.Count];
            sum += (a.X - origin.X) * (b.Y - origin.Y) - (b.X - origin.X) * (a.Y - origin.Y);
        }
        return sum / 2.0;
    }

    /// <summary>
    /// Which way round the ring goes. A ring with no area has no direction to
    /// report; <see cref="Validate"/> rejects it before anybody asks.
    /// </summary>
    public static ChainOrientation Orientation(FlattenedClosedChain closed) =>
        SignedArea(closed) < 0.0 ? ChainOrientation.Clockwise : ChainOrientation.CounterClockwise;

    /// <summary>
    /// What is wrong with this ring, or null when nothing is.
    ///
    /// <para>Checked in order, because a later answer would be misleading
    /// before an earlier one: a ring of two points has no pairs to compare, and
    /// a ring of three collinear points passes every pair test there is - all
    /// three of its pairs are adjacent - and is caught only by its area.</para>
    ///
    /// <para>The pairwise scan runs in ascending index order and reports the
    /// first offending pair, so the same ring always names the same place. It is
    /// quadratic in the number of flattened points; at a few hundred points per
    /// contour that is a few tens of thousands of comparisons, which is not
    /// worth a sweep line.</para>
    /// </summary>
    public static ClosedChainDefect? Validate(FlattenedClosedChain closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        if (closed.Points.Count < 3)
            return new ClosedChainDefect(ClosedChainDefectKind.TooFewPoints, null, null);

        // A ring whose area is no more than a band of the tolerance's width laid
        // along its whole length encloses nothing anybody could act on.
        if (Math.Abs(SignedArea(closed))
            <= SimplicityToleranceAuthoringPixels * closed.TotalLength)
        {
            return new ClosedChainDefect(ClosedChainDefectKind.ZeroArea, null, null);
        }

        var segments = BezierChain.Segments(closed);
        var count = segments.Count;
        for (var first = 0; first < count; first++)
        {
            for (var second = first + 1; second < count; second++)
            {
                var offending = second == first + 1
                    ? SharesMoreThanItsCorner(segments[first], segments[second])
                    : first == 0 && second == count - 1
                        ? SharesMoreThanItsCorner(segments[second], segments[first])
                        : ComeTooClose(segments[first], segments[second]);
                if (offending)
                {
                    return new ClosedChainDefect(
                        ClosedChainDefectKind.SelfIntersecting, first, second);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Whether two segments that follow one another share anything but the
    /// corner between them.
    ///
    /// <para>Two segments meeting at a point lie on two lines, and two lines
    /// meet once - so unless they are collinear, the corner is all they can
    /// share and there is nothing to look for. If they are collinear and fold
    /// back over one another, then one of the two far ends has run onto the
    /// other segment: a short fold puts the second segment's end on the first,
    /// a long one puts the first segment's start on the second. Asking about
    /// those two ends therefore catches every overlap, and lets a sharp corner
    /// stay a sharp corner.</para>
    /// </summary>
    private static bool SharesMoreThanItsCorner(ChainSegment first, ChainSegment second) =>
        DistanceToSegment(Start(first), second) <= SimplicityToleranceAuthoringPixels
        || DistanceToSegment(End(second), first) <= SimplicityToleranceAuthoringPixels;

    /// <summary>
    /// Whether two segments that are not neighbours reach each other at all.
    ///
    /// <para>Two cases, and they need each other. Segments crossing in an X have
    /// all four of their ends far from one another, so only the sign test finds
    /// them; segments that touch, nearly touch, or lie along one another never
    /// cross, so only the distance finds them. The sign test is used with no
    /// tolerance of its own, and does not need one: where its determinants are
    /// too small to trust, the two segments are within the tolerance of each
    /// other anyway, and the distance rejects them whatever the signs
    /// said.</para>
    /// </summary>
    private static bool ComeTooClose(ChainSegment first, ChainSegment second)
    {
        if (Cross(first, second)) return true;
        return DistanceToSegment(Start(first), second) <= SimplicityToleranceAuthoringPixels
            || DistanceToSegment(End(first), second) <= SimplicityToleranceAuthoringPixels
            || DistanceToSegment(Start(second), first) <= SimplicityToleranceAuthoringPixels
            || DistanceToSegment(End(second), first) <= SimplicityToleranceAuthoringPixels;
    }

    /// <summary>
    /// Whether the two segments cross properly: each one's ends fall on opposite
    /// sides of the other.
    /// </summary>
    private static bool Cross(ChainSegment first, ChainSegment second)
    {
        var startOfSecond = SideOf(first, Start(second));
        var endOfSecond = SideOf(first, End(second));
        var startOfFirst = SideOf(second, Start(first));
        var endOfFirst = SideOf(second, End(first));
        return Opposite(startOfSecond, endOfSecond) && Opposite(startOfFirst, endOfFirst);
    }

    private static bool Opposite(double one, double other) =>
        (one > 0.0 && other < 0.0) || (one < 0.0 && other > 0.0);

    private static double SideOf(ChainSegment segment, ChainPoint point) =>
        segment.DeltaX * (point.Y - segment.StartY)
        - segment.DeltaY * (point.X - segment.StartX);

    private static double DistanceToSegment(ChainPoint point, ChainSegment segment) =>
        Math.Sqrt(segment.ProjectTo(point.X, point.Y).DistanceSquared);

    private static ChainPoint Start(ChainSegment segment) =>
        new(segment.StartX, segment.StartY);

    private static ChainPoint End(ChainSegment segment) =>
        new(segment.StartX + segment.DeltaX, segment.StartY + segment.DeltaY);
}
