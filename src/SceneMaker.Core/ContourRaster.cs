namespace SceneMaker.Core;

/// <summary>
/// Turns a closed contour into the Terrain cells it covers.
///
/// <para>One question, asked once: a cell belongs to a contour when its centre
/// lies inside it. That is the same question <see cref="WaterGeometry.Corridor"/>
/// asks of a river, which is what keeps a mountain's edge and a river's edge
/// from disagreeing by half a cell where they meet.</para>
///
/// <para>A centre within
/// <see cref="ClosedChainGeometry.SimplicityToleranceAuthoringPixels"/> of the
/// outline counts as inside. That is not a separate leniency: the water corridor
/// already admits a centre lying exactly on its edge, and a contour whose own
/// edges may sit no closer together than that tolerance cannot then be rastered
/// by a rule that pretends to resolve less. Everywhere else a horizontal ray
/// decides by parity - even-odd, which agrees with the winding rule for the
/// simple rings this accepts and only for those, which is why it accepts only
/// those.</para>
///
/// <para>The ray needs no tolerance of its own for the same reason the
/// simplicity test's sign check needs none: where the crossing it computes is
/// too close to the centre to trust, the centre is already within the tolerance
/// of that segment and the boundary rule has answered.</para>
///
/// <para>Nothing here reads the winding. The same outline drawn clockwise and
/// counter-clockwise fills identically, which is what an orientation that is
/// derived rather than applied is worth.</para>
/// </summary>
public static class ContourRaster
{
    /// <summary>
    /// Whether an authoring-pixel position lies inside the contour. The
    /// predicate the raster is built from, so what a pointer picks and what a
    /// Scene rasters can never disagree.
    /// </summary>
    public static bool Contains(FlattenedClosedChain ring, double authoringX, double authoringY)
    {
        var contour = PreparedContour.For(ring);
        return contour.Contains(authoringX, authoringY, contour.Segments);
    }

    /// <summary>
    /// The Terrain cells this contour covers, canonically ordered by Y then X.
    /// Cells outside the Scene are dropped rather than refused - a contour is
    /// allowed to run off the edge of the map, it simply stops being authored
    /// there.
    ///
    /// <para>The contour is checked once, here, and the cells are then tested
    /// against what that check prepared. Validating per cell would run the
    /// pairwise scan thousands of times to learn the same thing.</para>
    /// </summary>
    public static IReadOnlyList<TerrainCellCoordinate> TerrainCells(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        FlattenedClosedChain ring)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        var contour = PreparedContour.For(ring);
        var step = metrics.AuthoringPixelsPerTerrainCell;
        var tolerance = ClosedChainGeometry.SimplicityToleranceAuthoringPixels;

        // Widened by the tolerance: a centre just outside the outline's own box
        // can still be near enough to it to count.
        var firstX = Math.Max(
            0, WorkspaceMetrics.FloorDivide((int)Math.Floor(contour.MinX - tolerance), step));
        var lastX = Math.Min(
            scene.SizeCells.Width - 1,
            WorkspaceMetrics.FloorDivide((int)Math.Ceiling(contour.MaxX + tolerance), step));
        var firstY = Math.Max(
            0, WorkspaceMetrics.FloorDivide((int)Math.Floor(contour.MinY - tolerance), step));
        var lastY = Math.Min(
            scene.SizeCells.Height - 1,
            WorkspaceMetrics.FloorDivide((int)Math.Ceiling(contour.MaxY + tolerance), step));

        List<TerrainCellCoordinate> cells = [];
        List<ChainSegment> row = [];
        var half = step / 2.0;
        for (var y = firstY; y <= lastY; y++)
        {
            // Which segments a row can possibly meet, and nothing else. This is
            // an optimisation and only that: a segment whose ends are both on
            // the same side of the row cannot cross its ray, and one further
            // than the tolerance away in Y cannot be within the tolerance of a
            // point on it. The rule the cells are judged by is the same either
            // way.
            var centreY = y * step + half;
            row.Clear();
            foreach (var segment in contour.Segments)
            {
                if (centreY >= segment.MinY - tolerance && centreY <= segment.MaxY + tolerance)
                    row.Add(segment);
            }
            if (row.Count == 0) continue;

            for (var x = firstX; x <= lastX; x++)
            {
                if (contour.Contains(x * step + half, centreY, row))
                    cells.Add(new TerrainCellCoordinate(x, y));
            }
        }
        return cells;
    }

    /// <summary>
    /// A contour that has been checked once and taken apart into the segments
    /// the tests need. Holding one is the promise that its ring is simple, which
    /// is what lets the cell test be even-odd.
    /// </summary>
    private sealed class PreparedContour
    {
        private PreparedContour(
            IReadOnlyList<ChainSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            Segments = segments;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public IReadOnlyList<ChainSegment> Segments { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }

        public static PreparedContour For(FlattenedClosedChain ring)
        {
            ArgumentNullException.ThrowIfNull(ring);
            if (ClosedChainGeometry.Validate(ring) is { } defect)
            {
                throw new SceneMakerDocumentException(
                    $"A contour that is {Describe(defect.Kind)} cannot be filled.");
            }

            var minX = double.MaxValue;
            var maxX = double.MinValue;
            var minY = double.MaxValue;
            var maxY = double.MinValue;
            foreach (var point in ring.Points)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }
            return new PreparedContour(BezierChain.Segments(ring), minX, maxX, minY, maxY);
        }

        /// <summary>
        /// The rule, in one place. <paramref name="candidates"/> is whatever the
        /// caller has narrowed the ring down to; narrowing it is allowed to save
        /// work and not to change an answer.
        /// </summary>
        public bool Contains(double x, double y, IReadOnlyList<ChainSegment> candidates)
        {
            var tolerance = ClosedChainGeometry.SimplicityToleranceAuthoringPixels;
            var inside = false;
            foreach (var segment in candidates)
            {
                if (Math.Sqrt(segment.ProjectTo(x, y).DistanceSquared) <= tolerance) return true;

                // Half open in Y, so a vertex sitting exactly on the ray belongs
                // to one of its two segments and not to both.
                var endY = segment.StartY + segment.DeltaY;
                if (segment.StartY > y == endY > y) continue;
                var crossingX = segment.StartX
                    + (y - segment.StartY) / segment.DeltaY * segment.DeltaX;
                // Strictly to the right. A crossing exactly at the point would
                // mean the point is on the segment, and that was answered above.
                if (crossingX > x) inside = !inside;
            }
            return inside;
        }

        private static string Describe(ClosedChainDefectKind kind) => kind switch
        {
            ClosedChainDefectKind.TooFewPoints => "made of fewer than three distinct points",
            ClosedChainDefectKind.ZeroArea => "flat enough to enclose nothing",
            ClosedChainDefectKind.SelfIntersecting => "in contact with itself",
            _ => throw new InvalidOperationException($"Unknown contour defect '{kind}'."),
        };
    }
}
