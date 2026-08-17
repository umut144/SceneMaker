namespace SceneMaker.Core;

public sealed class CanvasViewState
{
    public const double MinimumZoom = 0.25;
    public const double MaximumZoom = 16.0;
    public const double KeyboardPanMaximumSpeed = 640.0;

    private double _keyboardPanVelocityX;
    private double _keyboardPanVelocityY;

    public CanvasViewState(double panX = 32.0, double panY = 32.0, double zoom = 1.0)
    {
        if (!double.IsFinite(panX) || !double.IsFinite(panY))
            throw new ArgumentOutOfRangeException(nameof(panX), "Canvas pan must be finite.");
        if (!double.IsFinite(zoom) || zoom < MinimumZoom || zoom > MaximumZoom)
            throw new ArgumentOutOfRangeException(nameof(zoom), "Canvas zoom is outside its view range.");
        PanX = panX;
        PanY = panY;
        Zoom = zoom;
    }

    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public double Zoom { get; private set; }
    public double KeyboardPanVelocityX => _keyboardPanVelocityX;
    public double KeyboardPanVelocityY => _keyboardPanVelocityY;

    public void PanBy(double screenDeltaX, double screenDeltaY)
    {
        if (!double.IsFinite(screenDeltaX) || !double.IsFinite(screenDeltaY))
            throw new ArgumentOutOfRangeException(nameof(screenDeltaX), "Canvas pan delta must be finite.");
        PanX += screenDeltaX;
        PanY += screenDeltaY;
    }

    /// <summary>
    /// Advances constant-speed keyboard panning in screen pixels. Simultaneous
    /// directions retain their full components, making diagonal panning faster.
    /// </summary>
    public bool AdvanceKeyboardPan(double inputX, double inputY, double elapsedSeconds)
    {
        if (!double.IsFinite(inputX) || !double.IsFinite(inputY)
            || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        if (elapsedSeconds == 0.0) return false;

        if (inputX == 0.0 && inputY == 0.0)
        {
            _keyboardPanVelocityX = 0.0;
            _keyboardPanVelocityY = 0.0;
            return false;
        }

        _keyboardPanVelocityX = inputX * KeyboardPanMaximumSpeed;
        _keyboardPanVelocityY = inputY * KeyboardPanMaximumSpeed;

        var previousX = PanX;
        var previousY = PanY;
        PanX += _keyboardPanVelocityX * elapsedSeconds;
        PanY += _keyboardPanVelocityY * elapsedSeconds;
        return PanX != previousX || PanY != previousY;
    }

    public void ZoomBy(double factor, double pivotScreenX, double pivotScreenY)
    {
        if (!double.IsFinite(factor) || factor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(factor));
        if (!double.IsFinite(pivotScreenX) || !double.IsFinite(pivotScreenY))
            throw new ArgumentOutOfRangeException(nameof(pivotScreenX));

        var nextZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        var logicalPivotX = (pivotScreenX - PanX) / Zoom;
        var logicalPivotY = (pivotScreenY - PanY) / Zoom;
        PanX = pivotScreenX - (logicalPivotX * nextZoom);
        PanY = pivotScreenY - (logicalPivotY * nextZoom);
        Zoom = nextZoom;
    }

    public (int X, int Y) ScreenToTerrainCell(
        double screenX,
        double screenY,
        int sceneHeightCells,
        int authoringPixelsPerTerrainCell)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            throw new ArgumentOutOfRangeException(nameof(screenX));
        if (sceneHeightCells <= 0 || authoringPixelsPerTerrainCell <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHeightCells));
        var logicalX = (screenX - PanX) / Zoom;
        var topDownCellY = checked((int)Math.Floor(
            (screenY - PanY) / Zoom / authoringPixelsPerTerrainCell));
        return (
            checked((int)Math.Floor(logicalX / authoringPixelsPerTerrainCell)),
            checked(sceneHeightCells - 1 - topDownCellY));
    }

    public (int X, int Y) ScreenToAuthoringPixel(
        double screenX,
        double screenY,
        int sceneHeightAuthoringPixels)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            throw new ArgumentOutOfRangeException(nameof(screenX));
        if (sceneHeightAuthoringPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHeightAuthoringPixels));
        var logicalX = (screenX - PanX) / Zoom;
        var logicalY = sceneHeightAuthoringPixels - ((screenY - PanY) / Zoom);
        return (
            checked((int)Math.Floor(logicalX)),
            checked((int)Math.Floor(logicalY)));
    }
}
