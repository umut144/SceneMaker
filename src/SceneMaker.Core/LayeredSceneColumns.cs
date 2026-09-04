namespace SceneMaker.Core;

/// <summary>What occupies one resolved vertical span.</summary>
public enum LayeredColumnSpanKind
{
    TerrainSolid,
    IndependentFill,
    IndependentSurface,
}

/// <summary>
/// One closed vertical span at an X/Y position. A null bottom means the solid
/// continues down without a finite bound; Terrain starts that way, fills have
/// two finite boundaries, and a surface has equal bottom and top boundaries.
/// </summary>
public sealed record LayeredColumnSpan(
    decimal? BottomMeters,
    decimal TopMeters,
    string AssetKey,
    LayeredColumnSpanKind Kind,
    string? SourceId);

/// <summary>The surface a top-down view sees after applying an optional clip.</summary>
public sealed record VisibleLayeredSurface(
    decimal ElevationMeters,
    string AssetKey,
    LayeredColumnSpanKind Kind,
    string? SourceId,
    bool IsSectionFace);

/// <summary>
/// The resolved contents of one vertical Scene column after every Terrain cut
/// has been applied and every independent fill and surface has been retained.
/// </summary>
public sealed class LayeredSceneColumn
{
    internal LayeredSceneColumn(
        IReadOnlyList<LayeredColumnSpan> terrainSolids,
        IReadOnlyList<LayeredColumnSpan> fills,
        IReadOnlyList<LayeredColumnSpan> surfaces)
    {
        TerrainSolids = terrainSolids;
        Fills = fills;
        Surfaces = surfaces;
    }

    public IReadOnlyList<LayeredColumnSpan> TerrainSolids { get; }
    public IReadOnlyList<LayeredColumnSpan> Fills { get; }
    public IReadOnlyList<LayeredColumnSpan> Surfaces { get; }

    /// <summary>
    /// Returns the highest boundary visible from above after removing
    /// everything above <paramref name="clipElevationMeters"/>. A null clip is
    /// the ordinary, unlimited top-down view.
    ///
    /// <para>If the plane passes through a span, the plane itself is its visible
    /// section face. If it passes through a void, the highest span below it is
    /// visible instead. A fill wins an exact tie with Terrain: water beginning
    /// at its bed is visible rather than being hidden by the floor it touches.</para>
    /// </summary>
    public VisibleLayeredSurface? VisibleAt(decimal? clipElevationMeters = null)
    {
        var visible = HighestIn(TerrainSolids, clipElevationMeters, null);
        visible = HighestIn(Fills, clipElevationMeters, visible);
        return HighestIn(Surfaces, clipElevationMeters, visible);
    }

    private static VisibleLayeredSurface? HighestIn(
        IReadOnlyList<LayeredColumnSpan> spans,
        decimal? clipElevationMeters,
        VisibleLayeredSurface? visible)
    {
        foreach (var span in spans)
        {
            if (clipElevationMeters is { } clip
                && span.BottomMeters is { } bottom
                && clip < bottom)
            {
                continue;
            }

            var isSectionFace = clipElevationMeters is { } plane && plane < span.TopMeters;
            var elevation = isSectionFace ? clipElevationMeters!.Value : span.TopMeters;
            var candidate = new VisibleLayeredSurface(
                elevation,
                span.AssetKey,
                span.Kind,
                span.SourceId,
                isSectionFace);
            if (IsAbove(candidate, visible)) visible = candidate;
        }
        return visible;
    }

    /// <summary>
    /// Returns the top-down surface visible inside an inclusive finite height
    /// band. Geometry above the upper plane is clipped as usual; a surface
    /// below the lower plane is outside the inspected band and therefore absent.
    /// </summary>
    public VisibleLayeredSurface? VisibleBetween(
        decimal lowerElevationMeters,
        decimal upperElevationMeters)
    {
        if (upperElevationMeters < lowerElevationMeters)
            throw new ArgumentOutOfRangeException(nameof(upperElevationMeters));
        var visible = VisibleAt(upperElevationMeters);
        return visible is not null && visible.ElevationMeters >= lowerElevationMeters
            ? visible
            : null;
    }

    private static bool IsAbove(
        VisibleLayeredSurface candidate,
        VisibleLayeredSurface? current)
    {
        if (current is null || candidate.ElevationMeters > current.ElevationMeters) return true;
        if (candidate.ElevationMeters < current.ElevationMeters) return false;
        if (candidate.Kind != current.Kind)
            return KindPriority(candidate.Kind) > KindPriority(current.Kind);
        return string.CompareOrdinal(candidate.SourceId, current.SourceId) < 0;
    }

    private static int KindPriority(LayeredColumnSpanKind kind) => kind switch
    {
        LayeredColumnSpanKind.TerrainSolid => 0,
        LayeredColumnSpanKind.IndependentFill => 1,
        LayeredColumnSpanKind.IndependentSurface => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// A prepared, engine-neutral Layered-3D view of one Scene. Terrain is folded
/// through every Elevation Region once, each water corridor is rasterized once,
/// and Paths are sampled from their shared runtime bake. Subtractive intervals
/// cut Terrain while every Path keeps its independent surface; repeated column
/// questions then only inspect prepared spans.
/// </summary>
public sealed class LayeredSceneColumns
{
    private readonly WorkspaceMetrics _metrics;
    private readonly int _sceneWidthWaterCells;
    private readonly int _sceneHeightWaterCells;
    private readonly int _sceneWidthAuthoringPixels;
    private readonly int _sceneHeightAuthoringPixels;
    private readonly IReadOnlyDictionary<TerrainCellCoordinate, TerrainCellDocument> _terrain;
    private readonly IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<WaterLayer>> _water;
    private readonly IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<RouteLayer>> _routes;
    private readonly IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<RouteCutLayer>> _routeCuts;
    // Section drawing asks every visible cell again on every redraw. Resolve a
    // column only once for this immutable prepared Scene instead of allocating
    // its interval lists per frame.
    private readonly Dictionary<WaterCellCoordinate, LayeredSceneColumn> _resolved = [];

    private LayeredSceneColumns(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyDictionary<TerrainCellCoordinate, TerrainCellDocument> terrain,
        IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<WaterLayer>> water,
        IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<RouteLayer>> routes,
        IReadOnlyDictionary<WaterCellCoordinate, IReadOnlyList<RouteCutLayer>> routeCuts)
    {
        _metrics = metrics;
        _sceneWidthWaterCells = metrics.SceneWidthWaterCells(scene);
        _sceneHeightWaterCells = metrics.SceneHeightWaterCells(scene);
        _sceneWidthAuthoringPixels = metrics.SceneWidthAuthoringPixels(scene);
        _sceneHeightAuthoringPixels = metrics.SceneHeightAuthoringPixels(scene);
        _terrain = terrain;
        _water = water;
        _routes = routes;
        _routeCuts = routeCuts;
    }

    public static LayeredSceneColumns Prepare(SceneDocument scene, WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);

        var terrain = ElevationRegionGeometry
            .EffectiveTerrainCells(scene, metrics)
            .ToDictionary(
                static cell => new TerrainCellCoordinate(cell.X, cell.Y),
                static cell => cell);
        Dictionary<WaterCellCoordinate, List<WaterLayer>> water = [];
        foreach (var body in scene.WaterBodies)
        {
            foreach (var cell in WaterGeometry.Corridor(scene, metrics, body))
            {
                var coordinate = new WaterCellCoordinate(cell.X, cell.Y);
                if (!water.TryGetValue(coordinate, out var layers))
                {
                    layers = [];
                    water.Add(coordinate, layers);
                }
                layers.Add(new WaterLayer(
                    body.WaterBodyId,
                    body.AssetKey,
                    cell.BedMeters,
                    cell.SurfaceMeters,
                    cell.CutTopMeters));
            }
        }

        var orderedWater = water.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<WaterLayer>)[.. pair.Value
                .OrderBy(static layer => layer.BedMeters)
                .ThenBy(static layer => layer.SurfaceMeters)
                .ThenBy(static layer => layer.CutTopMeters)
                .ThenBy(static layer => layer.WaterBodyId, StringComparer.Ordinal)]);
        var routeRaster = RouteSurfaceRaster.Prepare(scene, metrics);
        var routes = routeRaster.Cells
            .GroupBy(static cell => new WaterCellCoordinate(cell.X, cell.Y))
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<RouteLayer>)[.. group
                    .Select(static cell => new RouteLayer(
                        cell.RouteSurfaceId,
                        cell.AssetKey,
                        cell.ElevationMeters))
                    .OrderBy(static layer => layer.ElevationMeters)
                    .ThenBy(static layer => layer.RouteSurfaceId, StringComparer.Ordinal)
                    .ThenBy(static layer => layer.AssetKey, StringComparer.Ordinal)]);
        var routeCuts = routeRaster.Cuts
            .GroupBy(static cut => new WaterCellCoordinate(cut.X, cut.Y))
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<RouteCutLayer>)[.. group
                    .Select(static cut => new RouteCutLayer(
                        cut.RouteSurfaceId,
                        cut.SegmentId,
                        cut.BottomMeters,
                        cut.TopMeters))
                    .OrderBy(static cut => cut.BottomMeters)
                    .ThenBy(static cut => cut.TopMeters)
                    .ThenBy(static cut => cut.RouteSurfaceId, StringComparer.Ordinal)
                    .ThenBy(static cut => cut.SegmentId, StringComparer.Ordinal)]);
        return new LayeredSceneColumns(
            scene,
            metrics,
            terrain,
            orderedWater,
            routes,
            routeCuts);
    }

    /// <summary>
    /// Resolves the water-grid cell containing an arbitrary Scene-local
    /// authoring position. The water grid is the finest authored volumetric
    /// raster today; continuous Path cuts use that same coordinate space.
    /// </summary>
    public LayeredSceneColumn AtAuthoringPosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Column position must be finite.");
        if (x < 0.0 || y < 0.0
            || x >= _sceneWidthAuthoringPixels
            || y >= _sceneHeightAuthoringPixels)
        {
            return Empty();
        }

        var step = _metrics.AuthoringPixelsPerWaterCell;
        return AtWaterCell(
            checked((int)Math.Floor(x / step)),
            checked((int)Math.Floor(y / step)));
    }

    /// <summary>Resolves one cell of the Workspace's water raster.</summary>
    public LayeredSceneColumn AtWaterCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _sceneWidthWaterCells || y >= _sceneHeightWaterCells)
            return Empty();

        var coordinate = new WaterCellCoordinate(x, y);
        if (_resolved.TryGetValue(coordinate, out var resolved)) return resolved;
        _water.TryGetValue(coordinate, out var water);
        water ??= [];
        _routes.TryGetValue(coordinate, out var routes);
        routes ??= [];
        _routeCuts.TryGetValue(coordinate, out var routeCuts);
        routeCuts ??= [];
        var terrainCoordinate = new TerrainCellCoordinate(
            WorkspaceMetrics.FloorDivide(x, _metrics.WaterCellsPerTerrainCell),
            WorkspaceMetrics.FloorDivide(y, _metrics.WaterCellsPerTerrainCell));
        _terrain.TryGetValue(terrainCoordinate, out var terrain);

        resolved = new LayeredSceneColumn(
            ResolveTerrain(terrain, water, routeCuts),
            [.. water.Select(static layer => new LayeredColumnSpan(
                    layer.BedMeters,
                    layer.SurfaceMeters,
                    layer.AssetKey,
                    LayeredColumnSpanKind.IndependentFill,
                    layer.WaterBodyId))],
            [.. routes.Select(static layer => new LayeredColumnSpan(
                    layer.ElevationMeters,
                    layer.ElevationMeters,
                    layer.AssetKey,
                    LayeredColumnSpanKind.IndependentSurface,
                    layer.RouteSurfaceId))]);
        _resolved.Add(coordinate, resolved);
        return resolved;
    }

    private static IReadOnlyList<LayeredColumnSpan> ResolveTerrain(
        TerrainCellDocument? terrain,
        IReadOnlyList<WaterLayer> water,
        IReadOnlyList<RouteCutLayer> routeCuts)
    {
        if (terrain is null) return [];
        if (water.Count == 0 && routeCuts.Count == 0)
        {
            return
            [
                new LayeredColumnSpan(
                    null,
                    terrain.ElevationMeters,
                    terrain.AssetKey,
                    LayeredColumnSpanKind.TerrainSolid,
                    null),
            ];
        }

        var cuts = MergeCuts(water, routeCuts);
        List<LayeredColumnSpan> solids = [];
        decimal? bottom = null;
        foreach (var cut in cuts)
        {
            if (cut.BottomMeters > terrain.ElevationMeters) break;
            solids.Add(new LayeredColumnSpan(
                bottom,
                cut.BottomMeters,
                terrain.AssetKey,
                LayeredColumnSpanKind.TerrainSolid,
                null));
            if (cut.TopMeters >= terrain.ElevationMeters) return solids;
            bottom = cut.TopMeters;
        }

        solids.Add(new LayeredColumnSpan(
            bottom,
            terrain.ElevationMeters,
            terrain.AssetKey,
            LayeredColumnSpanKind.TerrainSolid,
            null));
        return solids;
    }

    private static IReadOnlyList<CutSpan> MergeCuts(
        IReadOnlyList<WaterLayer> water,
        IReadOnlyList<RouteCutLayer> routeCuts)
    {
        var ordered = water
            .Select(static layer => new CutSpan(layer.BedMeters, layer.CutTopMeters))
            .Concat(routeCuts.Select(static layer =>
                new CutSpan(layer.BottomMeters, layer.TopMeters)))
            .OrderBy(static cut => cut.BottomMeters)
            .ThenBy(static cut => cut.TopMeters)
            .ToList();
        List<CutSpan> merged = [];
        foreach (var cut in ordered)
        {
            if (merged.Count == 0 || cut.BottomMeters > merged[^1].TopMeters)
            {
                merged.Add(cut);
                continue;
            }
            if (cut.TopMeters > merged[^1].TopMeters)
                merged[^1] = merged[^1] with { TopMeters = cut.TopMeters };
        }
        return merged;
    }

    private static LayeredSceneColumn Empty() => new([], [], []);

    private readonly record struct WaterCellCoordinate(int X, int Y);
    private readonly record struct CutSpan(decimal BottomMeters, decimal TopMeters);
    private sealed record WaterLayer(
        string WaterBodyId,
        string AssetKey,
        decimal BedMeters,
        decimal SurfaceMeters,
        decimal CutTopMeters);
    private sealed record RouteLayer(
        string RouteSurfaceId,
        string AssetKey,
        decimal ElevationMeters);
    private sealed record RouteCutLayer(
        string RouteSurfaceId,
        string SegmentId,
        decimal BottomMeters,
        decimal TopMeters);
}
