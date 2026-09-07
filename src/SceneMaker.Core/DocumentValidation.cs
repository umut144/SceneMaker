using System.Text.RegularExpressions;

namespace SceneMaker.Core;

public sealed class SceneMakerDocumentException : Exception
{
    public SceneMakerDocumentException(string message) : base(message) { }
    public SceneMakerDocumentException(string message, Exception innerException) : base(message, innerException) { }
}

public static partial class DocumentValidation
{
    public static void Validate(SceneDocument? document)
    {
        if (document is null)
            throw new SceneMakerDocumentException("Scene document must not be null.");
        if (document.Schema != SceneMakerSchemas.Scene
            || document.Version != SceneMakerSchemas.SceneVersion)
        {
            throw new SceneMakerDocumentException(
                $"Scene must use {SceneMakerSchemas.Scene} version {SceneMakerSchemas.SceneVersion}.");
        }
        if (document.CoordinateSpace != SceneMakerSchemas.CoordinateSpace)
        {
            throw new SceneMakerDocumentException(
                $"Scene coordinate_space must be '{SceneMakerSchemas.CoordinateSpace}'.");
        }

        ValidateStableId("scene_id", document.SceneId);
        if (!Enum.IsDefined(document.SceneKind))
            throw new SceneMakerDocumentException("Scene requires a supported scene_kind.");
        if (document.SizeCells is null)
            throw new SceneMakerDocumentException("Scene requires size_cells.");
        if (document.SizeCells.Width <= 0 || document.SizeCells.Height <= 0)
            throw new SceneMakerDocumentException("Scene size_cells must be positive on both axes.");

        if (document.TerrainCells is null)
            throw new SceneMakerDocumentException("Scene requires terrain_cells.");

        TerrainCellDocument? previous = null;
        foreach (var cell in document.TerrainCells)
        {
            if (cell.X < 0 || cell.X >= document.SizeCells.Width
                || cell.Y < 0 || cell.Y >= document.SizeCells.Height)
            {
                throw new SceneMakerDocumentException(
                    $"Terrain cell ({cell.X}, {cell.Y}) lies outside size_cells.");
            }
            if (previous is not null
                && (cell.Y < previous.Y || cell.Y == previous.Y && cell.X <= previous.X))
            {
                throw new SceneMakerDocumentException(
                    "Terrain cells must be unique and canonically ordered by Y, then X.");
            }
            previous = cell;
        }
        if (document.Props is null)
            throw new SceneMakerDocumentException("Scene requires props.");

        string? previousInstanceId = null;
        foreach (var prop in document.Props)
        {
            ValidateStableId("prop instance_id", prop.InstanceId);
            if (previousInstanceId is not null
                && string.CompareOrdinal(prop.InstanceId, previousInstanceId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Props must have unique instance IDs in canonical ordinal order.");
            }
            if (prop.PositionAuthoringPx is null)
                throw new SceneMakerDocumentException(
                    $"Placement '{prop.InstanceId}' requires position_authoring_px.");
            previousInstanceId = prop.InstanceId;
        }

        ValidateElevationRegions(document);
        ValidateRouteSurfaces(document);
        ValidateBridges(document);
        ValidateActivationGroups(document);
        ValidateWaterBodies(document);

        if (document.TemplateAnchors is null)
            throw new SceneMakerDocumentException("Scene requires template_anchors.");

        if (document.SceneKind == SceneKind.Instance)
        {
            if (document.TemplateDefinition is not null)
            {
                throw new SceneMakerDocumentException(
                    "Scene Instance requires template_definition to be null.");
            }
        }
        else
        {
            if (document.TemplateDefinition is null)
                throw new SceneMakerDocumentException("Scene Template requires template_definition.");
            if (document.TemplateAnchors.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own Template Anchors.");

            // Composition moves Terrain cells, Props and nothing else. A
            // Template carrying a river would therefore lose it silently at
            // every Anchor it is placed at, which is worse than refusing to
            // author one until composition knows what to do with it.
            if (document.WaterBodies.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own water bodies.");

            // A group exists to relate water bodies, and a Template has none.
            // An empty group would be a name a consumer could bind a trigger to
            // that switches nothing, which is worse than refusing it.
            if (document.ActivationGroups.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own activation groups.");
            if (document.ElevationRegions.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own elevation regions.");
            if (document.Bridges.Count > 0)
                throw new SceneMakerDocumentException("Scene Template cannot own bridges.");
            ValidateGroupNumber("Scene Template", document.TemplateDefinition.GroupNumber);
        }

        string? previousAnchorId = null;
        foreach (var anchor in document.TemplateAnchors)
        {
            ValidateStableId("template anchor_id", anchor.AnchorId);
            if (previousAnchorId is not null
                && string.CompareOrdinal(anchor.AnchorId, previousAnchorId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Template Anchors must have unique IDs in canonical ordinal order.");
            }
            ValidateGroupNumber($"Template Anchor '{anchor.AnchorId}'", anchor.GroupNumber);
            previousAnchorId = anchor.AnchorId;
        }
    }

    public static void ValidateGrid(SceneDocument? document, WorkspaceMetrics metrics)
    {
        Validate(document);
        ArgumentNullException.ThrowIfNull(metrics);
        try
        {
            _ = metrics.SceneWidthAuthoringPixels(document!);
            _ = metrics.SceneHeightAuthoringPixels(document!);
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Scene size_cells exceeds the Workspace authoring coordinate range.", exception);
        }

        ValidateElevation("Scene default_elevation_meters", document!.DefaultElevationMeters, metrics);
        foreach (var cell in document.TerrainCells)
        {
            ValidateElevation(
                $"Terrain cell ({cell.X}, {cell.Y}) elevation_meters",
                cell.ElevationMeters,
                metrics);
        }
        foreach (var prop in document.Props)
        {
            ValidateElevation(
                $"Placement '{prop.InstanceId}' elevation_meters",
                prop.ElevationMeters,
                metrics);
        }
        foreach (var body in document.ElevationRegions)
        {
            ValidateElevation(
                $"Elevation region '{body.ElevationRegionId}' elevation_meters",
                body.ElevationMeters,
                metrics);
            for (var index = 0; index < body.Points.Count; index++)
            {
                // The same two rules `Draw ElevationRegion` authors by, and only for
                // the anchor. A handle is a curve control rather than a place:
                // snapping one would snap the shape of the curve, and the curve
                // it shapes is allowed to bulge past the edge of the map, where
                // the raster simply stops.
                ValidateGridAnchor(
                    $"Elevation region '{body.ElevationRegionId}' point {index}",
                    body.Points[index].PositionAuthoringPx,
                    document.SizeCells,
                    metrics);
            }
        }
        _ = ElevationRegionGeometry.EffectiveTerrainCells(document, metrics);

        foreach (var bridge in document.Bridges)
        {
            // One height for the whole span, chosen directly, so it sits on
            // the quantum like every other directly authored elevation.
            ValidateElevation(
                $"Bridge '{bridge.BridgeId}' elevation_meters",
                bridge.ElevationMeters,
                metrics);

            // A deck is a continuous band like a route, so its ends stay
            // inside the Scene without being held to either raster. Whether
            // its corners reach past the edge is a question about the width
            // too, and the editing operation answers it.
            ValidateAuthoringPosition(
                $"Bridge '{bridge.BridgeId}' start",
                bridge.StartAuthoringPx,
                document.SizeCells,
                metrics.AuthoringPixelsPerTerrainCell);
            ValidateAuthoringPosition(
                $"Bridge '{bridge.BridgeId}' end",
                bridge.EndAuthoringPx,
                document.SizeCells,
                metrics.AuthoringPixelsPerTerrainCell);

            // How much span the gaps leave over is a question about metres, so
            // it can only be asked here. Authoring already refuses it while the
            // author turns the number; this is for the documents that arrive
            // some other way. Without it a bridge whose gaps eat the whole span
            // loads, draws nothing - the Canvas asks for the layout and is told
            // "none" - and only fails much later, at export, where the reason
            // is furthest from the scene that carries it.
            if (BridgeGeometry.PlankDepthMeters(
                    BridgeGeometry.LengthMeters(metrics, bridge),
                    bridge.PlankCount,
                    bridge.PlankGapMeters) <= 0m)
            {
                throw new SceneMakerDocumentException(
                    $"Bridge '{bridge.BridgeId}' leaves no room for a plank between its gaps.");
            }
        }

        foreach (var route in document.RouteSurfaces)
        {
            for (var index = 0; index < route.Points.Count; index++)
            {
                // Only the first height is chosen directly. Later anchors are
                // derived from grade and Bezier arc length and may therefore
                // lie between the Workspace's authoring quanta.
                if (index == 0)
                {
                    ValidateElevation(
                        $"Route surface '{route.RouteSurfaceId}' starting elevation_meters",
                        route.Points[index].ElevationMeters,
                        metrics);
                }
                // A route is a continuous band and is not rasterized. Its
                // authoring-pixel anchor must stay inside the Scene but has no
                // arbitrary Terrain- or water-grid alignment requirement.
                ValidateAuthoringPosition(
                    $"Route surface '{route.RouteSurfaceId}' point {index}",
                    route.Points[index].PositionAuthoringPx,
                    document.SizeCells,
                    metrics.AuthoringPixelsPerTerrainCell);
            }
        }

        if (document.SceneKind == SceneKind.Template)
        {
            ValidateGridAnchor(
                "Scene Template insertion anchor",
                document.TemplateDefinition!.InsertionAnchorAuthoringPx,
                document.SizeCells,
                metrics);
        }
        foreach (var anchor in document.TemplateAnchors)
        {
            ValidateGridAnchor(
                $"Template Anchor '{anchor.AnchorId}'",
                anchor.PositionAuthoringPx,
                document.SizeCells,
                metrics);
        }
        foreach (var body in document.WaterBodies)
        {
            for (var index = 0; index < body.Points.Count; index++)
            {
                ValidateElevation(
                    $"Water body '{body.WaterBodyId}' point {index} elevation_meters",
                    body.Points[index].ElevationMeters,
                    metrics);
                ValidateGridPosition(
                    $"Water body '{body.WaterBodyId}' point {index}",
                    body.Points[index].PositionAuthoringPx,
                    document.SizeCells,
                    metrics.AuthoringPixelsPerTerrainCell,
                    metrics.AuthoringPixelsPerWaterCell);
            }
        }
    }

    private static void ValidateElevationRegions(SceneDocument document)
    {
        if (document.ElevationRegions is null)
            throw new SceneMakerDocumentException("Scene requires elevation_regions.");

        string? previousBodyId = null;
        foreach (var body in document.ElevationRegions)
        {
            ValidateStableId("elevation_region_id", body.ElevationRegionId);
            if (previousBodyId is not null
                && string.CompareOrdinal(body.ElevationRegionId, previousBodyId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Elevation regions must have unique IDs in canonical ordinal order.");
            }
            previousBodyId = body.ElevationRegionId;

            var label = $"Elevation region '{body.ElevationRegionId}'";
            if (body.Points is null || body.Points.Count < 2)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires at least two curve points to close a contour.");
            }
            foreach (var point in body.Points)
            {
                if (point.PositionAuthoringPx is null)
                    throw new SceneMakerDocumentException($"{label} requires position_authoring_px on every point.");
                if (point.HandleInAuthoringPx is null || point.HandleOutAuthoringPx is null)
                    throw new SceneMakerDocumentException($"{label} requires both handles on every point.");
                if (!Enum.IsDefined(point.Mode))
                    throw new SceneMakerDocumentException($"{label} requires a supported point mode.");
                if (point.Mode == ElevationRegionPointMode.Linear
                    && !(point.HandleInAuthoringPx.IsZero() && point.HandleOutAuthoringPx.IsZero()))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} has a linear point carrying handles; a linear point's handles are zero.");
                }
            }
            _ = ElevationRegionGeometry.RequireContour(body);
        }
    }

    /// <summary>
    /// Checks what a bridge is on its own terms. Whether its posts can stand
    /// where they would stand is a question about the rest of the Scene and
    /// about the Workspace's Assets, so it belongs to the editing operation
    /// rather than to the document shape.
    /// </summary>
    private static void ValidateBridges(SceneDocument document)
    {
        if (document.Bridges is null)
            throw new SceneMakerDocumentException("Scene requires bridges.");

        string? previousBridgeId = null;
        foreach (var bridge in document.Bridges)
        {
            ValidateStableId("bridge_id", bridge.BridgeId);
            if (previousBridgeId is not null
                && string.CompareOrdinal(bridge.BridgeId, previousBridgeId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Bridges must have unique IDs in canonical ordinal order.");
            }
            previousBridgeId = bridge.BridgeId;

            var label = $"Bridge '{bridge.BridgeId}'";
            if (string.IsNullOrWhiteSpace(bridge.PlankAssetKey))
                throw new SceneMakerDocumentException($"{label} requires a plank_asset_key.");
            if (string.IsNullOrWhiteSpace(bridge.AnchorAssetKey))
                throw new SceneMakerDocumentException($"{label} requires an anchor_asset_key.");
            if (bridge.WidthMeters <= 0m)
                throw new SceneMakerDocumentException($"{label} requires a positive width_meters.");
            if (bridge.PlankCount <= 0)
                throw new SceneMakerDocumentException($"{label} requires a positive plank_count.");
            if (bridge.PlankGapMeters < 0m)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires a plank_gap_meters of zero or more.");
            }
            if (bridge.StartAuthoringPx is null || bridge.EndAuthoringPx is null)
                throw new SceneMakerDocumentException($"{label} requires both ends.");

            // A span with no length has no direction, and without one there is
            // no left and no right to put a post on.
            if (bridge.StartAuthoringPx.X == bridge.EndAuthoringPx.X
                && bridge.StartAuthoringPx.Y == bridge.EndAuthoringPx.Y)
            {
                throw new SceneMakerDocumentException($"{label} has both ends in the same place.");
            }
        }
    }

    /// <summary>
    /// Checks the authored source of continuously inclined routes without
    /// assigning an Actor capability or a horizontal grid to them.
    /// </summary>
    private static void ValidateRouteSurfaces(SceneDocument document)
    {
        if (document.RouteSurfaces is null)
            throw new SceneMakerDocumentException("Scene requires route_surfaces.");

        string? previousRouteId = null;
        HashSet<string> segmentIds = new(StringComparer.Ordinal);
        foreach (var route in document.RouteSurfaces)
        {
            ValidateStableId("route_surface_id", route.RouteSurfaceId);
            if (previousRouteId is not null
                && string.CompareOrdinal(route.RouteSurfaceId, previousRouteId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Route surfaces must have unique IDs in canonical ordinal order.");
            }
            previousRouteId = route.RouteSurfaceId;

            var label = $"Route surface '{route.RouteSurfaceId}'";
            if (string.IsNullOrWhiteSpace(route.AssetKey))
                throw new SceneMakerDocumentException($"{label} requires an asset_key.");
            if (route.Points is null || route.Points.Count < 2)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires at least two curve points.");
            }
            if (route.Segments is null || route.Segments.Count != route.Points.Count - 1)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires exactly one segment for every consecutive point pair.");
            }

            string? previousSegmentId = null;
            foreach (var segment in route.Segments)
            {
                ValidateStableId("segment_id", segment.SegmentId);
                if (!segmentIds.Add(segment.SegmentId))
                {
                    throw new SceneMakerDocumentException(
                        $"Segment ID '{segment.SegmentId}' is duplicated across route surfaces.");
                }
                if (previousSegmentId is not null
                    && string.CompareOrdinal(segment.SegmentId, previousSegmentId) <= 0)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} segments must have unique IDs in canonical ordinal order.");
                }
                if (!RouteSurfaceEditing.IsSupportedGradePercent(segment.GradePercent))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} segment '{segment.SegmentId}' has unsupported grade_percent {segment.GradePercent}.");
                }
                if (!Enum.IsDefined(segment.Operation))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} segment '{segment.SegmentId}' has an unsupported operation.");
                }
                if (segment.Operation == RouteSegmentOperation.Additive
                    && segment.ClearanceAboveMeters is not null)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} segment '{segment.SegmentId}' is additive and must leave "
                        + "clearance_above_meters null.");
                }
                if (segment.Operation == RouteSegmentOperation.Subtractive
                    && segment.ClearanceAboveMeters is not > 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} segment '{segment.SegmentId}' is subtractive and needs "
                        + "positive clearance_above_meters.");
                }
                previousSegmentId = segment.SegmentId;
            }

            foreach (var point in route.Points)
            {
                if (point.PositionAuthoringPx is null)
                    throw new SceneMakerDocumentException(
                        $"{label} requires position_authoring_px on every point.");
                if (point.HandleInAuthoringPx is null || point.HandleOutAuthoringPx is null)
                    throw new SceneMakerDocumentException(
                        $"{label} requires both handles on every point.");
                if (!Enum.IsDefined(point.Mode))
                    throw new SceneMakerDocumentException(
                        $"{label} requires a supported point mode.");
                if (point.WidthMeters <= 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} requires a positive width_meters on every point.");
                }
                if (point.Mode == RoutePointMode.Linear
                    && !(point.HandleInAuthoringPx.IsZero()
                        && point.HandleOutAuthoringPx.IsZero()))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} has a linear point carrying handles; a linear point's handles are zero.");
                }
            }

            for (var index = 0; index + 1 < route.Points.Count; index++)
            {
                if (RouteSurfaceGeometry.HasPositiveRun(
                        route.Points[index], route.Points[index + 1]))
                {
                    continue;
                }
                throw new SceneMakerDocumentException(
                    $"{label} has no horizontal run between points {index} and {index + 1}.");
            }
        }
    }

    private static void ValidateElevation(
        string label,
        decimal elevationMeters,
        WorkspaceMetrics metrics)
    {
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"{label} must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m; found {elevationMeters:0.############################} m."));
        }
    }

    /// <summary>
    /// The states a Scene can be in.
    ///
    /// <para>Two states at least, because a group with one switches nothing and
    /// would be a name a consumer binds a trigger to for no effect. The initial
    /// state is named rather than taken to be the first, so the authored order
    /// of the states can stay whatever reads best.</para>
    /// </summary>
    private static void ValidateActivationGroups(SceneDocument document)
    {
        if (document.ActivationGroups is null)
            throw new SceneMakerDocumentException("Scene requires activation_groups.");

        string? previousGroup = null;
        foreach (var group in document.ActivationGroups)
        {
            ValidateStableId("activation group", group.Group);
            if (previousGroup is not null
                && string.CompareOrdinal(group.Group, previousGroup) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Activation groups must have unique names in canonical ordinal order.");
            }
            previousGroup = group.Group;

            var label = $"Activation group '{group.Group}'";
            if (group.States is null || group.States.Count < 2)
                throw new SceneMakerDocumentException($"{label} requires at least two states.");
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var state in group.States)
            {
                ValidateStableId($"{label} state", state);
                if (!seen.Add(state))
                    throw new SceneMakerDocumentException($"{label} lists state '{state}' twice.");
            }
            if (group.InitialState is null || !seen.Contains(group.InitialState))
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires an initial_state that is one of its states.");
            }
        }
    }

    /// <summary>
    /// What a body says about when it exists and what it meets.
    ///
    /// <para><c>active_in</c> is checked against its own group rather than
    /// against every state in the Scene, because a state name is only meaningful
    /// inside the group that declares it.</para>
    /// </summary>
    private static void ValidateWaterBodyRelations(SceneDocument document, WaterBodyDocument body)
    {
        var label = $"Water body '{body.WaterBodyId}'";

        if (body.Activation is { } activation)
        {
            var group = document.ActivationGroups.FirstOrDefault(
                candidate => string.Equals(candidate.Group, activation.Group, StringComparison.Ordinal))
                ?? throw new SceneMakerDocumentException(
                    $"{label} names activation group '{activation.Group}', which the Scene does not declare.");
            if (!Enum.IsDefined(activation.Inactive))
                throw new SceneMakerDocumentException($"{label} requires a supported inactive.");
            if (activation.ActiveIn is null || activation.ActiveIn.Count == 0)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires at least one state in active_in; a body active in none of them is a body nobody can see.");
            }
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var state in activation.ActiveIn)
            {
                if (!group.States.Contains(state, StringComparer.Ordinal))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} is active in '{state}', which is not a state of group '{group.Group}'.");
                }
                if (!seen.Add(state))
                    throw new SceneMakerDocumentException($"{label} lists state '{state}' twice.");
            }
        }

        if (body.Junctions is null)
            throw new SceneMakerDocumentException($"{label} requires junctions.");

        (WaterEnd End, string Id)? previous = null;
        foreach (var claim in body.Junctions)
        {
            if (!Enum.IsDefined(claim.End))
                throw new SceneMakerDocumentException($"{label} requires a supported junction end.");
            ValidateStableId($"{label} junction water_body_id", claim.WaterBodyId);
            if (string.Equals(claim.WaterBodyId, body.WaterBodyId, StringComparison.Ordinal))
                throw new SceneMakerDocumentException($"{label} cannot meet itself.");
            if (!document.WaterBodies.Any(
                    other => string.Equals(other.WaterBodyId, claim.WaterBodyId, StringComparison.Ordinal)))
            {
                throw new SceneMakerDocumentException(
                    $"{label} meets '{claim.WaterBodyId}', which the Scene does not contain.");
            }
            var current = (claim.End, claim.WaterBodyId);
            if (previous is { } earlier
                && (earlier.End > current.End
                    || (earlier.End == current.End
                        && string.CompareOrdinal(earlier.Id, current.Item2) >= 0)))
            {
                throw new SceneMakerDocumentException(
                    $"{label} must list its junctions once each, by end and then by id.");
            }
            previous = current;
        }
    }

    /// <summary>
    /// The authored curve, checked without the Workspace grid. Where its points
    /// are allowed to sit is a grid question and belongs to
    /// <see cref="ValidateGrid"/>; that they describe a curve at all is checked
    /// here, so a document is never half a river.
    /// </summary>
    private static void ValidateWaterBodies(SceneDocument document)
    {
        if (document.WaterBodies is null)
            throw new SceneMakerDocumentException("Scene requires water_bodies.");

        string? previousBodyId = null;
        foreach (var body in document.WaterBodies)
        {
            ValidateStableId("water_body_id", body.WaterBodyId);
            if (previousBodyId is not null
                && string.CompareOrdinal(body.WaterBodyId, previousBodyId) <= 0)
            {
                throw new SceneMakerDocumentException(
                    "Water bodies must have unique IDs in canonical ordinal order.");
            }
            previousBodyId = body.WaterBodyId;

            ValidateWaterBodyRelations(document, body);

            var label = $"Water body '{body.WaterBodyId}'";
            if (!Enum.IsDefined(body.WaterKind))
                throw new SceneMakerDocumentException($"{label} requires a supported water_kind.");
            if (string.IsNullOrWhiteSpace(body.AssetKey))
                throw new SceneMakerDocumentException($"{label} requires an asset_key.");
            if (body.Points is null || body.Points.Count < 2)
            {
                throw new SceneMakerDocumentException(
                    $"{label} requires at least two curve points; the first is its source and the last its mouth.");
            }

            AuthoringPixelPosition? previousPosition = null;
            foreach (var point in body.Points)
            {
                if (point.PositionAuthoringPx is null)
                    throw new SceneMakerDocumentException($"{label} requires position_authoring_px on every point.");
                if (point.HandleInAuthoringPx is null || point.HandleOutAuthoringPx is null)
                    throw new SceneMakerDocumentException($"{label} requires both handles on every point.");
                if (!Enum.IsDefined(point.Mode))
                    throw new SceneMakerDocumentException($"{label} requires a supported point mode.");
                if (point.WidthMeters <= 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} requires a positive width_meters on every point.");
                }

                // Water with no depth is not water, and negative headroom would
                // cut below the surface it is measured from. Heights themselves
                // stay unconstrained: how high water sits is the author's, and
                // a Scene may work below zero.
                if (point.ChannelDepthMeters <= 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} requires a positive channel_depth_meters on every point.");
                }
                if (point.ClearanceAboveMeters < 0m)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} cannot require negative clearance_above_meters.");
                }

                // A linear point is exactly the absence of handles. Storing a
                // handle next to it would leave two answers to what the curve
                // does there, and the reader could not tell which one wins.
                if (point.Mode == WaterPointMode.Linear
                    && !(point.HandleInAuthoringPx.IsZero() && point.HandleOutAuthoringPx.IsZero()))
                {
                    throw new SceneMakerDocumentException(
                        $"{label} has a linear point carrying handles; a linear point's handles are zero.");
                }
                if (previousPosition is not null
                    && previousPosition.X == point.PositionAuthoringPx.X
                    && previousPosition.Y == point.PositionAuthoringPx.Y)
                {
                    throw new SceneMakerDocumentException(
                        $"{label} repeats the point ({point.PositionAuthoringPx.X}, {point.PositionAuthoringPx.Y}); consecutive points must differ.");
                }
                previousPosition = point.PositionAuthoringPx;
            }
        }
    }

    private static void ValidateGroupNumber(string label, int groupNumber)
    {
        if (groupNumber <= 0)
            throw new SceneMakerDocumentException($"{label} group_number must be positive.");
    }

    private static void ValidateGridAnchor(
        string label,
        AuthoringPixelPosition? position,
        SceneSizeCells size,
        WorkspaceMetrics metrics) =>
        ValidateGridPosition(
            label,
            position,
            size,
            metrics.AuthoringPixelsPerTerrainCell,
            metrics.AuthoringPixelsPerTerrainCell);

    /// <summary>
    /// A position inside the Scene that sits on a grid. Bounds are measured in
    /// Terrain cells because that is what a Scene's size is; the step it snaps
    /// to is its own - the WorldGrid for an Anchor, the finer water grid for a
    /// curve point.
    /// </summary>
    private static void ValidateGridPosition(
        string label,
        AuthoringPixelPosition? position,
        SceneSizeCells size,
        int terrainStep,
        int snapStep)
    {
        ValidateAuthoringPosition(label, position, size, terrainStep);
        if (position!.X % snapStep != 0 || position.Y % snapStep != 0)
        {
            throw new SceneMakerDocumentException(
                $"{label} must align to the {snapStep}-authoring-pixel grid.");
        }
    }

    /// <summary>
    /// A position inside the Scene in authoring pixels, with no statement about
    /// which horizontal grid - if any - it belongs to.
    /// </summary>
    private static void ValidateAuthoringPosition(
        string label,
        AuthoringPixelPosition? position,
        SceneSizeCells size,
        int terrainStep)
    {
        if (position is null)
            throw new SceneMakerDocumentException($"{label} requires an authoring-pixel position.");
        var width = checked(size.Width * terrainStep);
        var height = checked(size.Height * terrainStep);
        if (position.X < 0 || position.X > width
            || position.Y < 0 || position.Y > height)
        {
            throw new SceneMakerDocumentException($"{label} lies outside Scene bounds.");
        }
    }

    public static void ValidateStableId(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !StableIdRegex().IsMatch(value))
        {
            throw new SceneMakerDocumentException(
                $"{label} must use lowercase snake_case segments separated by dots.");
        }
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
