namespace SceneMaker.Core;

/// <summary>
/// Terrain editing over the canonically ordered cell list.
///
/// These operations take a canonical document and produce a canonical one, so
/// they no longer validate the whole document on entry and exit. Full
/// validation belongs to the IO boundaries — <see cref="DocumentJson"/>,
/// <see cref="SceneStore"/> and <see cref="SceneExport"/> all still run it — and
/// running it per edit made a single painted cell walk every cell in the Scene
/// twice.
///
/// Because the list stays ordered by Y then X, a cell is located by binary
/// search rather than by re-sorting the whole list after every change.
/// </summary>
public static class TerrainEditing
{
    /// <summary>
    /// Painting a cell is only possible with an Asset that is authored as
    /// cells. A curve Asset carries a shape the raster is derived from, so a
    /// hand-painted cell of it would be a cell nothing produced and nothing
    /// maintains.
    /// </summary>
    private static TerrainDisplayAsset RequirePaintable(
        TerrainDisplayCatalog terrainAssets,
        string assetKey)
    {
        var asset = terrainAssets.Resolve(assetKey);
        if (asset.Authoring != TerrainAuthoring.Cells)
        {
            throw new SceneMakerDocumentException(
                $"Terrain Asset '{assetKey}' is authored as a curve and cannot be painted as cells.");
        }
        return asset;
    }

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
        string assetKey,
        decimal? elevationMeters = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        _ = RequirePaintable(terrainAssets, assetKey);
        RequireInsideScene(scene, cellX, cellY);
        var elevation = elevationMeters ?? scene.DefaultElevationMeters;

        var cells = new List<TerrainCellDocument>(scene.TerrainCells.Count + 1);
        cells.AddRange(scene.TerrainCells);
        SetCell(cells, cellX, cellY, assetKey, elevation);
        return scene with { TerrainCells = cells };
    }

    public static SceneDocument PaintLine(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        int startCellX,
        int startCellY,
        int endCellX,
        int endCellY,
        string assetKey,
        decimal? elevationMeters = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        _ = RequirePaintable(terrainAssets, assetKey);
        RequireInsideScene(scene, startCellX, startCellY);
        RequireInsideScene(scene, endCellX, endCellY);
        var elevation = elevationMeters ?? scene.DefaultElevationMeters;

        // A straight line between two cells inside the Scene rectangle stays
        // inside it, so the individual cells need no further bounds check.
        var line = LineCells(startCellX, startCellY, endCellX, endCellY);
        var cells = new List<TerrainCellDocument>(scene.TerrainCells.Count + line.Count);
        cells.AddRange(scene.TerrainCells);
        foreach (var cell in line)
            SetCell(cells, cell.X, cell.Y, assetKey, elevation);
        return scene with { TerrainCells = cells };
    }

    public static SceneDocument EraseLine(
        SceneDocument scene,
        int startCellX,
        int startCellY,
        int endCellX,
        int endCellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RequireInsideScene(scene, startCellX, startCellY);
        RequireInsideScene(scene, endCellX, endCellY);

        var line = LineCells(startCellX, startCellY, endCellX, endCellY).ToHashSet();
        var cells = scene.TerrainCells
            .Where(cell => !line.Contains(new TerrainCellCoordinate(cell.X, cell.Y)))
            .ToList();
        return cells.Count == scene.TerrainCells.Count
            ? scene
            : scene with { TerrainCells = cells };
    }

    public static IReadOnlyList<TerrainCellCoordinate> LineCells(
        int startCellX,
        int startCellY,
        int endCellX,
        int endCellY)
    {
        List<TerrainCellCoordinate> cells = [];
        var x = startCellX;
        var y = startCellY;
        var deltaX = Math.Abs(endCellX - startCellX);
        var stepX = startCellX < endCellX ? 1 : -1;
        var deltaY = -Math.Abs(endCellY - startCellY);
        var stepY = startCellY < endCellY ? 1 : -1;
        var error = deltaX + deltaY;

        while (true)
        {
            cells.Add(new TerrainCellCoordinate(x, y));
            if (x == endCellX && y == endCellY) break;
            var doubledError = 2 * error;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }
            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }

        return cells;
    }

    public static SceneDocument Erase(SceneDocument scene, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RequireInsideScene(scene, cellX, cellY);

        var index = FindCell(scene.TerrainCells, cellX, cellY);
        if (index < 0) return scene;
        var cells = new List<TerrainCellDocument>(scene.TerrainCells);
        cells.RemoveAt(index);
        return scene with { TerrainCells = cells };
    }

    public static SceneDocument Fill(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets,
        int startCellX,
        int startCellY,
        string assetKey,
        decimal? elevationMeters = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        _ = RequirePaintable(terrainAssets, assetKey);
        RequireInsideScene(scene, startCellX, startCellY);
        var elevation = elevationMeters ?? scene.DefaultElevationMeters;

        var cells = scene.TerrainCells.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell);
        var start = new TerrainCellCoordinate(startCellX, startCellY);
        string? sourceAssetKey = cells.TryGetValue(start, out var source) ? source.AssetKey : null;
        if (sourceAssetKey == assetKey) return scene;

        var region = ConnectedRegion(scene, cells, start, sourceAssetKey);
        foreach (var coordinate in region)
        {
            cells[coordinate] = new TerrainCellDocument
            {
                X = coordinate.X,
                Y = coordinate.Y,
                AssetKey = assetKey,
                ElevationMeters = elevation,
            };
        }
        return scene with
        {
            TerrainCells = cells
                .OrderBy(static pair => pair.Key.Y)
                .ThenBy(static pair => pair.Key.X)
                .Select(static pair => pair.Value)
                .ToList(),
        };
    }

    public static SceneDocument EraseFill(SceneDocument scene, int startCellX, int startCellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RequireInsideScene(scene, startCellX, startCellY);

        var cells = scene.TerrainCells.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell);
        var start = new TerrainCellCoordinate(startCellX, startCellY);
        if (!cells.TryGetValue(start, out var source)) return scene;

        var region = ConnectedRegion(scene, cells, start, source.AssetKey);
        return scene with
        {
            TerrainCells = scene.TerrainCells
                .Where(cell => !region.Contains(new TerrainCellCoordinate(cell.X, cell.Y)))
                .ToList(),
        };
    }

    /// <summary>
    /// Cells reachable from <paramref name="start"/> over cardinal neighbours
    /// that carry <paramref name="sourceAssetKey"/>. A null source key means
    /// "no Terrain authored here", which is a region of its own.
    /// </summary>
    private static HashSet<TerrainCellCoordinate> ConnectedRegion(
        SceneDocument scene,
        IReadOnlyDictionary<TerrainCellCoordinate, TerrainCellDocument> cells,
        TerrainCellCoordinate start,
        string? sourceAssetKey)
    {
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

                string? neighbourAssetKey = cells.TryGetValue(neighbour, out var neighbourCell)
                    ? neighbourCell.AssetKey
                    : null;
                if (neighbourAssetKey != sourceAssetKey) continue;
                region.Add(neighbour);
                frontier.Enqueue(neighbour);
            }
        }
        return region;
    }

    /// <summary>
    /// The top of the solid column at this cell, or null where no Terrain is
    /// authored. Cheap enough to ask per pointer press: the cell list is
    /// canonically ordered, so this is a binary search rather than a walk.
    /// </summary>
    public static decimal? ElevationAt(SceneDocument scene, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var index = FindCell(scene.TerrainCells, cellX, cellY);
        return index < 0 ? null : scene.TerrainCells[index].ElevationMeters;
    }

    /// <summary>Runs at the IO boundary, so it validates the whole document.</summary>
    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var cell in scene.TerrainCells)
            _ = RequirePaintable(terrainAssets, cell.AssetKey);
    }

    /// <summary>Replaces or inserts one cell, keeping the list canonical.</summary>
    private static void SetCell(
        List<TerrainCellDocument> cells,
        int cellX,
        int cellY,
        string assetKey,
        decimal elevationMeters)
    {
        var cell = new TerrainCellDocument
        {
            X = cellX,
            Y = cellY,
            AssetKey = assetKey,
            ElevationMeters = elevationMeters,
        };
        var index = FindCell(cells, cellX, cellY);
        if (index >= 0) cells[index] = cell;
        else cells.Insert(~index, cell);
    }

    /// <summary>
    /// Index of the cell in the canonically ordered list, or the bitwise
    /// complement of the position it would take.
    /// </summary>
    private static int FindCell(IReadOnlyList<TerrainCellDocument> cells, int cellX, int cellY)
    {
        var low = 0;
        var high = cells.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = cells[middle];
            var order = candidate.Y != cellY
                ? candidate.Y.CompareTo(cellY)
                : candidate.X.CompareTo(cellX);
            if (order == 0) return middle;
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        return ~low;
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
        PropBoundsAuthoringPixels bounds,
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

    /// <summary>
    /// The set of Terrain cells the Scene has authored. Callers that check many
    /// footprints against the same Scene — the drawing path checks one per Prop
    /// and one per line preview anchor, every frame — build this once instead of
    /// letting every check walk the whole cell list.
    /// </summary>
    public static HashSet<TerrainCellCoordinate> AuthoredCells(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        HashSet<TerrainCellCoordinate> authored = new(scene.TerrainCells.Count);
        foreach (var cell in scene.TerrainCells)
            authored.Add(new TerrainCellCoordinate(cell.X, cell.Y));
        return authored;
    }

    public static IReadOnlyList<TerrainCellCoordinate> MissingCells(
        IReadOnlySet<TerrainCellCoordinate> authoredCells,
        PropBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(authoredCells);
        return IntersectedCells(bounds, metrics)
            .Where(cell => !authoredCells.Contains(cell))
            .ToList();
    }

    public static IReadOnlyList<TerrainCellCoordinate> MissingCells(
        SceneDocument scene,
        PropBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics) =>
        MissingCells(AuthoredCells(scene), bounds, metrics);

    public static bool IsComplete(
        IReadOnlySet<TerrainCellCoordinate> authoredCells,
        PropBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(authoredCells);
        return IntersectedCells(bounds, metrics).All(authoredCells.Contains);
    }

    public static bool IsComplete(
        SceneDocument scene,
        PropBoundsAuthoringPixels bounds,
        WorkspaceMetrics metrics) =>
        IsComplete(AuthoredCells(scene), bounds, metrics);

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
