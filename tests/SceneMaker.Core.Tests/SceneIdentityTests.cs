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
        SceneStore.CreateInstance(workspace.Game, "grove", 4, 4);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateTemplate(workspace.Game, "grove", 2, 2, 1, 0, 0));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceCannotTakeTheIdOfATemplate()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateTemplate(workspace.Game, "grove", 2, 2, 1, 0, 0);

        Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateInstance(workspace.Game, "grove", 4, 4));
    }

    [Fact]
    public void TheSameIdTwiceInOneDirectoryIsStillRefused()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(workspace.Game, "grove", 4, 4);

        Assert.Throws<SceneMakerDocumentException>(() =>
            SceneStore.CreateInstance(workspace.Game, "grove", 4, 4));
    }

    [Fact]
    public void ExportingRefusesAWorkspaceWhoseIdsCollide()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(workspace.Game, "grove", 4, 4);
        // Both files are individually valid - an Instance in scenes/ and a
        // Template in templates/, each matching its directory. Only their
        // shared id is the problem, and it is invisible until the export names
        // both of them the same file. Creating this pair through the store is
        // refused, so a hand-edited Workspace is the only way in.
        File.WriteAllText(
            Path.Combine(
                workspace.Game.TemplatesDirectoryPath,
                "grove" + SceneStore.FileSuffix),
            DocumentJson.Serialize(SceneDocument.CreateTemplate("grove", 2, 2, 1, 0, 0)));

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => SceneExport.WriteGame(session, workspace.Game));

        Assert.Contains("names 2 Scenes", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(
            Path.Combine(workspace.Game.DirectoryPath, SceneExport.DirectoryName)));
    }

    [Fact]
    public void WorkspaceExportValidatesEverySceneBeforeReplacingAnyOutput()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        _ = SceneStore.CreateInstance(workspace.Game, "a", 4, 4);
        var second = SceneStore.CreateInstance(workspace.Game, "b", 4, 4);

        // A curve-authored Asset painted as a cell. The document shape is
        // valid, so it saves; only the export, which knows the catalog, can
        // refuse it - which is the point of validating every Scene first.
        var invalid = second.Document with
        {
            TerrainCells =
            [
                new TerrainCellDocument
                {
                    X = 0,
                    Y = 0,
                    AssetKey = "river",
                    ElevationMeters = 1m,
                },
            ],
        };
        SceneStore.Save(workspace.Game, second with { Document = invalid });
        var exportDirectory = Path.Combine(workspace.Game.DirectoryPath, SceneExport.DirectoryName);
        Directory.CreateDirectory(exportDirectory);
        var firstExport = Path.Combine(exportDirectory, "a" + SceneExport.FileSuffix);
        File.WriteAllText(firstExport, "previous export");

        var exception = Assert.Throws<SceneMakerDocumentException>(
            () => SceneExport.WriteGame(session, workspace.Game));

        Assert.Contains(
            "authored as a curve", exception.Message, StringComparison.Ordinal);
        Assert.Equal("previous export", File.ReadAllText(firstExport));
        Assert.False(File.Exists(Path.Combine(exportDirectory, "b" + SceneExport.FileSuffix)));
    }

    [Fact]
    public void ASceneIsFoundByItsIdInEitherDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        SceneStore.CreateInstance(workspace.Game, "map", 4, 4);
        SceneStore.CreateTemplate(workspace.Game, "grove", 2, 2, 1, 0, 0);

        Assert.EndsWith(
            Path.Combine("scenes", "map" + SceneStore.FileSuffix),
            SceneStore.ResolvePath(workspace.Game, "map"),
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("templates", "grove" + SceneStore.FileSuffix),
            SceneStore.ResolvePath(workspace.Game, "grove"),
            StringComparison.Ordinal);
    }
}
