using SceneMaker.Core;

if (args is not [var workspaceArgument, var sceneId])
{
    Console.Error.WriteLine("Usage: SceneMaker.Cli <workspace-directory> <scene-id>");
    return 2;
}

try
{
    var workspaceDirectory = Path.GetFullPath(workspaceArgument);
    var catalog = PolyToolsCatalogImporter.Load(workspaceDirectory);
    var configuration = WorkspaceConfigurationStore.Load(workspaceDirectory, catalog);
    var workspace = WorkspaceStore.Load(workspaceDirectory, catalog);
    var scene = SceneStore.Load(
        workspace,
        Path.Combine(workspace.ScenesDirectoryPath, sceneId + SceneStore.FileSuffix));
    var output = SceneExport.Write(
        workspace,
        scene,
        configuration,
        TerrainDisplayCatalogLoader.Load(catalog, configuration),
        PropDisplayCatalogLoader.Load(catalog, configuration));
    Console.WriteLine(output);
    return 0;
}
catch (SceneMakerDocumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
