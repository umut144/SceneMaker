using System.Collections.ObjectModel;

namespace SceneMaker.Core;

public sealed record PlacementDisplayAsset(
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

public sealed class PlacementDisplayCatalog
{
    private readonly IReadOnlyDictionary<string, PlacementDisplayAsset> _byKey;

    internal PlacementDisplayCatalog(
        WorkspaceMetrics metrics,
        SortedDictionary<string, PlacementDisplayAsset> byKey)
    {
        Metrics = metrics;
        _byKey = new ReadOnlyDictionary<string, PlacementDisplayAsset>(byKey);
    }

    public WorkspaceMetrics Metrics { get; }
    public IReadOnlyList<PlacementDisplayAsset> Assets => [.. _byKey.Values];

    public PlacementDisplayAsset Resolve(string assetKey) =>
        _byKey.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Placement asset_key '{assetKey}' is not enabled in this Workspace.");
}

public static class PlacementDisplayCatalogLoader
{
    public static PlacementDisplayCatalog Load(PolyToolsCatalog catalog, WorkspaceConfiguration workspace)
    {
        SortedDictionary<string, PlacementDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            if (workspace.EffectiveRole(profile, catalogAsset) != AuthoringAssetRole.Prop)
                continue;
            assets.Add(profile.AssetKey, Create(
                profile, catalogAsset, workspace.Grid.AuthoringPixelsPerMeter));
        }
        return new PlacementDisplayCatalog(workspace.Metrics, assets);
    }

    internal static PlacementDisplayAsset Create(
        WorkspaceAssetProfile profile,
        PolyToolsCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        var bounds = catalogAsset.BoundsMeters;
        var left = checked((int)decimal.Floor(bounds.MinimumX * authoringPixelsPerMeter));
        var bottom = checked((int)decimal.Floor(bounds.MinimumY * authoringPixelsPerMeter));
        var right = checked((int)decimal.Ceiling(bounds.MaximumX * authoringPixelsPerMeter));
        var top = checked((int)decimal.Ceiling(bounds.MaximumY * authoringPixelsPerMeter));
        var width = checked(right - left);
        var height = checked(top - bottom);
        var anchorX = checked(-left);
        var anchorY = checked(-bottom);
        if (width <= 0 || height <= 0)
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' has an empty authoring footprint.");
        if (anchorX < 0 || anchorY < 0 || anchorX > width || anchorY > height)
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' PolyTools pivot lies outside its visible authoring footprint.");
        return new PlacementDisplayAsset(
            profile.AssetKey,
            catalogAsset.Name,
            profile.Color,
            width / authoringPixelsPerMeter,
            height / authoringPixelsPerMeter,
            anchorX / authoringPixelsPerMeter,
            anchorY / authoringPixelsPerMeter,
            width,
            height,
            anchorX,
            anchorY);
    }
}
