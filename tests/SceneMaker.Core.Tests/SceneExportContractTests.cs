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
                "required_template_groups", "scene",
            ],
            Keys(root));
        Assert.Equal("scene_maker_scene_export", root.GetProperty("format").GetString());
        Assert.Equal(4, root.GetProperty("version").GetInt32());
        Assert.Equal("test_world", root.GetProperty("workspace_key").GetString());
        Assert.Equal(
            ["terrain_cell_meters", "authoring_pixels_per_meter", "game_pixels_per_meter"],
            Keys(root.GetProperty("grid")));
    }

    [Fact]
    public void TheEmbeddedSceneHasExactlyTheAgreedShape()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Export(workspace).GetProperty("scene");

        Assert.Equal(
            [
                "schema", "version", "scene_id", "scene_kind", "coordinate_space",
                "size_cells", "terrain_cells", "props", "template_definition",
                "template_anchors", "default_elevation_meters",
            ],
            Keys(scene));
        Assert.Equal("srt.scene_maker_scene", scene.GetProperty("schema").GetString());
        Assert.Equal(7, scene.GetProperty("version").GetInt32());
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
            ["grass", "portal", "sand", "stone"],
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
    public void TheRequiredTemplateGroupsAreTheOnesTheAnchorsAskFor()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = TestScenes.Instance(workspace);
        scene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 32, 32, 4);
        scene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 64, 64, 2);
        scene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 96, 96, 4);

        var path = SceneExport.Write(
            session,
            new LoadedScene(
                Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
                scene));

        using var parsed = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            [2, 4],
            parsed.RootElement.GetProperty("required_template_groups")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
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

    [Fact]
    public void ASceneWithoutAnchorsRequiresNoTemplateGroups()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Empty(Export(workspace).GetProperty("required_template_groups").EnumerateArray());
    }

    private static IEnumerable<string> Keys(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    private static JsonElement Export(TestWorkspace workspace)
    {
        using var parsed = JsonDocument.Parse(ExportedJson(workspace));
        return parsed.RootElement.Clone();
    }

    /// <summary>Exports a Scene carrying one Terrain cell kind and one Prop.</summary>
    private static string ExportedJson(TestWorkspace workspace)
    {
        var document = PropEditing.Place(
            TestScenes.Instance(workspace), workspace.Props, 32, 32, "stone");
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            document);
        return File.ReadAllText(SceneExport.Write(session, scene));
    }
}
