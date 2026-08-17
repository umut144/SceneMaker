using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public sealed record WorkspaceGridConfiguration(
    decimal TerrainCellMeters,
    decimal AuthoringPixelsPerMeter,
    decimal GamePixelsPerMeter);

public sealed record WorkspaceAssetProfile(
    string AssetKey,
    string Color,
    decimal? FootprintWidthMeters,
    decimal? FootprintHeightMeters,
    decimal? AnchorXMeters,
    decimal? AnchorYMeters);

public sealed class WorkspaceConfiguration
{
    private readonly IReadOnlyDictionary<string, WorkspaceAssetProfile> _assetProfiles;

    internal WorkspaceConfiguration(
        string workspaceKey,
        WorkspaceGridConfiguration grid,
        SortedDictionary<string, WorkspaceAssetProfile> assetProfiles)
    {
        WorkspaceKey = workspaceKey;
        Grid = grid;
        Metrics = new WorkspaceMetrics(grid);
        _assetProfiles = new ReadOnlyDictionary<string, WorkspaceAssetProfile>(assetProfiles);
    }

    public string WorkspaceKey { get; }
    public WorkspaceGridConfiguration Grid { get; }
    public WorkspaceMetrics Metrics { get; }
    public IReadOnlyList<WorkspaceAssetProfile> AssetProfiles => [.. _assetProfiles.Values];

    public WorkspaceAssetProfile ResolveAssetProfile(string assetKey) =>
        _assetProfiles.TryGetValue(assetKey, out var profile)
            ? profile
            : throw new SceneMakerDocumentException(
                $"Workspace '{WorkspaceKey}' does not configure asset_key '{assetKey}'.");
}

public static class WorkspaceConfigurationStore
{
    public const string FileName = "config.json";
    public const string Format = "scene_maker_workspace";
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static WorkspaceConfiguration Load(string workspaceDirectory, SceneMakerCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentNullException.ThrowIfNull(catalog);
        var path = Path.Combine(Path.GetFullPath(workspaceDirectory), FileName);
        try
        {
            var document = JsonSerializer.Deserialize<ConfigurationDocument>(
                File.ReadAllText(path), JsonOptions)
                ?? throw new SceneMakerDocumentException("Workspace config must not be JSON null.");
            if (document.Format != Format || document.Version != Version)
                throw new SceneMakerDocumentException(
                    $"Workspace config must use {Format} version {Version}.");
            if (string.IsNullOrWhiteSpace(document.WorkspaceKey))
                throw new SceneMakerDocumentException("Workspace config requires workspace_key.");
            if (document.Grid is null
                || document.Grid.TerrainCellMeters <= 0m
                || document.Grid.AuthoringPixelsPerMeter <= 0m
                || document.Grid.GamePixelsPerMeter <= 0m)
            {
                throw new SceneMakerDocumentException(
                    "Workspace grid requires positive terrain_cell_meters, authoring_pixels_per_meter, and game_pixels_per_meter.");
            }
            var pixelsPerCell = document.Grid.TerrainCellMeters * document.Grid.AuthoringPixelsPerMeter;
            if (pixelsPerCell != decimal.Truncate(pixelsPerCell) || pixelsPerCell < 1m)
            {
                throw new SceneMakerDocumentException(
                    "terrain_cell_meters × authoring_pixels_per_meter must be a positive whole authoring pixel count.");
            }
            if (document.Assets is null)
                throw new SceneMakerDocumentException("Workspace config requires an assets array.");

            SortedDictionary<string, WorkspaceAssetProfile> profiles =
                new(StringComparer.Ordinal);
            foreach (var entry in document.Assets)
            {
                var asset = catalog.Resolve(entry.AssetKey);
                ValidateProfile(entry, asset);
                var profile = new WorkspaceAssetProfile(
                    entry.AssetKey,
                    entry.Color,
                    entry.FootprintMeters?.Width,
                    entry.FootprintMeters?.Height,
                    entry.AnchorMeters?.X,
                    entry.AnchorMeters?.Y);
                if (!profiles.TryAdd(entry.AssetKey, profile))
                    throw new SceneMakerDocumentException(
                        $"Workspace config contains duplicate asset_key '{entry.AssetKey}'.");
            }
            return new WorkspaceConfiguration(
                document.WorkspaceKey,
                new WorkspaceGridConfiguration(
                    document.Grid.TerrainCellMeters,
                    document.Grid.AuthoringPixelsPerMeter,
                    document.Grid.GamePixelsPerMeter),
                profiles);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load Workspace config: {exception.Message}", exception);
        }
    }

    public static void CreateDefault(string workspaceDirectory, string workspaceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceKey);
        var path = Path.Combine(Path.GetFullPath(workspaceDirectory), FileName);
        if (File.Exists(path))
            throw new SceneMakerDocumentException($"Workspace config '{path}' already exists.");
        var document = $$"""
        {
          "format": "{{Format}}",
          "version": {{Version}},
          "workspace_key": "{{workspaceKey}}",
          "grid": {
            "terrain_cell_meters": 0.5,
            "authoring_pixels_per_meter": 32,
            "game_pixels_per_meter": 128
          },
          "assets": []
        }
        """ + "\n";
        AtomicTextFile.WriteNew(path, document);
    }

    private static void ValidateProfile(AssetProfileDocument entry, SceneMakerCatalogAsset asset)
    {
        if (string.IsNullOrWhiteSpace(entry.AssetKey)
            || string.IsNullOrWhiteSpace(entry.Color)
            || entry.Color.Length != 7
            || entry.Color[0] != '#')
        {
            throw new SceneMakerDocumentException(
                "Every Workspace asset profile requires asset_key and a #RRGGBB color.");
        }
        if (asset.Category == SceneMakerAssetCategory.Terrain)
        {
            if (entry.FootprintMeters is not null || entry.AnchorMeters is not null)
                throw new SceneMakerDocumentException(
                    $"Terrain asset '{asset.AssetKey}' must not define a footprint or anchor.");
            return;
        }
        if (entry.FootprintMeters is null || entry.AnchorMeters is null
            || entry.FootprintMeters.Width <= 0m || entry.FootprintMeters.Height <= 0m
            || entry.AnchorMeters.X < 0m || entry.AnchorMeters.Y < 0m
            || entry.AnchorMeters.X > entry.FootprintMeters.Width
            || entry.AnchorMeters.Y > entry.FootprintMeters.Height)
        {
            throw new SceneMakerDocumentException(
                $"{asset.Category} asset '{asset.AssetKey}' requires a positive footprint and an in-bounds anchor.");
        }
    }

    private sealed record ConfigurationDocument
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string WorkspaceKey { get; init; }
        public required GridDocument Grid { get; init; }
        public required List<AssetProfileDocument> Assets { get; init; }
    }

    private sealed record GridDocument
    {
        public required decimal TerrainCellMeters { get; init; }
        public required decimal AuthoringPixelsPerMeter { get; init; }
        public required decimal GamePixelsPerMeter { get; init; }
    }

    private sealed record AssetProfileDocument
    {
        public required string AssetKey { get; init; }
        public required string Color { get; init; }
        public SizeDocument? FootprintMeters { get; init; }
        public PointDocument? AnchorMeters { get; init; }
    }

    private sealed record SizeDocument
    {
        public required decimal Width { get; init; }
        public required decimal Height { get; init; }
    }

    private sealed record PointDocument
    {
        public required decimal X { get; init; }
        public required decimal Y { get; init; }
    }
}
