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
        var pixelsPerCell = TerrainCellMeters * AuthoringPixelsPerMeter;
        if (pixelsPerCell <= 0m || pixelsPerCell != decimal.Truncate(pixelsPerCell))
            throw new SceneMakerDocumentException(
                "terrain_cell_meters × authoring_pixels_per_meter must be a positive whole authoring pixel count.");
        AuthoringPixelsPerTerrainCell = checked((int)pixelsPerCell);
    }

    public decimal TerrainCellMeters { get; }
    public decimal AuthoringPixelsPerMeter { get; }
    public decimal GamePixelsPerMeter { get; }
    public int AuthoringPixelsPerTerrainCell { get; }
    public decimal MetersPerAuthoringPixel => 1m / AuthoringPixelsPerMeter;

    public int SceneWidthAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Width * AuthoringPixelsPerTerrainCell);

    public int SceneHeightAuthoringPixels(SceneDocument scene) =>
        checked(scene.SizeCells.Height * AuthoringPixelsPerTerrainCell);
}
