namespace SceneMaker.Core;

/// <summary>
/// Workspace-owned spatial conversion rules. This is deliberately an instance,
/// so a scene never acquires its grid scale from a process-wide default.
/// </summary>
public sealed class WorkspaceMetrics
{
    public WorkspaceMetrics(WorkspaceGridConfiguration grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        TerrainCellMeters = grid.TerrainCellMeters;
        AuthoringPixelsPerMeter = grid.AuthoringPixelsPerMeter;
        GamePixelsPerMeter = grid.GamePixelsPerMeter;
        WaterCellMeters = grid.WaterCellMeters;
        var pixelsPerCell = TerrainCellMeters * AuthoringPixelsPerMeter;
        if (pixelsPerCell <= 0m || pixelsPerCell != decimal.Truncate(pixelsPerCell))
            throw new SceneMakerDocumentException(
                "terrain_cell_meters × authoring_pixels_per_meter must be a positive whole authoring pixel count.");
        AuthoringPixelsPerTerrainCell = checked((int)pixelsPerCell);

        var pixelsPerWaterCell = WaterCellMeters * AuthoringPixelsPerMeter;
        if (pixelsPerWaterCell <= 0m || pixelsPerWaterCell != decimal.Truncate(pixelsPerWaterCell))
            throw new SceneMakerDocumentException(
                "water_cell_meters × authoring_pixels_per_meter must be a positive whole authoring pixel count.");
        AuthoringPixelsPerWaterCell = checked((int)pixelsPerWaterCell);

        // The water grid is finer than the Terrain grid so a bank can follow a
        // curve; it has to nest inside it, or a water cell would straddle two
        // Terrain cells and no consumer could say which ground it lies on.
        var waterCellsPerTerrainCell = TerrainCellMeters / WaterCellMeters;
        if (waterCellsPerTerrainCell < 1m
            || waterCellsPerTerrainCell != decimal.Truncate(waterCellsPerTerrainCell))
        {
            throw new SceneMakerDocumentException(
                "terrain_cell_meters ÷ water_cell_meters must be a whole number of water cells per Terrain cell.");
        }
        WaterCellsPerTerrainCell = checked((int)waterCellsPerTerrainCell);
    }

    public decimal TerrainCellMeters { get; }
    public decimal AuthoringPixelsPerMeter { get; }
    public decimal GamePixelsPerMeter { get; }
    public decimal WaterCellMeters { get; }
    public int AuthoringPixelsPerTerrainCell { get; }
    public int AuthoringPixelsPerWaterCell { get; }
    public int WaterCellsPerTerrainCell { get; }
    public decimal MetersPerAuthoringPixel => 1m / AuthoringPixelsPerMeter;

    public int SceneWidthAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Width * AuthoringPixelsPerTerrainCell);

    public int SceneHeightAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Height * AuthoringPixelsPerTerrainCell);

    /// <summary>The nearest position on the Terrain grid.</summary>
    public int SnapToTerrainGrid(int authoringPixels) =>
        SnapToGrid(authoringPixels, AuthoringPixelsPerTerrainCell);

    /// <summary>The nearest position on the finer water grid.</summary>
    public int SnapToWaterGrid(int authoringPixels) =>
        SnapToGrid(authoringPixels, AuthoringPixelsPerWaterCell);

    /// <summary>
    /// Rounds an authoring-pixel coordinate to a grid of <paramref name="step"/>
    /// pixels. Half a step rounds away from zero, so a position exactly between
    /// two grid lines always lands on the same one.
    /// </summary>
    public static int SnapToGrid(int coordinate, int step)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        return checked((int)Math.Round(
            (decimal)coordinate / step,
            MidpointRounding.AwayFromZero) * step);
    }

    public int SceneWidthWaterCells(SceneDocument scene) =>
        checked(scene.SizeCells.Width * WaterCellsPerTerrainCell);

    public int SceneHeightWaterCells(SceneDocument scene) =>
        checked(scene.SizeCells.Height * WaterCellsPerTerrainCell);
}
