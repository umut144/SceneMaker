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
/// <para>A Shrink that would cut into anything already authored in the
/// removed strip is refused rather than silently dropping it:
/// <see cref="DocumentValidation.ValidateGrid"/> - used for every direction,
/// not only the two that shift - rejects a result with a Terrain cell, an
/// Elevation Region point, a Route, a Bridge, a Water Body or a Template
/// Anchor outside the smaller bounds.</para>
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
