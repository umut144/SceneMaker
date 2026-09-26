using SceneMaker.Core;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> [scene-id]");
    Console.Error.WriteLine("Without a scene-id every Scene and Scene Template in the Workspace is exported.");
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
        var scenePath = SceneStore.ResolvePath(session.Workspace, args[1]);
        written = [SceneExport.Write(session, SceneStore.Load(session.Workspace, scenePath))];
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
