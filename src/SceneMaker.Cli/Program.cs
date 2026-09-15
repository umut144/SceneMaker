using SceneMaker.Core;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> [game-key] [scene-id]");
    Console.Error.WriteLine("Without a game-key every Game in the Workspace is exported.");
    Console.Error.WriteLine("Without a scene-id every Scene and Scene Template in the Game is exported.");
    return 2;
}

try
{
    var session = WorkspaceSession.Load(Path.GetFullPath(args[0]));
    IReadOnlyList<SceneExportResult> written;
    if (args.Length == 1)
    {
        written = SceneExport.WriteWorkspace(session);
    }
    else
    {
        var game = GameStore.Load(session.Workspace, args[1]);
        if (args.Length == 3)
        {
            var scenePath = SceneStore.ResolvePath(game, args[2]);
            written = [SceneExport.Write(session, game, SceneStore.Load(game, scenePath))];
        }
        else
        {
            written = SceneExport.WriteGame(session, game);
        }
    }

    foreach (var result in written)
    {
        Console.WriteLine(result.Path);
        // Warnings go to stderr so that piping the paths somewhere stays exact.
        foreach (var warning in result.Warnings) Console.Error.WriteLine(warning);
    }
    return 0;
}
catch (SceneMakerDocumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
