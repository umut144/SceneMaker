namespace SceneMaker.Core;

/// <summary>
/// One open Workspace with everything derived from it, held as a single value.
/// A session either exists completely or not at all: there is no state in which
/// a Workspace is open but its PolyTools catalog, its configuration or its
/// display catalogs are missing.
///
/// This lives in Core, not in the editor: opening a Workspace is not an editor
/// concern, and the headless exporter does exactly the same thing.
///
/// Loading builds the whole session before anything is handed out, so a failed
/// load cannot leave a half-opened Workspace behind. Callers keep one nullable
/// reference and swap it as a unit instead of assigning correlated fields.
/// </summary>
public sealed record WorkspaceSession(
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
    public static WorkspaceSession Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var workspace = WorkspaceStore.Load(workspaceDirectory);
        var configuration = WorkspaceConfigurationStore.Load(workspace.DirectoryPath);
        var catalog = PolyToolsCatalogImporter.Load(workspaceDirectory);
        if (!string.Equals(configuration.WorkspaceKey, catalog.WorldKey, StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Workspace '{configuration.WorkspaceKey}' requires PolyTools world "
                + $"'{configuration.WorkspaceKey}', not '{catalog.WorldKey}'.");
        }
        return Derive(workspace, catalog, configuration);
    }

    /// <summary>
    /// Derives the session that <paramref name="assetProfiles"/> would produce.
    /// Nothing is written and this session is left untouched, so the caller can
    /// validate the candidate before persisting and adopting it.
    /// </summary>
    public WorkspaceSession WithAssetProfiles(IEnumerable<WorkspaceAssetProfile> assetProfiles) =>
        Derive(Workspace, Catalog, Configuration.WithAssetProfiles(assetProfiles));

    private static WorkspaceSession Derive(
        LoadedWorkspace workspace,
        PolyToolsCatalog catalog,
        WorkspaceConfiguration configuration) =>
        new(
            workspace,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(configuration),
            PropDisplayCatalogLoader.Load(catalog, configuration));
}
