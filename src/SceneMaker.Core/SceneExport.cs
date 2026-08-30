using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

/// <summary>
/// Writes a self-contained, engine-neutral scene snapshot. Consumers resolve
/// asset keys in their own asset systems; the SceneMaker catalog is not exported.
/// </summary>
public static class SceneExport
{
    public const string Format = "scene_maker_scene_export";
    public const int Version = 1;
    public const string DirectoryName = "exports";
    public const string FileSuffix = ".scene_export.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Write(
        LoadedWorkspace workspace,
        LoadedScene scene,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrainAssets,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(scene.Document, configuration, terrainAssets, placementAssets, transitionAssets);
        var document = new ExportDocument
        {
            Format = Format,
            Version = Version,
            WorkspaceKey = configuration.WorkspaceKey,
            Grid = new ExportGridDocument
            {
                TerrainCellMeters = configuration.Grid.TerrainCellMeters,
                AuthoringPixelsPerMeter = configuration.Grid.AuthoringPixelsPerMeter,
                GamePixelsPerMeter = configuration.Grid.GamePixelsPerMeter,
            },
            AssetProfiles = ExportProfiles(
                configuration, placementAssets, transitionAssets),
            Scene = scene.Document,
        };
        var directory = Path.Combine(workspace.DirectoryPath, DirectoryName);
        var path = Path.Combine(directory, scene.Document.SceneId + FileSuffix);
        AtomicTextFile.Write(path, JsonSerializer.Serialize(document, JsonOptions) + "\n");
        return path;
    }

    private static List<ExportAssetProfileDocument> ExportProfiles(
        WorkspaceConfiguration configuration,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        var placements = placementAssets.Assets.ToDictionary(
            static asset => asset.AssetKey, StringComparer.Ordinal);
        var transitions = transitionAssets.Assets.ToDictionary(
            static asset => asset.AssetKey, StringComparer.Ordinal);
        return configuration.AssetProfiles
            .OrderBy(static profile => profile.AssetKey, StringComparer.Ordinal)
            .Select(profile =>
            {
                if (placements.TryGetValue(profile.AssetKey, out var placement))
                {
                    return SpatialProfile(
                        placement.AssetKey,
                        placement.WidthMeters,
                        placement.HeightMeters,
                        placement.AnchorXMeters,
                        placement.AnchorYMeters);
                }
                if (transitions.TryGetValue(profile.AssetKey, out var transition))
                {
                    return SpatialProfile(
                        transition.AssetKey,
                        transition.WidthMeters,
                        transition.HeightMeters,
                        transition.AnchorXMeters,
                        transition.AnchorYMeters);
                }
                return new ExportAssetProfileDocument { AssetKey = profile.AssetKey };
            })
            .ToList();
    }

    private static ExportAssetProfileDocument SpatialProfile(
        string assetKey,
        decimal width,
        decimal height,
        decimal anchorX,
        decimal anchorY) => new()
    {
        AssetKey = assetKey,
        FootprintMeters = new ExportSizeDocument { Width = width, Height = height },
        AnchorMeters = new ExportPointDocument { X = anchorX, Y = anchorY },
    };

    private static void Validate(
        SceneDocument scene,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrainAssets,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        DocumentValidation.ValidateGrid(scene, configuration.Metrics);
        TerrainEditing.ValidateAssetReferences(scene, terrainAssets);
        PlacementEditing.ValidateAssetReferences(scene, placementAssets, transitionAssets);
        TransitionEditing.ValidateAssetReferences(scene, placementAssets, transitionAssets);
        foreach (var placement in scene.Placements)
        {
            var missing = TerrainCoverage.MissingCells(
                scene,
                PlacementEditing.BoundsFor(
                    placementAssets.Resolve(placement.AssetKey),
                    placement.PositionAuthoringPx.X,
                    placement.PositionAuthoringPx.Y),
                configuration.Metrics);
            RequireTerrainCoverage("Placement", placement.InstanceId, missing);
        }
        foreach (var transition in scene.Transitions)
        {
            var missing = TerrainCoverage.MissingCells(
                scene,
                TransitionEditing.BoundsFor(
                    transitionAssets.Resolve(transition.AssetKey),
                    transition.PositionAuthoringPx.X,
                    transition.PositionAuthoringPx.Y),
                configuration.Metrics);
            RequireTerrainCoverage("Transition", transition.InstanceId, missing);
        }
    }

    private static void RequireTerrainCoverage(
        string label,
        string instanceId,
        IReadOnlyList<TerrainCellCoordinate> missing)
    {
        if (missing.Count == 0) return;
        throw new SceneMakerDocumentException(
            $"Cannot export {label} '{instanceId}': Terrain is missing at {TerrainCoverage.FormatMissingCells(missing)}.");
    }

    private sealed record ExportDocument
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string WorkspaceKey { get; init; }
        public required ExportGridDocument Grid { get; init; }
        public required List<ExportAssetProfileDocument> AssetProfiles { get; init; }
        public required SceneDocument Scene { get; init; }
    }

    private sealed record ExportGridDocument
    {
        public required decimal TerrainCellMeters { get; init; }
        public required decimal AuthoringPixelsPerMeter { get; init; }
        public required decimal GamePixelsPerMeter { get; init; }
    }

    private sealed record ExportAssetProfileDocument
    {
        public required string AssetKey { get; init; }
        public ExportSizeDocument? FootprintMeters { get; init; }
        public ExportPointDocument? AnchorMeters { get; init; }
    }

    private sealed record ExportSizeDocument
    {
        public required decimal Width { get; init; }
        public required decimal Height { get; init; }
    }

    private sealed record ExportPointDocument
    {
        public required decimal X { get; init; }
        public required decimal Y { get; init; }
    }
}
