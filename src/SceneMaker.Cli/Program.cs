using SceneMaker.Core;

if (args is not [var workspaceArgument, var sceneId])
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> <scene-id>");
    return 2;
}

try
{
    var session = WorkspaceSession.Load(Path.GetFullPath(workspaceArgument));
    var scene = SceneStore.Load(
        session.Workspace,
        Path.Combine(session.Workspace.ScenesDirectoryPath, sceneId + SceneStore.FileSuffix));
    var output = SceneExport.Write(session, scene);
    Console.WriteLine(output);
    return 0;
}
catch (SceneMakerDocumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
