using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MMORPG.Simulation.WorldAssets;

namespace SceneMaker.Core;

public sealed record PlacementDisplayAsset(
    uint AssetId,
    string Key,
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
    private readonly IReadOnlyDictionary<uint, PlacementDisplayAsset> _byId;

    internal PlacementDisplayCatalog(SortedDictionary<uint, PlacementDisplayAsset> byId) =>
        _byId = new ReadOnlyDictionary<uint, PlacementDisplayAsset>(byId);

    public IReadOnlyList<PlacementDisplayAsset> Assets => [.. _byId.Values];

    public PlacementDisplayAsset Resolve(uint assetId) =>
        _byId.TryGetValue(assetId, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Placement Asset ID {assetId} is not enabled in SceneMaker.");
}

public static partial class PlacementDisplayCatalogLoader
{
    public const string Schema = "srt.scene_maker_placement_display";
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static PlacementDisplayCatalog Load(string displayPath, string worldAssetCatalogPath)
    {
        var display = LoadDisplay(displayPath);
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

        SortedDictionary<uint, PlacementDisplayAsset> resolved = [];
        uint? previousId = null;
        foreach (var entry in display.Assets)
        {
            if (previousId is not null && entry.AssetId <= previousId)
                throw new SceneMakerDocumentException(
                    "Placement display Asset IDs must be unique and strictly increasing.");
            previousId = entry.AssetId;
            if (string.IsNullOrWhiteSpace(entry.Color) || !ColorRegex().IsMatch(entry.Color))
                throw new SceneMakerDocumentException(
                    $"Placement display Asset ID {entry.AssetId} requires an uppercase #RRGGBB color.");

            var catalogEntry = canonical.Catalog.Assets.SingleOrDefault(
                asset => asset.AssetId == entry.AssetId)
                ?? throw new SceneMakerDocumentException(
                    $"Placement display references unknown World Asset ID {entry.AssetId}.");
            if (catalogEntry.Status != WorldAssetStatus.Active
                || catalogEntry.Domain != WorldAssetDomain.Placements)
            {
                throw new SceneMakerDocumentException(
                    $"SceneMaker Placement Asset '{catalogEntry.Key}' must be active in the placements ID domain.");
            }

            var definition = canonical.Resolve(entry.AssetId);
            var spatial = definition.Spatial
                ?? throw new SceneMakerDocumentException(
                    $"SceneMaker Placement Asset '{catalogEntry.Key}' requires reviewed spatial data.");
            var anchorX = ExactAuthoringPixels(spatial.AnchorMeters.X, catalogEntry.Key, "anchor x");
            var anchorY = ExactAuthoringPixels(spatial.AnchorMeters.Y, catalogEntry.Key, "anchor y");
            var width = ConservativeAuthoringPixels(spatial.ExtentMeters.Width);
            var height = ConservativeAuthoringPixels(spatial.ExtentMeters.Height);
            if (anchorX > width || anchorY > height)
                throw new SceneMakerDocumentException(
                    $"Placement Asset '{catalogEntry.Key}' anchor lies outside its conservative footprint.");

            resolved.Add(entry.AssetId, new PlacementDisplayAsset(
                entry.AssetId,
                definition.Asset.Key,
                definition.Asset.Name,
                entry.Color,
                spatial.ExtentMeters.Width,
                spatial.ExtentMeters.Height,
                spatial.AnchorMeters.X,
                spatial.AnchorMeters.Y,
                width,
                height,
                anchorX,
                anchorY));
        }
        return new PlacementDisplayCatalog(resolved);
    }

    private static PlacementDisplayDocument LoadDisplay(string displayPath)
    {
        try
        {
            var display = JsonSerializer.Deserialize<PlacementDisplayDocument>(
                File.ReadAllText(Path.GetFullPath(displayPath)), JsonOptions)
                ?? throw new SceneMakerDocumentException(
                    "Placement display document must not be JSON null.");
            if (display.Schema != Schema || display.Version != Version)
                throw new SceneMakerDocumentException(
                    $"Placement display data must use {Schema} version {Version}.");
            if (display.Assets is null || display.Assets.Count == 0)
                throw new SceneMakerDocumentException(
                    "Placement display data requires at least one Asset.");
            return display;
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load SceneMaker Placement display data: {exception.Message}", exception);
        }
    }

    private static int ConservativeAuthoringPixels(decimal meters) =>
        checked((int)decimal.Ceiling(meters * AuthoringMetrics.AuthoringPixelsPerMeter));

    private static int ExactAuthoringPixels(decimal meters, string key, string label)
    {
        var scaled = meters * AuthoringMetrics.AuthoringPixelsPerMeter;
        if (scaled != decimal.Truncate(scaled))
            throw new SceneMakerDocumentException(
                $"Placement Asset '{key}' {label} must map exactly to an integer authoring pixel.");
        return checked((int)scaled);
    }

    private sealed record PlacementDisplayDocument
    {
        public required string Schema { get; init; }
        public required int Version { get; init; }
        public required List<PlacementDisplayEntry> Assets { get; init; }
    }

    private sealed record PlacementDisplayEntry
    {
        public required uint AssetId { get; init; }
        public required string Color { get; init; }
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
