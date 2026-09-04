using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A Scene id names one Scene in the whole Workspace. Consumers name a Template
/// by that id and have to survive a reconnect with it, and the exports live in
/// one flat directory named by id - so a collision would silently cost a file.
/// </summary>
public sealed class SceneIdentityTests
{
    [Fact]
    public void ATemplateCannotTakeTheIdOfAnInstance()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(session.Workspace, "grove", 4, 4);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateTemplate(session.Workspace, "grove", 2, 2, 1, 0, 0));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceCannotTakeTheIdOfATemplate()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateTemplate(session.Workspace, "grove", 2, 2, 1, 0, 0);

        Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateInstance(session.Workspace, "grove", 4, 4));
    }

    [Fact]
    public void TheSameIdTwiceInOneDirectoryIsStillRefused()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(session.Workspace, "grove", 4, 4);

        Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateInstance(session.Workspace, "grove", 4, 4));
    }

    [Fact]
    public void ExportingRefusesAWorkspaceWhoseIdsCollide()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(session.Workspace, "grove", 4, 4);
        // Both files are individually valid - an Instance in scenes/ and a
        // Template in templates/, each matching its directory. Only their
        // shared id is the problem, and it is invisible until the export names
        // both of them the same file. Creating this pair through the store is
        // refused, so a hand-edited Workspace is the only way in.
        File.WriteAllText(
            Path.Combine(
                session.Workspace.TemplatesDirectoryPath,
                "grove" + SceneStore.FileSuffix),
            DocumentJson.Serialize(SceneDocument.CreateTemplate("grove", 2, 2, 1, 0, 0)));

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => SceneExport.WriteWorkspace(session));

        Assert.Contains("names 2 Scenes", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(
            Path.Combine(workspace.RootPath, SceneExport.DirectoryName)));
    }

    [Fact]
    public void WorkspaceExportValidatesEverySceneBeforeReplacingAnyOutput()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        _ = SceneStore.CreateInstance(session.Workspace, "a", 4, 4);
        var second = SceneStore.CreateInstance(session.Workspace, "b", 4, 4);
        var subtractive = RouteSurfaceEditing.Place(
            second.Document,
            session.TerrainAssets,
            session.Configuration.Metrics,
            [
                RouteSurfaceEditing.Point(32, 32, RoutePointMode.Linear, 1m),
                RouteSurfaceEditing.Point(96, 32, RoutePointMode.Linear, 1m),
            ],
            [RouteSegmentAuthoring.Subtractive(RouteGradePreset.Level, 1m)],
            "grass");
        SceneStore.Save(session.Workspace, second with { Document = subtractive });
        var exportDirectory = Path.Combine(workspace.RootPath, SceneExport.DirectoryName);
        Directory.CreateDirectory(exportDirectory);
        var firstExport = Path.Combine(exportDirectory, "a" + SceneExport.FileSuffix);
        File.WriteAllText(firstExport, "previous export");

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => SceneExport.WriteWorkspace(session));

        Assert.Contains("subtractive", exception.Message, StringComparison.Ordinal);
        Assert.Equal("previous export", File.ReadAllText(firstExport));
        Assert.False(File.Exists(Path.Combine(exportDirectory, "b" + SceneExport.FileSuffix)));
    }

    [Fact]
    public void ASceneIsFoundByItsIdInEitherDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(session.Workspace, "map", 4, 4);
        SceneStore.CreateTemplate(session.Workspace, "grove", 2, 2, 1, 0, 0);

        Assert.EndsWith(
            Path.Combine("scenes", "map" + SceneStore.FileSuffix),
            SceneStore.ResolvePath(session.Workspace, "map"),
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("templates", "grove" + SceneStore.FileSuffix),
            SceneStore.ResolvePath(session.Workspace, "grove"),
            StringComparison.Ordinal);
    }
}
