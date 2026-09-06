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
    BridgeKitResolution BridgeKit)
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
        // The bridge kit is read once, here, from the Set the Workspace names,
        // and never refuses the Workspace. Every way it can fail - no Set named,
        // no import to read one from, a Set that is not a kit, a member this
        // Workspace enables under no name - leaves the Workspace open and the
        // reason recorded, because narrowing the enabled Assets is an ordinary
        // edit and a Workspace that stopped opening after one would be a
        // Workspace an author could edit shut. The refusal belongs where a
        // bridge is authored or exported, and both already resolve what they
        // name; the Bridge tool says the reason instead of a generic no.
        var bridgeKit = ResolveBridgeKit(workspace, configuration, placementRequests.Length);
        return Derive(workspace, catalog, configuration, bridgeKit);
    }

    private static BridgeKitResolution ResolveBridgeKit(
        LoadedWorkspace workspace,
        WorkspaceConfiguration configuration,
        int placementCount)
    {
        if (configuration.BridgeSetAssetKey is not { } bridgeSet)
        {
            return new BridgeKitResolution.Unavailable(
                "This Workspace names no PolyTools Set to build bridges from.");
        }
        if (placementCount == 0)
        {
            return new BridgeKitResolution.Unavailable(
                "This Workspace enables no Placement, so it has no PolyTools import "
                + $"to read the Set '{bridgeSet}' from.");
        }
        // The Set names its members by PolyTools id; the Workspace says what it
        // calls each id. Both directions are needed because the bridge record
        // stores SceneMaker's name, and the export has to be readable without
        // resolving a Set.
        var workspaceKeysByAssetId = configuration.AssetProfiles
            .Where(static profile => profile.PolyToolsAssetId is not null)
            .ToDictionary(
                static profile => profile.PolyToolsAssetId!,
                static profile => profile.AssetKey,
                StringComparer.Ordinal);
        try
        {
            // Qualified: this record's own BridgeKit property shadows the type
            // now that the two no longer share a name.
            return SceneMaker.Core.BridgeKit.Resolve(
                bridgeSet,
                PolyToolsCatalogImporter.LoadSetMemberAssetIds(workspace.DirectoryPath, bridgeSet),
                workspaceKeysByAssetId);
        }
        catch (SceneMakerDocumentException exception)
        {
            // Caught, not propagated, and only around the kit: the importer is
            // right to refuse a manifest it cannot read, and this is the one
            // caller for whom that is not worth an unopenable Workspace. Its
            // message already says what PolyTools published wrongly, so it is
            // passed through rather than restated.
            return new BridgeKitResolution.Unavailable(exception.Message);
        }
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
        BridgeKitResolution bridgeKit) =>
        new(
            workspace,
            catalog,
            configuration,
            TerrainDisplayCatalogLoader.Load(configuration),
            PropDisplayCatalogLoader.Load(catalog, configuration),
            bridgeKit);
}
