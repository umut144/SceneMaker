using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MMORPG.Simulation.WorldAssets;

namespace SceneMaker.Core;

public sealed record TerrainDisplayAsset(
    uint AssetId,
    string Key,
    string Name,
    string Color);

public sealed class TerrainDisplayCatalog
{
    private readonly IReadOnlyDictionary<uint, TerrainDisplayAsset> _byId;

    internal TerrainDisplayCatalog(SortedDictionary<uint, TerrainDisplayAsset> byId) =>
        _byId = new ReadOnlyDictionary<uint, TerrainDisplayAsset>(byId);

    public IReadOnlyList<TerrainDisplayAsset> Assets => [.. _byId.Values];

    public TerrainDisplayAsset Resolve(uint assetId) =>
        _byId.TryGetValue(assetId, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Terrain Asset ID {assetId} is not enabled in SceneMaker.");
}

public static partial class TerrainDisplayCatalogLoader
{
    public const string Schema = "srt.scene_maker_terrain_display";
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static TerrainDisplayCatalog Load(string displayPath, string worldAssetCatalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldAssetCatalogPath);
        TerrainDisplayDocument display;
        try
        {
            display = JsonSerializer.Deserialize<TerrainDisplayDocument>(
                File.ReadAllText(Path.GetFullPath(displayPath)), JsonOptions)
                ?? throw new SceneMakerDocumentException("Terrain display document must not be JSON null.");
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load SceneMaker Terrain display data: {exception.Message}", exception);
        }

        if (display.Schema != Schema || display.Version != Version)
        {
            throw new SceneMakerDocumentException(
                $"Terrain display data must use {Schema} version {Version}.");
        }
        if (display.Assets is null || display.Assets.Count == 0)
            throw new SceneMakerDocumentException("Terrain display data requires at least one Asset.");

        LoadedWorldAssetCatalog canonical;
        try
        {
            canonical = WorldAssetCatalogLoader.Load(worldAssetCatalogPath);
        }
        catch (WorldAssetCatalogException exception)
        {
            throw new SceneMakerDocumentException(
                $"World Asset catalog is invalid: {exception.Message}", exception);
        }

        SortedDictionary<uint, TerrainDisplayAsset> resolved = [];
        uint? previousId = null;
        foreach (var entry in display.Assets)
        {
            if (previousId is not null && entry.AssetId <= previousId)
            {
                throw new SceneMakerDocumentException(
                    "Terrain display Asset IDs must be unique and strictly increasing.");
            }
            previousId = entry.AssetId;
            if (string.IsNullOrWhiteSpace(entry.Color) || !ColorRegex().IsMatch(entry.Color))
            {
                throw new SceneMakerDocumentException(
                    $"Terrain display Asset ID {entry.AssetId} requires an uppercase #RRGGBB color.");
            }

            var catalogEntry = canonical.Catalog.Assets.SingleOrDefault(
                asset => asset.AssetId == entry.AssetId)
                ?? throw new SceneMakerDocumentException(
                    $"Terrain display references unknown World Asset ID {entry.AssetId}.");
            if (catalogEntry.Status != WorldAssetStatus.Active
                || catalogEntry.Domain != WorldAssetDomain.Biomes
                || catalogEntry.Type != WorldAssetType.Terrain)
            {
                throw new SceneMakerDocumentException(
                    $"SceneMaker Terrain Asset '{catalogEntry.Key}' must be active terrain in the biomes ID domain.");
            }

            var definition = canonical.Resolve(entry.AssetId);
            resolved.Add(entry.AssetId, new TerrainDisplayAsset(
                entry.AssetId,
                definition.Asset.Key,
                definition.Asset.Name,
                entry.Color));
        }
        return new TerrainDisplayCatalog(resolved);
    }

    private sealed record TerrainDisplayDocument
    {
        public required string Schema { get; init; }
        public required int Version { get; init; }
        public required List<TerrainDisplayEntry> Assets { get; init; }
    }

    private sealed record TerrainDisplayEntry
    {
        public required uint AssetId { get; init; }
        public required string Color { get; init; }
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
