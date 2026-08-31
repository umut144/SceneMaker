using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// Everything the editor needs in order to work inside one Workspace, held as a
/// single value. A session either exists completely or not at all: there is no
/// state in which a Workspace is open but its PolyTools catalog, its
/// configuration or its display catalogs are missing.
///
/// Loading builds the whole session before anything is handed out, so a failed
/// load cannot leave a half-opened Workspace behind. Callers keep one nullable
/// reference and swap it as a unit instead of assigning correlated fields.
/// </summary>
public sealed record EditorSession(
    LoadedWorkspace Workspace,
    PolyToolsCatalog Catalog,
    WorkspaceConfiguration Configuration,
    TerrainDisplayCatalog TerrainAssets,
    PropDisplayCatalog PropAssets)
{
    public string WorkspaceKey => Workspace.WorkspaceKey;
    public string DirectoryPath => Workspace.DirectoryPath;
    public WorkspaceMetrics Metrics => Configuration.Metrics;

    /// <summary>
    /// Opens the Workspace in <paramref name="workspaceDirectory"/>. Throws
    /// <see cref="SceneMakerDocumentException"/> when the Workspace, its
    /// synchronized PolyTools import or its configuration cannot be read.
    /// </summary>
    public static EditorSession Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var catalog = PolyToolsCatalogImporter.Load(workspaceDirectory);
        // WorkspaceStore.Load already asserts that the configuration names this
        // directory, which is what makes the reload below consistent with it.
        var workspace = WorkspaceStore.Load(workspaceDirectory, catalog);
        var configuration = WorkspaceConfigurationStore.Load(workspace.DirectoryPath, catalog);
        return Derive(workspace, catalog, configuration);
    }

    /// <summary>
    /// Derives the session that <paramref name="assetProfiles"/> would produce.
    /// Nothing is written and this session is left untouched, so the caller can
    /// validate the candidate before persisting and adopting it.
    /// </summary>
    public EditorSession WithAssetProfiles(IEnumerable<WorkspaceAssetProfile> assetProfiles) =>
        Derive(Workspace, Catalog, Configuration.WithAssetProfiles(assetProfiles, Catalog));

    private static EditorSession Derive(
        LoadedWorkspace workspace,
        PolyToolsCatalog catalog,
        WorkspaceConfiguration configuration) =>
        new(
            workspace,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(catalog, configuration),
            PropDisplayCatalogLoader.Load(catalog, configuration));
}
