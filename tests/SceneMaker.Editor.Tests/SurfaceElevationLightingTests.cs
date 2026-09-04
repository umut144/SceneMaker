using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class SurfaceElevationLightingTests
{
    [Fact]
    public void OneMetreIsAlwaysFortyFivePercent()
    {
        Assert.Equal(
            0.45,
            SurfaceElevationLighting.Brightness(1m, -20m, 200m));
    }

    [Fact]
    public void TheHighestSurfaceAboveTheAnchorKeepsItsFullAssetColour()
    {
        Assert.Equal(
            1.0,
            SurfaceElevationLighting.Brightness(10m, 1m, 10m));
        Assert.Equal(
            0.725,
            SurfaceElevationLighting.Brightness(5.5m, 1m, 10m),
            precision: 12);
    }

    [Fact]
    public void ElevationsBelowOneMetreDarkenTowardAVisibleFloor()
    {
        Assert.Equal(
            0.25,
            SurfaceElevationLighting.Brightness(0m, 0m, 10m));
        Assert.Equal(
            0.35,
            SurfaceElevationLighting.Brightness(0.5m, 0m, 10m),
            precision: 12);
    }

    [Fact]
    public void ValuesOutsideTheRepresentedRangeAreClamped()
    {
        Assert.Equal(
            0.25,
            SurfaceElevationLighting.Brightness(-2m, 0m, 10m));
        Assert.Equal(
            1.0,
            SurfaceElevationLighting.Brightness(20m, 0m, 10m));
    }

    [Fact]
    public void AFlatSceneRetainsTheBrightnessOfItsAbsoluteSideOfTheAnchor()
    {
        Assert.Equal(
            0.25,
            SurfaceElevationLighting.Brightness(0m, 0m, 0m));
        Assert.Equal(
            0.45,
            SurfaceElevationLighting.Brightness(1m, 1m, 1m));
        Assert.Equal(
            1.0,
            SurfaceElevationLighting.Brightness(5m, 5m, 5m));
    }

    [Fact]
    public void AnInvertedRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SurfaceElevationLighting.Brightness(1m, 2m, 1m));
    }
}
