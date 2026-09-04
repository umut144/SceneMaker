using System.Globalization;

namespace SceneMaker.Core;

/// <summary>One point of a closed mountain contour while it is being drawn.</summary>
public readonly record struct MountainDraftPoint(
    int X,
    int Y,
    MountainPointMode Mode,
    AuthoringPixelOffset? DraggedHandleOut = null);

/// <summary>Which Bezier handle of a mountain point is being edited.</summary>
public enum MountainHandleSide
{
    In,
    Out,
}

/// <summary>Pure edits for closed, level-topped mountain bodies.</summary>
public static class MountainEditing
{
    /// <summary>
    /// Authors one closed contour at one absolute top. No Asset is asked for: a
    /// mountain raises painted Terrain and the painted cell keeps saying what
    /// the surface is made of.
    /// </summary>
    public static SceneDocument Place(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<MountainCurvePointDocument> points,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(points);
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Mountain elevation {elevationMeters:0.############################} m must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var body = new MountainBodyDocument
        {
            MountainBodyId = NextMountainBodyId(scene),
            ElevationMeters = elevationMeters,
            Points = [.. points],
        };
        _ = MountainGeometry.RequireContour(body);
        return scene with
        {
            MountainBodies = scene.MountainBodies
                .Append(body)
                .OrderBy(static value => value.MountainBodyId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static SceneDocument Remove(SceneDocument scene, string mountainBodyId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.MountainBodies
            .Where(body => !string.Equals(
                body.MountainBodyId,
                mountainBodyId,
                StringComparison.Ordinal))
            .ToList();
        return remaining.Count == scene.MountainBodies.Count
            ? scene
            : scene with { MountainBodies = remaining };
    }

    /// <summary>
    /// Changes one body's absolute top without changing its identity or shape.
    /// A lower top is valid even when it raises nothing: mountains never cut
    /// painted Terrain down, and a later edit may make the body effective again.
    /// </summary>
    public static SceneDocument SetElevation(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        string mountainBodyId,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountainBodyId);
        if (!metrics.IsElevationAligned(elevationMeters))
        {
            throw new SceneMakerDocumentException(
                FormattableString.Invariant(
                    $"Mountain elevation {elevationMeters:0.############################} m must align to the Workspace elevation quantum of {metrics.ElevationQuantumMeters:0.############################} m."));
        }

        var found = false;
        var bodies = scene.MountainBodies.Select(body =>
        {
            if (!string.Equals(body.MountainBodyId, mountainBodyId, StringComparison.Ordinal))
                return body;
            found = true;
            return body with { ElevationMeters = elevationMeters };
        }).ToList();
        if (!found)
        {
            throw new SceneMakerDocumentException(
                $"Mountain body '{mountainBodyId}' does not exist.");
        }
        return scene with { MountainBodies = bodies };
    }

    /// <summary>
    /// Replaces one body's contour without changing its identity or top. The
    /// contour remains authored truth; its cells are derived again wherever
    /// the Scene is drawn or exported.
    /// </summary>
    public static SceneDocument Reshape(
        SceneDocument scene,
        string mountainBodyId,
        IReadOnlyList<MountainCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountainBodyId);
        ArgumentNullException.ThrowIfNull(points);
        var body = scene.MountainBodies.FirstOrDefault(candidate => string.Equals(
            candidate.MountainBodyId, mountainBodyId, StringComparison.Ordinal));
        if (body is null)
        {
            throw new SceneMakerDocumentException(
                $"Mountain body '{mountainBodyId}' does not exist.");
        }

        var reshaped = body with { Points = [.. points] };
        _ = MountainGeometry.RequireContour(reshaped);
        return scene with
        {
            MountainBodies = scene.MountainBodies
                .Select(candidate => string.Equals(
                    candidate.MountainBodyId, mountainBodyId, StringComparison.Ordinal)
                        ? reshaped
                        : candidate)
                .ToList(),
        };
    }

    /// <summary>
    /// The same reshape the edit commits, answered without throwing so a live
    /// point drag can show whether releasing it would succeed.
    /// </summary>
    public sealed record MountainReshape(
        SceneDocument? Scene,
        MountainBodyDocument? Body,
        string? Reason);

    public static MountainReshape TryReshape(
        SceneDocument scene,
        string mountainBodyId,
        IReadOnlyList<MountainCurvePointDocument> points)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            var reshapedScene = Reshape(scene, mountainBodyId, points);
            return new MountainReshape(
                reshapedScene,
                reshapedScene.MountainBodies.Single(body => string.Equals(
                    body.MountainBodyId, mountainBodyId, StringComparison.Ordinal)),
                Reason: null);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new MountainReshape(Scene: null, Body: null, exception.Message);
        }
    }

    /// <summary>
    /// The topmost mountain body whose contour covers a Terrain cell.
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
    /// question - see <see cref="MountainGeometry.CellsRaisedBy"/>. Equal tops
    /// are settled by ordinal body ID, so the answer is the same every time
    /// rather than depending on document order.</para>
    /// </summary>
    public static MountainBodyDocument? FindAtCell(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        TerrainCellCoordinate cell)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        return scene.MountainBodies
            .Where(body => MountainGeometry.TerrainCells(scene, metrics, body).Contains(cell))
            .OrderBy(static body => body.ElevationMeters)
            .ThenBy(static body => body.MountainBodyId, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// One attempt at authoring a mountain: the Scene it would produce and the
    /// body it would add, or the reason it cannot be done.
    /// </summary>
    public sealed record MountainPlacement(
        SceneDocument? Scene,
        MountainBodyDocument? Body,
        string? Reason);

    /// <summary>
    /// Everything that has to hold for a contour to become a mountain, asked
    /// once and answered without throwing: the elevation sits on the Workspace
    /// quantum and the ring is a usable contour. That is the whole list - a
    /// mountain no longer brings a material that could disagree with anything
    /// already in the Scene, so the fold has nothing left to refuse.
    ///
    /// <para>Both the preview and the key that commits it go through here. Two
    /// separate implementations of "would this work" is how a preview comes to
    /// promise something the commit then refuses.</para>
    /// </summary>
    public static MountainPlacement TryPlace(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        IReadOnlyList<MountainCurvePointDocument> points,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            var placed = Place(scene, metrics, points, elevationMeters);
            var authored = placed.MountainBodies.Single(candidate => !scene.MountainBodies.Any(
                existing => string.Equals(
                    existing.MountainBodyId,
                    candidate.MountainBodyId,
                    StringComparison.Ordinal)));
            return new MountainPlacement(placed, authored, Reason: null);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new MountainPlacement(Scene: null, Body: null, exception.Message);
        }
    }

    /// <summary>
    /// Resolves a closed draft into stored Bezier points. Unlike an open river,
    /// every aligned point has two neighbours: the last and first points are
    /// neighbours across the closing edge too.
    /// </summary>
    public static IReadOnlyList<MountainCurvePointDocument> ResolveContour(
        IReadOnlyList<MountainDraftPoint> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<MountainCurvePointDocument> points = new(draft.Count);
        for (var index = 0; index < draft.Count; index++)
        {
            var current = draft[index];
            if (current.Mode == MountainPointMode.Linear)
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
                MountainPointMode.Aligned,
                handleIn: handleIn,
                handleOut: handleOut));
        }
        return points;
    }

    public static MountainCurvePointDocument Point(
        int authoringX,
        int authoringY,
        MountainPointMode mode = MountainPointMode.Linear,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = authoringX, Y = authoringY },
        Mode = mode,
        HandleInAuthoringPx = mode == MountainPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = mode == MountainPointMode.Linear
            ? AuthoringPixelOffset.Zero
            : handleOut ?? AuthoringPixelOffset.Zero,
    };

    /// <summary>
    /// Returns one contour with a point changed between a sharp linear corner
    /// and an aligned Bezier point. Turning alignment on supplies useful cyclic
    /// automatic handles; turning it off removes both handles.
    /// </summary>
    public static IReadOnlyList<MountainCurvePointDocument> WithPointMode(
        IReadOnlyList<MountainCurvePointDocument> points,
        int pointIndex,
        MountainPointMode mode)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (pointIndex < 0 || pointIndex >= points.Count)
            throw new ArgumentOutOfRangeException(nameof(pointIndex));

        var changed = points.ToList();
        var point = points[pointIndex];
        if (mode == MountainPointMode.Linear)
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
    public static IReadOnlyList<MountainCurvePointDocument> WithMovedHandle(
        IReadOnlyList<MountainCurvePointDocument> points,
        int pointIndex,
        MountainHandleSide side,
        AuthoringPixelOffset offset)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(offset);
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        if (pointIndex < 0 || pointIndex >= points.Count)
            throw new ArgumentOutOfRangeException(nameof(pointIndex));
        var point = points[pointIndex];
        if (point.Mode != MountainPointMode.Aligned)
            throw new SceneMakerDocumentException(
                $"Mountain point {pointIndex} is Linear and has no handles to move.");

        var changed = points.ToList();
        changed[pointIndex] = side == MountainHandleSide.In
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

    private static MountainDraftPoint DraftOf(MountainCurvePointDocument point) => new(
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
        MountainDraftPoint current,
        MountainDraftPoint previous)
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
        MountainDraftPoint current,
        MountainDraftPoint previous,
        MountainDraftPoint next)
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

    private static AuthoringPixelOffset Third(MountainDraftPoint from, MountainDraftPoint to) =>
        Offset((to.X - from.X) / 3.0, (to.Y - from.Y) / 3.0);

    private static double Distance(MountainDraftPoint from, MountainDraftPoint to)
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

    private static string NextMountainBodyId(SceneDocument scene)
    {
        const string prefix = "mountain_";
        HashSet<int> used = [];
        foreach (var body in scene.MountainBodies)
        {
            if (!body.MountainBodyId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(
                    body.MountainBodyId.AsSpan(prefix.Length),
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
        throw new SceneMakerDocumentException("Scene has exhausted stable mountain body IDs.");
    }
}
