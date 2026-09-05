using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RouteSurfaceBakeTests
{
    [Fact]
    public void CurvedAuthoredSegmentsMapEveryQuadAndJoinToTheirOwnInterval()
    {
        using var workspace = TestWorkspace.Create();
        var route = Route(
            Point(0, 0, handleOut: new AuthoringPixelOffset { X = 0, Y = 64 }),
            Point(64, 0, handleIn: new AuthoringPixelOffset { X = 0, Y = 64 }),
            Point(96, 32));

        var bake = RouteSurfaceBake.Build(workspace.Metrics, route);
        var flattenedSegmentCount = bake.CenterlineSamples.Count - 1;
        var expectedTriangleIndexCount = flattenedSegmentCount * 6
            + (flattenedSegmentCount - 1) * RouteSurfaceBake.RoundJoinSides * 3;
        var first = RouteSurfaceBake.TrianglesForSegment(bake, bake.Segments[0]).ToList();
        var second = RouteSurfaceBake.TrianglesForSegment(bake, bake.Segments[1]).ToList();
        var portalSample = bake.Segments[0].EndSampleIndex;
        var portalJoinOffset = flattenedSegmentCount * 6
            + (portalSample - 1) * RouteSurfaceBake.RoundJoinSides * 3;
        var afterPortalJoin = portalJoinOffset + RouteSurfaceBake.RoundJoinSides * 3;

        Assert.True(bake.Segments[0].EndSampleIndex - bake.Segments[0].StartSampleIndex > 1);
        Assert.Equal(expectedTriangleIndexCount, bake.TriangleIndices.Count);
        Assert.All(first, triangle =>
        {
            Assert.Equal(0, triangle.TriangleOffset % 3);
            Assert.InRange(triangle.TriangleOffset, 0, bake.TriangleIndices.Count - 3);
        });
        Assert.All(second, triangle =>
        {
            Assert.Equal(0, triangle.TriangleOffset % 3);
            Assert.InRange(triangle.TriangleOffset, 0, bake.TriangleIndices.Count - 3);
        });
        Assert.Equal(
            RouteSurfaceBake.RoundJoinSides + 2,
            first.Count(triangle =>
                triangle.EndPortalSampleIndex == portalSample
                && triangle.StartPortalSampleIndex is null));
        Assert.Equal(
            RouteSurfaceBake.RoundJoinSides + 2,
            second.Count(triangle =>
                triangle.StartPortalSampleIndex == portalSample
                && triangle.EndPortalSampleIndex is null));
        Assert.Equal(
            first.Where(triangle =>
                    triangle.TriangleOffset >= portalJoinOffset
                    && triangle.TriangleOffset < afterPortalJoin)
                .Select(static triangle => triangle.TriangleOffset),
            second.Where(triangle =>
                    triangle.TriangleOffset >= portalJoinOffset
                    && triangle.TriangleOffset < afterPortalJoin)
                .Select(static triangle => triangle.TriangleOffset));
    }

    [Fact]
    public void SegmentTriangleLookupRefusesAnUnexpectedBakeLayout()
    {
        using var workspace = TestWorkspace.Create();
        var bake = RouteSurfaceBake.Build(
            workspace.Metrics,
            Route(Point(0, 0), Point(32, 0)));
        var malformed = bake with
        {
            TriangleIndices = [.. bake.TriangleIndices, 0, 1, 2],
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            RouteSurfaceBake.TrianglesForSegment(malformed, malformed.Segments[0]).ToList());

        Assert.Contains("unexpected baked triangle layout", exception.Message,
            StringComparison.Ordinal);
    }

    private static RouteSurfaceDocument Route(params RouteSurfacePointDocument[] points) => new()
    {
        RouteSurfaceId = "route_0001",
        AssetKey = "grass",
        Points = [.. points],
        Segments = Enumerable.Range(1, points.Length - 1)
            .Select(ordinal => new RouteSurfaceSegmentDocument
            {
                SegmentId = $"route_0001.segment_{ordinal:0000}",
                GradePercent = 0,
                Operation = RouteSegmentOperation.Additive,
                ClearanceAboveMeters = null,
            })
            .ToList(),
    };

    private static RouteSurfacePointDocument Point(
        int x,
        int y,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = x, Y = y },
        Mode = RoutePointMode.Aligned,
        HandleInAuthoringPx = handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = handleOut ?? AuthoringPixelOffset.Zero,
        ElevationMeters = 1m,
        WidthMeters = 1m,
    };
}
