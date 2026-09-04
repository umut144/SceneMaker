using SceneMaker.Core;

if (args.Length < 1)
{
    PrintUsage();
    return 2;
}

try
{
    var session = WorkspaceSession.Load(Path.GetFullPath(args[0]));
    string? sceneId = null;
    var formatName = "voxels";
    for (var index = 1; index < args.Length; index++)
    {
        if (args[index] == "--format" && index + 1 < args.Length)
        {
            formatName = args[++index];
            continue;
        }
        if (args[index].StartsWith("--", StringComparison.Ordinal) || sceneId is not null)
        {
            PrintUsage();
            return 2;
        }
        sceneId = args[index];
    }

    var legacy = string.Equals(formatName, "legacy", StringComparison.OrdinalIgnoreCase);
    var voxelFormat = formatName.ToLowerInvariant() switch
    {
        "heightfield" => VoxelExportFormat.Heightfield,
        "mesh" => VoxelExportFormat.SurfaceMesh,
        "voxels" => VoxelExportFormat.CompressedVoxels,
        "legacy" => VoxelExportFormat.CompressedVoxels,
        _ => throw new SceneMakerDocumentException(
            $"Unknown export format '{formatName}'; use heightfield, mesh, voxels, or legacy."),
    };

    IReadOnlyList<SceneExportResult> written;
    if (sceneId is not null)
    {
        var scenePath = SceneStore.ResolvePath(session.Workspace, sceneId);
        var scene = SceneStore.Load(session.Workspace, scenePath);
        written = [legacy
            ? SceneExport.Write(session, scene)
            : VoxelSceneExport.Write(session, scene, voxelFormat)];
    }
    else
    {
        written = legacy
            ? SceneExport.WriteWorkspace(session)
            : VoxelSceneExport.WriteWorkspace(session, voxelFormat);
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

static void PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: WorldVoxMaker.Cli <workspace-directory> [scene-id] [--format heightfield|mesh|voxels|legacy]");
    Console.Error.WriteLine(
        "Default is lossless compressed voxels; without scene-id every Scene and Template is exported.");
}
