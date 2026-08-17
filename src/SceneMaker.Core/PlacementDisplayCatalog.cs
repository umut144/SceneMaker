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
    public static PlacementDisplayCatalog Load(SceneMakerCatalog catalog, WorkspaceConfiguration workspace)
    {
        SortedDictionary<string, PlacementDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            if (catalogAsset.Category != SceneMakerAssetCategory.Prop) continue;
            assets.Add(profile.AssetKey, Create(
                profile, catalogAsset, workspace.Grid.AuthoringPixelsPerMeter));
        }
        return new PlacementDisplayCatalog(workspace.Metrics, assets);
    }

    internal static PlacementDisplayAsset Create(
        WorkspaceAssetProfile profile,
        SceneMakerCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        var width = checked((int)decimal.Ceiling(profile.FootprintWidthMeters!.Value * authoringPixelsPerMeter));
        var height = checked((int)decimal.Ceiling(profile.FootprintHeightMeters!.Value * authoringPixelsPerMeter));
        var anchorX = ExactPixels(profile.AnchorXMeters!.Value, authoringPixelsPerMeter, profile.AssetKey, "anchor x");
        var anchorY = ExactPixels(profile.AnchorYMeters!.Value, authoringPixelsPerMeter, profile.AssetKey, "anchor y");
        if (anchorX > width || anchorY > height)
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' anchor lies outside its authoring footprint.");
        return new PlacementDisplayAsset(
            profile.AssetKey,
            catalogAsset.Name,
            profile.Color,
            profile.FootprintWidthMeters.Value,
            profile.FootprintHeightMeters.Value,
            profile.AnchorXMeters.Value,
            profile.AnchorYMeters.Value,
            width,
            height,
            anchorX,
            anchorY);
    }

    internal static int ExactPixels(decimal meters, decimal pixelsPerMeter, string key, string label)
    {
        var pixels = meters * pixelsPerMeter;
        if (pixels != decimal.Truncate(pixels))
            throw new SceneMakerDocumentException(
                $"Asset '{key}' {label} must map to a whole authoring pixel.");
        return checked((int)pixels);
    }
}
