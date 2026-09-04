using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class CanvasViewStateTests
{
    [Fact]
    public void PresentationIsAThreeWayTransientChoiceWithASectionElevation()
    {
        var view = new CanvasViewState(sectionElevationMeters: 1.125m);

        Assert.Equal(CanvasPresentationMode.Normal, view.PresentationMode);
        Assert.Equal(1.125m, view.SectionElevationMeters);

        view.SelectPresentation(CanvasPresentationMode.Heightmap);
        Assert.Equal(CanvasPresentationMode.Heightmap, view.PresentationMode);

        view.SelectPresentation(CanvasPresentationMode.Section);
        view.SetSectionElevation(8.25m);
        Assert.Equal(CanvasPresentationMode.Section, view.PresentationMode);
        Assert.Equal(8.25m, view.SectionElevationMeters);
    }

    [Fact]
    public void PresentationRejectsValuesOutsideItsClosedChoice()
    {
        var view = new CanvasViewState();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            view.SelectPresentation((CanvasPresentationMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CanvasViewState(presentationMode: (CanvasPresentationMode)99));
    }

    [Fact]
    public void ZoomKeepsTheLogicalPointUnderThePivotInPlace()
    {
        var view = new CanvasViewState(panX: 10.0, panY: 20.0, zoom: 1.0);
        const double pivotX = 100.0;
        const double pivotY = 50.0;
        var logicalX = (pivotX - view.PanX) / view.Zoom;
        var logicalY = (pivotY - view.PanY) / view.Zoom;

        view.ZoomBy(2.0, pivotX, pivotY);

        Assert.Equal(2.0, view.Zoom, 10);
        Assert.Equal(logicalX, (pivotX - view.PanX) / view.Zoom, 10);
        Assert.Equal(logicalY, (pivotY - view.PanY) / view.Zoom, 10);
    }

    [Fact]
    public void ZoomIsClampedToItsViewRange()
    {
        var view = new CanvasViewState();

        view.ZoomBy(1000.0, 0.0, 0.0);
        Assert.Equal(CanvasViewState.MaximumZoom, view.Zoom, 10);

        view.ZoomBy(0.0001, 0.0, 0.0);
        Assert.Equal(CanvasViewState.MinimumZoom, view.Zoom, 10);
    }

    [Fact]
    public void ZoomRejectsFactorsThatAreNotPositiveAndFinite()
    {
        var view = new CanvasViewState();

        Assert.Throws<ArgumentOutOfRangeException>(() => view.ZoomBy(0.0, 0.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ZoomBy(-2.0, 0.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ZoomBy(double.NaN, 0.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ZoomBy(2.0, double.NaN, 0.0));
    }

    [Fact]
    public void ScreenToTerrainCellFlipsTheYAxis()
    {
        var view = new CanvasViewState(panX: 0.0, panY: 0.0, zoom: 1.0);

        // The scene is 10 cells of 32 authoring pixels high, so the top screen
        // row is the topmost cell and the bottom row is cell Y zero.
        Assert.Equal((0, 9), view.ScreenToTerrainCell(0.0, 0.0, 10, 32));
        Assert.Equal((2, 0), view.ScreenToTerrainCell(64.0, 288.0, 10, 32));
        Assert.Equal((1, 8), view.ScreenToTerrainCell(63.0, 63.0, 10, 32));
    }

    [Fact]
    public void ScreenToAuthoringPixelFlipsTheYAxisAndHonoursZoom()
    {
        var view = new CanvasViewState(panX: 0.0, panY: 0.0, zoom: 1.0);

        Assert.Equal((0, 320), view.ScreenToAuthoringPixel(0.0, 0.0, 320));
        Assert.Equal((10, 0), view.ScreenToAuthoringPixel(10.0, 320.0, 320));

        var zoomed = new CanvasViewState(panX: 0.0, panY: 0.0, zoom: 2.0);
        Assert.Equal((50, 270), zoomed.ScreenToAuthoringPixel(100.0, 100.0, 320));
    }

    [Fact]
    public void ProjectionRejectsDegenerateSceneSizes()
    {
        var view = new CanvasViewState();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.ScreenToTerrainCell(0.0, 0.0, 0, 32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.ScreenToTerrainCell(0.0, 0.0, 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.ScreenToAuthoringPixel(0.0, 0.0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.ScreenToAuthoringPixel(double.NaN, 0.0, 320));
    }

    [Fact]
    public void KeyboardPanMovesOnlyWhileThereIsInputAndElapsedTime()
    {
        var view = new CanvasViewState(panX: 0.0, panY: 0.0);

        Assert.False(view.AdvanceKeyboardPan(0.0, 0.0, 0.5));
        Assert.False(view.AdvanceKeyboardPan(1.0, 0.0, 0.0));
        Assert.Equal(0.0, view.PanX, 10);

        Assert.True(view.AdvanceKeyboardPan(1.0, 0.0, 0.5));
        Assert.Equal(CanvasViewState.KeyboardPanMaximumSpeed * 0.5, view.PanX, 10);
        Assert.Equal(0.0, view.PanY, 10);
        Assert.Equal(CanvasViewState.KeyboardPanMaximumSpeed, view.KeyboardPanVelocityX, 10);
    }

    [Fact]
    public void KeyboardPanKeepsFullComponentsSoDiagonalsAreFaster()
    {
        var view = new CanvasViewState(panX: 0.0, panY: 0.0);

        view.AdvanceKeyboardPan(1.0, 1.0, 0.5);

        var expected = CanvasViewState.KeyboardPanMaximumSpeed * 0.5;
        Assert.Equal(expected, view.PanX, 10);
        Assert.Equal(expected, view.PanY, 10);
    }

    [Fact]
    public void ReleasingEveryPanKeyClearsTheVelocity()
    {
        var view = new CanvasViewState(panX: 0.0, panY: 0.0);
        view.AdvanceKeyboardPan(1.0, 1.0, 0.5);

        Assert.False(view.AdvanceKeyboardPan(0.0, 0.0, 0.5));

        Assert.Equal(0.0, view.KeyboardPanVelocityX, 10);
        Assert.Equal(0.0, view.KeyboardPanVelocityY, 10);
    }

    [Fact]
    public void PanByAccumulatesAndRejectsNonFiniteDeltas()
    {
        var view = new CanvasViewState(panX: 10.0, panY: 20.0);

        view.PanBy(5.0, -5.0);

        Assert.Equal(15.0, view.PanX, 10);
        Assert.Equal(15.0, view.PanY, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PanBy(double.NaN, 0.0));
    }

    [Fact]
    public void ConstructorRejectsAViewOutsideItsRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CanvasViewState(zoom: CanvasViewState.MinimumZoom / 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CanvasViewState(zoom: CanvasViewState.MaximumZoom * 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CanvasViewState(panX: double.PositiveInfinity));
    }
}
