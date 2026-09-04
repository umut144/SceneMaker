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
        ElevationQuantumMeters = grid.ElevationQuantumMeters;
        VoxelSizeMeters = grid.VoxelSizeMeters;
        VoxelSubgridMeters = grid.VoxelSubgridMeters;
        if (ElevationQuantumMeters <= 0m)
            throw new SceneMakerDocumentException(
                "elevation_quantum_meters must be positive.");
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

        var subgridSteps = VoxelSizeMeters / VoxelSubgridMeters;
        if (VoxelSizeMeters <= 0m
            || VoxelSubgridMeters <= 0m
            || subgridSteps != decimal.Truncate(subgridSteps))
        {
            throw new SceneMakerDocumentException(
                "voxel_size_meters must contain a positive whole number of voxel_subgrid_meters steps.");
        }
        VoxelSubgridStepsPerVoxel = checked((int)subgridSteps);
        var pixelsPerVoxel = VoxelSizeMeters * AuthoringPixelsPerMeter;
        if (pixelsPerVoxel != decimal.Truncate(pixelsPerVoxel))
        {
            throw new SceneMakerDocumentException(
                "voxel_size_meters × authoring_pixels_per_meter must be a whole authoring pixel count.");
        }
        AuthoringPixelsPerVoxel = checked((int)pixelsPerVoxel);
    }

    public decimal TerrainCellMeters { get; }
    public decimal AuthoringPixelsPerMeter { get; }
    public decimal GamePixelsPerMeter { get; }
    public decimal WaterCellMeters { get; }
    public decimal ElevationQuantumMeters { get; }
    public decimal VoxelSizeMeters { get; }
    public decimal VoxelSubgridMeters { get; }
    public int AuthoringPixelsPerTerrainCell { get; }
    public int AuthoringPixelsPerWaterCell { get; }
    public int WaterCellsPerTerrainCell { get; }
    public int VoxelSubgridStepsPerVoxel { get; }
    public int AuthoringPixelsPerVoxel { get; }
    public decimal MetersPerAuthoringPixel => 1m / AuthoringPixelsPerMeter;

    /// <summary>Whether an absolute authored height lies on this Workspace's vertical grid.</summary>
    public bool IsElevationAligned(decimal elevationMeters) =>
        elevationMeters % ElevationQuantumMeters == 0m;

    /// <summary>
    /// The nearest height on the Workspace's vertical grid. An exact half-step
    /// rounds away from zero, matching the spatial grid rule.
    /// </summary>
    public decimal SnapElevation(decimal elevationMeters) =>
        Math.Round(
            elevationMeters / ElevationQuantumMeters,
            MidpointRounding.AwayFromZero) * ElevationQuantumMeters;

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

    /// <summary>
    /// Which cell of a <paramref name="step"/>-pixel grid an authoring-pixel
    /// coordinate falls in, counting downwards on both sides of the origin.
    ///
    /// <para>C# division truncates towards zero, so -1 / 32 is 0 and the cell
    /// just left of the origin would share an index with the cell just right of
    /// it. Grid arithmetic lives here, next to the rounding rule, so that
    /// nothing that rasterizes has to keep its own copy of it - there were two,
    /// and a third would have been written the day a hill needed one.</para>
    /// </summary>
    public static int FloorDivide(int value, int step)
    {
        var quotient = value / step;
        var remainder = value % step;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public int SceneWidthWaterCells(SceneDocument scene) =>
        checked(scene.SizeCells.Width * WaterCellsPerTerrainCell);

    public int SceneHeightWaterCells(SceneDocument scene) =>
        checked(scene.SizeCells.Height * WaterCellsPerTerrainCell);
}
