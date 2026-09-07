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
    public const int Version = 16;
    public const string DirectoryName = "exports";
    public const string FileSuffix = ".scene_export.json";

    // ElevationRegion contours remain folded into Terrain. Embedded scene 15
    // authors a bridge deck as a Placement repeated along the span - a count
    // and a gap - and export 14 adds their derived half: the planks laid out,
    // the deck quad the walking surface is, and the four corner posts, all
    // worked out here rather than by every consumer.
    //
    // Export 14 also widens `surface` on an asset_profile: it is no longer
    // Terrain-only, because a deck is walked on and is wood while nothing
    // underneath it can say so.
    //
    // The Asset a bridge names is plank_asset_key, not deck_asset_key: a deck
    // is the row, and the Asset is the one part it repeats. Export 13 called
    // it after the whole and was never read by a consumer.
    //
    // Export 15 gives a bridge deck the centerline a Path already has - the
    // bake had it all along and 14 dropped it - and says what lies under each
    // end of the deck, so a consumer can hang its navigation on the same line
    // it hangs a Path on and tell a bridge that reaches ground from one that
    // ends over the river. The embedded Scene is unchanged.
    //
    // Export 16 draws the water. `water_bakes` ships every water body's
    // surface as the band a Path and a deck already ship - the same vertices,
    // triangles, edge loops and centerline samples, from the same flattener
    // and mesher - so a consumer needs no third way to turn authored data into
    // geometry. Height sits on the sample and never on the body, because a
    // river falls. The raster is unchanged and stays the truth about which
    // cells are water; the band is the truth about where the water is seen.
    // The embedded Scene is unchanged again: both halves are derived, and
    // nothing an author writes moved.
    private const int EmbeddedSceneVersion = 15;

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
            WaterBakes = ExportWaterBakes(scene.Document, configuration.Metrics),
            RouteSurfaceBakes = scene.Document.RouteSurfaces
                .Select(route => RouteSurfaceBake.Build(configuration.Metrics, route))
                .ToList(),
            RouteSurfaceCutRaster = ExportRouteSurfaceCutRaster(
                scene.Document, configuration.Metrics),
            BridgeBakes = ExportBridgeBakes(
                scene.Document, configuration.Metrics, propAssets),
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

        // A Workspace export is one operation. Refuse every Scene before the
        // first output is replaced, otherwise one late invalid Scene leaves a
        // mixture of new and stale files in the exports directory.
        foreach (var scene in scenes)
        {
            Validate(
                scene.Document,
                session.Configuration,
                session.TerrainAssets,
                session.PropAssets);
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
        var effectiveTerrain = ElevationRegionGeometry.EffectiveTerrainCells(scene, metrics);
        var authored = effectiveTerrain
            .Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y))
            .ToHashSet();
        var tops = effectiveTerrain.ToDictionary(
            static cell => new TerrainCellCoordinate(cell.X, cell.Y),
            static cell => cell.ElevationMeters);
        List<string> warnings = [];

        // An excavation that lands nowhere inside the Scene is not an error -
        // a Path drawn past the edge is an ordinary state of an unfinished map
        // - but it is exactly the case where the export would otherwise say
        // nothing at all about an interval the author deliberately marked.
        var subtractive = scene.RouteSurfaces
            .SelectMany(static route => route.Segments)
            .Where(static segment =>
                segment.Operation == RouteSegmentOperation.Subtractive)
            .ToList();
        if (subtractive.Count > 0)
        {
            var excavated = RouteSurfaceRaster
                .Cuts(scene, metrics)
                .Select(static cut => cut.SegmentId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var segment in subtractive.Where(
                segment => !excavated.Contains(segment.SegmentId)))
            {
                warnings.Add(
                    $"Path segment '{segment.SegmentId}' is subtractive but removes no Terrain inside the Scene.");
            }
        }

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
    /// The other derived half of the water: the band a consumer draws, in the
    /// order and with the identity <c>water_raster</c> uses, so the two halves
    /// of one body are joined by nothing more than their position in the array
    /// and the id they both carry.
    /// </summary>
    private static List<ExportWaterBakeDocument> ExportWaterBakes(
        SceneDocument scene,
        WorkspaceMetrics metrics) =>
        scene.WaterBodies
            .Select(body =>
            {
                var band = WaterGeometry.SurfaceBand(metrics, body);
                return new ExportWaterBakeDocument
                {
                    WaterBodyId = body.WaterBodyId,
                    AssetKey = body.AssetKey,
                    Vertices = band.Vertices,
                    TriangleIndices = band.TriangleIndices,
                    BoundaryEdges = band.BoundaryEdges,
                    CenterlineSamples = band.CenterlineSamples,
                };
            })
            .ToList();

    /// <summary>
    /// The derived half of every bridge. The deck goes through the same bake a
    /// Path does, because it is the same band; the posts are the corners this
    /// Workspace's anchor Asset stands at, worked out here so no consumer has
    /// to agree with SceneMaker about where a corner is.
    /// </summary>
    private static List<ExportBridgeBakeDocument> ExportBridgeBakes(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        PropDisplayCatalog propAssets)
    {
        // Prepared once for every bridge: the same folded Terrain, water and
        // bands the Section view resolves, so what an author saw under a
        // deck's end is what the consumer is told is there.
        var columns = scene.Bridges.Count == 0 ? null : LayeredSceneColumns.Prepare(scene, metrics);
        return scene.Bridges
            .Select(bridge =>
            {
                var bake = RouteSurfaceBake.Build(metrics, BridgeGeometry.DeckRoute(bridge));
                var layout = BridgeGeometry.Planks(metrics, bridge);
                return new ExportBridgeBakeDocument
                {
                    BridgeId = bridge.BridgeId,
                    PlankAssetKey = bridge.PlankAssetKey,
                    LengthMeters = layout.LengthMeters,
                    HeadingDegrees = layout.HeadingDegrees,
                    PlankCount = bridge.PlankCount,
                    PlankGapMeters = bridge.PlankGapMeters,
                    PlankDepthMeters = layout.PlankDepthMeters,
                    Planks = layout.Planks
                        .Select(plank => new ExportBridgePlankDocument
                        {
                            PlankId = FormattableString.Invariant(
                                $"{bridge.BridgeId}.plank_{plank.Index:0000}"),
                            AssetKey = bridge.PlankAssetKey,
                            XMeters = plank.CenterXMeters,
                            YMeters = plank.CenterYMeters,
                            ElevationMeters = bridge.ElevationMeters,
                            DepthMeters = plank.DepthMeters,
                            WidthMeters = plank.WidthMeters,
                        })
                        .ToList(),
                    Vertices = bake.Vertices,
                    TriangleIndices = bake.TriangleIndices,
                    BoundaryEdges = bake.BoundaryEdges,
                    CenterlineSamples = bake.CenterlineSamples,
                    GroundAtStart = GroundUnder(columns!, bridge, bridge.StartAuthoringPx),
                    GroundAtEnd = GroundUnder(columns!, bridge, bridge.EndAuthoringPx),
                    Posts = BridgeGeometry.Corners(metrics, bridge)
                        .Select(corner => new ExportBridgePostDocument
                        {
                            PostId = $"{bridge.BridgeId}.post_{CornerSuffix(corner.Kind)}",
                            Corner = corner.Kind,
                            AssetKey = bridge.AnchorAssetKey,
                            XMeters = corner.XMeters,
                            YMeters = corner.YMeters,
                            ElevationMeters = bridge.ElevationMeters,
                        })
                        .ToList(),
                };
            })
            .ToList();
    }

    /// <summary>
    /// What a top-down look at one end of the deck finds once the deck itself
    /// is taken out of the way: Terrain, water, a Path, or another bridge's
    /// deck - or nothing, where no Terrain was painted and nothing else lies
    /// there. Terrain owns no id, so its source is null.
    /// </summary>
    private static ExportBridgeGroundDocument? GroundUnder(
        LayeredSceneColumns columns,
        BridgeDocument bridge,
        AuthoringPixelPosition end)
    {
        var visible = columns
            .AtAuthoringPosition(end.X, end.Y)
            .Without(bridge.BridgeId)
            .VisibleAt();
        return visible is null
            ? null
            : new ExportBridgeGroundDocument
            {
                ElevationMeters = visible.ElevationMeters,
                AssetKey = visible.AssetKey,
                SourceId = visible.SourceId,
            };
    }

    /// <summary>
    /// The stable tail of a post's ID. It uses the dot the way a route segment
    /// does, which is also what keeps it apart from an authored Placement ID:
    /// those are always {asset_key}_{0000} and never contain one.
    /// </summary>
    private static string CornerSuffix(BridgeCornerKind kind) => kind switch
    {
        BridgeCornerKind.StartLeft => "start_left",
        BridgeCornerKind.StartRight => "start_right",
        BridgeCornerKind.EndLeft => "end_left",
        BridgeCornerKind.EndRight => "end_right",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The derived half of an excavating Path: which cells each subtractive
    /// interval takes out of the Terrain, and between which two heights. The
    /// cells come from the same raster the Section view reads, so what an
    /// author inspects and what a consumer subtracts cannot drift apart.
    /// </summary>
    private static List<ExportRouteSurfaceCutDocument> ExportRouteSurfaceCutRaster(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        var excavating = scene.RouteSurfaces
            .Where(static route => route.Segments.Any(static segment =>
                segment.Operation == RouteSegmentOperation.Subtractive))
            .ToList();
        if (excavating.Count == 0) return [];
        var cuts = RouteSurfaceRaster.Cuts(scene, metrics);
        return excavating
            .Select(route => new ExportRouteSurfaceCutDocument
            {
                RouteSurfaceId = route.RouteSurfaceId,
                Cells = cuts
                    .Where(cut => StringComparer.Ordinal.Equals(
                        cut.RouteSurfaceId, route.RouteSurfaceId))
                    .Select(static cut => new ExportRouteSurfaceCutCellDocument
                    {
                        X = cut.X,
                        Y = cut.Y,
                        SegmentId = cut.SegmentId,
                        FloorMeters = cut.BottomMeters,
                        CutTopMeters = cut.TopMeters,
                    })
                    .ToList(),
            })
            .ToList();
    }

    /// <summary>
    /// The runtime Scene is a derived snapshot, not the authoring document.
    /// ElevationRegion contours are folded into Terrain and deliberately omitted, so
    /// adding an editor source does not change the export shape or its reader.
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
        TerrainCells = [.. ElevationRegionGeometry.EffectiveTerrainCells(scene, metrics)],
        Props = scene.Props,
        WaterBodies = scene.WaterBodies,
        Bridges = scene.Bridges,
        RouteSurfaces = scene.RouteSurfaces.Select(static route =>
            new ExportRouteSurfaceDocument
            {
                RouteSurfaceId = route.RouteSurfaceId,
                AssetKey = route.AssetKey,
                Points = route.Points,
                Segments = route.Segments.Select(static segment =>
                    new ExportRouteSurfaceSegmentDocument
                    {
                        SegmentId = segment.SegmentId,
                        GradePercent = segment.GradePercent,
                        Operation = segment.Operation,
                        ClearanceAboveMeters = segment.ClearanceAboveMeters,
                    }).ToList(),
            }).ToList(),
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
        PropEditing.ValidateAssetReferences(scene, propAssets);
        RouteSurfaceEditing.ValidateAssetReferences(scene, terrainAssets);
        WaterEditing.ValidateAssetReferences(scene, terrainAssets);

        // A bridge is only exportable while both of its Assets are still
        // enabled. Refusing here rather than writing a post or a plank with no
        // model is the same promise the placement rule already makes.
        foreach (var bridge in scene.Bridges)
        {
            _ = propAssets.Resolve(bridge.AnchorAssetKey);
            _ = propAssets.Resolve(bridge.PlankAssetKey);
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

        /// <summary>
        /// The visible surface of every water body, one band per body in the
        /// same order. Empty for a Scene without water.
        /// </summary>
        public required List<ExportWaterBakeDocument> WaterBakes { get; init; }

        /// <summary>
        /// Runtime-ready Path geometry derived by the same code the Canvas
        /// uses. Empty for a Scene without Paths.
        /// </summary>
        public required List<BakedRouteSurface> RouteSurfaceBakes { get; init; }

        /// <summary>
        /// The Terrain each subtractive Path interval removes, as cells. One
        /// entry per Path that carries at least one such interval; a Path that
        /// only materializes surface is absent rather than present and empty.
        /// </summary>
        public required List<ExportRouteSurfaceCutDocument> RouteSurfaceCutRaster { get; init; }

        /// <summary>
        /// Runtime-ready bridge geometry: the deck, and the posts standing at
        /// its corners. Empty for a Scene without bridges.
        /// </summary>
        public required List<ExportBridgeBakeDocument> BridgeBakes { get; init; }

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
        public required List<ExportRouteSurfaceDocument> RouteSurfaces { get; init; }
        public required List<BridgeDocument> Bridges { get; init; }
        public required TemplateDefinitionDocument? TemplateDefinition { get; init; }
        public required List<TemplateAnchorDocument> TemplateAnchors { get; init; }
        public required decimal DefaultElevationMeters { get; init; }
    }

    /// <summary>
    /// The route shape promised by export schema 10. Authoring-only segment
    /// fields stay out until the runtime contract deliberately advances.
    /// </summary>
    private sealed record ExportRouteSurfaceDocument
    {
        public required string RouteSurfaceId { get; init; }
        public required string AssetKey { get; init; }
        public required List<RouteSurfacePointDocument> Points { get; init; }
        public required List<ExportRouteSurfaceSegmentDocument> Segments { get; init; }
    }

    private sealed record ExportRouteSurfaceSegmentDocument
    {
        public required string SegmentId { get; init; }
        public required int GradePercent { get; init; }

        /// <summary>
        /// Whether this interval materializes its surface or excavates the
        /// Terrain above it. It is the authored meaning rather than something
        /// recovered from whether Terrain happens to overlap the Path today.
        /// </summary>
        public required RouteSegmentOperation Operation { get; init; }

        /// <summary>
        /// The headroom a subtractive interval asks for above its own floor,
        /// in metres. Null for an additive interval, which removes nothing.
        /// </summary>
        public decimal? ClearanceAboveMeters { get; init; }
    }

    /// <summary>
    /// The excavation of one Path, as cells on the water grid - the finest
    /// authored volumetric raster. It sits beside the Scene for the same reason
    /// the water raster does: the Scene block is what the author wrote, and
    /// this is what SceneMaker worked out from it. Deriving it here rather than
    /// leaving it to a consumer keeps the portal rule at an operation
    /// transition in one place.
    /// </summary>
    /// <summary>
    /// The runtime half of a bridge: the planks its deck is built from, the
    /// quad they add up to, and the four posts, all already worked out. A
    /// consumer places what it is given rather than redoing the arithmetic -
    /// which is also what keeps the two from ever disagreeing.
    ///
    /// <para>The authored numbers travel beside the laid-out ones on purpose.
    /// The planks are the authority for what to build; count and gap are there
    /// so a consumer can say what it was asked for, not so it can lay the deck
    /// out a second time.</para>
    /// </summary>
    private sealed record ExportBridgeBakeDocument
    {
        public required string BridgeId { get; init; }

        /// <summary>The Placement Asset this deck is planked with.</summary>
        public required string PlankAssetKey { get; init; }

        /// <summary>The span, end to end.</summary>
        public required decimal LengthMeters { get; init; }

        /// <summary>
        /// Which way the span points, counter-clockwise from +X. Every plank
        /// and every post shares it; a bridge is straight, so one angle
        /// describes the whole thing.
        /// </summary>
        public required decimal HeadingDegrees { get; init; }

        /// <summary>What was authored: how many planks, and the gap between.</summary>
        public required int PlankCount { get; init; }

        public required decimal PlankGapMeters { get; init; }

        /// <summary>What that came out as along the span.</summary>
        public required decimal PlankDepthMeters { get; init; }

        public required List<ExportBridgePlankDocument> Planks { get; init; }

        /// <summary>
        /// The deck as one quad. It is the walking surface, and because a
        /// bridge is exactly one quad its boundary_edges is that surface's
        /// whole outline - unlike a route bake, which lists a loop per
        /// primitive. The gaps between planks are not holes in it: what a
        /// simulation walks on is the deck, and the planks are what it looks
        /// like.
        /// </summary>
        public required IReadOnlyList<RouteSurfaceBakeVertex> Vertices { get; init; }

        public required IReadOnlyList<int> TriangleIndices { get; init; }
        public required IReadOnlyList<RouteSurfaceBoundaryEdge> BoundaryEdges { get; init; }

        /// <summary>
        /// The deck's centerline, in the shape a route bake's has, from the
        /// start end to the end end with the station running 0 to the length.
        /// It is the same line the same flattener draws for a Path, so a
        /// consumer walking a Path and a consumer walking a deck follow one
        /// rule. A straight span flattens to its two ends and nothing between;
        /// the planks are what it looks like, not where one may stand, and
        /// their count has no say here.
        /// </summary>
        public required IReadOnlyList<RouteSurfaceCenterlineSample> CenterlineSamples { get; init; }

        /// <summary>
        /// What lies under each end of the deck, or null where nothing does.
        /// Read from the same columns the Section view shows the author, with
        /// this deck itself taken out of the column. Whether that ground is
        /// within a step of the deck is the Actor's question, not the map's,
        /// so the map says what is there and leaves the comparison to whoever
        /// knows the Actor.
        /// </summary>
        public required ExportBridgeGroundDocument? GroundAtStart { get; init; }

        public required ExportBridgeGroundDocument? GroundAtEnd { get; init; }
        public required List<ExportBridgePostDocument> Posts { get; init; }
    }

    /// <summary>
    /// The top-down visible surface at one point: how high it is, which Asset
    /// it presents, and which authored thing put it there - null for Terrain,
    /// which nothing individually owns. A consumer reads walkability off the
    /// Asset's profile, as it does for every other surface.
    /// </summary>
    private sealed record ExportBridgeGroundDocument
    {
        public required decimal ElevationMeters { get; init; }
        public required string AssetKey { get; init; }
        public required string? SourceId { get; init; }
    }

    /// <summary>
    /// One plank, as a box rather than a mesh: where its centre is, how deep
    /// it runs along the span and how wide across it. A consumer stretches its
    /// own plank model to those two numbers and turns it by the bridge's
    /// heading. Derived, so it carries no instance ID from the Scene.
    /// </summary>
    private sealed record ExportBridgePlankDocument
    {
        public required string PlankId { get; init; }
        public required string AssetKey { get; init; }
        public required decimal XMeters { get; init; }
        public required decimal YMeters { get; init; }

        /// <summary>The deck height this plank's top sits at.</summary>
        public required decimal ElevationMeters { get; init; }

        /// <summary>Along the span.</summary>
        public required decimal DepthMeters { get; init; }

        /// <summary>Across it - always the full deck width.</summary>
        public required decimal WidthMeters { get; init; }
    }

    /// <summary>
    /// One corner post: an ordinary Placement Asset at a worked-out position.
    /// It is derived rather than authored, so it carries no instance ID from
    /// the Scene and cannot be edited on its own - the bridge is what decides
    /// where it stands.
    /// </summary>
    private sealed record ExportBridgePostDocument
    {
        public required string PostId { get; init; }

        /// <summary>Which corner, walking from the start end to the end end.</summary>
        public required BridgeCornerKind Corner { get; init; }

        public required string AssetKey { get; init; }
        public required decimal XMeters { get; init; }
        public required decimal YMeters { get; init; }

        /// <summary>
        /// The deck height this corner sits at. How far a post reaches below
        /// or above it is the model's business, not the map's.
        /// </summary>
        public required decimal ElevationMeters { get; init; }
    }

    private sealed record ExportRouteSurfaceCutDocument
    {
        public required string RouteSurfaceId { get; init; }
        public required List<ExportRouteSurfaceCutCellDocument> Cells { get; init; }
    }

    /// <summary>
    /// One excavated cell: the interval that asked for it, the Path floor that
    /// survives the cut, and the top of the Terrain volume removed above it.
    /// Two numbers rather than a floor and a clearance, because the clearance
    /// is authored per interval while the floor changes along the route.
    /// </summary>
    private sealed record ExportRouteSurfaceCutCellDocument
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        public required string SegmentId { get; init; }
        public required decimal FloorMeters { get; init; }
        public required decimal CutTopMeters { get; init; }
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

    /// <summary>
    /// One water body as it is drawn: the same band a Path and a bridge deck
    /// ship, in the same shape and at the same place, so a consumer that has a
    /// reader for those has one for this.
    ///
    /// <para>It carries no segments. A Path's intervals hold a grade, an
    /// operation and a clearance, and a river has none of the three; its
    /// authored points are in the Scene block and its volume is in the raster.
    /// It carries no single elevation either - a river falls, so the height is
    /// on every vertex and every sample, and a consumer that checked a deck for
    /// one level height must not check this for one.</para>
    /// </summary>
    private sealed record ExportWaterBakeDocument
    {
        public required string WaterBodyId { get; init; }

        /// <summary>What the band is made of, as the raster's entry says it.</summary>
        public required string AssetKey { get; init; }

        public required IReadOnlyList<RouteSurfaceBakeVertex> Vertices { get; init; }
        public required IReadOnlyList<int> TriangleIndices { get; init; }
        public required IReadOnlyList<RouteSurfaceBoundaryEdge> BoundaryEdges { get; init; }

        /// <summary>
        /// The centerline from source to mouth, with the station running from
        /// zero at the source. Its density is the shared flattener's rule and
        /// nothing the raster decided: a consumer cutting the band into chunks
        /// at stations gets the same line the Canvas drew.
        /// </summary>
        public required IReadOnlyList<RouteSurfaceCenterlineSample> CenterlineSamples { get; init; }
    }

    private sealed record ExportAssetProfileDocument
    {
        public required string AssetKey { get; init; }

        /// <summary>
        /// The domain this Asset presents to a simulation - "land", "water",
        /// "wood", and whatever a consumer adds later. Null when the Asset
        /// makes no such claim, which is most Placements: a tree is stood
        /// beside, not walked on. A Placement that is walked on says so here,
        /// because a bridge deck has no Terrain underneath it to say it for
        /// it. It sits on the Asset rather than on every cell or instance so
        /// that one Asset cannot contradict itself, and so a consumer joins it
        /// through the asset_key it reads anyway.
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
