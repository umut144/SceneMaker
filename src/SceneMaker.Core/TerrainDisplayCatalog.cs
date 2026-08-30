using System.Collections.ObjectModel;

namespace SceneMaker.Core;

public sealed record TerrainDisplayAsset(string AssetKey, string Name, string Color);

public sealed class TerrainDisplayCatalog
{
    private readonly IReadOnlyDictionary<string, TerrainDisplayAsset> _byKey;

    internal TerrainDisplayCatalog(SortedDictionary<string, TerrainDisplayAsset> byKey) =>
        _byKey = new ReadOnlyDictionary<string, TerrainDisplayAsset>(byKey);

    public IReadOnlyList<TerrainDisplayAsset> Assets => [.. _byKey.Values];

    public TerrainDisplayAsset Resolve(string assetKey) =>
        _byKey.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Terrain asset_key '{assetKey}' is not enabled in this Workspace.");
}

public static class TerrainDisplayCatalogLoader
{
    public static TerrainDisplayCatalog Load(PolyToolsCatalog catalog, WorkspaceConfiguration workspace)
    {
        SortedDictionary<string, TerrainDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            if (workspace.EffectiveRole(profile, catalogAsset) != AuthoringAssetRole.Terrain)
                continue;
            assets.Add(profile.AssetKey, new TerrainDisplayAsset(
                profile.AssetKey, catalogAsset.Name, profile.Color));
        }
        return new TerrainDisplayCatalog(assets);
    }
}
