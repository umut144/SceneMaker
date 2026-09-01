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
    public const int Version = 3;
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

    /// <summary>Exports <paramref name="scene"/> out of its open Workspace.</summary>
    public static string Write(WorkspaceSession session, LoadedScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Write(
            session.Workspace,
            scene,
            session.Configuration,
            session.TerrainAssets,
            session.PropAssets);
    }

    public static string Write(
        LoadedWorkspace workspace,
        LoadedScene scene,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrainAssets,
        PropDisplayCatalog propAssets)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(scene.Document, configuration, terrainAssets, propAssets);
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
            AssetProfiles = ExportProfiles(configuration, propAssets),
            RequiredTemplateGroups = RequiredTemplateGroups(scene.Document),
            Scene = scene.Document,
        };
        var directory = Path.Combine(workspace.DirectoryPath, DirectoryName);
        var path = Path.Combine(directory, scene.Document.SceneId + FileSuffix);
        AtomicTextFile.Write(path, JsonSerializer.Serialize(document, JsonOptions) + "\n");
        return path;
    }

    /// <summary>
    /// Exports every Scene of the Workspace, Instances and Templates alike.
    /// Templates ship as their own files so that one of them can be replaced
    /// between seasons without rewriting the map that uses it.
    /// </summary>
    public static IReadOnlyList<string> WriteWorkspace(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return SceneStore
            .EnumeratePaths(session.Workspace)
            .Select(path => Write(session, SceneStore.Load(session.Workspace, path)))
            .ToList();
    }

    /// <summary>
    /// The Template groups this Scene's Anchors ask for, so a consumer can tell
    /// on load that a group it needs has no Template left - the failure a
    /// seasonal Template swap invites.
    /// </summary>
    private static List<int> RequiredTemplateGroups(SceneDocument scene) =>
        [.. scene.TemplateAnchors
            .Select(static anchor => anchor.GroupNumber)
            .Distinct()
            .Order()];

    private static List<ExportAssetProfileDocument> ExportProfiles(
        WorkspaceConfiguration configuration,
        PropDisplayCatalog propAssets)
    {
        var props = propAssets.Assets.ToDictionary(
            static asset => asset.AssetKey, StringComparer.Ordinal);
        return configuration.AssetProfiles
            .OrderBy(static profile => profile.AssetKey, StringComparer.Ordinal)
            .Select(profile => props.TryGetValue(profile.AssetKey, out var prop)
                ? new ExportAssetProfileDocument
                {
                    AssetKey = prop.AssetKey,
                    FootprintMeters = new ExportSizeDocument
                    {
                        Width = prop.WidthMeters,
                        Height = prop.HeightMeters,
                    },
                    AnchorMeters = new ExportPointDocument
                    {
                        X = prop.AnchorXMeters,
                        Y = prop.AnchorYMeters,
                    },
                }
                : new ExportAssetProfileDocument { AssetKey = profile.AssetKey })
            .ToList();
    }

    private static void Validate(
        SceneDocument scene,
        WorkspaceConfiguration configuration,
        TerrainDisplayCatalog terrainAssets,
        PropDisplayCatalog propAssets)
    {
        DocumentValidation.ValidateGrid(scene, configuration.Metrics);
        TerrainEditing.ValidateAssetReferences(scene, terrainAssets);
        PropEditing.ValidateAssetReferences(scene, propAssets);
        var authored = TerrainCoverage.AuthoredCells(scene);
        foreach (var prop in scene.Props)
        {
            var missing = TerrainCoverage.MissingCells(
                authored,
                PropEditing.BoundsFor(
                    propAssets.Resolve(prop.AssetKey),
                    prop.PositionAuthoringPx.X,
                    prop.PositionAuthoringPx.Y),
                configuration.Metrics);
            if (missing.Count == 0) continue;
            throw new SceneMakerDocumentException(
                $"Cannot export Prop '{prop.InstanceId}': Terrain is missing at {TerrainCoverage.FormatMissingCells(missing)}.");
        }
    }

    private sealed record ExportDocument
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string WorkspaceKey { get; init; }
        public required ExportGridDocument Grid { get; init; }
        public required List<ExportAssetProfileDocument> AssetProfiles { get; init; }
        public required List<int> RequiredTemplateGroups { get; init; }
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
