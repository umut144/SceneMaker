using MMORPG.Simulation;
using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void AuthoringMetricsExtendWorldGridWithoutChangingIt()
    {
        Assert.Equal(0.5f, WorldGrid.TileMeters);
        Assert.Equal(16, AuthoringMetrics.AuthoringPixelsPerWorldGridCell);
        Assert.Equal(0.03125m, AuthoringMetrics.MetersPerAuthoringPixel);

        var scene = SceneDocument.Create("scene.metrics", 7, 5);
        Assert.Equal(112, AuthoringMetrics.SceneWidthAuthoringPixels(scene));
        Assert.Equal(80, AuthoringMetrics.SceneHeightAuthoringPixels(scene));
    }

    [Fact]
    public void WorkspaceAndSceneRoundTripCanonically()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "village_workspace");
        var scene = SceneStore.Create(workspace, "scene.village_square", 40, 24);

        Assert.Equal(
            Path.Combine(temporary.Path, "village_workspace"),
            workspace.DirectoryPath);
        Assert.Equal(
            Path.Combine(workspace.DirectoryPath, "scenes", "scene.village_square.scene.json"),
            scene.FilePath);
        Assert.True(File.Exists(Path.Combine(workspace.DirectoryPath, "workspace.json")));
        Assert.True(Directory.Exists(Path.Combine(workspace.DirectoryPath, "scenes")));
        Assert.True(Directory.Exists(Path.Combine(workspace.DirectoryPath, "templates")));

        var loadedWorkspace = WorkspaceStore.Load(Path.Combine(
            workspace.DirectoryPath,
            WorkspaceStore.ManifestFileName));
        var loadedScene = SceneStore.Load(loadedWorkspace, scene.FilePath);

        Assert.Equal(workspace.Document, loadedWorkspace.Document);
        Assert.Equal(
            DocumentJson.Serialize(scene.Document),
            DocumentJson.Serialize(loadedScene.Document));
        Assert.Equal(SceneMakerSchemas.CoordinateSpace, loadedScene.Document.CoordinateSpace);
        Assert.Equal("scene_local_bottom_left_y_up", loadedScene.Document.CoordinateSpace);
        Assert.Equal(DocumentJson.Serialize(scene.Document), File.ReadAllText(scene.FilePath));

        SceneStore.Save(loadedWorkspace, loadedScene);
        Assert.Equal(DocumentJson.Serialize(scene.Document), File.ReadAllText(scene.FilePath));
        Assert.Empty(Directory.EnumerateFiles(
            loadedWorkspace.ScenesDirectoryPath,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void CreateRefusesToOverwriteStableDocuments()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");
        _ = SceneStore.Create(workspace, "scene.one", 1, 1);

        Assert.Throws<SceneMakerDocumentException>(() =>
            WorkspaceStore.Create(temporary.Path, "workspace"));
        Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.Create(workspace, "scene.one", 2, 2));
    }

    [Fact]
    public void TemplateScenesUseTheWorkspaceTemplatesDirectory()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");

        var instance = SceneStore.Create(workspace, "scene.one", 2, 2);
        var template = SceneStore.Create(
            workspace,
            "template.one",
            2,
            2,
            SceneKind.Template);

        Assert.Equal(
            Path.Combine(workspace.ScenesDirectoryPath, "scene.one.scene.json"),
            instance.FilePath);
        Assert.Equal(
            Path.Combine(workspace.TemplatesDirectoryPath, "template.one.scene.json"),
            template.FilePath);
        Assert.Equal(
            [instance.FilePath, template.FilePath],
            SceneStore.EnumeratePaths(workspace));
        Assert.Equal(SceneKind.Template, SceneStore.Load(workspace, template.FilePath).Document.SceneKind);
    }

    [Fact]
    public void WorkspaceLoadRequiresIdDirectoryScenesAndTemplatesDirectories()
    {
        using var temporary = TemporaryDirectory.Create();
        var mismatchedDirectory = Path.Combine(temporary.Path, "wrong_name");
        Directory.CreateDirectory(Path.Combine(mismatchedDirectory, "scenes"));
        File.WriteAllText(
            Path.Combine(mismatchedDirectory, "workspace.json"),
            DocumentJson.Serialize(WorkspaceDocument.Create("right_name")));

        var missingScenesDirectory = Path.Combine(temporary.Path, "missing_scenes");
        Directory.CreateDirectory(missingScenesDirectory);
        File.WriteAllText(
            Path.Combine(missingScenesDirectory, "workspace.json"),
            DocumentJson.Serialize(WorkspaceDocument.Create("missing_scenes")));

        var missingTemplatesDirectory = Path.Combine(temporary.Path, "missing_templates");
        Directory.CreateDirectory(Path.Combine(missingTemplatesDirectory, "scenes"));
        File.WriteAllText(
            Path.Combine(missingTemplatesDirectory, "workspace.json"),
            DocumentJson.Serialize(WorkspaceDocument.Create("missing_templates")));

        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceStore.Load(
            Path.Combine(mismatchedDirectory, "workspace.json")));
        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceStore.Load(
            Path.Combine(missingScenesDirectory, "workspace.json")));
        Assert.Throws<SceneMakerDocumentException>(() => WorkspaceStore.Load(
            Path.Combine(missingTemplatesDirectory, "workspace.json")));
    }

    [Theory]
    [InlineData("Scene")]
    [InlineData("scene-name")]
    [InlineData("scene..name")]
    [InlineData("../scene")]
    [InlineData("")]
    public void StableIdsRejectAmbiguousOrUnsafeNames(string sceneId)
    {
        var scene = SceneDocument.Create(sceneId, 1, 1);
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
    }

    [Fact]
    public void SceneValidationRejectsInvalidCoordinateContractsAndSizes()
    {
        var wrongCoordinates = SceneDocument.Create("scene.test", 1, 1) with
        {
            CoordinateSpace = "center_y_up",
        };
        var zeroWidth = SceneDocument.Create("scene.test", 0, 1);
        var overflowing = SceneDocument.Create("scene.test", int.MaxValue, 1);

        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(wrongCoordinates));
        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(zeroWidth));
        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(overflowing));
    }

    [Fact]
    public void StrictJsonRejectsUnknownMissingAndMalformedData()
    {
        const string unknown = """
            {
              "schema": "srt.scene_maker_scene",
              "version": 5,
              "scene_id": "scene.test",
              "coordinate_space": "scene_local_bottom_left_y_up",
              "size_cells": {"width": 1, "height": 1},
              "unknown": true
            }
            """;
        const string missingSize = """
            {
              "schema": "srt.scene_maker_scene",
              "version": 5,
              "scene_id": "scene.test",
              "coordinate_space": "scene_local_bottom_left_y_up"
            }
            """;

        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentJson.DeserializeScene(unknown));
        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentJson.DeserializeScene(missingSize));
        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentJson.DeserializeScene("{"));
    }

    [Fact]
    public void SceneLoadCannotEscapeItsWorkspace()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");
        var outside = Path.Combine(temporary.Path, "outside.scene.json");
        File.WriteAllText(outside, DocumentJson.Serialize(SceneDocument.Create("outside", 1, 1)));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.Load(workspace, outside));
        Assert.Contains("directly inside", exception.Message);
    }

    [Fact]
    public void SceneLoadRequiresSceneJsonCompoundSuffix()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");
        var plainJsonPath = Path.Combine(workspace.ScenesDirectoryPath, "scene.one.json");
        File.WriteAllText(
            plainJsonPath,
            DocumentJson.Serialize(SceneDocument.Create("scene.one", 1, 1)));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.Load(workspace, plainJsonPath));
        Assert.Contains(".scene.json", exception.Message);
    }

    [Fact]
    public void CanvasNavigationCannotMutateSceneData()
    {
        var scene = SceneDocument.Create("scene.navigation", 30, 18);
        var before = DocumentJson.Serialize(scene);
        var view = new CanvasViewState();

        var pivotLogicalX = (640.0 - view.PanX) / view.Zoom;
        var pivotLogicalY = (360.0 - view.PanY) / view.Zoom;
        view.PanBy(48.0, -96.0);
        view.ZoomBy(1.25, 640.0, 360.0);
        var afterZoomLogicalX = (640.0 - view.PanX) / view.Zoom;
        var afterZoomLogicalY = (360.0 - view.PanY) / view.Zoom;

        Assert.NotEqual(pivotLogicalX, afterZoomLogicalX);
        Assert.NotEqual(pivotLogicalY, afterZoomLogicalY);
        Assert.Equal(before, DocumentJson.Serialize(scene));
        Assert.Equal(1.25, view.Zoom);
    }

    [Fact]
    public void ZoomKeepsItsChosenScreenPivotStable()
    {
        var view = new CanvasViewState();
        const double pivotX = 640.0;
        const double pivotY = 360.0;
        var logicalX = (pivotX - view.PanX) / view.Zoom;
        var logicalY = (pivotY - view.PanY) / view.Zoom;

        view.ZoomBy(1.25, pivotX, pivotY);

        Assert.Equal(logicalX, (pivotX - view.PanX) / view.Zoom, precision: 10);
        Assert.Equal(logicalY, (pivotY - view.PanY) / view.Zoom, precision: 10);
    }

    [Fact]
    public void KeyboardPanUsesConstantSpeedAndStopsOnRelease()
    {
        var horizontal = new CanvasViewState();
        _ = horizontal.AdvanceKeyboardPan(1.0, 0.0, 0.2);
        var firstSpeed = horizontal.KeyboardPanVelocityX;
        _ = horizontal.AdvanceKeyboardPan(1.0, 0.0, 0.2);
        Assert.Equal(CanvasViewState.KeyboardPanMaximumSpeed, firstSpeed, precision: 8);
        Assert.Equal(firstSpeed, horizontal.KeyboardPanVelocityX, precision: 8);
        Assert.InRange(horizontal.KeyboardPanVelocityX, 0.0, CanvasViewState.KeyboardPanMaximumSpeed);

        var diagonal = new CanvasViewState();
        _ = diagonal.AdvanceKeyboardPan(1.0, 1.0, 2.0);
        Assert.Equal(CanvasViewState.KeyboardPanMaximumSpeed, diagonal.KeyboardPanVelocityX, precision: 8);
        Assert.Equal(CanvasViewState.KeyboardPanMaximumSpeed, diagonal.KeyboardPanVelocityY, precision: 8);
        Assert.True(Math.Sqrt((diagonal.KeyboardPanVelocityX * diagonal.KeyboardPanVelocityX)
            + (diagonal.KeyboardPanVelocityY * diagonal.KeyboardPanVelocityY))
            > CanvasViewState.KeyboardPanMaximumSpeed);

        Assert.False(horizontal.AdvanceKeyboardPan(0.0, 0.0, 1.0));
        Assert.Equal(0.0, horizontal.KeyboardPanVelocityX, precision: 8);
        Assert.True(horizontal.PanX > 32.0);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"scene-maker-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
