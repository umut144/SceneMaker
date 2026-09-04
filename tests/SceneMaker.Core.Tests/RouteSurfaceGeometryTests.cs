using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RouteSurfaceGeometryTests
{
    [Fact]
    public void ElevationIsContinuousOverCenterlineArcLength()
    {
        using var workspace = TestWorkspace.Create();
        var route = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 1.0m), Point(32, 0, 2.0m), Point(160, 0, 4.0m)]);

        Assert.Equal(1.0, route.ElevationAt(-1));
        Assert.Equal(1.5, route.ElevationAt(16));
        Assert.Equal(3.0, route.ElevationAt(96));
        Assert.Equal(4.0, route.ElevationAt(200));
    }

    [Fact]
    public void IntermediateElevationDoesNotSnapBackToTheWorkspaceQuantum()
    {
        using var workspace = TestWorkspace.Create();
        var route = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 1.0m), Point(32, 0, 1.125m)]);

        Assert.Equal(1.0625, route.ElevationAt(16));
        Assert.False(workspace.Metrics.IsElevationAligned(1.0625m));
    }

    [Fact]
    public void ALongerCurvedPlanReducesTheGradeWithoutChangingTheRise()
    {
        using var workspace = TestWorkspace.Create();
        var straight = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 0m), Point(160, 0, 5m)]);
        var curved = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [
                Point(0, 0, 0m, handleOutY: 160),
                Point(160, 0, 5m, handleInY: 160),
            ]);

        Assert.Equal(5.0, Assert.Single(curved.GradeSegments).RiseMeters);
        Assert.True(curved.TotalLengthMeters > straight.TotalLengthMeters);
        Assert.True(curved.MaximumAbsoluteRisePerMeter < straight.MaximumAbsoluteRisePerMeter);
    }

    [Fact]
    public void UphillAndDownhillKeepTheirDirectionButShareAnAbsoluteMaximum()
    {
        using var workspace = TestWorkspace.Create();
        var route = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 1m), Point(32, 0, 1.25m), Point(96, 0, 0.75m)]);

        Assert.Equal(0.25, route.GradeSegments[0].RisePerMeter);
        Assert.Equal(-0.25, route.GradeSegments[1].RisePerMeter);
        Assert.Equal(0.25, route.MaximumAbsoluteRisePerMeter);
    }

    [Fact]
    public void AVerticalChangeWithoutHorizontalRunHasInfiniteGrade()
    {
        using var workspace = TestWorkspace.Create();
        var route = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 1m), Point(0, 0, 2m), Point(32, 0, 2m)]);

        var vertical = route.GradeSegments[0];
        Assert.Equal(0.0, vertical.RunMeters);
        Assert.Equal(1.0, vertical.RiseMeters);
        Assert.Equal(double.PositiveInfinity, vertical.RisePerMeter);
        Assert.Equal(double.PositiveInfinity, route.MaximumAbsoluteRisePerMeter);
    }

    [Fact]
    public void VariableWidthUsesTheSharedOpenCorridor()
    {
        using var workspace = TestWorkspace.Create();
        var route = RouteSurfaceGeometry.Prepare(
            workspace.Metrics,
            [Point(0, 0, 1m, widthMeters: 1m), Point(64, 0, 1m, widthMeters: 3m)]);

        Assert.NotNull(route.Corridor.NearestStation(48, 40));
        Assert.Null(route.Corridor.NearestStation(16, 40));
    }

    [Fact]
    public void WidthMustBePositive()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            RouteSurfaceGeometry.Prepare(
                workspace.Metrics,
                [Point(0, 0, 1m), Point(32, 0, 2m, widthMeters: 0m)]));

        Assert.Contains("positive widths", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARouteNeedsTwoPoints()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            RouteSurfaceGeometry.Prepare(workspace.Metrics, [Point(0, 0, 1m)]));

        Assert.Contains("at least two points", exception.Message, StringComparison.Ordinal);
    }

    private static RouteSurfacePoint Point(
        double x,
        double y,
        decimal elevationMeters,
        decimal widthMeters = 1m,
        double handleInX = 0,
        double handleInY = 0,
        double handleOutX = 0,
        double handleOutY = 0) =>
        new(
            new BezierChainPoint(
                x,
                y,
                handleInX,
                handleInY,
                handleOutX,
                handleOutY),
            widthMeters,
            elevationMeters);
}
