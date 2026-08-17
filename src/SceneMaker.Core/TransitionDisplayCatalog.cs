using System.Collections.ObjectModel;

namespace SceneMaker.Core;

public sealed record TransitionDisplayAsset(
    string AssetKey,
    string Name,
    string Color,
    decimal WidthMeters,
    decimal HeightMeters,
    decimal AnchorXMeters,
    decimal AnchorYMeters,
    int FootprintWidthAuthoringPixels,
    int FootprintHeightAuthoringPixels,
    int AnchorXAuthoringPixels,
    int AnchorYAuthoringPixels);

public sealed class TransitionDisplayCatalog
{
    private readonly IReadOnlyDictionary<string, TransitionDisplayAsset> _byKey;

    internal TransitionDisplayCatalog(
        WorkspaceMetrics metrics,
        SortedDictionary<string, TransitionDisplayAsset> byKey)
    {
        Metrics = metrics;
        _byKey = new ReadOnlyDictionary<string, TransitionDisplayAsset>(byKey);
    }

    public IReadOnlyList<TransitionDisplayAsset> Assets => [.. _byKey.Values];
    public WorkspaceMetrics Metrics { get; }

    public TransitionDisplayAsset Resolve(string assetKey) =>
        _byKey.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Transition asset_key '{assetKey}' is not enabled in this Workspace.");
}

public static class TransitionDisplayCatalogLoader
{
    public static TransitionDisplayCatalog Load(SceneMakerCatalog catalog, WorkspaceConfiguration workspace)
    {
        SortedDictionary<string, TransitionDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            if (catalogAsset.Category != SceneMakerAssetCategory.Transition) continue;
            var spatial = PlacementDisplayCatalogLoader.Create(
                profile, catalogAsset, workspace.Grid.AuthoringPixelsPerMeter);
            assets.Add(profile.AssetKey, new TransitionDisplayAsset(
                spatial.AssetKey, spatial.Name, spatial.Color,
                spatial.WidthMeters, spatial.HeightMeters,
                spatial.AnchorXMeters, spatial.AnchorYMeters,
                spatial.FootprintWidthAuthoringPixels, spatial.FootprintHeightAuthoringPixels,
                spatial.AnchorXAuthoringPixels, spatial.AnchorYAuthoringPixels));
        }
        return new TransitionDisplayCatalog(workspace.Metrics, assets);
    }
}
