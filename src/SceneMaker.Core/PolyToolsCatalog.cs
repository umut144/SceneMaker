using System.Collections.ObjectModel;
using System.Text.Json;

namespace SceneMaker.Core;

public enum PolyToolsAssetType
{
    Terrain,
    Prop,
}

public sealed record AssetBoundsMeters(
    decimal MinimumX,
    decimal MinimumY,
    decimal MaximumX,
    decimal MaximumY);

public sealed record PolyToolsCatalogAsset(
    string AssetKey,
    PolyToolsAssetType AssetType,
    string Name,
    AssetBoundsMeters BoundsMeters);

public sealed class PolyToolsCatalog
{
    private readonly IReadOnlyDictionary<string, PolyToolsCatalogAsset> _assets;

    internal PolyToolsCatalog(
        string worldKey,
        SortedDictionary<string, PolyToolsCatalogAsset> assets)
    {
        WorldKey = worldKey;
        _assets = new ReadOnlyDictionary<string, PolyToolsCatalogAsset>(assets);
    }

    public string WorldKey { get; }
    public IReadOnlyList<PolyToolsCatalogAsset> Assets => [.. _assets.Values];

    public PolyToolsCatalogAsset Resolve(string assetKey) =>
        _assets.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Asset key '{assetKey}' is not available in the synchronized PolyTools catalog.");
}

public static class PolyToolsCatalogImporter
{
    public const string ImportDirectoryName = "imports";
    public const string PolyToolsDirectoryName = "polytools";
    public const string CatalogFileName = "catalog.json";
    public const int CatalogSchemaVersion = 1;
    public const int ManifestSchemaVersion = 14;

    public static PolyToolsCatalog Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var importDirectory = Path.Combine(
            Path.GetFullPath(workspaceDirectory),
            ImportDirectoryName,
            PolyToolsDirectoryName);
        var catalogPath = Path.Combine(importDirectory, CatalogFileName);

        try
        {
            using var catalogJson = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var root = RequireObject(catalogJson.RootElement, "PolyTools catalog");
            RequireInteger(root, "schema_version", CatalogSchemaVersion, "PolyTools catalog");
            var worldKey = RequireString(root, "world_key", "PolyTools catalog");
            DocumentValidation.ValidateStableId("PolyTools world_key", worldKey);
            _ = RequireString(root, "world_name", "PolyTools catalog");
            var entries = RequireArray(root, "assets", "PolyTools catalog");
            if (entries.GetArrayLength() == 0)
                throw new SceneMakerDocumentException("PolyTools catalog requires at least one asset.");

            SortedDictionary<string, CatalogEntry> catalogEntries = new(StringComparer.Ordinal);
            foreach (var element in entries.EnumerateArray())
            {
                var entryObject = RequireObject(element, "PolyTools catalog asset");
                var key = RequireString(entryObject, "asset_key", "PolyTools catalog asset");
                DocumentValidation.ValidateStableId("PolyTools asset_key", key);
                var displayName = RequireString(entryObject, "display_name", $"PolyTools asset '{key}'");
                var assetType = RequireString(entryObject, "asset_type", $"PolyTools asset '{key}'");
                if (assetType is not ("character" or "props" or "weapons" or "terrain" or "icons" or "symbols"))
                {
                    throw new SceneMakerDocumentException(
                        $"PolyTools asset '{key}' has unsupported asset_type '{assetType}'.");
                }
                var runtimePackage = RequireString(
                    entryObject, "runtime_package", $"PolyTools asset '{key}'");
                var expectedPackage = $"PolyToolsRuntimeExports/{key}/manifest.json";
                if (!string.Equals(runtimePackage, expectedPackage, StringComparison.Ordinal))
                {
                    throw new SceneMakerDocumentException(
                        $"PolyTools asset '{key}' runtime_package must be '{expectedPackage}'.");
                }
                if (!catalogEntries.TryAdd(
                        key,
                        new CatalogEntry(key, displayName, assetType, runtimePackage)))
                {
                    throw new SceneMakerDocumentException(
                        $"PolyTools catalog contains duplicate asset_key '{key}'.");
                }
            }

            Dictionary<string, RuntimeManifest> manifests = new(StringComparer.Ordinal);
            foreach (var entry in catalogEntries.Values)
            {
                var manifestPath = Path.Combine(
                    importDirectory,
                    entry.RuntimePackage.Replace('/', Path.DirectorySeparatorChar));
                manifests.Add(entry.AssetKey, LoadManifest(manifestPath, entry));
            }

            SortedDictionary<string, PolyToolsCatalogAsset> assets = new(StringComparer.Ordinal);
            foreach (var entry in catalogEntries.Values)
            {
                var authoringType = entry.AssetType switch
                {
                    "terrain" => PolyToolsAssetType.Terrain,
                    "props" => PolyToolsAssetType.Prop,
                    _ => (PolyToolsAssetType?)null,
                };
                if (authoringType is null) continue;

                var bounds = BoundsForAsset(
                    manifests[entry.AssetKey], manifests, new HashSet<string>(StringComparer.Ordinal));
                assets.Add(entry.AssetKey, new PolyToolsCatalogAsset(
                    entry.AssetKey,
                    authoringType.Value,
                    entry.DisplayName,
                    bounds));
            }
            if (assets.Count == 0)
            {
                throw new SceneMakerDocumentException(
                    "PolyTools catalog contains no terrain or props available for authoring.");
            }
            return new PolyToolsCatalog(worldKey, assets);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException
                                          or UnauthorizedAccessException or OverflowException)
        {
            throw new SceneMakerDocumentException(
                $"Could not import synchronized PolyTools catalog: {exception.Message}", exception);
        }
    }

    private static RuntimeManifest LoadManifest(string path, CatalogEntry entry)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = RequireObject(json.RootElement, $"PolyTools manifest '{entry.AssetKey}'");
        RequireInteger(
            root,
            "schema_version",
            ManifestSchemaVersion,
            $"PolyTools manifest '{entry.AssetKey}'");
        var assetKey = RequireString(root, "asset_key", $"PolyTools manifest '{entry.AssetKey}'");
        var assetType = RequireString(root, "asset_type", $"PolyTools manifest '{entry.AssetKey}'");
        if (!string.Equals(assetKey, entry.AssetKey, StringComparison.Ordinal)
            || !string.Equals(assetType, entry.AssetType, StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"PolyTools manifest '{entry.AssetKey}' does not match its catalog entry.");
        }

        var assetPivot = RequirePoint(root, "asset_pivot", $"PolyTools manifest '{entry.AssetKey}'");
        var componentArray = RequireArray(root, "components", $"PolyTools manifest '{entry.AssetKey}'");
        if (componentArray.GetArrayLength() == 0)
            throw new SceneMakerDocumentException($"PolyTools manifest '{entry.AssetKey}' has no Components.");

        SortedDictionary<string, RuntimeComponent> components = new(StringComparer.Ordinal);
        foreach (var element in componentArray.EnumerateArray())
        {
            var componentObject = RequireObject(element, $"PolyTools manifest '{entry.AssetKey}' Component");
            var componentId = RequireString(
                componentObject, "component_id", $"PolyTools manifest '{entry.AssetKey}' Component");
            var parentId = OptionalString(componentObject, "parent_component_id");
            var localTransform = RequireTransform(
                componentObject, "local_transform", $"PolyTools Component '{componentId}'");
            var kind = OptionalString(componentObject, "kind");
            var sourceAssetKey = OptionalString(componentObject, "source_asset_key");
            if (string.Equals(kind, "asset_reference", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(sourceAssetKey))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Asset Reference '{componentId}' requires source_asset_key.");
            }

            var visibleVertices = new List<Point>();
            AddMeshVertices(componentObject, "mesh", visibleVertices, componentId, requireOutline: false);
            AddMeshVertices(
                componentObject,
                "contour_stroke_mesh",
                visibleVertices,
                componentId,
                requireOutline: true);
            if (!components.TryAdd(componentId, new RuntimeComponent(
                    componentId,
                    parentId,
                    localTransform,
                    sourceAssetKey,
                    visibleVertices)))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools manifest '{entry.AssetKey}' contains duplicate Component '{componentId}'.");
            }
        }

        foreach (var component in components.Values)
        {
            if (component.ParentComponentId is not null
                && !components.ContainsKey(component.ParentComponentId))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Component '{component.ComponentId}' references missing parent '{component.ParentComponentId}'.");
            }
        }
        return new RuntimeManifest(entry.AssetKey, assetPivot, components);
    }

    private static AssetBoundsMeters BoundsForAsset(
        RuntimeManifest manifest,
        IReadOnlyDictionary<string, RuntimeManifest> manifests,
        HashSet<string> visitingAssets)
    {
        if (!visitingAssets.Add(manifest.AssetKey))
            throw new SceneMakerDocumentException($"PolyTools Asset Reference cycle reaches '{manifest.AssetKey}'.");

        Bounds? bounds = null;
        var assetRoot = Transform.Translation(-manifest.AssetPivot.X, -manifest.AssetPivot.Y);
        foreach (var component in manifest.Components.Values)
        {
            var world = assetRoot.Compose(ComponentWorldTransform(
                component,
                manifest.Components,
                new HashSet<string>(StringComparer.Ordinal)));
            foreach (var vertex in component.VisibleVertices)
                bounds = Bounds.Include(bounds, world.Apply(vertex));

            if (component.SourceAssetKey is null) continue;
            if (!manifests.TryGetValue(component.SourceAssetKey, out var referenced))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Asset Reference '{component.ComponentId}' targets missing asset '{component.SourceAssetKey}'.");
            }
            var referencedBounds = BoundsForAsset(referenced, manifests, visitingAssets);
            foreach (var corner in Corners(referencedBounds))
                bounds = Bounds.Include(bounds, world.Apply(corner));
        }
        visitingAssets.Remove(manifest.AssetKey);

        if (bounds is null)
            throw new SceneMakerDocumentException($"PolyTools asset '{manifest.AssetKey}' has no visible geometry.");
        return new AssetBoundsMeters(
            checked((decimal)bounds.Value.MinimumX),
            checked((decimal)bounds.Value.MinimumY),
            checked((decimal)bounds.Value.MaximumX),
            checked((decimal)bounds.Value.MaximumY));
    }

    private static Transform ComponentWorldTransform(
        RuntimeComponent component,
        IReadOnlyDictionary<string, RuntimeComponent> components,
        HashSet<string> visiting)
    {
        if (!visiting.Add(component.ComponentId))
            throw new SceneMakerDocumentException(
                $"PolyTools Component hierarchy cycle reaches '{component.ComponentId}'.");
        var transform = component.LocalTransform;
        if (component.ParentComponentId is not null)
        {
            transform = ComponentWorldTransform(
                components[component.ParentComponentId], components, visiting).Compose(transform);
        }
        visiting.Remove(component.ComponentId);
        return transform;
    }

    private static IEnumerable<Point> Corners(AssetBoundsMeters bounds)
    {
        var minimumX = (double)bounds.MinimumX;
        var minimumY = (double)bounds.MinimumY;
        var maximumX = (double)bounds.MaximumX;
        var maximumY = (double)bounds.MaximumY;
        yield return new Point(minimumX, minimumY);
        yield return new Point(maximumX, minimumY);
        yield return new Point(minimumX, maximumY);
        yield return new Point(maximumX, maximumY);
    }

    private static void AddMeshVertices(
        JsonElement component,
        string propertyName,
        List<Point> destination,
        string componentId,
        bool requireOutline)
    {
        if (!component.TryGetProperty(propertyName, out var mesh)
            || mesh.ValueKind == JsonValueKind.Null)
            return;
        RequireObject(mesh, $"PolyTools Component '{componentId}' {propertyName}");
        if (requireOutline)
        {
            if (!mesh.TryGetProperty("has_outline", out var hasOutline)
                || hasOutline.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Component '{componentId}' contour_stroke_mesh requires has_outline.");
            }
            if (!hasOutline.GetBoolean()) return;
        }
        var vertices = RequireArray(mesh, "vertices", $"PolyTools Component '{componentId}' {propertyName}");
        foreach (var vertex in vertices.EnumerateArray())
            destination.Add(RequirePoint(vertex, $"PolyTools Component '{componentId}' vertex"));
    }

    private static Transform RequireTransform(JsonElement owner, string propertyName, string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
            throw new SceneMakerDocumentException($"{label} requires {propertyName}.");
        var transform = RequireObject(value, $"{label} {propertyName}");
        var position = RequirePoint(transform, "position", $"{label} {propertyName}");
        var scale = RequirePoint(transform, "scale", $"{label} {propertyName}");
        var rotation = RequireFiniteNumber(
            transform, "rotation_radians", $"{label} {propertyName}");
        return Transform.From(position, rotation, scale);
    }

    private static JsonElement RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new SceneMakerDocumentException($"{label} must be an object.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement owner, string propertyName, string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new SceneMakerDocumentException($"{label} requires array {propertyName}.");
        return value;
    }

    private static string RequireString(JsonElement owner, string propertyName, string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new SceneMakerDocumentException($"{label} requires non-empty string {propertyName}.");
        }
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new SceneMakerDocumentException($"PolyTools {propertyName} must be a string or null.");
        var result = value.GetString();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static void RequireInteger(
        JsonElement owner,
        string propertyName,
        int expected,
        string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || !value.TryGetInt32(out var actual)
            || actual != expected)
        {
            throw new SceneMakerDocumentException(
                $"{label} must use {propertyName} {expected}.");
        }
    }

    private static Point RequirePoint(JsonElement owner, string propertyName, string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
            throw new SceneMakerDocumentException($"{label} requires {propertyName}.");
        return RequirePoint(value, $"{label} {propertyName}");
    }

    private static Point RequirePoint(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
            throw new SceneMakerDocumentException($"{label} must be a two-number array.");
        return new Point(
            RequireFiniteNumber(value[0], label),
            RequireFiniteNumber(value[1], label));
    }

    private static double RequireFiniteNumber(
        JsonElement owner,
        string propertyName,
        string label)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
            throw new SceneMakerDocumentException($"{label} requires {propertyName}.");
        return RequireFiniteNumber(value, $"{label} {propertyName}");
    }

    private static double RequireFiniteNumber(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw new SceneMakerDocumentException($"{label} must be finite.");
        }
        return number;
    }

    private sealed record CatalogEntry(
        string AssetKey,
        string DisplayName,
        string AssetType,
        string RuntimePackage);

    private sealed record RuntimeManifest(
        string AssetKey,
        Point AssetPivot,
        SortedDictionary<string, RuntimeComponent> Components);

    private sealed record RuntimeComponent(
        string ComponentId,
        string? ParentComponentId,
        Transform LocalTransform,
        string? SourceAssetKey,
        IReadOnlyList<Point> VisibleVertices);

    private readonly record struct Point(double X, double Y);

    private readonly record struct Transform(
        double M11,
        double M12,
        double M21,
        double M22,
        double TranslationX,
        double TranslationY)
    {
        public static Transform Translation(double x, double y) =>
            new(1d, 0d, 0d, 1d, x, y);

        public static Transform From(Point position, double rotation, Point scale)
        {
            var cosine = Math.Cos(rotation);
            var sine = Math.Sin(rotation);
            return new Transform(
                cosine * scale.X,
                -sine * scale.Y,
                sine * scale.X,
                cosine * scale.Y,
                position.X,
                position.Y);
        }

        public Point Apply(Point point) => new(
            M11 * point.X + M12 * point.Y + TranslationX,
            M21 * point.X + M22 * point.Y + TranslationY);

        public Transform Compose(Transform local) => new(
            M11 * local.M11 + M12 * local.M21,
            M11 * local.M12 + M12 * local.M22,
            M21 * local.M11 + M22 * local.M21,
            M21 * local.M12 + M22 * local.M22,
            M11 * local.TranslationX + M12 * local.TranslationY + TranslationX,
            M21 * local.TranslationX + M22 * local.TranslationY + TranslationY);
    }

    private readonly record struct Bounds(
        double MinimumX,
        double MinimumY,
        double MaximumX,
        double MaximumY)
    {
        public static Bounds Include(Bounds? bounds, Point point) => bounds is null
            ? new Bounds(point.X, point.Y, point.X, point.Y)
            : new Bounds(
                Math.Min(bounds.Value.MinimumX, point.X),
                Math.Min(bounds.Value.MinimumY, point.Y),
                Math.Max(bounds.Value.MaximumX, point.X),
                Math.Max(bounds.Value.MaximumY, point.Y));
    }
}
