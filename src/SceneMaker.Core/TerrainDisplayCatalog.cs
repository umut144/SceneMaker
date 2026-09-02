using System.Collections.ObjectModel;

namespace SceneMaker.Core;

/// <summary>
/// A Terrain Asset as the editor shows and uses it. <paramref name="Authoring"/>
/// is what decides which tools it offers and whether it may be painted or
/// drawn; it is Workspace data, never a guess from the Asset's surface.
/// </summary>
public sealed record TerrainDisplayAsset(
    string AssetKey,
    string Name,
    string Color,
    TerrainAuthoring Authoring);

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
            if (catalogAsset.AssetType != PolyToolsAssetType.Terrain) continue;
            assets.Add(profile.AssetKey, new TerrainDisplayAsset(
                profile.AssetKey,
                catalogAsset.Name,
                profile.Color,
                // Non-null for every Terrain Asset; the configuration refuses
                // to load one without it.
                profile.Authoring!.Value));
        }
        return new TerrainDisplayCatalog(assets);
    }
}
