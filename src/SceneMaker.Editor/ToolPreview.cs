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

/// <summary>The closed mountain contour being drawn and its derived Terrain fill.</summary>
public sealed record MountainDraftPreview(
    IReadOnlyList<MountainDraftPoint> Points,
    IReadOnlyList<MountainCurvePointDocument> Curve,
    IReadOnlyList<ChainPoint> Outline,
    IReadOnlyList<TerrainCellCoordinate> Cells,
    bool IsValid)
{
    public static MountainDraftPreview Empty { get; } = new([], [], [], [], false);
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

    /// <summary>
    /// What the mountain draft would author. The outline is shown even when it
    /// crosses itself; the fill appears only when the exact Core contour rule
    /// accepts it.
    /// </summary>
    public static MountainDraftPreview BuildMountainDraft(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorTool tool,
        IReadOnlyList<MountainDraftPoint> draft,
        MountainDraftPoint? pending)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
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
        if (points.Count < 3)
            return new MountainDraftPreview(points, curve, [], [], false);

        var body = new MountainBodyDocument
        {
            MountainBodyId = "mountain_preview",
            AssetKey = "preview",
            ElevationMeters = 0m,
            Points = [.. curve],
        };
        var outline = MountainGeometry.Flatten(body).Points;
        try
        {
            return new MountainDraftPreview(
                points,
                curve,
                outline,
                MountainGeometry.TerrainCells(scene, metrics, body),
                true);
        }
        catch (SceneMakerDocumentException)
        {
            return new MountainDraftPreview(points, curve, outline, [], false);
        }
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
