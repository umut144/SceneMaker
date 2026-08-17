using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MMORPG.Simulation.WorldAssets;

namespace SceneMaker.Core;

public sealed record TransitionDisplayAsset(
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

public sealed class TransitionDisplayCatalog
{
    private readonly IReadOnlyDictionary<uint, TransitionDisplayAsset> _byId;

    internal TransitionDisplayCatalog(SortedDictionary<uint, TransitionDisplayAsset> byId) =>
        _byId = new ReadOnlyDictionary<uint, TransitionDisplayAsset>(byId);

    public IReadOnlyList<TransitionDisplayAsset> Assets => [.. _byId.Values];

    public TransitionDisplayAsset Resolve(uint assetId) =>
        _byId.TryGetValue(assetId, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Transition Asset ID {assetId} is not enabled in SceneMaker.");
}

public static partial class TransitionDisplayCatalogLoader
{
    public const string Schema = "srt.scene_maker_transition_display";
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static TransitionDisplayCatalog Load(string displayPath, string worldAssetCatalogPath)
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

        SortedDictionary<uint, TransitionDisplayAsset> resolved = [];
        uint? previousId = null;
        foreach (var entry in display.Assets)
        {
            if (previousId is not null && entry.AssetId <= previousId)
                throw new SceneMakerDocumentException(
                    "Transition display Asset IDs must be unique and strictly increasing.");
            previousId = entry.AssetId;
            if (string.IsNullOrWhiteSpace(entry.Color) || !ColorRegex().IsMatch(entry.Color))
                throw new SceneMakerDocumentException(
                    $"Transition display Asset ID {entry.AssetId} requires an uppercase #RRGGBB color.");

            var catalogEntry = canonical.Catalog.Assets.SingleOrDefault(
                asset => asset.AssetId == entry.AssetId)
                ?? throw new SceneMakerDocumentException(
                    $"Transition display references unknown World Asset ID {entry.AssetId}.");
            if (catalogEntry.Status != WorldAssetStatus.Active
                || catalogEntry.Domain != WorldAssetDomain.Transitions
                || catalogEntry.Type != WorldAssetType.Transition)
            {
                throw new SceneMakerDocumentException(
                    $"SceneMaker Transition Asset '{catalogEntry.Key}' must be an active Transition.");
            }

            var definition = canonical.Resolve(entry.AssetId);
            var spatial = definition.Spatial
                ?? throw new SceneMakerDocumentException(
                    $"SceneMaker Transition Asset '{catalogEntry.Key}' requires reviewed spatial data.");
            var anchorX = ExactAuthoringPixels(spatial.AnchorMeters.X, catalogEntry.Key, "anchor x");
            var anchorY = ExactAuthoringPixels(spatial.AnchorMeters.Y, catalogEntry.Key, "anchor y");
            var width = ConservativeAuthoringPixels(spatial.ExtentMeters.Width);
            var height = ConservativeAuthoringPixels(spatial.ExtentMeters.Height);
            if (anchorX > width || anchorY > height)
                throw new SceneMakerDocumentException(
                    $"Transition Asset '{catalogEntry.Key}' anchor lies outside its conservative footprint.");

            resolved.Add(entry.AssetId, new TransitionDisplayAsset(
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
        return new TransitionDisplayCatalog(resolved);
    }

    private static TransitionDisplayDocument LoadDisplay(string displayPath)
    {
        try
        {
            var display = JsonSerializer.Deserialize<TransitionDisplayDocument>(
                File.ReadAllText(Path.GetFullPath(displayPath)), JsonOptions)
                ?? throw new SceneMakerDocumentException(
                    "Transition display document must not be JSON null.");
            if (display.Schema != Schema || display.Version != Version)
                throw new SceneMakerDocumentException(
                    $"Transition display data must use {Schema} version {Version}.");
            if (display.Assets is null || display.Assets.Count == 0)
                throw new SceneMakerDocumentException(
                    "Transition display data requires at least one Asset.");
            return display;
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load SceneMaker Transition display data: {exception.Message}", exception);
        }
    }

    private static int ConservativeAuthoringPixels(decimal meters) =>
        checked((int)decimal.Ceiling(meters * AuthoringMetrics.AuthoringPixelsPerMeter));

    private static int ExactAuthoringPixels(decimal meters, string key, string label)
    {
        var scaled = meters * AuthoringMetrics.AuthoringPixelsPerMeter;
        if (scaled != decimal.Truncate(scaled))
            throw new SceneMakerDocumentException(
                $"Transition Asset '{key}' {label} must map exactly to an integer authoring pixel.");
        return checked((int)scaled);
    }

    private sealed record TransitionDisplayDocument
    {
        public required string Schema { get; init; }
        public required int Version { get; init; }
        public required List<TransitionDisplayEntry> Assets { get; init; }
    }

    private sealed record TransitionDisplayEntry
    {
        public required uint AssetId { get; init; }
        public required string Color { get; init; }
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
