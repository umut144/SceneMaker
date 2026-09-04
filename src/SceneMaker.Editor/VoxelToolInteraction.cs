using SceneMaker.Core;

namespace SceneMaker.Editor;

public enum VoxelAuthoringTool
{
    Tile,
    Hill,
    Path,
}

public sealed record VoxelToolSettings(
    VoxelAuthoringTool Tool,
    VoxelEditMode EditMode,
    string? AssetKey,
    decimal HillBaseElevationMeters,
    decimal HillTopElevationMeters,
    decimal PathWidthMeters,
    decimal PathHeightMeters,
    decimal PathElevationMeters,
    decimal PathGrade,
    bool GradeFollowing,
    VoxelPathSupportMode PathSupportMode);

public sealed record VoxelToolContext(
    SceneDocument Scene,
    WorkspaceConfiguration Workspace,
    WorkspaceMetrics Metrics,
    VoxelToolSettings Settings);

/// <summary>
/// The single input path for WorldVoxMaker tools. It owns only transient point
/// drafts; every committed outcome remains a pure SceneDocument transform.
/// </summary>
public sealed class VoxelToolInteraction
{
    public const string TileStroke = "voxel-tile";

    private readonly List<VoxelPointMeters> _hillDraft = [];
    private readonly List<VoxelPathPoint> _pathDraft = [];

    public IReadOnlyList<VoxelPointMeters> HillDraft => _hillDraft;
    public IReadOnlyList<VoxelPathPoint> PathDraft => _pathDraft;

    public void SelectTool(VoxelAuthoringTool tool)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        _hillDraft.Clear();
        _pathDraft.Clear();
    }

    public ToolOutcome PointerPressed(
        VoxelToolContext context,
        VoxelCoordinate voxel,
        VoxelPointMeters metricPoint) =>
        context.Settings.Tool switch
        {
            VoxelAuthoringTool.Tile => Tile(context, voxel),
            VoxelAuthoringTool.Hill => AddHillPoint(context, metricPoint),
            VoxelAuthoringTool.Path => AddPathPoint(context, metricPoint),
            _ => throw new ArgumentOutOfRangeException(nameof(context.Settings.Tool)),
        };

    public ToolOutcome PointerDragged(
        VoxelToolContext context,
        VoxelCoordinate voxel,
        VoxelPointMeters metricPoint) =>
        context.Settings.Tool == VoxelAuthoringTool.Tile
            ? Tile(context, voxel)
            : ToolOutcome.Idle.Instance;

    public ToolOutcome KeyPressed(VoxelToolContext context, ToolKey key)
    {
        if (key == ToolKey.Escape)
        {
            if (context.Settings.Tool == VoxelAuthoringTool.Hill && _hillDraft.Count > 0)
            {
                _hillDraft.RemoveAt(_hillDraft.Count - 1);
                return new ToolOutcome.Message("Removed the last VoxelHill point.");
            }
            if (context.Settings.Tool == VoxelAuthoringTool.Path && _pathDraft.Count > 0)
            {
                _pathDraft.RemoveAt(_pathDraft.Count - 1);
                return new ToolOutcome.Message("Removed the last VoxelPath point.");
            }
            return ToolOutcome.Idle.Instance;
        }

        return context.Settings.Tool switch
        {
            VoxelAuthoringTool.Hill => CommitHill(context),
            VoxelAuthoringTool.Path => CommitPath(context),
            _ => ToolOutcome.Idle.Instance,
        };
    }

    private static ToolOutcome Tile(VoxelToolContext context, VoxelCoordinate coordinate)
    {
        var settings = context.Settings;
        return new ToolOutcome.Edit(
            "VoxelTile",
            document => ApplyGrid(document, context.Metrics, grid =>
                VoxelTileEditing.Apply(
                    grid,
                    context.Workspace,
                    coordinate,
                    settings.EditMode,
                    settings.AssetKey)),
            TileStroke,
            (_, _) => settings.EditMode == VoxelEditMode.Additive
                ? $"Added voxel ({coordinate.X}, {coordinate.Y}, {coordinate.Z})."
                : $"Removed voxel ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).");
    }

    private ToolOutcome AddHillPoint(VoxelToolContext context, VoxelPointMeters point)
    {
        var snapped = SnapPlan(context.Metrics, point);
        _hillDraft.Add(snapped);
        return new ToolOutcome.Message(
            $"VoxelHill point {_hillDraft.Count}: {snapped.X:0.###}, {snapped.Z:0.###} m; Enter closes the volume.");
    }

    private ToolOutcome AddPathPoint(VoxelToolContext context, VoxelPointMeters point)
    {
        var snapped = SnapPlan(context.Metrics, point);
        var settings = context.Settings;
        var elevation = settings.PathElevationMeters;
        if (settings.GradeFollowing && _pathDraft.Count > 0)
        {
            var previous = _pathDraft[^1];
            elevation = VoxelPathEditing.ElevationAtGrade(
                previous.Position,
                previous.ElevationMeters,
                new VoxelPlanPointMeters(snapped.X, snapped.Z),
                settings.PathGrade);
        }
        elevation = Snap(context.Metrics, elevation);
        _pathDraft.Add(new VoxelPathPoint(
            new VoxelPlanPointMeters(snapped.X, snapped.Z),
            VoxelPlanOffsetMeters.Zero,
            VoxelPlanOffsetMeters.Zero,
            Snap(context.Metrics, settings.PathWidthMeters),
            Snap(context.Metrics, settings.PathHeightMeters),
            elevation));
        return new ToolOutcome.Message(
            $"VoxelPath point {_pathDraft.Count}: elevation {elevation:0.###} m; Enter builds the corridor.");
    }

    private ToolOutcome CommitHill(VoxelToolContext context)
    {
        if (_hillDraft.Count < 3)
            return new ToolOutcome.Message("VoxelHill needs at least three footprint points.");
        var footprint = _hillDraft.ToArray();
        _hillDraft.Clear();
        var settings = context.Settings;
        return new ToolOutcome.Edit(
            "VoxelHill",
            document => ApplyGrid(document, context.Metrics, grid =>
                VoxelHillEditing.Apply(
                    grid,
                    footprint,
                    Snap(context.Metrics, settings.HillBaseElevationMeters),
                    Snap(context.Metrics, settings.HillTopElevationMeters),
                    settings.EditMode)),
            Describe: (_, _) => settings.EditMode == VoxelEditMode.Additive
                ? "Built VoxelHill volume."
                : "Cut VoxelHill volume.");
    }

    private ToolOutcome CommitPath(VoxelToolContext context)
    {
        if (_pathDraft.Count < 2)
            return new ToolOutcome.Message("VoxelPath needs at least two control points.");
        var points = _pathDraft.ToArray();
        _pathDraft.Clear();
        var settings = context.Settings;
        return new ToolOutcome.Edit(
            "VoxelPath",
            document => ApplyGrid(document, context.Metrics, grid =>
                VoxelPathEditing.Apply(
                    grid,
                    context.Workspace,
                    points,
                    settings.EditMode,
                    settings.PathSupportMode,
                    settings.AssetKey)),
            Describe: (_, _) => settings.EditMode == VoxelEditMode.Additive
                ? "Built VoxelPath volume."
                : "Cut VoxelPath volume.");
    }

    private static SceneDocument ApplyGrid(
        SceneDocument document,
        WorkspaceMetrics metrics,
        Func<VoxelGrid, VoxelGrid> edit)
    {
        var before = VoxelDocumentEditing.ToGrid(document, metrics);
        var after = edit(before);
        return ReferenceEquals(before, after)
            ? document
            : VoxelDocumentEditing.WithGrid(document, after);
    }

    private static VoxelPointMeters SnapPlan(
        WorkspaceMetrics metrics,
        VoxelPointMeters point) =>
        new(Snap(metrics, point.X), point.Y, Snap(metrics, point.Z));

    private static decimal Snap(WorkspaceMetrics metrics, decimal value) =>
        Math.Round(
            value / metrics.VoxelSubgridMeters,
            MidpointRounding.AwayFromZero) * metrics.VoxelSubgridMeters;
}
