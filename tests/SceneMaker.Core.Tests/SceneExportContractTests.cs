using System.Text.Json;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The export is the contract with the Bevy runtime, which parses it strictly.
/// These tests pin the shape rather than the values: a renamed, added or
/// dropped key fails here instead of in a runtime parser in another repository.
/// When one of them fails, the reader in BevyProjects/world01 has to follow and
/// <see cref="SceneExport.Version"/> has to go up.
/// </summary>
public sealed class SceneExportContractTests
{
    [Fact]
    public void TheExportedDocumentHasExactlyTheAgreedShape()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace);

        Assert.Equal(
            [
                "format", "version", "workspace_key", "grid", "asset_profiles",
                "water_raster", "route_surface_bakes", "route_surface_cut_raster",
                "bridge_bakes", "scene",
            ],
            Keys(root));
        Assert.Equal("scene_maker_scene_export", root.GetProperty("format").GetString());
        Assert.Equal(15, root.GetProperty("version").GetInt32());
        Assert.Equal("test_world", root.GetProperty("workspace_key").GetString());
        Assert.Equal(
            [
                "terrain_cell_meters", "authoring_pixels_per_meter", "game_pixels_per_meter",
                "water_cell_meters",
            ],
            Keys(root.GetProperty("grid")));
        Assert.Equal(0.5m, root.GetProperty("grid").GetProperty("water_cell_meters").GetDecimal());
    }

    [Fact]
    public void TheEmbeddedSceneHasExactlyTheAgreedShape()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Export(workspace).GetProperty("scene");

        Assert.Equal(
            [
                "schema", "version", "scene_id", "scene_kind", "coordinate_space",
                "size_cells", "terrain_cells", "props", "water_bodies", "route_surfaces",
                "bridges", "template_definition", "template_anchors",
                "default_elevation_meters",
            ],
            Keys(scene));
        Assert.Equal("srt.scene_maker_scene", scene.GetProperty("schema").GetString());
        Assert.Equal(15, scene.GetProperty("version").GetInt32());
        Assert.Equal("instance", scene.GetProperty("scene_kind").GetString());
        Assert.Equal(
            "scene_local_bottom_left_y_up",
            scene.GetProperty("coordinate_space").GetString());
        Assert.Equal(["width", "height"], Keys(scene.GetProperty("size_cells")));
        Assert.Equal(
            ["x", "y", "asset_key", "elevation_meters"],
            Keys(scene.GetProperty("terrain_cells").EnumerateArray().First()));
        Assert.Equal(
            ["instance_id", "asset_key", "position_authoring_px", "elevation_meters"],
            Keys(scene.GetProperty("props").EnumerateArray().First()));
    }

    [Fact]
    public void PropProfilesCarryDerivedGeometryAndTerrainProfilesCarryNone()
    {
        using var workspace = TestWorkspace.Create();
        var profiles = Export(workspace).GetProperty("asset_profiles").EnumerateArray().ToList();

        Assert.Equal(
            ["grass", "portal", "river", "sand", "stone"],
            profiles.Select(profile => profile.GetProperty("asset_key").GetString()));
        foreach (var profile in profiles)
            Assert.Equal(["asset_key", "surface", "footprint_meters", "anchor_meters"], Keys(profile));

        var stone = profiles.Single(profile => profile.GetProperty("asset_key").GetString() == "stone");
        Assert.Equal(["width", "height"], Keys(stone.GetProperty("footprint_meters")));
        Assert.Equal(["x", "y"], Keys(stone.GetProperty("anchor_meters")));
        // This Placement presents no surface. Most do not - a stone is stood
        // beside, not walked on - but the field is open to them now, because a
        // bridge deck is walked on and has no Terrain under it to say so.
        Assert.Equal(JsonValueKind.Null, stone.GetProperty("surface").ValueKind);

        var grass = profiles.Single(profile => profile.GetProperty("asset_key").GetString() == "grass");
        Assert.Equal("land", grass.GetProperty("surface").GetString());
        Assert.Equal(JsonValueKind.Null, grass.GetProperty("footprint_meters").ValueKind);
        Assert.Equal(JsonValueKind.Null, grass.GetProperty("anchor_meters").ValueKind);
        Assert.Equal(
            "sand",
            profiles.Single(profile => profile.GetProperty("asset_key").GetString() == "sand")
                .GetProperty("surface").GetString());
        // Water is a surface like any other. Nothing in SceneMaker reads the
        // token; the Asset carries it and the simulation gives it meaning.
        Assert.Equal(
            "water",
            profiles.Single(profile => profile.GetProperty("asset_key").GetString() == "river")
                .GetProperty("surface").GetString());
    }

    /// <summary>
    /// A bridge ships both halves: what was authored - a deck Asset, a count
    /// and a gap - and what that laid out as. The planks are the authority for
    /// what to build; the parameters are there so a consumer can say what it
    /// was asked for, not so it can lay the deck out a second time.
    /// </summary>
    [Fact]
    public void BridgesShipTheirPlanksTheirDeckQuadAndTheirFourPosts()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace, WithBridge);

        var authored = Assert.Single(
            root.GetProperty("scene").GetProperty("bridges").EnumerateArray());
        Assert.Equal(
            [
                "bridge_id", "plank_asset_key", "anchor_asset_key", "start_authoring_px",
                "end_authoring_px", "width_meters", "elevation_meters", "plank_count",
                "plank_gap_meters",
            ],
            Keys(authored));

        var bake = Assert.Single(root.GetProperty("bridge_bakes").EnumerateArray());
        Assert.Equal(
            [
                "bridge_id", "plank_asset_key", "length_meters", "heading_degrees",
                "plank_count", "plank_gap_meters", "plank_depth_meters", "planks",
                "vertices", "triangle_indices", "boundary_edges", "centerline_samples",
                "ground_at_start", "ground_at_end", "posts",
            ],
            Keys(bake));

        var plank = bake.GetProperty("planks").EnumerateArray().First();
        Assert.Equal(
            [
                "plank_id", "asset_key", "x_meters", "y_meters", "elevation_meters",
                "depth_meters", "width_meters",
            ],
            Keys(plank));

        var post = bake.GetProperty("posts").EnumerateArray().First();
        Assert.Equal(
            ["post_id", "corner", "asset_key", "x_meters", "y_meters", "elevation_meters"],
            Keys(post));
    }

    /// <summary>
    /// A deck is a way, and ways have a centerline in this contract already, so
    /// a bridge carries one in the same shape at the same place: from start to
    /// end, station 0 to the length, from the flattener that draws a Path's -
    /// which for a straight span is its two ends and nothing between. The
    /// planks are what it looks like, not where one may stand, so their count
    /// has no say in it.
    ///
    /// <para>Beneath each end the export says what the Section view would show
    /// the author there with the deck lifted off: here grass at the start and
    /// the river at the end. Whether that is within a step is the Actor's
    /// question, and the map does not answer it.</para>
    /// </summary>
    [Fact]
    public void ABridgeCarriesAPathsCenterlineAndSaysWhatLiesUnderEachEnd()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(
            workspace,
            (scene, ws) => BridgeEditing.Place(
                WithRiver(scene, ws),
                ws.Props,
                128, 128, 128, 32,
                "portal", "stone",
                2m, 3m,
                BridgeEditing.DefaultPlankCount,
                BridgeEditing.DefaultPlankGapMeters));
        var bake = Assert.Single(root.GetProperty("bridge_bakes").EnumerateArray());

        var samples = bake.GetProperty("centerline_samples").EnumerateArray().ToList();
        Assert.Equal(2, samples.Count);
        Assert.Equal(
            ["x_meters", "y_meters", "elevation_meters", "width_meters", "station_meters", "authored_point_index"],
            Keys(samples[0]));
        Assert.Equal(4m, samples[0].GetProperty("x_meters").GetDecimal());
        Assert.Equal(4m, samples[0].GetProperty("y_meters").GetDecimal());
        Assert.Equal(0m, samples[0].GetProperty("station_meters").GetDecimal());
        Assert.Equal(0, samples[0].GetProperty("authored_point_index").GetInt32());
        Assert.Equal(1m, samples[1].GetProperty("y_meters").GetDecimal());
        Assert.Equal(3m, samples[1].GetProperty("station_meters").GetDecimal());
        Assert.Equal(1, samples[1].GetProperty("authored_point_index").GetInt32());
        Assert.All(samples, sample =>
        {
            Assert.Equal(3m, sample.GetProperty("elevation_meters").GetDecimal());
            Assert.Equal(2m, sample.GetProperty("width_meters").GetDecimal());
        });
        Assert.Equal(
            bake.GetProperty("length_meters").GetDecimal(),
            samples[1].GetProperty("station_meters").GetDecimal());

        // The contract Scene's grass lies at the ground default of one metre.
        var start = bake.GetProperty("ground_at_start");
        Assert.Equal(["elevation_meters", "asset_key", "source_id"], Keys(start));
        Assert.Equal(SceneDocument.GroundElevationMeters, start.GetProperty("elevation_meters").GetDecimal());
        Assert.Equal("grass", start.GetProperty("asset_key").GetString());
        Assert.Equal(JsonValueKind.Null, start.GetProperty("source_id").ValueKind);

        var end = bake.GetProperty("ground_at_end");
        Assert.Equal(2m, end.GetProperty("elevation_meters").GetDecimal());
        Assert.Equal("river", end.GetProperty("asset_key").GetString());
        Assert.StartsWith("river_", end.GetProperty("source_id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAuthoredCurveStaysInTheSceneAndItsRasterSitsBesideIt()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace, WithRiver);

        var body = Assert.Single(root.GetProperty("scene").GetProperty("water_bodies").EnumerateArray());
        Assert.Equal(
            ["water_body_id", "water_kind", "asset_key", "points"],
            Keys(body));
        Assert.Equal("river_0001", body.GetProperty("water_body_id").GetString());
        Assert.Equal("river", body.GetProperty("water_kind").GetString());

        // The heights live on the points, not on the body: one river falls,
        // deepens and ducks under a hill along its length.
        var point = body.GetProperty("points").EnumerateArray().First();
        Assert.Equal(
            [
                "position_authoring_px", "mode", "handle_in_authoring_px",
                "handle_out_authoring_px", "elevation_meters", "channel_depth_meters",
                "clearance_above_meters", "width_meters",
            ],
            Keys(point));
        Assert.Equal("linear", point.GetProperty("mode").GetString());
        Assert.Equal(["x", "y"], Keys(point.GetProperty("position_authoring_px")));
        Assert.Equal(2.0m, point.GetProperty("elevation_meters").GetDecimal());
        Assert.Equal(1.0m, point.GetProperty("width_meters").GetDecimal());

        var raster = Assert.Single(root.GetProperty("water_raster").EnumerateArray());
        Assert.Equal(
            ["water_body_id", "water_kind", "asset_key", "cells"],
            Keys(raster));
        Assert.Equal("river_0001", raster.GetProperty("water_body_id").GetString());

        // Two spans sharing a floor, as three numbers: water fills bed..surface,
        // and bed..cut_top is what the Terrain gives up for it.
        var cell = raster.GetProperty("cells").EnumerateArray().First();
        Assert.Equal(
            ["x", "y", "bed_meters", "surface_meters", "cut_top_meters"],
            Keys(cell));
        Assert.Equal(1.5m, cell.GetProperty("bed_meters").GetDecimal());
        Assert.Equal(2.0m, cell.GetProperty("surface_meters").GetDecimal());
        Assert.Equal(7.0m, cell.GetProperty("cut_top_meters").GetDecimal());
    }

    [Fact]
    public void ASceneWithoutWaterCarriesEmptyWaterArraysRatherThanNull()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace);

        Assert.Empty(root.GetProperty("water_raster").EnumerateArray());
        Assert.Empty(root.GetProperty("route_surface_bakes").EnumerateArray());
        Assert.Empty(root.GetProperty("route_surface_cut_raster").EnumerateArray());
        Assert.Empty(root.GetProperty("bridge_bakes").EnumerateArray());
        Assert.Empty(root.GetProperty("scene").GetProperty("bridges").EnumerateArray());
        Assert.Empty(root.GetProperty("scene").GetProperty("water_bodies").EnumerateArray());
        Assert.Empty(root.GetProperty("scene").GetProperty("route_surfaces").EnumerateArray());
    }

    [Fact]
    public void PathsShipAuthoredGradesAndTheCanvasRuntimeBake()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace, WithThreePaths);
        var routes = root.GetProperty("scene").GetProperty("route_surfaces")
            .EnumerateArray().ToList();
        var bakes = root.GetProperty("route_surface_bakes").EnumerateArray().ToList();

        Assert.Equal(3, routes.Count);
        Assert.All(routes, route => Assert.Equal(
            ["route_surface_id", "asset_key", "points", "segments"],
            Keys(route)));
        Assert.All(routes, route => Assert.All(
            route.GetProperty("segments").EnumerateArray(),
            segment => Assert.Equal(
                ["segment_id", "grade_percent", "operation", "clearance_above_meters"],
                Keys(segment))));
        Assert.Equal([0, 25, 50], routes.Select(route => route
            .GetProperty("segments")[0].GetProperty("grade_percent").GetInt32()));
        Assert.Equal(
            ["route_0001", "route_0002", "route_0003"],
            bakes.Select(bake => bake.GetProperty("route_surface_id").GetString()));
        var bake = bakes[1];
        Assert.Equal(
            [
                "route_surface_id", "asset_key", "vertices", "triangle_indices",
                "boundary_edges", "centerline_samples", "segments",
            ],
            Keys(bake));
        Assert.NotEmpty(bake.GetProperty("vertices").EnumerateArray());
        Assert.NotEmpty(bake.GetProperty("triangle_indices").EnumerateArray());
        Assert.NotEmpty(bake.GetProperty("boundary_edges").EnumerateArray());
        Assert.Equal(
            ["x_meters", "y_meters", "elevation_meters"],
            Keys(bake.GetProperty("vertices")[0]));
        Assert.Equal(
            ["start_vertex_index", "end_vertex_index"],
            Keys(bake.GetProperty("boundary_edges")[0]));
        Assert.Equal(
            [
                "x_meters", "y_meters", "elevation_meters", "width_meters",
                "station_meters", "authored_point_index",
            ],
            Keys(bake.GetProperty("centerline_samples")[0]));
        Assert.Equal(
            [
                "segment_id", "grade_percent", "operation", "clearance_above_meters",
                "start_point_index", "end_point_index",
                "start_sample_index", "end_sample_index",
            ],
            Keys(bake.GetProperty("segments")[0]));
        Assert.All(bakes, baked => Assert.All(
            baked.GetProperty("segments").EnumerateArray(),
            segment =>
            {
                Assert.Equal("additive", segment.GetProperty("operation").GetString());
                Assert.Equal(
                    JsonValueKind.Null,
                    segment.GetProperty("clearance_above_meters").ValueKind);
            }));
        Assert.Contains(
            bake.GetProperty("centerline_samples").EnumerateArray(),
            sample => sample.GetProperty("authored_point_index").GetInt32() == 1);
        Assert.Equal(25, bake.GetProperty("segments")[0].GetProperty("grade_percent").GetInt32());
    }

    /// <summary>
    /// Export 10 refused a subtractive interval outright rather than writing
    /// it through the additive shape. Export 11 carries the authored meaning
    /// and the cells it removes, so the excavation can neither be lost nor
    /// silently turned into a materialized surface.
    /// </summary>
    [Fact]
    public void ASubtractivePathShipsItsExcavationInsteadOfBeingRefused()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace, WithTunnelPath);
        var segment = root.GetProperty("scene").GetProperty("route_surfaces")[0]
            .GetProperty("segments")[0];

        Assert.Equal("subtractive", segment.GetProperty("operation").GetString());
        Assert.Equal(2.0m, segment.GetProperty("clearance_above_meters").GetDecimal());

        var cuts = root.GetProperty("route_surface_cut_raster").EnumerateArray().ToList();
        var cut = Assert.Single(cuts);
        Assert.Equal(["route_surface_id", "cells"], Keys(cut));
        Assert.Equal("route_0001", cut.GetProperty("route_surface_id").GetString());
        var cells = cut.GetProperty("cells").EnumerateArray().ToList();
        Assert.NotEmpty(cells);
        Assert.Equal(
            ["x", "y", "segment_id", "floor_meters", "cut_top_meters"],
            Keys(cells[0]));
        Assert.All(cells, cell =>
        {
            Assert.Equal(
                "route_0001.segment_0001",
                cell.GetProperty("segment_id").GetString());
            Assert.Equal(1.0m, cell.GetProperty("floor_meters").GetDecimal());
            Assert.Equal(3.0m, cell.GetProperty("cut_top_meters").GetDecimal());
        });

        // The bake repeats the authored meaning so a consumer reading only the
        // runtime half can tell the two kinds of interval apart.
        var baked = root.GetProperty("route_surface_bakes")[0].GetProperty("segments")[0];
        Assert.Equal("subtractive", baked.GetProperty("operation").GetString());
        Assert.Equal(2.0m, baked.GetProperty("clearance_above_meters").GetDecimal());
    }

    /// <summary>
    /// An excavation the author drew outside the Scene removes nothing. That is
    /// an ordinary state of an unfinished map, so it warns rather than refusing
    /// - but it must not pass in silence, which is the one thing an omitted
    /// excavation would look like from the outside.
    /// </summary>
    [Fact]
    public void AnExcavationThatRemovesNothingWarnsInsteadOfPassingSilently()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        var document = WithTunnelPath(TestScenes.Instance(workspace), workspace);
        var offMap = document with
        {
            RouteSurfaces =
            [
                document.RouteSurfaces[0] with
                {
                    Points =
                    [
                        .. document.RouteSurfaces[0].Points.Select(static point => point with
                        {
                            PositionAuthoringPx = new AuthoringPixelPosition
                            {
                                X = point.PositionAuthoringPx.X,
                                Y = -4096,
                            },
                        }),
                    ],
                },
            ],
        };

        var warnings = SceneExport.Warnings(offMap, session.Configuration.Metrics);

        Assert.Contains(
            warnings,
            warning => warning.Contains(
                "route_0001.segment_0001", StringComparison.Ordinal)
                && warning.Contains("removes no Terrain", StringComparison.Ordinal));
    }

    [Fact]
    public void NoEditorOnlyDataReachesTheExport()
    {
        using var workspace = TestWorkspace.Create();
        var json = ExportedJson(workspace);

        // Authoring colours belong to SceneMaker, never to the runtime.
        Assert.DoesNotContain("#99E550", json, StringComparison.Ordinal);
        Assert.DoesNotContain("color", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryCellAndEveryPropCarriesItsHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Export(workspace).GetProperty("scene");

        Assert.Equal(1.0m, scene.GetProperty("default_elevation_meters").GetDecimal());
        Assert.All(
            scene.GetProperty("terrain_cells").EnumerateArray(),
            cell => Assert.Equal(1.0m, cell.GetProperty("elevation_meters").GetDecimal()));
        Assert.All(
            scene.GetProperty("props").EnumerateArray(),
            prop => Assert.Equal(1.0m, prop.GetProperty("elevation_meters").GetDecimal()));
    }

    private static IEnumerable<string> Keys(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    private static JsonElement Export(
        TestWorkspace workspace,
        Func<SceneDocument, TestWorkspace, SceneDocument>? extend = null)
    {
        using var parsed = JsonDocument.Parse(ExportedJson(workspace, extend));
        return parsed.RootElement.Clone();
    }

    /// <summary>
    /// The worked example from the contract: water at 2 m, half a metre deep,
    /// five metres of headroom - so a bed at 1.5 m and a cut up to 7 m.
    /// </summary>
    private static SceneDocument WithRiver(SceneDocument scene, TestWorkspace workspace) =>
        WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

    /// <summary>
    /// A three-metre span across the small contract Scene, two metres wide and
    /// clear of the Placement that Scene already carries.
    /// </summary>
    private static SceneDocument WithBridge(SceneDocument scene, TestWorkspace workspace) =>
        BridgeEditing.Place(
            scene,
            workspace.Props,
            64,
            128,
            160,
            128,
            "portal",
            "stone",
            2m,
            1m,
            BridgeEditing.DefaultPlankCount,
            BridgeEditing.DefaultPlankGapMeters);

    private static SceneDocument WithThreePaths(SceneDocument scene, TestWorkspace workspace)
    {
        foreach (var grade in new[]
                 {
                     RouteGradePreset.Level,
                     RouteGradePreset.UpTwentyFivePercent,
                     RouteGradePreset.UpFiftyPercent,
                 })
        {
            var y = 32 + scene.RouteSurfaces.Count * 48;
            var draft = new[]
            {
                new GradedRouteDraftPoint(
                    32, y, RoutePointMode.Linear, 1m, RouteGradePreset.Level),
                new GradedRouteDraftPoint(
                    160, y, RoutePointMode.Linear, 1m, grade),
            };
            var points = RouteSurfaceEditing.ResolveGradedCurve(workspace.Metrics, 1m, draft);
            scene = RouteSurfaceEditing.Place(
                scene,
                workspace.Terrain,
                workspace.Metrics,
                points,
                [RouteSegmentAuthoring.Additive(grade)],
                "grass");
        }
        return scene;
    }

    /// <summary>
    /// One level Path authored as a tunnel: a subtractive interval asking for
    /// two metres of headroom over a floor at one metre.
    /// </summary>
    private static SceneDocument WithTunnelPath(SceneDocument scene, TestWorkspace workspace)
    {
        var draft = new[]
        {
            new GradedRouteDraftPoint(
                32, 32, RoutePointMode.Linear, 2m, RouteGradePreset.Level),
            new GradedRouteDraftPoint(
                160, 32, RoutePointMode.Linear, 2m, RouteGradePreset.Level),
        };
        var points = RouteSurfaceEditing.ResolveGradedCurve(workspace.Metrics, 1m, draft);
        return RouteSurfaceEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Metrics,
            points,
            [RouteSegmentAuthoring.Subtractive(RouteGradePreset.Level, 2.0m)],
            "grass");
    }

    /// <summary>Exports a Scene carrying one Terrain cell kind and one Prop.</summary>
    private static string ExportedJson(
        TestWorkspace workspace,
        Func<SceneDocument, TestWorkspace, SceneDocument>? extend = null)
    {
        var document = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 32, 32, "stone");
        if (extend is not null) document = extend(document, workspace);
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            document);
        return File.ReadAllText(SceneExport.Write(session, scene).Path);
    }
}
