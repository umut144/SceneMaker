using SceneMaker.Core;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> [scene-id]");
    Console.Error.WriteLine("Without a scene-id every Scene and Scene Template is exported.");
    return 2;
}

try
{
    var session = WorkspaceSession.Load(Path.GetFullPath(args[0]));
    IReadOnlyList<string> written;
    if (args.Length == 2)
    {
        var scenePath = SceneStore.ResolvePath(session.Workspace, args[1]);
        written = [SceneExport.Write(session, SceneStore.Load(session.Workspace, scenePath))];
    }
    else
    {
        written = SceneExport.WriteWorkspace(session);
    }

    foreach (var path in written) Console.WriteLine(path);
    return 0;
}
catch (SceneMakerDocumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
