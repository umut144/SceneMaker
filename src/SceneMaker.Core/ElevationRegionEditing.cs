using System.Globalization;

namespace SceneMaker.Core;

/// <summary>One point of a closed hill contour while it is being drawn.</summary>
public readonly record struct ElevationRegionDraftPoint(
    int X,
    int Y,
    ElevationRegionPointMode Mode,
    AuthoringPixelOffset? DraggedHandleOut = null);

/// <summary>Which Bezier handle of a hill point is being edited.</summary>
public enum ElevationRegionHandleSide
{
    In,
    Out,
}

/// <summary>Pure edits for closed, level-topped hill bodies.</summary>
public static class ElevationRegionEditing
{
    /// <summary>
    /// Authors one closed contour at one absolute top. No Asset is asked for: a
    /// hill raises painted Terrain and the painted cell keeps saying what
    /// the surface is made of.
    /// </summary>
    public static SceneDocument Place(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<ElevationRegionPointDocument> points,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Elevation region elevation {elevationMeters:0.############################} m must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var body = new ElevationRegionDocument
        {
            ElevationRegionId = NextElevationRegionId(scene),
            ElevationMeters = elevationMeters,
            Points = [.. points],
        };
        _ = ElevationRegionGeometry.RequireContour(body);
        return scene with
        {
            ElevationRegions = scene.ElevationRegions
                .Append(body)
                .OrderBy(static value => value.ElevationRegionId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static SceneDocument Remove(SceneDocument scene, string elevationRegionId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.ElevationRegions
            .Where(body => !string.Equals(
                body.ElevationRegionId,
                elevationRegionId,
                StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.ElevationRegions.Count
            ? scene
            : scene with { ElevationRegions = remaining };
    }

    /// <summary>
    /// Changes one body's absolute top without changing its identity or shape.
    /// A lower top is valid even when it raises nothing: hills never cut
    /// painted Terrain down, and a later edit may make the body effective again.
    /// </summary>
    public static SceneDocument SetElevation(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string elevationRegionId,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrWhiteSpace(elevationRegionId);
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Elevation region elevation {elevationMeters:0.############################} m must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var found = false;
        var bodies = scene.ElevationRegions.Select(body =>
        {
            if (!string.Equals(body.ElevationRegionId, elevationRegionId, StringComparison.Ordinal))
                return body;
            found = true;
            return body with { ElevationMeters = elevationMeters };
        }).ToList();
        if (!found)
        {
            throw new SceneMakerDocumentException(
                $"Elevation region '{elevationRegionId}' does not exist.");
        }
        return scene with { ElevationRegions = bodies };
    }

    /// <summary>
    /// Replaces one body's contour without changing its identity or top. The
    /// contour remains authored truth; its cells are derived again wherever
    /// the Scene is drawn or exported.
    /// </summary>
    public static SceneDocument Reshape(
        SceneDocument scene,
        string elevationRegionId,
        IReadOnlyList<ElevationRegionPointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(elevationRegionId);
        ArgumentNullException.ThrowIfNull(points);
        var body = scene.ElevationRegions.FirstOrDefault(candidate => string.Equals(
            candidate.ElevationRegionId, elevationRegionId, StringComparison.Ordinal));
        if (body is null)
        {
            throw new SceneMakerDocumentException(
                $"Elevation region '{elevationRegionId}' does not exist.");
        }

        var reshaped = body with { Points = [.. points] };
        _ = ElevationRegionGeometry.RequireContour(reshaped);
        return scene with
        {
            ElevationRegions = scene.ElevationRegions
                .Select(candidate => string.Equals(
                    candidate.ElevationRegionId, elevationRegionId, StringComparison.Ordinal)
                        ? reshaped
                        : candidate)
                .ToList(),
        };
    }

    /// <summary>
    /// The same reshape the edit commits, answered without throwing so a live
    /// point drag can show whether releasing it would succeed.
    /// </summary>
    public sealed record ElevationRegionReshape(
        SceneDocument? Scene,
        ElevationRegionDocument? Body,
        string? Reason);

    public static ElevationRegionReshape TryReshape(
        SceneDocument scene,
        string elevationRegionId,
        IReadOnlyList<ElevationRegionPointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            var reshapedScene = Reshape(scene, elevationRegionId, points);
            return new ElevationRegionReshape(
                reshapedScene,
                reshapedScene.ElevationRegions.Single(body => string.Equals(
                    body.ElevationRegionId, elevationRegionId, StringComparison.Ordinal)),
                Reason: null);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new ElevationRegionReshape(Scene: null, Body: null, exception.Message);
        }
    }

    /// <summary>
    /// The topmost hill body whose contour covers a Terrain cell.
    ///
    /// <para>The question is asked of the cell, not of the exact position
    /// inside it. Asking the contour whether it contains the pointer would use
    /// the same rule as the raster but a different sample point - the raster
    /// asks about a cell's centre - so near an edge the body the author sees
    /// filled and the body the pointer hits could differ by up to half a cell.
    /// Picking the cell first makes the two the same question.</para>
    ///
    /// <para>Among the bodies covering the cell the highest top wins, because
    /// that is the one an author would take themselves to be pointing at.
    /// Coverage is the whole question: a body over ground nobody painted has no
    /// visible surface there and is still picked, because it is still the body
    /// that is there. What it is holding up, if anything, is a second
    /// question - see <see cref="ElevationRegionGeometry.CellsRaisedBy"/>. Equal tops
    /// are settled by ordinal body ID, so the answer is the same every time
    /// rather than depending on document order.</para>
    /// </summary>
    public static ElevationRegionDocument? FindAtCell(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        TerrainCellCoordinate cell)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        return scene.ElevationRegions
            .Where(body => ElevationRegionGeometry.TerrainCells(scene, metrics, body).Contains(cell))
            .OrderBy(static body => body.ElevationMeters)
            .ThenBy(static body => body.ElevationRegionId, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// One attempt at authoring a hill: the Scene it would produce and the
    /// body it would add, or the reason it cannot be done.
    /// </summary>
    public sealed record ElevationRegionPlacement(
        SceneDocument? Scene,
        ElevationRegionDocument? Body,
        string? Reason);

    /// <summary>
    /// Everything that has to hold for a contour to become a hill, asked
    /// once and answered without throwing: the elevation sits on the Workspace
    /// quantum and the ring is a usable contour. That is the whole list - a
    /// hill no longer brings a material that could disagree with anything
    /// already in the Scene, so the fold has nothing left to refuse.
    ///
    /// <para>Both the preview and the key that commits it go through here. Two
    /// separate implementations of "would this work" is how a preview comes to
    /// promise something the commit then refuses.</para>
    /// </summary>
    public static ElevationRegionPlacement TryPlace(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<ElevationRegionPointDocument> points,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            var placed = Place(scene, metrics, points, elevationMeters);
            var authored = placed.ElevationRegions.Single(candidate => !scene.ElevationRegions.Any(
                existing => string.Equals(
                    existing.ElevationRegionId,
                    candidate.ElevationRegionId,
                    StringComparison.Ordinal)));
            return new ElevationRegionPlacement(placed, authored, Reason: null);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new ElevationRegionPlacement(Scene: null, Body: null, exception.Message);
        }
    }

    /// <summary>
    /// Resolves a closed draft into stored Bezier points. Unlike an open river,
    /// every aligned point has two neighbours: the last and first points are
    /// neighbours across the closing edge too.
    /// </summary>
    public static IReadOnlyList<ElevationRegionPointDocument> ResolveContour(
        IReadOnlyList<ElevationRegionDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<ElevationRegionPointDocument> points = new(draft.Count);
        for (var index = 0; index < draft.Count; index++)
        {
            var current = draft[index];
            if (current.Mode == ElevationRegionPointMode.Linear)
            {
                points.Add(Point(current.X, current.Y));
                continue;
            }

            var previous = draft[(index + draft.Count - 1) % draft.Count];
            var next = draft[(index + 1) % draft.Count];
            var (handleIn, handleOut) = current.DraggedHandleOut is { } dragged
                ? Mirrored(Orient(dragged, current, previous))
                : AutomaticHandles(current, previous, next);
            points.Add(Point(
                current.X,
                current.Y,
                ElevationRegionPointMode.Aligned,
                handleIn: handleIn,
                handleOut: handleOut));
        }
        return points;
    }

    public static ElevationRegionPointDocument Point(
        int authoringX,
        int authoringY,
        ElevationRegionPointMode mode = ElevationRegionPointMode.Linear,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = authoringX, Y = authoringY },
        Mode = mode,
        HandleInAuthoringPx = mode == ElevationRegionPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = mode == ElevationRegionPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleOut ?? AuthoringPixelOffset.Zero,
    };

    /// <summary>
    /// Returns one contour with a point changed between a sharp linear corner
    /// and an aligned Bezier point. Turning alignment on supplies useful cyclic
    /// automatic handles; turning it off removes both handles.
    /// </summary>
    public static IReadOnlyList<ElevationRegionPointDocument> WithPointMode(
        IReadOnlyList<ElevationRegionPointDocument> points,
        int pointIndex,
        ElevationRegionPointMode mode)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (pointIndex < 0 || pointIndex >= points.Count)
            throw new ArgumentOutOfRangeException(nameof(pointIndex));

        var changed = points.ToList();
        var point = points[pointIndex];
        if (mode == ElevationRegionPointMode.Linear)
        {
            changed[pointIndex] = point with
            {
                Mode = mode,
                HandleInAuthoringPx = AuthoringPixelOffset.Zero,
                HandleOutAuthoringPx = AuthoringPixelOffset.Zero,
            };
            return changed;
        }

        var current = DraftOf(point);
        var previous = DraftOf(points[(pointIndex + points.Count - 1) % points.Count]);
        var next = DraftOf(points[(pointIndex + 1) % points.Count]);
        var (handleIn, handleOut) = AutomaticHandles(current, previous, next);
        changed[pointIndex] = point with
        {
            Mode = mode,
            HandleInAuthoringPx = handleIn,
            HandleOutAuthoringPx = handleOut,
        };
        return changed;
    }

    /// <summary>
    /// Moves one handle of an aligned point. The opposite handle keeps its
    /// length and turns to remain collinear, which is the persisted meaning of
    /// Aligned: one tangent with independently adjustable lengths.
    /// </summary>
    public static IReadOnlyList<ElevationRegionPointDocument> WithMovedHandle(
        IReadOnlyList<ElevationRegionPointDocument> points,
        int pointIndex,
        ElevationRegionHandleSide side,
        AuthoringPixelOffset offset)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(offset);
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        if (pointIndex < 0 || pointIndex >= points.Count)
            throw new ArgumentOutOfRangeException(nameof(pointIndex));
        var point = points[pointIndex];
        if (point.Mode != ElevationRegionPointMode.Aligned)
            throw new SceneMakerDocumentException(
                $"Elevation region point {pointIndex} is Linear and has no handles to move.");

        var changed = points.ToList();
        changed[pointIndex] = side == ElevationRegionHandleSide.In
            ? point with
            {
                HandleInAuthoringPx = offset,
                HandleOutAuthoringPx = OppositeAlong(
                    offset, point.HandleOutAuthoringPx),
            }
            : point with
            {
                HandleInAuthoringPx = OppositeAlong(
                    offset, point.HandleInAuthoringPx),
                HandleOutAuthoringPx = offset,
            };
        return changed;
    }

    private static ElevationRegionDraftPoint DraftOf(ElevationRegionPointDocument point) => new(
        point.PositionAuthoringPx.X,
        point.PositionAuthoringPx.Y,
        point.Mode);

    private static AuthoringPixelOffset OppositeAlong(
        AuthoringPixelOffset direction,
        AuthoringPixelOffset previousOpposite)
    {
        var directionLength = Math.Sqrt(
            (double)direction.X * direction.X + (double)direction.Y * direction.Y);
        var oppositeLength = Math.Sqrt(
            (double)previousOpposite.X * previousOpposite.X
            + (double)previousOpposite.Y * previousOpposite.Y);
        return directionLength <= 0.0 || oppositeLength <= 0.0
            ? previousOpposite
            : Offset(
                -direction.X / directionLength * oppositeLength,
                -direction.Y / directionLength * oppositeLength);
    }

    private static AuthoringPixelOffset Orient(
        AuthoringPixelOffset handleOut,
        ElevationRegionDraftPoint current,
        ElevationRegionDraftPoint previous)
    {
        var travelX = current.X - previous.X;
        var travelY = current.Y - previous.Y;
        return handleOut.X * travelX + handleOut.Y * travelY < 0
            ? new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }
            : handleOut;
    }

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) Mirrored(
        AuthoringPixelOffset handleOut) =>
        (new AuthoringPixelOffset { X = -handleOut.X, Y = -handleOut.Y }, handleOut);

    private static (AuthoringPixelOffset In, AuthoringPixelOffset Out) AutomaticHandles(
        ElevationRegionDraftPoint current,
        ElevationRegionDraftPoint previous,
        ElevationRegionDraftPoint next)
    {
        var tangentX = (double)(next.X - previous.X);
        var tangentY = (double)(next.Y - previous.Y);
        var tangentLength = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (tangentLength <= 0.0)
            return (Third(current, previous), Third(current, next));

        var incoming = Distance(current, previous) / 3.0;
        var outgoing = Distance(current, next) / 3.0;
        return (
            Offset(-tangentX / tangentLength * incoming, -tangentY / tangentLength * incoming),
            Offset(tangentX / tangentLength * outgoing, tangentY / tangentLength * outgoing));
    }

    private static AuthoringPixelOffset Third(ElevationRegionDraftPoint from, ElevationRegionDraftPoint to) =>
        Offset((to.X - from.X) / 3.0, (to.Y - from.Y) / 3.0);

    private static double Distance(ElevationRegionDraftPoint from, ElevationRegionDraftPoint to)
    {
        var deltaX = (double)(to.X - from.X);
        var deltaY = (double)(to.Y - from.Y);
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static AuthoringPixelOffset Offset(double x, double y) => new()
    {
        X = (int)Math.Round(x, MidpointRounding.AwayFromZero),
        Y = (int)Math.Round(y, MidpointRounding.AwayFromZero),
    };

    private static string NextElevationRegionId(SceneDocument scene)
    {
        const string prefix = "mountain_";
        HashSet<int> used = [];
        foreach (var body in scene.ElevationRegions)
        {
            if (!body.ElevationRegionId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    body.ElevationRegionId.AsSpan(prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                used.Add(index);
            }
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            if (used.Add(index)) return $"{prefix}{index:0000}";
        }
        throw new SceneMakerDocumentException("Scene has exhausted stable hill body IDs.");
    }
}
