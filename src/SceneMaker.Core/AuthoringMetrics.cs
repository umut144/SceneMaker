using MMORPG.Simulation;

namespace SceneMaker.Core;

public static class AuthoringMetrics
{
    public const int AuthoringPixelsPerWorldGridCell = 16;
    public const int AuthoringPixelsPerMeter = 32;

    public static decimal MetersPerAuthoringPixel =>
        (decimal)WorldGrid.TileMeters / AuthoringPixelsPerWorldGridCell;

    public static int SceneWidthAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Width * AuthoringPixelsPerWorldGridCell);

    public static int SceneHeightAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Height * AuthoringPixelsPerWorldGridCell);
}
