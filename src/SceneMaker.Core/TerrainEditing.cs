namespace SceneMaker.Core;

public static class TerrainEditing
{
    private static readonly TerrainCellCoordinate[] CardinalNeighbours =
    [
        new(1, 0),
        new(0, 1),
        new(-1, 0),
        new(0, -1),
    ];

    public static SceneDocument Paint(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        int cellX,
        int cellY,
        string assetKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        _ = terrainAssets.Resolve(assetKey);

        if (cellX < 0 || cellX >= scene.SizeCells.Width
            || cellY < 0 || cellY >= scene.SizeCells.Height)
        {
            throw new SceneMakerDocumentException(
                $"Terrain cell ({cellX}, {cellY}) lies outside Scene '{scene.SceneId}'.");
        }

        var cells = scene.TerrainCells
            .Where(cell => cell.X != cellX || cell.Y != cellY)
            .Append(new TerrainCellDocument { X = cellX, Y = cellY, AssetKey = assetKey })
            .OrderBy(static cell => cell.Y)
            .ThenBy(static cell => cell.X)
            .ToList();
        var painted = scene with { TerrainCells = cells };
        DocumentValidation.Validate(painted);
        return painted;
    }

    public static SceneDocument Erase(SceneDocument scene, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DocumentValidation.Validate(scene);
        RequireInsideScene(scene, cellX, cellY);
        var erased = scene with
        {
            TerrainCells = scene.TerrainCells
                .Where(cell => cell.X != cellX || cell.Y != cellY)
                .ToList(),
        };
        if (erased.TerrainCells.Count == scene.TerrainCells.Count) return scene;
        DocumentValidation.Validate(erased);
        return erased;
    }

    public static SceneDocument Fill(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        int startCellX,
        int startCellY,
        string assetKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        _ = terrainAssets.Resolve(assetKey);
        RequireInsideScene(scene, startCellX, startCellY);

        Dictionary<TerrainCellCoordinate, string> cells = scene.TerrainCells.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell.AssetKey);
        var start = new TerrainCellCoordinate(startCellX, startCellY);
        string? sourceAssetKey = cells.TryGetValue(start, out var source) ? source : null;
        if (sourceAssetKey == assetKey) return scene;

        Queue<TerrainCellCoordinate> frontier = new();
        HashSet<TerrainCellCoordinate> region = [start];
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var offset in CardinalNeighbours)
            {
                var neighbour = new TerrainCellCoordinate(
                    current.X + offset.X,
                    current.Y + offset.Y);
                if (!IsInsideScene(scene, neighbour.X, neighbour.Y)
                    || region.Contains(neighbour))
                {
                    continue;
                }

                string? neighbourAssetKey = cells.TryGetValue(neighbour, out var neighbourAsset)
                    ? neighbourAsset
                    : null;
                if (neighbourAssetKey != sourceAssetKey) continue;
                region.Add(neighbour);
                frontier.Enqueue(neighbour);
            }
        }

        foreach (var coordinate in region) cells[coordinate] = assetKey;
        var filled = scene with
        {
            TerrainCells = cells
                .OrderBy(static pair => pair.Key.Y)
                .ThenBy(static pair => pair.Key.X)
                .Select(static pair => new TerrainCellDocument
                {
                    X = pair.Key.X,
                    Y = pair.Key.Y,
                    AssetKey = pair.Value,
                })
                .ToList(),
        };
        DocumentValidation.Validate(filled);
        return filled;
    }

    public static SceneDocument EraseFill(SceneDocument scene, int startCellX, int startCellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DocumentValidation.Validate(scene);
        RequireInsideScene(scene, startCellX, startCellY);

        var cells = scene.TerrainCells.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell.AssetKey);
        var start = new TerrainCellCoordinate(startCellX, startCellY);
        if (!cells.ContainsKey(start)) return scene;

        var sourceAssetKey = cells[start];
        Queue<TerrainCellCoordinate> frontier = new();
        HashSet<TerrainCellCoordinate> region = [start];
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var offset in CardinalNeighbours)
            {
                var neighbour = new TerrainCellCoordinate(
                    current.X + offset.X,
                    current.Y + offset.Y);
                if (!IsInsideScene(scene, neighbour.X, neighbour.Y)
                    || region.Contains(neighbour)
                    || !cells.TryGetValue(neighbour, out var assetKey)
                    || assetKey != sourceAssetKey)
                {
                    continue;
                }

                region.Add(neighbour);
                frontier.Enqueue(neighbour);
            }
        }

        var erased = scene with
        {
            TerrainCells = scene.TerrainCells
                .Where(cell => !region.Contains(new TerrainCellCoordinate(cell.X, cell.Y)))
                .ToList(),
        };
        DocumentValidation.Validate(erased);
        return erased;
    }

    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var cell in scene.TerrainCells)
            _ = terrainAssets.Resolve(cell.AssetKey);
    }

    private static void RequireInsideScene(SceneDocument scene, int cellX, int cellY)
    {
        if (!IsInsideScene(scene, cellX, cellY))
        {
            throw new SceneMakerDocumentException(
                $"Terrain cell ({cellX}, {cellY}) lies outside Scene '{scene.SceneId}'.");
        }
    }

    private static bool IsInsideScene(SceneDocument scene, int cellX, int cellY) =>
        cellX >= 0 && cellX < scene.SizeCells.Width
        && cellY >= 0 && cellY < scene.SizeCells.Height;
}

public readonly record struct TerrainCellCoordinate(int X, int Y);

public static class TerrainCoverage
{
    public static IReadOnlyList<TerrainCellCoordinate> IntersectedCells(
        PlacementBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var step = metrics.AuthoringPixelsPerTerrainCell;
        var firstX = FloorDivide(bounds.Left, step);
        var lastX = FloorDivide(checked(bounds.Right - 1), step);
        var firstY = FloorDivide(bounds.Bottom, step);
        var lastY = FloorDivide(checked(bounds.Top - 1), step);
        List<TerrainCellCoordinate> cells = [];
        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++)
            {
                cells.Add(new TerrainCellCoordinate(x, y));
            }
        }
        return cells;
    }

    public static IReadOnlyList<TerrainCellCoordinate> MissingCells(
        SceneDocument scene,
        PlacementBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var authored = scene.TerrainCells
            .Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y))
            .ToHashSet();
        return IntersectedCells(bounds, metrics)
            .Where(cell => !authored.Contains(cell))
            .ToList();
    }

    public static bool IsComplete(
        SceneDocument scene,
        PlacementBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics) =>
        MissingCells(scene, bounds, metrics).Count == 0;

    public static string FormatMissingCells(IReadOnlyList<TerrainCellCoordinate> cells)
    {
        if (cells.Count == 0) return "none";
        return string.Join(
            "; ",
            cells.GroupBy(static cell => cell.Y)
                .OrderBy(static group => group.Key)
                .Select(group => $"y={group.Key}: x={FormatXRanges(group.Select(static cell => cell.X))}"));
    }

    private static string FormatXRanges(IEnumerable<int> values)
    {
        var ordered = values.Distinct().Order().ToArray();
        List<string> ranges = [];
        for (var index = 0; index < ordered.Length;)
        {
            var start = ordered[index];
            var end = start;
            while (index + 1 < ordered.Length && ordered[index + 1] == end + 1)
            {
                index++;
                end = ordered[index];
            }
            ranges.Add(start == end ? start.ToString() : $"{start}..{end}");
            index++;
        }
        return string.Join(",", ranges);
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}
