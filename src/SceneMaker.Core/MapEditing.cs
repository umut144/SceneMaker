namespace SceneMaker.Core;

/// <summary>
/// Grows or shrinks one Scene edge at a time. North and East only move the far
/// edge, so an Extend there only ever relaxes bounds and a Shrink there only
/// ever tightens them without moving anything already inside the new bounds.
///
/// <para>West and South move the near edge instead. The origin sits at the
/// Scene's south-west corner (<see cref="SceneMakerSchemas.CoordinateSpace"/>),
/// so growing that edge moves the origin away from the ground it used to mark,
/// and shrinking it moves the origin onto ground that used to be further in;
/// every authored position is shifted by the same Cell count so what it
/// describes does not move with it. <see cref="DocumentValidation.Validate"/>
/// only bounds Terrain cells against <c>size_cells</c> and would pass a West or
/// South resize that changed the Scene without shifting anything - every
/// authoring-pixel position is still non-negative and simply sits at a
/// different distance from the far edge - while silently sliding every
/// authored Placement, water body, route, bridge and Anchor toward the wrong
/// corner.</para>
///
/// <para>A Shrink drops whatever Terrain and Placements no longer fit -
/// that strip disappears along with the Cells it stood on, the same way it
/// would if the author erased it first. Every other authored kind is left
/// to refuse instead: <see cref="DocumentValidation.ValidateGrid"/> - used
/// for every direction, not only the two that shift - rejects a result with
/// an Elevation Region point, a Route, a Bridge, a Water Body or a Template
/// Anchor outside the smaller bounds, because there is no single correct way
/// to cut a multi-point shape or a two-ended span in half automatically; an
/// author who wants that strip's Elevation Region gone erases it and shrinks
/// again.</para>
/// </summary>
public static class MapEditing
{
    public static SceneDocument ExtendNorth(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Grow(scene, widthCells: 0, heightCells: RequirePositive(cells), metrics);

    public static SceneDocument ExtendEast(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Grow(scene, widthCells: RequirePositive(cells), heightCells: 0, metrics);

    public static SceneDocument ExtendWest(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: RequirePositive(cells), heightCells: 0, metrics);

    public static SceneDocument ExtendSouth(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: 0, heightCells: RequirePositive(cells), metrics);

    public static SceneDocument ShrinkNorth(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Grow(scene, widthCells: 0, heightCells: -RequirePositive(cells), metrics);

    public static SceneDocument ShrinkEast(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Grow(scene, widthCells: -RequirePositive(cells), heightCells: 0, metrics);

    public static SceneDocument ShrinkWest(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: -RequirePositive(cells), heightCells: 0, metrics);

    public static SceneDocument ShrinkSouth(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: 0, heightCells: -RequirePositive(cells), metrics);

    private static SceneDocument Grow(
        SceneDocument scene, int widthCells, int heightCells, WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        RequireOneEdge(widthCells, heightCells);
        try
        {
            var resized = scene with
            {
                SizeCells = new SceneSizeCells
                {
                    Width = checked(scene.SizeCells.Width + widthCells),
                    Height = checked(scene.SizeCells.Height + heightCells),
                },
            };
            resized = DropTerrainAndPropsOutsideBounds(resized, metrics);
            DocumentValidation.ValidateGrid(resized, metrics);
            return resized;
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Map resize exceeds the integer authoring coordinate range.", exception);
        }
    }

    private static SceneDocument Shift(
        SceneDocument scene, int widthCells, int heightCells, WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        RequireOneEdge(widthCells, heightCells);
        try
        {
            var step = metrics.AuthoringPixelsPerTerrainCell;
            var offsetX = checked(widthCells * step);
            var offsetY = checked(heightCells * step);

            var shifted = scene with
            {
                SizeCells = new SceneSizeCells
                {
                    Width = checked(scene.SizeCells.Width + widthCells),
                    Height = checked(scene.SizeCells.Height + heightCells),
                },
                TerrainCells = scene.TerrainCells
                    .Select(cell => cell with
                    {
                        X = checked(cell.X + widthCells),
                        Y = checked(cell.Y + heightCells),
                    })
                    .ToList(),
                Props = scene.Props
                    .Select(prop => prop with
                    {
                        PositionAuthoringPx = ShiftPosition(prop.PositionAuthoringPx, offsetX, offsetY),
                    })
                    .ToList(),
                ElevationRegions = scene.ElevationRegions
                    .Select(region => region with
                    {
                        Points = region.Points
                            .Select(point => point with
                            {
                                PositionAuthoringPx = ShiftPosition(point.PositionAuthoringPx, offsetX, offsetY),
                            })
                            .ToList(),
                    })
                    .ToList(),
                RouteSurfaces = scene.RouteSurfaces
                    .Select(route => route with
                    {
                        Points = route.Points
                            .Select(point => point with
                            {
                                PositionAuthoringPx = ShiftPosition(point.PositionAuthoringPx, offsetX, offsetY),
                            })
                            .ToList(),
                    })
                    .ToList(),
                WaterBodies = scene.WaterBodies
                    .Select(body => body with
                    {
                        Points = body.Points
                            .Select(point => point with
                            {
                                PositionAuthoringPx = ShiftPosition(point.PositionAuthoringPx, offsetX, offsetY),
                            })
                            .ToList(),
                    })
                    .ToList(),
                Bridges = scene.Bridges
                    .Select(bridge => bridge with
                    {
                        StartAuthoringPx = ShiftPosition(bridge.StartAuthoringPx, offsetX, offsetY),
                        EndAuthoringPx = ShiftPosition(bridge.EndAuthoringPx, offsetX, offsetY),
                    })
                    .ToList(),
                TemplateDefinition = scene.TemplateDefinition is { } definition
                    ? definition with
                    {
                        InsertionAnchorAuthoringPx =
                            ShiftPosition(definition.InsertionAnchorAuthoringPx, offsetX, offsetY),
                    }
                    : null,
                TemplateAnchors = scene.TemplateAnchors
                    .Select(anchor => anchor with
                    {
                        PositionAuthoringPx = ShiftPosition(anchor.PositionAuthoringPx, offsetX, offsetY),
                    })
                    .ToList(),
            };
            shifted = DropTerrainAndPropsOutsideBounds(shifted, metrics);
            DocumentValidation.ValidateGrid(shifted, metrics);
            return shifted;
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Map resize exceeds the integer authoring coordinate range.", exception);
        }
    }

    private static AuthoringPixelPosition ShiftPosition(
        AuthoringPixelPosition position, int offsetX, int offsetY) => new()
    {
        X = checked(position.X + offsetX),
        Y = checked(position.Y + offsetY),
    };

    /// <summary>
    /// Drops Terrain cells and Placements that no longer fit
    /// <paramref name="scene"/>'s own (already resized and, for West or
    /// South, already shifted) bounds. A no-op on Extend, which only ever
    /// relaxes bounds and so never finds anything outside them here.
    ///
    /// <para>Terrain and Placements are the two authored kinds a Shrink takes
    /// back this way, chosen because both are simple - a Terrain cell is a
    /// single grid cell, a Placement a single point - so "no longer fits"
    /// has exactly one meaning for each. See the type doc for why every
    /// other authored kind is left to <see cref="DocumentValidation.ValidateGrid"/>
    /// instead.</para>
    /// </summary>
    private static SceneDocument DropTerrainAndPropsOutsideBounds(
        SceneDocument scene, WorkspaceMetrics metrics)
    {
        var size = scene.SizeCells;
        var widthPx = checked(size.Width * metrics.AuthoringPixelsPerTerrainCell);
        var heightPx = checked(size.Height * metrics.AuthoringPixelsPerTerrainCell);
        return scene with
        {
            TerrainCells = scene.TerrainCells
                .Where(cell => cell.X >= 0 && cell.X < size.Width
                    && cell.Y >= 0 && cell.Y < size.Height)
                .ToList(),
            // The same inclusive bound DocumentValidation.ValidateAuthoringPosition
            // holds every other authoring-pixel position to: a Placement sitting
            // exactly on the far edge still fits.
            Props = scene.Props
                .Where(prop =>
                    prop.PositionAuthoringPx.X >= 0 && prop.PositionAuthoringPx.X <= widthPx
                    && prop.PositionAuthoringPx.Y >= 0 && prop.PositionAuthoringPx.Y <= heightPx)
                .ToList(),
        };
    }

    private static int RequirePositive(int cells)
    {
        if (cells <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cells), "Map resize requires a positive Cell count.");
        }
        return cells;
    }

    private static void RequireOneEdge(int widthCells, int heightCells)
    {
        if (widthCells == 0 && heightCells == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthCells),
                "Map resize requires a non-zero Cell count on exactly one edge.");
        }
    }
}
