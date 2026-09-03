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
            ["format", "version", "workspace_key", "grid", "asset_profiles", "water_raster", "scene"],
            Keys(root));
        Assert.Equal("scene_maker_scene_export", root.GetProperty("format").GetString());
        Assert.Equal(9, root.GetProperty("version").GetInt32());
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
                "size_cells", "terrain_cells", "props", "water_bodies",
                "template_definition", "template_anchors", "default_elevation_meters",
            ],
            Keys(scene));
        Assert.Equal("srt.scene_maker_scene", scene.GetProperty("schema").GetString());
        Assert.Equal(10, scene.GetProperty("version").GetInt32());
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
        // A Prop has no surface: only Terrain presents one.
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
        // deepens and ducks under a mountain along its length.
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
        Assert.Empty(root.GetProperty("scene").GetProperty("water_bodies").EnumerateArray());
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
