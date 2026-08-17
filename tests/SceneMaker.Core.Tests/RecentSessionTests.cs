using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RecentSessionTests
{
    [Fact]
    public void RecentSessionRoundTripsWorkspaceAndSceneCanonically()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "recent_workspace");
        var scene = SceneStore.Create(workspace, "scene.recent", 3, 2);
        var statePath = Path.Combine(temporary.Path, "state", "recent_session.json");

        RecentSessionStore.Save(statePath, workspace, scene);
        var firstBytes = File.ReadAllBytes(statePath);
        var loaded = RecentSessionStore.Load(statePath);
        RecentSessionStore.Save(statePath, workspace, scene);

        Assert.NotNull(loaded);
        Assert.Equal(
            Path.Combine(workspace.DirectoryPath, WorkspaceStore.ManifestFileName),
            loaded.WorkspaceManifestPath);
        Assert.Equal(
            Path.Combine("scenes", "scene.recent.scene.json"),
            loaded.SceneRelativePath);
        Assert.Equal(firstBytes, File.ReadAllBytes(statePath));
        Assert.DoesNotContain(workspace.DirectoryPath, loaded.SceneRelativePath);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(statePath)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void RecentSessionCanRememberWorkspaceWithoutScene()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace_only");
        var statePath = Path.Combine(temporary.Path, "recent_session.json");

        RecentSessionStore.Save(statePath, workspace, scene: null);
        var loaded = RecentSessionStore.Load(statePath);

        Assert.NotNull(loaded);
        Assert.Null(loaded.SceneRelativePath);
    }

    [Fact]
    public void RecentSessionRestoresTemplatePathsRelativeToTheWorkspace()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");
        var template = SceneStore.Create(workspace, "template.one", 2, 2, SceneKind.Template);
        var statePath = Path.Combine(temporary.Path, "recent_session.json");

        RecentSessionStore.Save(statePath, workspace, template);
        var loaded = RecentSessionStore.Load(statePath);

        Assert.Equal(
            Path.Combine("templates", "template.one.scene.json"),
            loaded!.SceneRelativePath);
    }

    [Fact]
    public void MissingRecentSessionIsAnEmptyInitialState()
    {
        using var temporary = TemporaryDirectory.Create();
        Assert.Null(RecentSessionStore.Load(Path.Combine(temporary.Path, "missing.json")));
    }

    [Fact]
    public void RecentSessionRejectsUnknownFieldsAndRelativePaths()
    {
        using var temporary = TemporaryDirectory.Create();
        var unknownPath = Path.Combine(temporary.Path, "unknown.json");
        File.WriteAllText(unknownPath, """
            {
              "schema": "srt.scene_maker_recent_session",
              "version": 2,
              "workspace_manifest_path": "/tmp/workspace.json",
              "scene_relative_path": null,
              "unknown": true
            }
            """);
        var relativePath = Path.Combine(temporary.Path, "relative.json");
        File.WriteAllText(relativePath, """
            {
              "schema": "srt.scene_maker_recent_session",
              "version": 2,
              "workspace_manifest_path": "workspace.json",
              "scene_relative_path": null
            }
            """);

        Assert.Throws<SceneMakerDocumentException>(() => RecentSessionStore.Load(unknownPath));
        Assert.Throws<SceneMakerDocumentException>(() => RecentSessionStore.Load(relativePath));
    }

    [Fact]
    public void RecentSessionRejectsSceneOutsideWorkspace()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = WorkspaceStore.Create(temporary.Path, "workspace");
        var outside = new LoadedScene(
            Path.Combine(temporary.Path, "outside.scene.json"),
            SceneDocument.Create("outside", 1, 1));

        Assert.Throws<SceneMakerDocumentException>(() => RecentSessionStore.Save(
            Path.Combine(temporary.Path, "recent_session.json"),
            workspace,
            outside));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"scene-maker-session-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
