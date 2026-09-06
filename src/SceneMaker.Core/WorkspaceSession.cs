namespace SceneMaker.Core;

/// <summary>
/// One open Workspace with everything derived from it, held as a single value.
/// A session either exists completely or not at all: there is no state in which
/// a Workspace is open but its configuration or display catalogs are missing.
/// Placement geometry is a catalog too, but it is legitimately empty when the
/// Workspace configures no Placements.
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
    PropDisplayCatalog PropAssets,
    BridgeKit? BridgeKit)
{
    public string WorkspaceKey => Workspace.WorkspaceKey;
    public string DirectoryPath => Workspace.DirectoryPath;
    public WorkspaceMetrics Metrics => Configuration.Metrics;

    /// <summary>
    /// Opens the Workspace in <paramref name="workspaceDirectory"/>. Throws
    /// <see cref="SceneMakerDocumentException"/> when the Workspace or its
    /// configuration cannot be read, or when a configured Placement's
    /// synchronized PolyTools geometry is unavailable.
    /// </summary>
    public static WorkspaceSession Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var workspace = WorkspaceStore.Load(workspaceDirectory);
        var configuration = WorkspaceConfigurationStore.Load(workspace.DirectoryPath);
        var placementRequests = configuration.AssetProfiles
            .Where(static profile => profile.Role == WorkspaceAssetRole.Placement)
            .Select(static profile =>
                new PolyToolsAssetRequest(profile.AssetKey, profile.PolyToolsAssetId))
            .ToArray();
        var catalog = placementRequests.Length == 0
            ? PolyToolsCatalog.Empty(configuration.WorkspaceKey)
            : PolyToolsCatalogImporter.Load(workspace.DirectoryPath, placementRequests);
        if (placementRequests.Length > 0
            && !string.Equals(configuration.WorkspaceKey, catalog.WorldKey, StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Workspace '{configuration.WorkspaceKey}' requires PolyTools world "
                + $"'{configuration.WorkspaceKey}', not '{catalog.WorldKey}'.");
        }
        // The bridge kit is read once, here, from the Set the Workspace names.
        // A Workspace that names none, or that enables no Placements at all and
        // therefore has no import to read, simply has no kit - a state, not an
        // omission, which the Bridge tool says out loud.
        //
        // Whether its two members are enabled is deliberately not checked here.
        // Narrowing the enabled Assets is an ordinary edit, and a Workspace that
        // refused to open afterwards would be one an author could edit shut. The
        // refusal belongs where a bridge is actually authored or exported, and
        // both already resolve the Assets they name.
        var bridgeKit = configuration.BridgeSetAssetKey is { } bridgeSet && placementRequests.Length > 0
            ? PolyToolsCatalogImporter.LoadBridgeKit(workspace.DirectoryPath, bridgeSet)
            : null;
        return Derive(workspace, catalog, configuration, bridgeKit);
    }

    /// <summary>
    /// Derives the session that <paramref name="assetProfiles"/> would produce.
    /// Nothing is written and this session is left untouched, so the caller can
    /// validate the candidate before persisting and adopting it.
    /// </summary>
    public WorkspaceSession WithAssetProfiles(IEnumerable<WorkspaceAssetProfile> assetProfiles) =>
        Derive(Workspace, Catalog, Configuration.WithAssetProfiles(assetProfiles), BridgeKit);

    private static WorkspaceSession Derive(
        LoadedWorkspace workspace,
        PolyToolsCatalog catalog,
        WorkspaceConfiguration configuration,
        BridgeKit? bridgeKit) =>
        new(
            workspace,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(configuration),
            PropDisplayCatalogLoader.Load(catalog, configuration),
            bridgeKit);
}
