using SceneMaker.Core;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The Game half of the editor's lifecycle. No Load Game dialog exists yet
/// (see docs/TASKS.md#WORLD-01), so opening a Workspace with exactly one Game
/// auto-opens it - these tests are what guarantees that interim behavior
/// actually holds, and that it backs off once there is more than one Game to
/// choose between.
/// </summary>
public sealed class EditorControllerGameTests
{
    [Fact]
    public void OpeningAWorkspaceWithExactlyOneGameOpensItToo()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(workspace.RootPath);

        Assert.True(report.Succeeded);
        Assert.Contains("Game", report.Message, StringComparison.Ordinal);
        Assert.Equal(TestWorkspace.DefaultGameKey, controller.Game?.GameKey);
    }

    [Fact]
    public void OpeningAWorkspaceWithNoGameLeavesGameUnset()
    {
        using var parent = TestWorkspace.Create();
        var created = WorkspaceStore.Create(parent.RootPath, "gameless_world", 0.125m);
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(created.DirectoryPath);

        Assert.True(report.Succeeded);
        Assert.Null(controller.Game);
        Assert.Contains("Create a Game", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningAWorkspaceWithSeveralGamesLeavesTheChoiceToTheCaller()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateGame("second");
        var controller = new EditorController();

        var report = controller.OpenWorkspaceAt(workspace.RootPath);

        Assert.True(report.Succeeded);
        Assert.Null(controller.Game);
        Assert.Contains("Open a Game", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenGameOpensTheNamedGameAndClosesAnyOpenScene()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateGame("second");
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);
        controller.OpenGame("second");
        Assert.True(controller.CreateInstance("base", 4, 4).Succeeded);

        var report = controller.OpenGame(TestWorkspace.DefaultGameKey);

        Assert.True(report.Succeeded);
        Assert.Equal(TestWorkspace.DefaultGameKey, controller.Game?.GameKey);
        Assert.Null(controller.Scene);
    }

    [Fact]
    public void OpenGameRefusesAnUnknownKey()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);

        var report = controller.OpenGame("does_not_exist");

        Assert.False(report.Succeeded);
    }

    [Fact]
    public void OpenGameAtOpensAGameByItsAbsoluteDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var second = workspace.CreateGame("second");
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);

        var report = controller.OpenGameAt(second.DirectoryPath);

        Assert.True(report.Succeeded);
        Assert.Equal("second", controller.Game?.GameKey);
    }

    [Fact]
    public void CreateGameAddsAGameWithoutOpeningIt()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);

        var report = controller.CreateGame("second");

        Assert.True(report.Succeeded);
        Assert.Equal(TestWorkspace.DefaultGameKey, controller.Game?.GameKey);
        Assert.True(Directory.Exists(
            Path.Combine(workspace.RootPath, "second", GameStore.ScenesDirectoryName)));
    }

    [Fact]
    public void ClosingTheWorkspaceAlsoClosesTheOpenGameAndScene()
    {
        using var workspace = TestWorkspace.Create();
        var controller = new EditorController();
        controller.OpenWorkspaceAt(workspace.RootPath);
        Assert.True(controller.CreateInstance("base", 4, 4).Succeeded);

        controller.CloseWorkspace();

        Assert.Null(controller.Session);
        Assert.Null(controller.Game);
        Assert.Null(controller.Scene);
    }
}
