using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

/// <summary>
/// The Workspace's spatial metrics. <paramref name="WaterCellMeters"/> is the
/// finer grid authored water is rasterized onto - a river bank has to follow a
/// curve, which a whole Terrain cell cannot do - and must nest a whole number
/// of times inside a Terrain cell.
/// </summary>
public sealed record WorkspaceGridConfiguration(
    decimal TerrainCellMeters,
    decimal AuthoringPixelsPerMeter,
    decimal GamePixelsPerMeter,
    decimal WaterCellMeters,
    decimal ElevationQuantumMeters);

/// <summary>
/// How a Terrain Asset is authored. This is editor knowledge rather than game
/// meaning: <c>Cells</c> is painted cell by cell, <c>Curve</c> is drawn as a
/// centerline with a width and rasterized from it.
///
/// <para>The Asset says it rather than SceneMaker deriving it from
/// <see cref="WorkspaceAssetProfile.Surface"/>. A surface is an open token
/// whose meaning SceneMaker never reads - a world that calls its water "fluid",
/// or that draws its lava along a curve, has to work the same way.</para>
/// </summary>
public enum TerrainAuthoring
{
    Cells,
    Curve,
}

/// <summary>
/// What an Asset means to SceneMaker's authoring model. This is Workspace data,
/// never a translation of a PolyTools asset_type.
/// </summary>
public enum WorkspaceAssetRole
{
    Terrain,
    Placement,
}

/// <summary>
/// SceneMaker's closed authoring identity for one enabled Asset. Display name,
/// role, color and Terrain semantics all belong to the Workspace; PolyTools may
/// contribute geometry to a Placement with the same stable key, but it does not
/// name or classify the Asset for SceneMaker.
///
/// <para><see cref="Surface"/> is the domain a consumer's simulation reasons
/// about - "land", "water", and whatever comes later. It is an open token on
/// purpose: a new surface must not break the schema. It sits on the Asset
/// rather than on the cell, so a Terrain Asset cannot contradict itself from
/// one cell to the next. Null for everything that is not Terrain.</para>
/// </summary>
public sealed record WorkspaceAssetProfile(
    string AssetKey,
    string DisplayName,
    WorkspaceAssetRole Role,
    string Color,
    string? Surface = null,
    TerrainAuthoring? Authoring = null);

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
                $"Workspace '{WorkspaceKey}' does not enable asset_key '{assetKey}'.");

    public WorkspaceConfiguration WithAssetProfiles(
        IEnumerable<WorkspaceAssetProfile> assetProfiles) =>
        WorkspaceConfigurationStore.Create(WorkspaceKey, Grid, assetProfiles);
}

public static class WorkspaceConfigurationStore
{
    public const string FileName = "config.json";
    public const string Format = "scene_maker_workspace";
    public const int Version = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static WorkspaceConfiguration Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var path = Path.Combine(Path.GetFullPath(workspaceDirectory), FileName);
        try
        {
            var document = JsonSerializer.Deserialize<ConfigurationDocument>(
                File.ReadAllText(path), JsonOptions)
                ?? throw new SceneMakerDocumentException("Workspace config must not be JSON null.");
            return Parse(document);
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

    public static WorkspaceConfiguration Create(
        string workspaceKey,
        WorkspaceGridConfiguration grid,
        IEnumerable<WorkspaceAssetProfile> profiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceKey);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(profiles);
        var document = new ConfigurationDocument
        {
            Format = Format,
            Version = Version,
            WorkspaceKey = workspaceKey,
            Grid = new GridDocument
            {
                TerrainCellMeters = grid.TerrainCellMeters,
                AuthoringPixelsPerMeter = grid.AuthoringPixelsPerMeter,
                GamePixelsPerMeter = grid.GamePixelsPerMeter,
                WaterCellMeters = grid.WaterCellMeters,
                ElevationQuantumMeters = grid.ElevationQuantumMeters,
            },
            Assets = profiles.Select(profile => new AssetProfileDocument
            {
                AssetKey = profile.AssetKey,
                DisplayName = profile.DisplayName,
                Role = profile.Role,
                Color = profile.Color,
                Surface = profile.Surface,
                Authoring = profile.Authoring,
            }).ToList(),
        };
        return Parse(document);
    }

    public static void Save(string workspaceDirectory, WorkspaceConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentNullException.ThrowIfNull(configuration);
        var document = new ConfigurationDocument
        {
            Format = Format,
            Version = Version,
            WorkspaceKey = configuration.WorkspaceKey,
            Grid = new GridDocument
            {
                TerrainCellMeters = configuration.Grid.TerrainCellMeters,
                AuthoringPixelsPerMeter = configuration.Grid.AuthoringPixelsPerMeter,
                GamePixelsPerMeter = configuration.Grid.GamePixelsPerMeter,
                WaterCellMeters = configuration.Grid.WaterCellMeters,
                ElevationQuantumMeters = configuration.Grid.ElevationQuantumMeters,
            },
            Assets = configuration.AssetProfiles.Select(profile => new AssetProfileDocument
            {
                AssetKey = profile.AssetKey,
                DisplayName = profile.DisplayName,
                Role = profile.Role,
                Color = profile.Color,
                Surface = profile.Surface,
                Authoring = profile.Authoring,
            }).OrderBy(static entry => entry.AssetKey, StringComparer.Ordinal).ToList(),
        };
        var path = Path.Combine(Path.GetFullPath(workspaceDirectory), FileName);
        AtomicTextFile.Write(path, JsonSerializer.Serialize(document, JsonOptions) + "\n");
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
            "terrain_cell_meters": 1.0,
            "authoring_pixels_per_meter": 32,
            "game_pixels_per_meter": 192,
            "water_cell_meters": 0.5,
            "elevation_quantum_meters": 0.125
          },
          "assets": []
        }
        """ + "\n";
        AtomicTextFile.WriteNew(path, document);
    }

    private static void ValidateProfile(AssetProfileDocument entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AssetKey)
            || string.IsNullOrWhiteSpace(entry.DisplayName)
            || string.IsNullOrWhiteSpace(entry.Color)
            || entry.Color.Length != 7
            || entry.Color[0] != '#'
            || !entry.Color[1..].All(Uri.IsHexDigit))
        {
            throw new SceneMakerDocumentException(
                "Every Workspace asset profile requires asset_key, display_name, role, and a #RRGGBB color.");
        }

        if (!Enum.IsDefined(entry.Role))
        {
            throw new SceneMakerDocumentException(
                $"Asset '{entry.AssetKey}' declares an unsupported SceneMaker role.");
        }
        var isTerrain = entry.Role == WorkspaceAssetRole.Terrain;
        if (isTerrain && entry.Surface is null)
        {
            throw new SceneMakerDocumentException(
                $"Terrain Asset '{entry.AssetKey}' requires a surface.");
        }
        if (!isTerrain && entry.Surface is not null)
        {
            throw new SceneMakerDocumentException(
                $"Placement Asset '{entry.AssetKey}' must not declare a surface.");
        }
        // Asked of the Asset instead of guessed from its surface, so that which
        // tools it offers - and whether it may be painted at all - is authored
        // Workspace data like everything else here.
        if (isTerrain && entry.Authoring is null)
        {
            throw new SceneMakerDocumentException(
                $"Terrain Asset '{entry.AssetKey}' requires an authoring of 'cells' or 'curve'.");
        }
        if (!isTerrain && entry.Authoring is not null)
        {
            throw new SceneMakerDocumentException(
                $"Placement Asset '{entry.AssetKey}' must not declare an authoring.");
        }
        if (entry.Authoring is { } authoring && !Enum.IsDefined(authoring))
        {
            throw new SceneMakerDocumentException(
                $"Asset '{entry.AssetKey}' declares an unsupported authoring.");
        }
        if (entry.Surface is { } surface && !IsSurfaceToken(surface))
        {
            throw new SceneMakerDocumentException(
                $"Surface '{surface}' must be a lower_snake_case token such as 'land' or 'water'.");
        }
    }

    /// <summary>
    /// The set of surfaces stays open - a consumer adds "lava" without a schema
    /// change - so only the shape of the token is checked, never its value.
    /// </summary>
    private static bool IsSurfaceToken(string value) =>
        value.Length > 0
        && value[0] is >= 'a' and <= 'z'
        && value[^1] is >= 'a' and <= 'z'
        && value.All(static character => character is (>= 'a' and <= 'z') or '_')
        && !value.Contains("__", StringComparison.Ordinal);

    private static WorkspaceConfiguration Parse(ConfigurationDocument document)
    {
        if (document.Format != Format || document.Version != Version)
            throw new SceneMakerDocumentException($"Workspace config must use {Format} version {Version}.");
        if (string.IsNullOrWhiteSpace(document.WorkspaceKey))
            throw new SceneMakerDocumentException("Workspace config requires workspace_key.");
        if (document.Grid is null || document.Grid.TerrainCellMeters <= 0m
            || document.Grid.AuthoringPixelsPerMeter <= 0m || document.Grid.GamePixelsPerMeter <= 0m
            || document.Grid.WaterCellMeters <= 0m
            || document.Grid.ElevationQuantumMeters is not > 0m)
        {
            throw new SceneMakerDocumentException(
                "Workspace grid requires positive terrain_cell_meters, authoring_pixels_per_meter, game_pixels_per_meter, water_cell_meters, and elevation_quantum_meters.");
        }
        var grid = new WorkspaceGridConfiguration(
            document.Grid.TerrainCellMeters,
            document.Grid.AuthoringPixelsPerMeter,
            document.Grid.GamePixelsPerMeter,
            document.Grid.WaterCellMeters,
            document.Grid.ElevationQuantumMeters.Value);
        _ = new WorkspaceMetrics(grid);
        if (document.Assets is null)
            throw new SceneMakerDocumentException("Workspace config requires an assets array.");

        SortedDictionary<string, WorkspaceAssetProfile> profiles = new(StringComparer.Ordinal);
        foreach (var entry in document.Assets)
        {
            DocumentValidation.ValidateStableId("Workspace asset_key", entry.AssetKey);
            ValidateProfile(entry);
            var profile = new WorkspaceAssetProfile(
                entry.AssetKey,
                entry.DisplayName,
                entry.Role,
                entry.Color,
                entry.Surface,
                entry.Authoring);
            if (!profiles.TryAdd(entry.AssetKey, profile))
            {
                throw new SceneMakerDocumentException(
                    $"Workspace config contains duplicate asset_key '{entry.AssetKey}'.");
            }
        }
        return new WorkspaceConfiguration(document.WorkspaceKey, grid, profiles);
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
        public required decimal WaterCellMeters { get; init; }
        public decimal? ElevationQuantumMeters { get; init; }
    }

    private sealed record AssetProfileDocument
    {
        public required string AssetKey { get; init; }
        public required string DisplayName { get; init; }
        public required WorkspaceAssetRole Role { get; init; }
        public required string Color { get; init; }
        public string? Surface { get; init; }
        public TerrainAuthoring? Authoring { get; init; }
    }
}
