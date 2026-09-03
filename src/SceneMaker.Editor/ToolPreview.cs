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

public sealed record PropPreview(
    AuthoringPoint Anchor,
    PropBoundsAuthoringPixels Bounds,
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
/// What the mountain draft on the canvas would do if it were committed now.
///
/// <para><c>Incomplete</c> is not a refusal: the author has simply not placed
/// enough points yet, and the drawing so far is neither promised nor refused.
/// <c>Ready</c> is a promise - the same attempt the commit makes has already
/// succeeded against this Scene, Asset and height. <c>Blocked</c> carries the
/// reason it would fail.</para>
/// </summary>
public enum MountainDraftKind
{
    Incomplete,
    Ready,
    Blocked,
}

/// <summary>
/// The closed mountain contour being drawn. <see cref="Cells"/> is filled only
/// for <see cref="MountainDraftKind.Ready"/>, because a fill is a picture of
/// what the author would get and a blocked draft gets nothing;
/// <see cref="Explanation"/> is set for everything but Ready.
/// </summary>
public sealed record MountainDraftPreview(
    IReadOnlyList<MountainDraftPoint> Points,
    IReadOnlyList<MountainCurvePointDocument> Curve,
    IReadOnlyList<ChainPoint> Outline,
    IReadOnlyList<TerrainCellCoordinate> Cells,
    MountainDraftKind Kind,
    string? Explanation)
{
    public static MountainDraftPreview Empty { get; } = new(
        [], [], [], [], MountainDraftKind.Incomplete, ToolPreviewBuilder.IncompleteMountainDraft);
}

/// <summary>
/// The whole mountain body the eraser would remove, and the Terrain cells it
/// surfaces. Both come from the one picking function the erase itself uses, so
/// what is highlighted and what disappears cannot be two different bodies.
/// </summary>
public sealed record MountainEraserPreview(
    string? MountainBodyId,
    IReadOnlyList<TerrainCellCoordinate> Cells)
{
    public static MountainEraserPreview Empty { get; } = new(null, []);
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
            try
            {
                bounds = PropEditing.BoundsFor(asset, anchor.X, anchor.Y);
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
        if (tool != EditorTool.DrawRiver) return WaterDraftPreview.Empty;

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

    /// <summary>The points the canvas tool asks for before a contour may close.</summary>
    public const int MinimumMountainDraftPoints = 3;

    /// <summary>
    /// Why a draft is not ready yet. It is a count the drawing tool asks for
    /// rather than a geometric rule: two anchors whose handles bow the closing
    /// edges apart already enclose an area, and the document accepts that. What
    /// the canvas cannot offer is a way to author those handles without a third
    /// click to aim them, so the tool asks for three placed points.
    /// </summary>
    public const string IncompleteMountainDraft = "a contour needs at least three points.";

    /// <summary>Said when no Terrain Asset is chosen to give the contour a surface.</summary>
    public const string NoTerrainAsset = "choose a Terrain asset first.";

    /// <summary>
    /// What the mountain draft would author, and whether Enter would take it.
    ///
    /// <para>Ready means the whole attempt has already been made against this
    /// Scene, Asset and height and succeeded - the Asset is drawable, the height
    /// sits on the Workspace quantum, the ring is a usable contour, and the fold
    /// has no tie it cannot settle. Anything the commit would refuse is Blocked
    /// here with the same sentence, because both ask
    /// <see cref="MountainEditing.TryPlace"/> and neither decides anything of
    /// its own.</para>
    /// </summary>
    public static MountainDraftPreview BuildMountainDraft(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        TerrainDisplayCatalog terrainAssets,
        EditorTool tool,
        IReadOnlyList<MountainDraftPoint> draft,
        MountainDraftPoint? pending,
        string? selectedTerrainAssetKey,
        decimal elevationMeters)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(draft);
        if (tool != EditorTool.DrawMountain) return MountainDraftPreview.Empty;

        List<MountainDraftPoint> points = [.. draft];
        if (pending is { } value
            && (points.Count == 0 || points[^1].X != value.X || points[^1].Y != value.Y))
        {
            points.Add(value);
        }
        if (points.Count == 0) return MountainDraftPreview.Empty;

        var curve = MountainEditing.ResolveContour(points);
        if (points.Count < MinimumMountainDraftPoints)
        {
            return new MountainDraftPreview(
                points, curve, [], [], MountainDraftKind.Incomplete, IncompleteMountainDraft);
        }

        // Drawn from the resolved curve alone, so a contour that crosses itself
        // is still visible while it is being fixed.
        var outline = MountainGeometry.Flatten(new MountainBodyDocument
        {
            MountainBodyId = "mountain_preview",
            AssetKey = "preview",
            ElevationMeters = 0m,
            Points = [.. curve],
        }).Points;

        if (selectedTerrainAssetKey is not { } assetKey)
        {
            return new MountainDraftPreview(
                points, curve, outline, [], MountainDraftKind.Blocked, NoTerrainAsset);
        }

        var placement = MountainEditing.TryPlace(
            scene, metrics, terrainAssets, curve, assetKey, elevationMeters);
        if (placement is not { Scene: { } placed, Body: { } body })
        {
            return new MountainDraftPreview(
                points, curve, outline, [], MountainDraftKind.Blocked, placement.Reason);
        }

        return new MountainDraftPreview(
            points,
            curve,
            outline,
            MountainGeometry.TerrainCells(placed, metrics, body),
            MountainDraftKind.Ready,
            Explanation: null);
    }

    /// <summary>
    /// The mountain body the eraser would remove where the pointer is, and the
    /// cells it surfaces - so the author sees the whole body go before the click
    /// rather than the one cell under the cursor.
    /// </summary>
    public static MountainEraserPreview BuildMountainEraser(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        bool eraserEnabled,
        TerrainCellCoordinate? pointer)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        if (tool != EditorTool.DrawMountain || !eraserEnabled) return MountainEraserPreview.Empty;
        if (pointer is not { } cell) return MountainEraserPreview.Empty;
        if (MountainEditing.FindAtCell(scene, metrics, cell) is not { } body)
            return MountainEraserPreview.Empty;

        return new MountainEraserPreview(
            body.MountainBodyId,
            MountainGeometry.TerrainCells(scene, metrics, body));
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
