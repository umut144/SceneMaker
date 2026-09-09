using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>A pointer position in authoring pixels.</summary>
public readonly record struct AuthoringPoint(int X, int Y);

/// <summary>How a previewed Prop would fare if it were authored right now.</summary>
public enum PropPreviewKind
{
    /// <summary>Can be authored here.</summary>
    Ready,

    /// <summary>Cannot be authored here at all.</summary>
    Blocked,
}

/// <summary>
/// What the Placement tool would author under the pointer. It carries both
/// boxes because they answer different questions: <see cref="Bounds"/> is what
/// would be drawn, <see cref="Collision"/> is what would be occupied and thus
/// what <see cref="Kind"/> was decided by. Showing only the first would leave
/// a refusal looking arbitrary.
///
/// <para><see cref="Collision"/> is absent when the Asset occupies nothing.
/// That is not a missing answer: it is the answer, and it is why such a
/// Placement can never be refused for standing somewhere.</para>
/// </summary>
public sealed record PropPreview(
    AuthoringPoint Anchor,
    PropBoundsAuthoringPixels Bounds,
    PropBoundsAuthoringPixels? Collision,
    PropPreviewKind Kind,
    string? Explanation);

/// <summary>
/// The river being drawn: the points placed so far including the one still
/// being placed, the curve they describe, and the cells that curve would cover.
/// The last two are empty until there are two points, because one point is not
/// yet a curve.
/// </summary>
public sealed record WaterDraftPreview(
    IReadOnlyList<WaterDraftPoint> Points,
    IReadOnlyList<WaterCurvePointDocument> Curve,
    IReadOnlyList<ChainPoint> Centerline,
    IReadOnlyList<WaterCellSpan> Cells)
{
    public static WaterDraftPreview Empty { get; } = new([], [], [], []);
}

/// <summary>
/// The selected river as it currently stands, which during a drag is not where
/// the document has it. It carries the candidate itself, so the Canvas draws
/// one thing and the commit takes the same thing.
///
/// <para><see cref="Broken"/> is what a stated fork no longer supports. It is
/// not a refusal - the move is taken either way - it is the sentence the author
/// has to see before an export says it hours later.</para>
/// </summary>
public sealed record WaterSelectionPreview(
    WaterBodyDocument? Body,
    IReadOnlyList<ChainPoint> Centerline,
    IReadOnlyList<WaterCellSpan> Cells,
    IReadOnlyList<BrokenJunction> Broken,
    bool Detached)
{
    public static WaterSelectionPreview Empty { get; } = new(null, [], [], [], false);
}

/// <summary>
/// The open Path being drawn. A prepared surface is present only when at least
/// two points form valid route geometry; the same value is used by the canvas
/// and by Enter.
/// </summary>
public sealed record RouteDraftPreview(
    IReadOnlyList<GradedRouteDraftPoint> Points,
    IReadOnlyList<RouteSurfacePointDocument> Curve,
    PreparedRouteSurface? Surface,
    string? Explanation)
{
    public static RouteDraftPreview Empty { get; } = new([], [], null, null);
}

/// <summary>
/// What the hill draft on the canvas would do if it were committed now.
///
/// <para><c>Incomplete</c> is not a refusal: the author has simply not placed
/// enough points yet, and the drawing so far is neither promised nor refused.
/// <c>Ready</c> is a promise - the same attempt the commit makes has already
/// succeeded against this Scene and height. <c>Blocked</c> carries the reason
/// it would fail.</para>
/// </summary>
public enum ElevationRegionDraftKind
{
    Incomplete,
    Ready,
    Blocked,
}

/// <summary>
/// The closed hill contour being drawn.
///
/// <para><see cref="RaisedCells"/> is the difference the commit would make, not
/// the ground the contour covers: only painted cells whose visible height would
/// actually rise. Each carries the Asset it already has, so whoever draws the
/// fill paints the material that is really there and never works it out a second
/// time. A contour over unpainted ground, over cells that already stand as high,
/// or under a taller body raises nothing and fills nothing.</para>
///
/// <para><see cref="Explanation"/> carries the reason a draft is Incomplete or
/// Blocked - and, on a Ready draft that raises nothing, the note that says so.
/// </para>
/// </summary>
public sealed record ElevationRegionDraftPreview(
    IReadOnlyList<ElevationRegionDraftPoint> Points,
    IReadOnlyList<ElevationRegionPointDocument> Curve,
    IReadOnlyList<ChainPoint> Outline,
    IReadOnlyList<TerrainCellDocument> RaisedCells,
    ElevationRegionDraftKind Kind,
    string? Explanation)
{
    public static ElevationRegionDraftPreview Empty { get; } = new(
        [], [], [], [], ElevationRegionDraftKind.Incomplete, ToolPreviewBuilder.IncompleteElevationRegionDraft);
}

/// <summary>
/// The hill body the eraser would remove, and separately what removing it
/// would change.
///
/// <para>The two are not the same and must not be drawn as if they were: the
/// body is the whole contour, including the part of it over unpainted ground,
/// while <see cref="LoweredCells"/> is only the painted cells whose height this
/// body is currently holding up. The ID picks the contour out of the outlines
/// the canvas already has; the cells say what actually drops.</para>
/// </summary>
public sealed record ElevationRegionEraserPreview(
    string? ElevationRegionId,
    IReadOnlyList<TerrainCellDocument> LoweredCells)
{
    public static ElevationRegionEraserPreview Empty { get; } = new(null, []);
}

/// <summary>
/// The selected hill and the contour currently proposed while one of its
/// authored points is dragged. A blocked proposal keeps its outline visible so
/// the author can see and undo the bad shape; releasing it does not edit the
/// document.
/// </summary>
public sealed record ElevationRegionSelectionPreview(
    ElevationRegionDocument? Body,
    IReadOnlyList<ChainPoint> Outline,
    ElevationRegionDraftKind Kind,
    string? Explanation)
{
    public static ElevationRegionSelectionPreview Empty { get; } = new(
        null, [], ElevationRegionDraftKind.Incomplete, null);
}

public sealed record TerrainPreview(
    IReadOnlyList<TerrainCellCoordinate> Cells,
    bool Erasing)
{
    public static TerrainPreview Empty { get; } = new([], false);
}

/// <summary>
/// What the active tool would do if it acted where the pointer currently is.
///
/// Deciding what is previewed, and whether it could be authored, used to live in
/// the canvas next to the drawing code, which made it unreachable from a test.
/// These are pure functions of the Scene and the pointer state; the canvas only
/// turns the result into rectangles.
/// </summary>
/// <summary>
/// The bridge being drawn. <c>Incomplete</c> means only one end is fixed and
/// nothing has been asked yet; <c>Ready</c> is a promise, because the same call
/// the commit makes has already succeeded against this Scene; <c>Blocked</c>
/// carries the reason, which is usually a corner that cannot stand.
/// </summary>
public enum BridgeDraftKind
{
    Incomplete,
    Ready,
    Blocked,
}

/// <summary>
/// What the second click would author. The deck's planks and the four posts
/// are both derived here rather than in the Canvas, so what an author sees is
/// what the placement rule read - including how the planks come out, which is
/// the whole reason a count is worth turning while the draft is up.
/// </summary>
public sealed record BridgeDraftPreview(
    AuthoringPoint? Start,
    AuthoringPoint? End,
    IReadOnlyList<BridgeCorner> Corners,
    IReadOnlyList<PropBoundsAuthoringPixels> Posts,
    IReadOnlyList<BridgePlank> Planks,
    decimal LengthMeters,
    BridgeDraftKind Kind,
    string? Explanation)
{
    public static BridgeDraftPreview Empty { get; } = new(
        null, null, [], [], [], 0m, BridgeDraftKind.Incomplete, null);
}

/// <summary>
/// The selected bridge as it currently stands, which during a drag is not
/// where the document has it. It carries the candidate itself so the Canvas
/// draws one thing and the commit takes the same thing.
/// </summary>
public sealed record BridgeSelectionPreview(
    BridgeDocument? Bridge,
    IReadOnlyList<BridgeCorner> Corners,
    IReadOnlyList<PropBoundsAuthoringPixels> Posts,
    IReadOnlyList<BridgePlank> Planks,
    decimal LengthMeters,
    BridgeDraftKind Kind,
    string? Explanation)
{
    public static BridgeSelectionPreview Empty { get; } = new(
        null, [], [], [], 0m, BridgeDraftKind.Incomplete, null);
}

public static class ToolPreviewBuilder
{
    /// <summary>
    /// The Terrain cells the tool would touch. <paramref name="lineStart"/> is
    /// only set while a Terrain line is being dragged. Cells outside the Scene
    /// are dropped, because they cannot be painted.
    /// </summary>
    public static TerrainPreview BuildTerrain(
        SceneDocument scene,
        EditorTool tool,
        bool eraserEnabled,
        TerrainCellCoordinate? pointer,
        TerrainCellCoordinate? lineStart)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (tool is not (EditorTool.Pencil or EditorTool.Line)) return TerrainPreview.Empty;
        if (pointer is not { } cell) return TerrainPreview.Empty;

        IReadOnlyList<TerrainCellCoordinate> cells = tool == EditorTool.Line && lineStart is { } start
            ? TerrainEditing.LineCells(start.X, start.Y, cell.X, cell.Y)
            : [cell];

        return new TerrainPreview(
            cells.Where(value => IsInsideScene(scene, value)).ToList(),
            eraserEnabled);
    }

    /// <summary>
    /// The Props the tool would author. With the Line tool and a fixed start
    /// point this is the whole run of anchors; otherwise it is the single Prop
    /// under the pointer.
    /// </summary>
    public static IReadOnlyList<PropPreview> BuildProps(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        string? selectedAssetKey,
        EditorTool tool,
        AuthoringPoint? pointer,
        AuthoringPoint? lineStart,
        AuthoringPoint? lineEnd,
        int lineOffsetAuthoringPixels)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        if (selectedAssetKey is null) return [];
        if (tool is not (EditorTool.Pencil or EditorTool.Line)) return [];

        var asset = propAssets.Resolve(selectedAssetKey);
        var anchors = Anchors(asset, tool, pointer, lineStart, lineEnd, lineOffsetAuthoringPixels);

        List<PropPreview> previews = new(anchors.Count);
        foreach (var anchor in anchors)
        {
            PropBoundsAuthoringPixels bounds;
            PropBoundsAuthoringPixels? collision;
            try
            {
                bounds = PropEditing.BoundsFor(asset, anchor.X, anchor.Y);
                collision = PropEditing.CollisionBoundsFor(asset, anchor.X, anchor.Y);
            }
            catch (OverflowException)
            {
                // Off the supported coordinate range; there is nothing to draw.
                continue;
            }

            var validation = PropEditing.ValidateCandidate(
                scene,
                propAssets,
                anchor.X,
                anchor.Y,
                selectedAssetKey);
            previews.Add(new PropPreview(
                anchor,
                bounds,
                collision,
                validation.IsValid ? PropPreviewKind.Ready : PropPreviewKind.Blocked,
                validation.Reason));
        }
        return previews;
    }

    /// <summary>
    /// What the river being drawn would author. It resolves the handles and
    /// rasterizes with the same Core code the edit itself uses, so the corridor
    /// the author sees is the corridor they get.
    /// </summary>
    public static WaterDraftPreview BuildWaterDraft(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        IReadOnlyList<WaterDraftPoint> draft,
        WaterDraftPoint? pending)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(draft);
        if (tool is not (EditorTool.DrawRiver or EditorTool.CreateBranch))
            return WaterDraftPreview.Empty;

        List<WaterDraftPoint> points = [.. draft];
        // The pending point can still be sitting on the previous one for as
        // long as the pointer has not moved off it, and a curve with no length
        // has no centerline.
        if (pending is { } value
            && (points.Count == 0 || points[^1].X != value.X || points[^1].Y != value.Y))
        {
            points.Add(value);
        }
        if (points.Count == 0) return WaterDraftPreview.Empty;

        // The handles are resolved even for a single point, so that the one
        // being dragged out is drawn while it is being dragged.
        var curve = WaterEditing.ResolveCurve(points);
        if (points.Count < 2) return new WaterDraftPreview(points, curve, [], []);

        return new WaterDraftPreview(
            points,
            curve,
            WaterGeometry.Centerline(curve),
            WaterGeometry.Corridor(scene, metrics, curve));
    }

    /// <summary>
    /// The selected river exactly as stored, or the complete reshape a drag
    /// would commit. The corridor comes from the same rule the export rasters
    /// with, so what the author sees highlighted is the set of cells the body
    /// actually claims.
    /// </summary>
    public static WaterSelectionPreview BuildWaterSelection(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        string? selectedWaterBodyId,
        IReadOnlyList<WaterCurvePointDocument>? candidatePoints)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        if (tool != EditorTool.SelectRiver || selectedWaterBodyId is null)
            return WaterSelectionPreview.Empty;

        var stored = scene.WaterBodies.FirstOrDefault(body => string.Equals(
            body.WaterBodyId, selectedWaterBodyId, StringComparison.Ordinal));
        if (stored is null) return WaterSelectionPreview.Empty;

        var candidate = candidatePoints is null ? stored : stored with { Points = [.. candidatePoints] };
        var scope = candidatePoints is null ? scene : Replaced(scene, candidate);
        try
        {
            return new WaterSelectionPreview(
                candidate,
                WaterGeometry.Centerline(candidate.Points),
                WaterGeometry.Corridor(scene, metrics, candidate.Points),
                // Only what this body states. The document-wide list belongs in
                // a status line; used as a colour it turned every river red as
                // soon as one of them broke.
                WaterEditing.BrokenJunctions(scope, metrics)
                    .Where(entry => string.Equals(
                        entry.WaterBodyId, candidate.WaterBodyId, StringComparison.Ordinal))
                    .ToList(),
                !WaterAttachment.IsAttached(scope, metrics, candidate));
        }
        catch (SceneMakerDocumentException)
        {
            // A curve that is momentarily not a curve - two points on top of
            // each other during a drag - is a drag passing through, not a
            // refusal. The points are still drawn; the corridor is not.
            return new WaterSelectionPreview(candidate, [], [], [], false);
        }
    }

    private static SceneDocument Replaced(SceneDocument scene, WaterBodyDocument body) => scene with
    {
        WaterBodies = scene.WaterBodies
            .Select(candidate => string.Equals(
                candidate.WaterBodyId, body.WaterBodyId, StringComparison.Ordinal)
                    ? body
                    : candidate)
            .ToList(),
    };

    public static RouteDraftPreview BuildRouteDraft(
        WorkspaceMetrics metrics,
        EditorTool tool,
        decimal? startElevationMeters,
        IReadOnlyList<GradedRouteDraftPoint> draft,
        GradedRouteDraftPoint? pending)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(draft);
        if (tool != EditorTool.DrawPath) return RouteDraftPreview.Empty;

        List<GradedRouteDraftPoint> points = [.. draft];
        if (pending is { } value
            && (points.Count == 0
                || points[^1].X != value.X
                || points[^1].Y != value.Y
                || value.DraggedHandleOut is not null))
        {
            points.Add(value);
        }
        if (points.Count == 0) return RouteDraftPreview.Empty;

        if (startElevationMeters is not { } start)
        {
            return new RouteDraftPreview(
                points,
                [],
                null,
                "a Path needs a starting elevation");
        }
        try
        {
            var curve = RouteSurfaceEditing.ResolveGradedCurve(metrics, start, points);
            if (points.Count < 2)
            {
                return new RouteDraftPreview(
                    points,
                    curve,
                    null,
                    "a Path needs at least two points");
            }
            return new RouteDraftPreview(
                points,
                curve,
                RouteSurfaceGeometry.Prepare(metrics, curve),
                null);
        }
        catch (SceneMakerDocumentException exception)
        {
            return new RouteDraftPreview(points, [], null, exception.Message);
        }
    }

    /// <summary>The points the canvas tool asks for before a contour may close.</summary>
    public const int MinimumElevationRegionDraftPoints = 3;

    /// <summary>
    /// Why a draft is not ready yet. It is a count the drawing tool asks for
    /// rather than a geometric rule: two anchors whose handles bow the closing
    /// edges apart already enclose an area, and the document accepts that. What
    /// the canvas cannot offer is a way to author those handles without a third
    /// click to aim them, so the tool asks for three placed points.
    /// </summary>
    public const string IncompleteElevationRegionDraft = "a contour needs at least three points.";

    /// <summary>
    /// Said about a contour that is perfectly authorable and would, right now,
    /// lift nothing: it covers no painted Terrain, or none that is not already
    /// standing at least as high. The body is still worth authoring - painting
    /// under it later puts it to work.
    ///
    /// <para>Two shapes of the one clause: one to append to a sentence that has
    /// already named the body, and one that stands on its own as the draft's
    /// explanation. They say the same thing, so a reader who saw one recognises
    /// the other.</para>
    /// </summary>
    public const string RaisesNoTerrain = "currently raises no Terrain cells.";

    public const string ValidButRaisesNoTerrain =
        "Hill is valid but " + RaisesNoTerrain;

    /// <summary>
    /// What the hill draft would author, and whether Enter would take it.
    ///
    /// <para>Ready means the whole attempt has already been made against this
    /// Scene and height and succeeded - the height sits on the Workspace
    /// quantum and the ring is a usable contour. Anything the commit would
    /// refuse is Blocked here with the same sentence, because both ask
    /// <see cref="ElevationRegionEditing.TryPlace"/> and neither decides anything of
    /// its own. A Ready contour that would lift nothing stays Ready and says so:
    /// it is authorable, and refusing it would refuse a body the author means to
    /// paint under later.</para>
    /// </summary>
    public static ElevationRegionDraftPreview BuildElevationRegionDraft(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        IReadOnlyList<ElevationRegionDraftPoint> draft,
        ElevationRegionDraftPoint? pending,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(draft);
        if (tool != EditorTool.DrawElevationRegion) return ElevationRegionDraftPreview.Empty;

        List<ElevationRegionDraftPoint> points = [.. draft];
        if (pending is { } value
            && (points.Count == 0 || points[^1].X != value.X || points[^1].Y != value.Y))
        {
            points.Add(value);
        }
        if (points.Count == 0) return ElevationRegionDraftPreview.Empty;

        var curve = ElevationRegionEditing.ResolveContour(points);
        if (points.Count < MinimumElevationRegionDraftPoints)
        {
            return new ElevationRegionDraftPreview(
                points, curve, [], [], ElevationRegionDraftKind.Incomplete, IncompleteElevationRegionDraft);
        }

        // Drawn from the resolved curve alone, so a contour that crosses itself
        // is still visible while it is being fixed.
        var outline = ElevationRegionGeometry.Flatten(new ElevationRegionDocument
        {
            ElevationRegionId = "mountain_preview",
            ElevationMeters = 0m,
            Points = [.. curve],
        }).Points;

        var placement = ElevationRegionEditing.TryPlace(scene, metrics, curve, elevationMeters);
        if (placement is not { Body: { } body })
        {
            return new ElevationRegionDraftPreview(
                points, curve, outline, [], ElevationRegionDraftKind.Blocked, placement.Reason);
        }

        // Asked of the Scene the body is not in yet, which is what makes this
        // the difference the commit would make rather than the ground it covers.
        var raised = ElevationRegionGeometry.CellsRaisedBy(scene, metrics, body);
        return new ElevationRegionDraftPreview(
            points,
            curve,
            outline,
            raised,
            ElevationRegionDraftKind.Ready,
            raised.Count == 0 ? ValidButRaisesNoTerrain : null);
    }

    /// <summary>
    /// The hill body the eraser would remove where the pointer is, and the
    /// painted cells that would drop with it - so the author sees the whole
    /// body go before the click rather than the one cell under the cursor.
    ///
    /// <para>The two are answered separately on purpose. The body is picked by
    /// the contour covering the cell, whether or not anything is painted there;
    /// the cells are only those whose height this body is currently holding
    /// up. A body can therefore be picked and lower nothing, which is why the
    /// canvas draws its outline as well as the cells.</para>
    /// </summary>
    /// <summary>
    /// The bridge under the pointer, from the end already fixed. Everything it
    /// reports comes from the same calls the commit makes: a Ready draft cannot
    /// then be refused, and a Blocked one says which corner is in the way.
    /// </summary>
    public static BridgeDraftPreview BuildBridgeDraft(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        EditorTool tool,
        AuthoringPoint? start,
        AuthoringPoint? pointer,
        string? plankAssetKey,
        string? anchorAssetKey,
        decimal widthMeters,
        decimal elevationMeters,
        int plankCount,
        decimal plankGapMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        if (tool != EditorTool.DrawBridge) return BridgeDraftPreview.Empty;
        if (start is not { } fixedEnd) return BridgeDraftPreview.Empty;
        if (pointer is not { } end || plankAssetKey is null || anchorAssetKey is null)
        {
            return BridgeDraftPreview.Empty with { Start = fixedEnd };
        }

        // Two ends in one place is not a refusal to show in red; it is a
        // bridge the author has not finished asking for.
        if (fixedEnd.X == end.X && fixedEnd.Y == end.Y)
            return BridgeDraftPreview.Empty with { Start = fixedEnd, End = end };

        var candidate = new BridgeDocument
        {
            BridgeId = "bridge_preview",
            PlankAssetKey = plankAssetKey,
            AnchorAssetKey = anchorAssetKey,
            StartAuthoringPx = new AuthoringPixelPosition { X = fixedEnd.X, Y = fixedEnd.Y },
            EndAuthoringPx = new AuthoringPixelPosition { X = end.X, Y = end.Y },
            WidthMeters = widthMeters,
            ElevationMeters = elevationMeters,
            PlankCount = plankCount,
            PlankGapMeters = plankGapMeters,
        };
        var validation = BridgeEditing.ValidateCandidate(
            scene,
            propAssets,
            fixedEnd.X,
            fixedEnd.Y,
            end.X,
            end.Y,
            plankAssetKey,
            anchorAssetKey,
            widthMeters,
            elevationMeters,
            plankCount,
            plankGapMeters);

        IReadOnlyList<BridgeCorner> corners;
        IReadOnlyList<PropBoundsAuthoringPixels> posts;
        IReadOnlyList<BridgePlank> planks;
        try
        {
            corners = BridgeGeometry.Corners(propAssets.Metrics, candidate);
            posts = BridgeEditing.PostBounds(propAssets.Metrics, propAssets, candidate);
            planks = BridgeGeometry.Planks(propAssets.Metrics, candidate).Planks;
        }
        catch (SceneMakerDocumentException)
        {
            // Geometry that cannot be drawn is geometry that cannot be placed,
            // and the validation above already carries the reason.
            corners = [];
            posts = [];
            planks = [];
        }

        var pixelsPerMeter = (double)propAssets.Metrics.AuthoringPixelsPerMeter;
        var deltaX = (end.X - fixedEnd.X) / pixelsPerMeter;
        var deltaY = (end.Y - fixedEnd.Y) / pixelsPerMeter;
        var length = Math.Round(
            (decimal)Math.Sqrt(deltaX * deltaX + deltaY * deltaY),
            3,
            MidpointRounding.AwayFromZero);

        return new BridgeDraftPreview(
            fixedEnd,
            end,
            corners,
            posts,
            planks,
            length,
            validation.IsValid ? BridgeDraftKind.Ready : BridgeDraftKind.Blocked,
            validation.Reason);
    }

    /// <summary>
    /// The selected bridge, moved to where the drag currently has it. Nothing
    /// is drawn without a selection, and a bridge that is merely selected is
    /// always Ready - it is already in the document, so the question a draft
    /// asks does not apply until something moves.
    /// </summary>
    public static BridgeSelectionPreview BuildBridgeSelection(
        SceneDocument scene,
        PropDisplayCatalog propAssets,
        EditorTool tool,
        string? selectedBridgeId,
        (AuthoringPixelPosition Start, AuthoringPixelPosition End)? dragged)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(propAssets);
        if (tool != EditorTool.SelectBridge) return BridgeSelectionPreview.Empty;
        if (selectedBridgeId is null) return BridgeSelectionPreview.Empty;
        var stored = scene.Bridges.FirstOrDefault(bridge =>
            string.Equals(bridge.BridgeId, selectedBridgeId, StringComparison.Ordinal));
        if (stored is null) return BridgeSelectionPreview.Empty;

        var candidate = dragged is { } ends
            ? stored with { StartAuthoringPx = ends.Start, EndAuthoringPx = ends.End }
            : stored;

        // Two ends in one place is a drag passing through, not a refusal: it is
        // drawn as an unfinished move rather than in red.
        if (candidate.StartAuthoringPx == candidate.EndAuthoringPx)
            return BridgeSelectionPreview.Empty with { Bridge = candidate };

        var validation = dragged is null
            ? BridgeValidationResult.Valid
            : BridgeEditing.ValidateReshape(
                scene,
                propAssets,
                selectedBridgeId,
                candidate.StartAuthoringPx.X,
                candidate.StartAuthoringPx.Y,
                candidate.EndAuthoringPx.X,
                candidate.EndAuthoringPx.Y,
                candidate.WidthMeters,
                candidate.ElevationMeters,
                candidate.PlankCount,
                candidate.PlankGapMeters);

        IReadOnlyList<BridgeCorner> corners;
        IReadOnlyList<PropBoundsAuthoringPixels> posts;
        IReadOnlyList<BridgePlank> planks;
        decimal length;
        try
        {
            corners = BridgeGeometry.Corners(propAssets.Metrics, candidate);
            posts = BridgeEditing.PostBounds(propAssets.Metrics, propAssets, candidate);
            var layout = BridgeGeometry.Planks(propAssets.Metrics, candidate);
            planks = layout.Planks;
            length = layout.LengthMeters;
        }
        catch (SceneMakerDocumentException)
        {
            corners = [];
            posts = [];
            planks = [];
            length = 0m;
        }

        return new BridgeSelectionPreview(
            candidate,
            corners,
            posts,
            planks,
            length,
            validation.IsValid ? BridgeDraftKind.Ready : BridgeDraftKind.Blocked,
            validation.Reason);
    }

    public static ElevationRegionEraserPreview BuildElevationRegionEraser(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        bool eraserEnabled,
        TerrainCellCoordinate? pointer)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        if (tool != EditorTool.DrawElevationRegion || !eraserEnabled) return ElevationRegionEraserPreview.Empty;
        if (pointer is not { } cell) return ElevationRegionEraserPreview.Empty;
        if (ElevationRegionEditing.FindAtCell(scene, metrics, cell) is not { } body)
            return ElevationRegionEraserPreview.Empty;

        // The body is in the Scene, so this reads the other direction of the
        // same question: what it is holding up, and therefore what drops.
        return new ElevationRegionEraserPreview(
            body.ElevationRegionId,
            ElevationRegionGeometry.CellsRaisedBy(scene, metrics, body));
    }

    /// <summary>
    /// The selected hill exactly as stored, or the complete reshape that a
    /// point drag would commit. Validation is shared with the edit so a normal
    /// outline is a promise and a red outline is a refusal with the same reason.
    /// </summary>
    public static ElevationRegionSelectionPreview BuildElevationRegionSelection(
        SceneDocument scene,
        EditorTool tool,
        string? selectedElevationRegionId,
        IReadOnlyList<ElevationRegionPointDocument>? candidatePoints)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (tool != EditorTool.SelectElevationRegion || selectedElevationRegionId is null)
            return ElevationRegionSelectionPreview.Empty;

        var stored = scene.ElevationRegions.FirstOrDefault(body => string.Equals(
            body.ElevationRegionId, selectedElevationRegionId, StringComparison.Ordinal));
        if (stored is null) return ElevationRegionSelectionPreview.Empty;

        if (candidatePoints is null)
        {
            return new ElevationRegionSelectionPreview(
                stored,
                ElevationRegionGeometry.Flatten(stored).Points,
                ElevationRegionDraftKind.Ready,
                Explanation: null);
        }
        var points = candidatePoints.ToList();
        var candidate = stored with { Points = points };
        var outline = ElevationRegionGeometry.Flatten(candidate).Points;
        var reshape = ElevationRegionEditing.TryReshape(scene, stored.ElevationRegionId, points);
        return reshape.Body is { } accepted
            ? new ElevationRegionSelectionPreview(
                accepted, outline, ElevationRegionDraftKind.Ready, Explanation: null)
            : new ElevationRegionSelectionPreview(
                candidate, outline, ElevationRegionDraftKind.Blocked, reshape.Reason);
    }

    public static int CountOf(IReadOnlyList<PropPreview> previews, PropPreviewKind kind)
    {
        ArgumentNullException.ThrowIfNull(previews);
        var count = 0;
        foreach (var preview in previews)
        {
            if (preview.Kind == kind) count++;
        }
        return count;
    }

    private static IReadOnlyList<AuthoringPoint> Anchors(
        PropDisplayAsset asset,
        EditorTool tool,
        AuthoringPoint? pointer,
        AuthoringPoint? lineStart,
        AuthoringPoint? lineEnd,
        int lineOffsetAuthoringPixels)
    {
        if (tool == EditorTool.Line && lineStart is { } start)
        {
            if ((lineEnd ?? pointer) is not { } end) return [];
            return PropEditing.LineAnchors(
                    asset,
                    start.X,
                    start.Y,
                    end.X,
                    end.Y,
                    lineOffsetAuthoringPixels)
                .Select(static anchor => new AuthoringPoint(anchor.X, anchor.Y))
                .ToList();
        }
        return pointer is { } point ? [point] : [];
    }

    private static bool IsInsideScene(SceneDocument scene, TerrainCellCoordinate cell) =>
        cell.X >= 0 && cell.X < scene.SizeCells.Width
        && cell.Y >= 0 && cell.Y < scene.SizeCells.Height;
}
