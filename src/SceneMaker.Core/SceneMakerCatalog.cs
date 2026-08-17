using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public enum SceneMakerAssetCategory
{
    Terrain,
    Prop,
    Transition,
}

public sealed record SceneMakerCatalogAsset(
    string AssetKey,
    SceneMakerAssetCategory Category,
    string Name);

public sealed class SceneMakerCatalog
{
    private readonly IReadOnlyDictionary<string, SceneMakerCatalogAsset> _assets;

    internal SceneMakerCatalog(SortedDictionary<string, SceneMakerCatalogAsset> assets) =>
        _assets = new ReadOnlyDictionary<string, SceneMakerCatalogAsset>(assets);

    public IReadOnlyList<SceneMakerCatalogAsset> Assets => [.. _assets.Values];

    public SceneMakerCatalogAsset Resolve(string assetKey) =>
        _assets.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Asset key '{assetKey}' is not available in SceneMaker catalog.");
}

public static class SceneMakerCatalogLoader
{
    public const string Format = "scene_maker_catalog";
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    static SceneMakerCatalogLoader() =>
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

    public static SceneMakerCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var document = JsonSerializer.Deserialize<CatalogDocument>(
                File.ReadAllText(Path.GetFullPath(path)), JsonOptions)
                ?? throw new SceneMakerDocumentException("SceneMaker catalog must not be JSON null.");
            if (document.Format != Format || document.Version != Version)
                throw new SceneMakerDocumentException(
                    $"SceneMaker catalog must use {Format} version {Version}.");
            if (document.Assets is null || document.Assets.Count == 0)
                throw new SceneMakerDocumentException("SceneMaker catalog requires at least one asset.");

            SortedDictionary<string, SceneMakerCatalogAsset> assets =
                new(StringComparer.Ordinal);
            foreach (var entry in document.Assets)
            {
                if (string.IsNullOrWhiteSpace(entry.AssetKey)
                    || string.IsNullOrWhiteSpace(entry.Name))
                {
                    throw new SceneMakerDocumentException(
                        "Every SceneMaker catalog asset requires a non-empty asset_key and name.");
                }
                if (!assets.TryAdd(entry.AssetKey, new SceneMakerCatalogAsset(
                        entry.AssetKey, entry.Category, entry.Name)))
                {
                    throw new SceneMakerDocumentException(
                        $"SceneMaker catalog contains duplicate asset_key '{entry.AssetKey}'.");
                }
            }
            return new SceneMakerCatalog(assets);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load SceneMaker catalog: {exception.Message}", exception);
        }
    }

    private sealed record CatalogDocument
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required List<CatalogEntry> Assets { get; init; }
    }

    private sealed record CatalogEntry
    {
        public required string AssetKey { get; init; }
        public required SceneMakerAssetCategory Category { get; init; }
        public required string Name { get; init; }
    }
}
