using SceneMaker.Core;

if (args.Length is < 1 or > 2)
{
    WriteUsage();
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
    // A caller still on an older contract fails exactly like a typo: whatever
    // it passes as its second argument arrives here as a scene-id and names
    // no Scene. The message alone cannot tell those apart, and the caller is
    // usually a script nobody is reading at that moment, so repeat what the
    // second argument means now. Two lines, only where they can be the answer.
    if (args.Length == 2) WriteUsage();
    return 1;
}

// The one place the contract is written down, so the failure path above
// cannot drift away from what the entry check accepts.
static void WriteUsage()
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> [scene-id]");
    Console.Error.WriteLine(
        "Without a scene-id every Scene and Scene Template in the Workspace is exported.");
}
