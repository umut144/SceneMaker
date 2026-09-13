namespace SceneMaker.Core;

/// <summary>
/// Changes one Scene edge at a time. North and East only add Cells past the
/// far edge, so every existing local coordinate is already inside the larger
/// bounds and nothing has to move.
///
/// <para>West and South add Cells at the near edge instead. The origin sits at
/// the Scene's south-west corner (<see cref="SceneMakerSchemas.CoordinateSpace"/>),
/// so growing that edge moves the origin away from the ground it used to mark;
/// every authored position is shifted by the same Cell count so what it
/// describes does not move with it. <see cref="DocumentValidation.Validate"/>
/// only bounds Terrain cells against <c>size_cells</c> and would pass a West or
/// South extension that grew the Scene without shifting anything - every
/// authoring-pixel position is still non-negative and now further from the far
/// edge than before - while silently sliding every authored Placement, water
/// body, route, bridge and Anchor toward the wrong corner.</para>
/// </summary>
public static class MapEditing
{
    public static SceneDocument ExtendNorth(SceneDocument scene, int cells) =>
        Grow(scene, widthCells: 0, heightCells: cells);

    public static SceneDocument ExtendEast(SceneDocument scene, int cells) =>
        Grow(scene, widthCells: cells, heightCells: 0);

    public static SceneDocument ExtendWest(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: cells, heightCells: 0, metrics);

    public static SceneDocument ExtendSouth(SceneDocument scene, int cells, WorkspaceMetrics metrics) =>
        Shift(scene, widthCells: 0, heightCells: cells, metrics);

    private static SceneDocument Grow(SceneDocument scene, int widthCells, int heightCells)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RequireOneEdge(widthCells, heightCells);
        try
        {
            var expanded = scene with
            {
                SizeCells = new SceneSizeCells
                {
                    Width = checked(scene.SizeCells.Width + widthCells),
                    Height = checked(scene.SizeCells.Height + heightCells),
                },
            };
            DocumentValidation.Validate(expanded);
            return expanded;
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Map extension exceeds the integer authoring coordinate range.", exception);
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
                "Map extension exceeds the integer authoring coordinate range.", exception);
        }
    }

    private static AuthoringPixelPosition ShiftPosition(
        AuthoringPixelPosition position, int offsetX, int offsetY) => new()
    {
        X = checked(position.X + offsetX),
        Y = checked(position.Y + offsetY),
    };

    private static void RequireOneEdge(int widthCells, int heightCells)
    {
        if (widthCells < 0 || heightCells < 0 || widthCells == 0 && heightCells == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthCells),
                "Map extension requires a positive Cell count on exactly one edge.");
        }
    }
}
