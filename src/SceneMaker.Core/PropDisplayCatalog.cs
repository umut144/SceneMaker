using System.Collections.ObjectModel;

namespace SceneMaker.Core;

public sealed record PropDisplayAsset(
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

public sealed class PropDisplayCatalog
{
    private readonly IReadOnlyDictionary<string, PropDisplayAsset> _byKey;

    internal PropDisplayCatalog(
        WorkspaceMetrics metrics,
        SortedDictionary<string, PropDisplayAsset> byKey)
    {
        Metrics = metrics;
        _byKey = new ReadOnlyDictionary<string, PropDisplayAsset>(byKey);
    }

    public WorkspaceMetrics Metrics { get; }
    public IReadOnlyList<PropDisplayAsset> Assets => [.. _byKey.Values];

    public PropDisplayAsset Resolve(string assetKey) =>
        _byKey.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Placement asset_key '{assetKey}' is not enabled in this Workspace.");
}

public static class PropDisplayCatalogLoader
{
    public static PropDisplayCatalog Load(PolyToolsCatalog catalog, WorkspaceConfiguration workspace)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(workspace);
        SortedDictionary<string, PropDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            if (profile.Role != WorkspaceAssetRole.Placement) continue;
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            assets.Add(profile.AssetKey, Create(
                profile, catalogAsset, workspace.Grid.AuthoringPixelsPerMeter));
        }
        return new PropDisplayCatalog(workspace.Metrics, assets);
    }

    internal static PropDisplayAsset Create(
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
        return new PropDisplayAsset(
            profile.AssetKey,
            profile.DisplayName,
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
