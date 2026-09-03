using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

/// <summary>
/// Writes a self-contained, engine-neutral scene snapshot. Consumers resolve
/// asset keys in their own asset systems; the SceneMaker catalog is not exported.
/// </summary>
/// <summary>One written export: where it went, and what was odd about it.</summary>
public sealed record SceneExportResult(string Path, IReadOnlyList<string> Warnings);

public static class SceneExport
{
    public const string Format = "scene_maker_scene_export";
    public const int Version = 8;
    public const string DirectoryName = "exports";
    public const string FileSuffix = ".scene_export.json";

    // The embedded runtime Scene intentionally remains the shape export schema
    // 8 already promised. Authoring schema 11 adds mountain contours, but the
    // runtime receives their folded Terrain cells rather than editor sources.
    private const int EmbeddedSceneVersion = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Exports <paramref name="scene"/> out of its open Workspace.</summary>
    public static SceneExportResult Write(WorkspaceSession session, LoadedScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Write(
            session.Workspace,
            scene,
            session.Configuration,
            session.TerrainAssets,
            session.PropAssets);
    }

    public static SceneExportResult Write(
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
                WaterCellMeters = configuration.Grid.WaterCellMeters,
            },
            AssetProfiles = ExportProfiles(configuration, propAssets),
            WaterRaster = ExportWaterRaster(scene.Document, configuration.Metrics),
            Scene = ExportScene(scene.Document, configuration.Metrics),
        };
        var directory = Path.Combine(workspace.DirectoryPath, DirectoryName);
        var path = Path.Combine(directory, scene.Document.SceneId + FileSuffix);
        AtomicTextFile.Write(path, JsonSerializer.Serialize(document, JsonOptions) + "\n");
        return new SceneExportResult(path, Warnings(scene.Document, configuration.Metrics));
    }

    /// <summary>
    /// Exports every Scene of the Workspace, Instances and Templates alike.
    /// Templates ship as their own files so that one of them can be replaced
    /// between seasons without rewriting the map that uses it.
    /// </summary>
    public static IReadOnlyList<SceneExportResult> WriteWorkspace(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var scenes = SceneStore
            .EnumeratePaths(session.Workspace)
            .Select(path => SceneStore.Load(session.Workspace, path))
            .ToList();

        // Exports are named by Scene id in one flat directory, so two Scenes
        // sharing an id would silently overwrite one another and a consumer
        // would load a Workspace with a map missing. Creating such a pair is
        // already refused; this catches a Workspace edited by hand.
        var duplicate = scenes
            .GroupBy(static scene => scene.Document.SceneId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new SceneMakerDocumentException(
                $"Scene id '{duplicate.Key}' names {duplicate.Count()} Scenes in this Workspace; ids must be unique before exporting.");
        }

        return scenes.Select(scene => Write(session, scene)).ToList();
    }

    /// <summary>
    /// What the written export has to say for itself. The warnings are not
    /// failures: a river running off the edge of the authored Terrain is a
    /// normal state of an unfinished map, and refusing to export it would stop
    /// the author from looking at what they just drew. They travel with the
    /// path so that a caller cannot write the file without being handed them.
    /// </summary>
    public static IReadOnlyList<string> Warnings(SceneDocument scene, WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        var effectiveTerrain = MountainGeometry.EffectiveTerrainCells(scene, metrics);
        var authored = effectiveTerrain
            .Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y))
            .ToHashSet();
        var tops = effectiveTerrain.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell.ElevationMeters);
        List<string> warnings = [];
        foreach (var body in scene.WaterBodies)
        {
            var cells = WaterGeometry.Corridor(scene, metrics, body);
            var missing = WaterGeometry
                .CoveredTerrainCells(scene, metrics, body)
                .Where(cell => !authored.Contains(cell))
                .ToList();
            if (missing.Count > 0)
            {
                warnings.Add(
                    $"Water body '{body.WaterBodyId}' covers {missing.Count} Terrain cell{(missing.Count == 1 ? string.Empty : "s")} that carry no Terrain: {TerrainCoverage.FormatMissingCells(missing)}.");
            }

            // Water whose bed sits above the ground it crosses has nothing
            // holding it in. The corridor cut can only remove Terrain, never
            // raise it, so this is a river drawn higher than its valley.
            var floating = 0;
            var perTerrainCell = metrics.WaterCellsPerTerrainCell;
            foreach (var cell in cells)
            {
                var under = new TerrainCellCoordinate(
                    WorkspaceMetrics.FloorDivide(cell.X, perTerrainCell),
                    WorkspaceMetrics.FloorDivide(cell.Y, perTerrainCell));
                if (tops.TryGetValue(under, out var top) && cell.BedMeters > top) floating++;
            }
            if (floating > 0)
            {
                warnings.Add(
                    $"Water body '{body.WaterBodyId}' floats above the Terrain in {floating} water cell{(floating == 1 ? string.Empty : "s")}: its bed is higher than the ground under it.");
            }

            // The first point is the source and the last the mouth, so a
            // surface that climbs between them is water running uphill.
            for (var index = 0; index + 1 < body.Points.Count; index++)
            {
                if (body.Points[index + 1].ElevationMeters <= body.Points[index].ElevationMeters)
                    continue;
                warnings.Add(
                    $"Water body '{body.WaterBodyId}' rises from {body.Points[index].ElevationMeters:0.###} m to {body.Points[index + 1].ElevationMeters:0.###} m between points {index} and {index + 1}, which is upstream of its own mouth.");
                break;
            }
        }
        return warnings;
    }

    /// <summary>
    /// The derived half of the water: what the authored curves cover, as cells.
    /// It sits beside the Scene rather than inside it because the Scene block is
    /// what the author wrote and this is what SceneMaker worked out from it -
    /// the same split <c>asset_profiles</c> already makes.
    /// </summary>
    private static List<ExportWaterBodyDocument> ExportWaterRaster(
        SceneDocument scene,
        WorkspaceMetrics metrics) =>
        scene.WaterBodies
            .Select(body => new ExportWaterBodyDocument
            {
                WaterBodyId = body.WaterBodyId,
                WaterKind = body.WaterKind,
                AssetKey = body.AssetKey,
                Cells = WaterGeometry
                    .Corridor(scene, metrics, body)
                    .Select(static cell => new ExportWaterCellDocument
                    {
                        X = cell.X,
                        Y = cell.Y,
                        BedMeters = cell.BedMeters,
                        SurfaceMeters = cell.SurfaceMeters,
                        CutTopMeters = cell.CutTopMeters,
                    })
                    .ToList(),
            })
            .ToList();

    /// <summary>
    /// The runtime Scene is a derived snapshot, not the authoring document.
    /// Mountain contours are folded into Terrain and deliberately omitted, so
    /// adding an editor source does not change export schema 8 or its reader.
    /// </summary>
    private static ExportSceneDocument ExportScene(
        SceneDocument scene,
        WorkspaceMetrics metrics) => new()
    {
        Schema = scene.Schema,
        Version = EmbeddedSceneVersion,
        SceneId = scene.SceneId,
        SceneKind = scene.SceneKind,
        CoordinateSpace = scene.CoordinateSpace,
        SizeCells = scene.SizeCells,
        TerrainCells = [.. MountainGeometry.EffectiveTerrainCells(scene, metrics)],
        Props = scene.Props,
        WaterBodies = scene.WaterBodies,
        TemplateDefinition = scene.TemplateDefinition,
        TemplateAnchors = scene.TemplateAnchors,
        DefaultElevationMeters = scene.DefaultElevationMeters,
    };

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
                    Surface = profile.Surface,
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
                : new ExportAssetProfileDocument
                {
                    AssetKey = profile.AssetKey,
                    Surface = profile.Surface,
                })
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
        MountainEditing.ValidateAssetReferences(scene, terrainAssets);
        PropEditing.ValidateAssetReferences(scene, propAssets);
        WaterEditing.ValidateAssetReferences(scene, terrainAssets);
        var effective = scene with
        {
            TerrainCells = [.. MountainGeometry.EffectiveTerrainCells(
                scene,
                configuration.Metrics)],
        };
        var authored = TerrainCoverage.AuthoredCells(effective);
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

        /// <summary>
        /// The cells every authored water body covers. Bodies may overlap; a
        /// simulation reads their union. Empty for a Scene without water.
        /// </summary>
        public required List<ExportWaterBodyDocument> WaterRaster { get; init; }

        public required ExportSceneDocument Scene { get; init; }
    }

    /// <summary>
    /// The exact Scene shape export schema 8 promised. It is separate from the
    /// authoring record so editor-only source geometry cannot leak into a
    /// strict runtime reader merely because the authoring schema grows.
    /// </summary>
    private sealed record ExportSceneDocument
    {
        public required string Schema { get; init; }
        public required int Version { get; init; }
        public required string SceneId { get; init; }
        public required SceneKind SceneKind { get; init; }
        public required string CoordinateSpace { get; init; }
        public required SceneSizeCells SizeCells { get; init; }
        public required List<TerrainCellDocument> TerrainCells { get; init; }
        public required List<PropDocument> Props { get; init; }
        public required List<WaterBodyDocument> WaterBodies { get; init; }
        public required TemplateDefinitionDocument? TemplateDefinition { get; init; }
        public required List<TemplateAnchorDocument> TemplateAnchors { get; init; }
        public required decimal DefaultElevationMeters { get; init; }
    }

    private sealed record ExportGridDocument
    {
        public required decimal TerrainCellMeters { get; init; }
        public required decimal AuthoringPixelsPerMeter { get; init; }
        public required decimal GamePixelsPerMeter { get; init; }

        /// <summary>
        /// The edge length of one water cell. Finer than a Terrain cell so a
        /// bank can follow a curve, and nesting a whole number of times inside
        /// one so a water cell never straddles two of them.
        /// </summary>
        public required decimal WaterCellMeters { get; init; }
    }

    /// <summary>
    /// One water body as a consumer reads it: what it is made of, how high its
    /// surface sits, and the cells it covers. The authored curve that produced
    /// the cells stays in the Scene block, where a consumer that wants a smooth
    /// mesh instead of the raster can find it.
    /// </summary>
    private sealed record ExportWaterBodyDocument
    {
        public required string WaterBodyId { get; init; }
        public required WaterKind WaterKind { get; init; }
        public required string AssetKey { get; init; }
        public required List<ExportWaterCellDocument> Cells { get; init; }
    }

    /// <summary>
    /// One cell of a body, as two vertical spans sharing a floor: the water
    /// fills bed..surface, and bed..cut_top is taken out of the Terrain. Three
    /// numbers rather than two spans, because the two always share that floor.
    /// </summary>
    private sealed record ExportWaterCellDocument
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        public required decimal BedMeters { get; init; }
        public required decimal SurfaceMeters { get; init; }
        public required decimal CutTopMeters { get; init; }
    }

    private sealed record ExportAssetProfileDocument
    {
        public required string AssetKey { get; init; }

        /// <summary>
        /// The domain this Terrain presents to a simulation - "land", "water",
        /// and whatever a consumer adds later. Null for everything that is not
        /// Terrain. It sits here rather than on every cell so that one Asset
        /// cannot contradict itself, and so a consumer joins it through the
        /// asset_key it reads for the cell anyway.
        /// </summary>
        public string? Surface { get; init; }

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
