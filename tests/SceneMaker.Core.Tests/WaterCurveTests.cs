using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// Turning a drawn curve into stored points. These are the rules the PolyTools
/// Bezier tool follows, so that the same gestures produce the same shape in both
/// applications.
/// </summary>
public sealed class WaterCurveTests
{
    [Fact]
    public void ALinearPointStoresNoHandles()
    {
        var curve = WaterEditing.ResolveCurve(
        [
            Draft(32, 32, WaterPointMode.Linear),
            Draft(160, 32, WaterPointMode.Linear),
        ]);

        Assert.All(curve, point =>
        {
            Assert.True(point.HandleInAuthoringPx.IsZero());
            Assert.True(point.HandleOutAuthoringPx.IsZero());
        });
    }

    [Fact]
    public void ADraggedHandleIsKeptAndMirroredIntoTheIncomingOne()
    {
        var curve = WaterEditing.ResolveCurve(
        [
            Draft(
                32, 32, WaterPointMode.Aligned,
                new AuthoringPixelOffset { X = 0, Y = 48 }),
            Draft(160, 32, WaterPointMode.Linear),
        ]);

        Assert.Equal(new AuthoringPixelOffset { X = 0, Y = 48 }, curve[0].HandleOutAuthoringPx);
        Assert.Equal(new AuthoringPixelOffset { X = 0, Y = -48 }, curve[0].HandleInAuthoringPx);
    }

    [Fact]
    public void AHandleDraggedBackwardsIsTurnedToFollowTheCurve()
    {
        // The curve travels east; the author pulled the handle west. Kept as
        // drawn it would put a cusp where they drew a bend.
        var curve = WaterEditing.ResolveCurve(
        [
            Draft(32, 32, WaterPointMode.Linear),
            Draft(
                160, 32, WaterPointMode.Aligned,
                new AuthoringPixelOffset { X = -32, Y = 16 }),
        ]);

        Assert.Equal(new AuthoringPixelOffset { X = 32, Y = -16 }, curve[1].HandleOutAuthoringPx);
        Assert.Equal(new AuthoringPixelOffset { X = -32, Y = 16 }, curve[1].HandleInAuthoringPx);
    }

    [Fact]
    public void AnAlignedPointThatWasOnlyClickedTakesItsTangentFromItsNeighbours()
    {
        var curve = WaterEditing.ResolveCurve(
        [
            Draft(0, 0, WaterPointMode.Aligned),
            Draft(96, 96, WaterPointMode.Aligned),
            Draft(192, 0, WaterPointMode.Aligned),
        ]);

        // The middle point sits between (0, 0) and (192, 0): its tangent runs
        // due east, a third of the distance to each neighbour.
        var distance = Math.Sqrt(96.0 * 96.0 + 96.0 * 96.0) / 3.0;
        var expected = (int)Math.Round(distance, MidpointRounding.AwayFromZero);
        Assert.Equal(new AuthoringPixelOffset { X = expected, Y = 0 }, curve[1].HandleOutAuthoringPx);
        Assert.Equal(new AuthoringPixelOffset { X = -expected, Y = 0 }, curve[1].HandleInAuthoringPx);

        // An end of the curve has one neighbour and one handle, a third of the
        // way towards it.
        Assert.Equal(new AuthoringPixelOffset { X = 32, Y = 32 }, curve[0].HandleOutAuthoringPx);
        Assert.True(curve[0].HandleInAuthoringPx.IsZero());
        Assert.True(curve[^1].HandleOutAuthoringPx.IsZero());
    }

    [Fact]
    public void AResolvedCurveIsAValidRiver()
    {
        using var workspace = TestWorkspace.Create();
        var curve = WaterEditing.ResolveCurve(
        [
            Draft(32, 32, WaterPointMode.Linear),
            Draft(96, 96, WaterPointMode.Aligned),
            Draft(160, 32, WaterPointMode.Linear),
        ]);

        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace), workspace.Terrain, curve, "river");

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
        Assert.Equal(3, Assert.Single(scene.WaterBodies).Points.Count);
    }

    /// <summary>
    /// A draft point at the fixture's usual heights: water at ground level,
    /// half a metre deep, five metres of headroom. The tests here are about
    /// plan geometry, so the section stays out of their way.
    /// </summary>
    private static WaterDraftPoint Draft(
        int x,
        int y,
        WaterPointMode mode,
        AuthoringPixelOffset? handle = null) =>
        new(x, y, mode, 1.0m, 0.5m, 5.0m, 4.0m, handle);

}
