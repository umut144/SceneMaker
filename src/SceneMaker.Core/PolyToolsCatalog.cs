using System.Collections.ObjectModel;
using System.Text.Json;

namespace SceneMaker.Core;

public sealed record AssetBoundsMeters(
    decimal MinimumX,
    decimal MinimumY,
    decimal MaximumX,
    decimal MaximumY);

/// <summary>
/// One named Component of a PolyTools Asset with the bounds it and its
/// descendants occupy in the Asset's own space. It is geometry and nothing
/// else: which Component means something to SceneMaker is a Workspace
/// decision, so the import offers every name and picks none.
/// </summary>
public sealed record PolyToolsComponentBounds(
    string Name,
    AssetBoundsMeters BoundsMeters,

    /// <summary>
    /// What this part alone occupies: the collision Regions whose source is
    /// this Component or one below it. An Asset's own collision answer covers
    /// everything it is made of, which is the wrong answer for a part that
    /// gets set somewhere by itself. Null when no Region hangs on this part.
    /// </summary>
    AssetBoundsMeters? CollisionBoundsMeters);

public sealed record PolyToolsCatalogAsset(
    string AssetKey,
    AssetBoundsMeters BoundsMeters,
    IReadOnlyList<PolyToolsComponentBounds> Components,
    AssetBoundsMeters? CollisionBoundsMeters);

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

    internal static PolyToolsCatalog Empty(string worldKey)
    {
        DocumentValidation.ValidateStableId("Workspace key", worldKey);
        return new PolyToolsCatalog(worldKey, new(StringComparer.Ordinal));
    }

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
    public const int ManifestSchemaVersion = 16;

    /// <summary>
    /// Imports every legacy authoring Asset. Tests for the PolyTools boundary
    /// use this entry point directly; a Workspace session uses the narrower
    /// overload below and asks only for configured Placement geometry.
    /// </summary>
    public static PolyToolsCatalog Load(string workspaceDirectory) =>
        Load(workspaceDirectory, requestedAssetKeys: null);

    public static PolyToolsCatalog Load(
        string workspaceDirectory,
        IEnumerable<string> requestedAssetKeys)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetKeys);
        return Load(
            workspaceDirectory,
            requestedAssetKeys.ToHashSet(StringComparer.Ordinal));
    }

    private static PolyToolsCatalog Load(
        string workspaceDirectory,
        IReadOnlySet<string>? requestedAssetKeys)
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
                _ = RequireString(entryObject, "display_name", $"PolyTools asset '{key}'");
                var assetType = RequireString(entryObject, "asset_type", $"PolyTools asset '{key}'");
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
                        new CatalogEntry(key, assetType, runtimePackage)))
                {
                    throw new SceneMakerDocumentException(
                        $"PolyTools catalog contains duplicate asset_key '{key}'.");
                }
            }

            var roots = requestedAssetKeys is null
                ? catalogEntries.Values
                    .Where(static entry => entry.AssetType is "terrain" or "props")
                    .ToArray()
                : catalogEntries.Values
                    .Where(entry => requestedAssetKeys.Contains(entry.AssetKey))
                    .ToArray();
            if (requestedAssetKeys is not null && roots.Length != requestedAssetKeys.Count)
            {
                var missing = requestedAssetKeys
                    .Where(key => !catalogEntries.ContainsKey(key))
                    .Order(StringComparer.Ordinal)
                    .First();
                throw new SceneMakerDocumentException(
                    $"Placement asset_key '{missing}' has no synchronized PolyTools geometry.");
            }

            Dictionary<string, RuntimeManifest> manifests = new(StringComparer.Ordinal);
            foreach (var rootEntry in roots)
                LoadManifestClosure(importDirectory, rootEntry, catalogEntries, manifests);

            SortedDictionary<string, PolyToolsCatalogAsset> assets = new(StringComparer.Ordinal);
            foreach (var entry in roots)
            {
                var manifest = manifests[entry.AssetKey];
                var bounds = BoundsForAsset(
                    manifest, manifests, new HashSet<string>(StringComparer.Ordinal));
                assets.Add(entry.AssetKey, new PolyToolsCatalogAsset(
                    entry.AssetKey,
                    bounds,
                    ComponentBounds(manifest, manifests),
                    CollisionBounds(manifest)));
            }
            if (requestedAssetKeys is null && assets.Count == 0)
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

    private static void LoadManifestClosure(
        string importDirectory,
        CatalogEntry entry,
        IReadOnlyDictionary<string, CatalogEntry> catalogEntries,
        IDictionary<string, RuntimeManifest> manifests)
    {
        if (manifests.ContainsKey(entry.AssetKey)) return;
        var manifestPath = Path.Combine(
            importDirectory,
            entry.RuntimePackage.Replace('/', Path.DirectorySeparatorChar));
        var manifest = LoadManifest(manifestPath, entry);
        manifests.Add(entry.AssetKey, manifest);

        foreach (var sourceAssetKey in manifest.Components.Values
                     .Select(static component => component.SourceAssetKey)
                     .Where(static key => key is not null)
                     .Cast<string>()
                     .Distinct(StringComparer.Ordinal))
        {
            if (!catalogEntries.TryGetValue(sourceAssetKey, out var sourceEntry))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Asset Reference targets missing asset '{sourceAssetKey}'.");
            }
            LoadManifestClosure(importDirectory, sourceEntry, catalogEntries, manifests);
        }
    }

    private static RuntimeManifest LoadManifest(string path, CatalogEntry entry)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = RequireObject(json.RootElement, $"PolyTools manifest '{entry.AssetKey}'");
        RequireManifestSchema(root, $"PolyTools manifest '{entry.AssetKey}'");
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

            // A Component name is how a Workspace addresses one part of an
            // Asset. It is optional here because an unnamed Component is
            // simply one nothing can point at, not a broken manifest.
            var componentName = OptionalString(componentObject, "name");
            var parentId = OptionalString(componentObject, "parent_component_id");
            var localTransform = RequireTransform(
                componentObject, "local_transform", $"PolyTools Component '{componentId}'");
            var kind = OptionalString(componentObject, "kind");
            var sourceAssetKey = OptionalString(componentObject, "source_asset_key");
            var isAssetReference = string.Equals(kind, "asset_reference", StringComparison.Ordinal);
            if (isAssetReference
                && string.IsNullOrWhiteSpace(sourceAssetKey))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools Asset Reference '{componentId}' requires source_asset_key.");
            }

            var visibleVertices = new List<Point>();
            var regionVertices = new List<Point>();
            AddMeshVertices(componentObject, "mesh", visibleVertices, componentId, requireOutline: false);
            AddMeshVertices(
                componentObject, "closed_region_mesh", regionVertices, componentId, requireOutline: false);
            if (regionVertices.Count == 0)
            {
                AddMeshVertices(
                    componentObject, "mesh", regionVertices, componentId, requireOutline: false);
            }
            AddMeshVertices(
                componentObject,
                "contour_stroke_mesh",
                visibleVertices,
                componentId,
                requireOutline: true);
            var hasMesh = HasObject(componentObject, "mesh");
            var hasClosedRegionMesh = HasObject(componentObject, "closed_region_mesh");
            if (!components.TryAdd(componentId, new RuntimeComponent(
                    componentId,
                    componentName,
                    parentId,
                    localTransform,
                    sourceAssetKey,
                    visibleVertices,
                    regionVertices,
                    isAssetReference,
                    hasMesh,
                    hasClosedRegionMesh)))
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
        var regions = LoadRegions(root, components, entry.AssetKey);
        return new RuntimeManifest(entry.AssetKey, assetPivot, components, regions);
    }

    private static IReadOnlyList<RuntimeRegion> LoadRegions(
        JsonElement root,
        IReadOnlyDictionary<string, RuntimeComponent> components,
        string assetKey)
    {
        var array = RequireArray(root, "regions", $"PolyTools manifest '{assetKey}'");
        var regions = new List<RuntimeRegion>();
        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array.EnumerateArray())
        {
            var regionObject = RequireObject(element, $"PolyTools manifest '{assetKey}' Region");
            var regionId = RequireString(regionObject, "region_id", $"PolyTools manifest '{assetKey}' Region");
            DocumentValidation.ValidateStableId("PolyTools region_id", regionId);
            if (!regionIds.Add(regionId))
            {
                throw new SceneMakerDocumentException(
                    $"PolyTools manifest '{assetKey}' contains duplicate Region '{regionId}'.");
            }

            var label = $"PolyTools Region '{regionId}'";
            var name = RequireString(regionObject, "name", label);
            if (!IsLowerSnakeCase(name))
                throw new SceneMakerDocumentException($"{label} name must use lower_snake_case.");
            var role = RequireString(regionObject, "role", label);
            if (role is not ("attack" or "hurt" or "collision"))
                throw new SceneMakerDocumentException($"{label} has unsupported role '{role}'.");

            var geometrySource = RequireString(regionObject, "geometry_source", label);
            var sourceComponentId = RequireString(regionObject, "source_component_id", label);
            if (!components.TryGetValue(sourceComponentId, out var sourceComponent))
            {
                throw new SceneMakerDocumentException(
                    $"{label} references missing Component '{sourceComponentId}'.");
            }

            switch (geometrySource)
            {
                case "authored":
                {
                    var geometry = RequireIndexedGeometry(regionObject, label);
                    regions.Add(new AuthoredRuntimeRegion(
                        regionId,
                        name,
                        role,
                        sourceComponentId,
                        geometry.Vertices,
                        geometry.Indices));
                    break;
                }
                case "component":
                    if (regionObject.TryGetProperty("vertices", out _)
                        || regionObject.TryGetProperty("indices", out _))
                    {
                        throw new SceneMakerDocumentException(
                            $"{label} with component geometry must not contain vertices or indices.");
                    }
                    if (sourceComponent.IsAssetReference
                        || !sourceComponent.HasMesh && !sourceComponent.HasClosedRegionMesh)
                    {
                        throw new SceneMakerDocumentException(
                            $"{label} requires an ordinary source Component with closed geometry.");
                    }
                    regions.Add(new ComponentBoundRuntimeRegion(
                        regionId,
                        name,
                        role,
                        sourceComponentId));
                    break;
                default:
                    throw new SceneMakerDocumentException(
                        $"{label} has unsupported geometry_source '{geometrySource}'.");
            }
        }
        return regions;
    }

    private static IndexedGeometry RequireIndexedGeometry(JsonElement owner, string label)
    {
        var vertexArray = RequireArray(owner, "vertices", label);
        if (vertexArray.GetArrayLength() == 0)
            throw new SceneMakerDocumentException($"{label} requires non-empty vertices.");
        var vertices = vertexArray.EnumerateArray()
            .Select(vertex => RequirePoint(vertex, $"{label} vertex"))
            .ToArray();

        var indexArray = RequireArray(owner, "indices", label);
        if (indexArray.GetArrayLength() == 0 || indexArray.GetArrayLength() % 3 != 0)
            throw new SceneMakerDocumentException($"{label} indices must contain complete triangles.");
        var indices = new int[indexArray.GetArrayLength()];
        for (var index = 0; index < indices.Length; index++)
        {
            if (!indexArray[index].TryGetInt32(out var vertexIndex)
                || vertexIndex < 0
                || vertexIndex >= vertices.Length)
            {
                throw new SceneMakerDocumentException($"{label} contains an invalid vertex index.");
            }
            indices[index] = vertexIndex;
        }
        for (var index = 0; index < indices.Length; index += 3)
        {
            if (indices[index] == indices[index + 1]
                || indices[index] == indices[index + 2]
                || indices[index + 1] == indices[index + 2])
            {
                throw new SceneMakerDocumentException($"{label} contains a degenerate triangle.");
            }
        }
        return new IndexedGeometry(vertices, indices);
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

    /// <summary>
    /// What an Asset occupies for the purpose of standing somewhere: the
    /// bounds of every Region it authored with the collision role, together,
    /// in the Asset's own space. A model says for itself what it collides
    /// with, and it may say it more than once - an Ankh carries two - so the
    /// answer is their union rather than any single one of them.
    ///
    /// <para>Null when the Asset authored no collision Region at all. Whether
    /// that is allowed is not a question about geometry, so it is answered
    /// where roles are known rather than here.</para>
    /// </summary>
    private static AssetBoundsMeters? CollisionBounds(
        RuntimeManifest manifest,
        IReadOnlySet<string>? limitToComponentIds = null)
    {
        var assetRoot = Transform.Translation(-manifest.AssetPivot.X, -manifest.AssetPivot.Y);
        Bounds? bounds = null;
        foreach (var region in manifest.Regions)
        {
            if (!StringComparer.Ordinal.Equals(region.Role, RuntimeRegion.CollisionRole)) continue;
            if (limitToComponentIds is not null
                && !limitToComponentIds.Contains(region.SourceComponentId))
            {
                continue;
            }
            var source = manifest.Components[region.SourceComponentId];
            var world = assetRoot.Compose(ComponentWorldTransform(
                source,
                manifest.Components,
                new HashSet<string>(StringComparer.Ordinal)));

            // An authored Region brings its own outline in the source
            // Component's space; a Component-bound one stands for that
            // Component's own closed geometry.
            var points = region is AuthoredRuntimeRegion authored
                ? authored.Vertices
                : source.RegionVertices;
            foreach (var point in points)
                bounds = Bounds.Include(bounds, world.Apply(point));
        }
        if (bounds is null) return null;
        return new AssetBoundsMeters(
            checked((decimal)bounds.Value.MinimumX),
            checked((decimal)bounds.Value.MinimumY),
            checked((decimal)bounds.Value.MaximumX),
            checked((decimal)bounds.Value.MaximumY));
    }

    /// <summary>
    /// The bounds every named Component occupies in the Asset's own space,
    /// each including its transitive children, because one part of a model is
    /// what it and everything hanging under it covers. An unnamed Component
    /// contributes to its parent and is not offered on its own: a name is how
    /// a Workspace addresses a part, and there is nothing to address without
    /// one. The result is ordered by name so the same manifest always yields
    /// the same list.
    /// </summary>
    private static IReadOnlyList<PolyToolsComponentBounds> ComponentBounds(
        RuntimeManifest manifest,
        IReadOnlyDictionary<string, RuntimeManifest> manifests)
    {
        var assetRoot = Transform.Translation(-manifest.AssetPivot.X, -manifest.AssetPivot.Y);
        Dictionary<string, List<Point>> points = new(StringComparer.Ordinal);
        Dictionary<string, List<RuntimeComponent>> children = new(StringComparer.Ordinal);
        foreach (var component in manifest.Components.Values)
        {
            var world = assetRoot.Compose(ComponentWorldTransform(
                component,
                manifest.Components,
                new HashSet<string>(StringComparer.Ordinal)));
            List<Point> own = [.. component.VisibleVertices.Select(world.Apply)];
            if (component.SourceAssetKey is not null
                && manifests.TryGetValue(component.SourceAssetKey, out var referenced))
            {
                var referencedBounds = BoundsForAsset(
                    referenced, manifests, new HashSet<string>(StringComparer.Ordinal));
                own.AddRange(Corners(referencedBounds).Select(world.Apply));
            }
            points.Add(component.ComponentId, own);
            if (component.ParentComponentId is not { } parent) continue;
            if (!children.TryGetValue(parent, out var siblings))
            {
                siblings = [];
                children.Add(parent, siblings);
            }
            siblings.Add(component);
        }

        List<PolyToolsComponentBounds> named = [];
        foreach (var component in manifest.Components.Values)
        {
            if (string.IsNullOrWhiteSpace(component.Name)) continue;
            Bounds? bounds = null;
            Stack<RuntimeComponent> pending = new();
            pending.Push(component);
            HashSet<string> seen = new(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var current = pending.Pop();

                // The parent chain is already known to be acyclic, but the
                // guard costs nothing and keeps this loop safe on its own.
                if (!seen.Add(current.ComponentId)) continue;
                foreach (var point in points[current.ComponentId])
                    bounds = Bounds.Include(bounds, point);
                if (!children.TryGetValue(current.ComponentId, out var descendants)) continue;
                foreach (var child in descendants) pending.Push(child);
            }
            if (bounds is null) continue;
            named.Add(new PolyToolsComponentBounds(
                component.Name,
                new AssetBoundsMeters(
                    checked((decimal)bounds.Value.MinimumX),
                    checked((decimal)bounds.Value.MinimumY),
                    checked((decimal)bounds.Value.MaximumX),
                    checked((decimal)bounds.Value.MaximumY)),
                CollisionBounds(manifest, seen)));
        }
        return [.. named.OrderBy(static entry => entry.Name, StringComparer.Ordinal)];
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

    private static bool HasObject(JsonElement owner, string propertyName) =>
        owner.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object;

    private static bool IsLowerSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value[0] is < 'a' or > 'z'
            || value[^1] == '_')
        {
            return false;
        }

        var previousWasUnderscore = false;
        foreach (var character in value)
        {
            if (character == '_')
            {
                if (previousWasUnderscore) return false;
                previousWasUnderscore = true;
            }
            else if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9'))
            {
                return false;
            }
            else
            {
                previousWasUnderscore = false;
            }
        }
        return true;
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

    private static void RequireManifestSchema(JsonElement owner, string label)
    {
        if (!owner.TryGetProperty("schema_version", out var value)
            || !value.TryGetInt32(out var actual)
            || actual != ManifestSchemaVersion)
        {
            throw new SceneMakerDocumentException(
                $"{label} must use schema_version {ManifestSchemaVersion}.");
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
        string AssetType,
        string RuntimePackage);

    private sealed record RuntimeManifest(
        string AssetKey,
        Point AssetPivot,
        SortedDictionary<string, RuntimeComponent> Components,
        IReadOnlyList<RuntimeRegion> Regions);

    private sealed record RuntimeComponent(
        string ComponentId,
        string? Name,
        string? ParentComponentId,
        Transform LocalTransform,
        string? SourceAssetKey,
        IReadOnlyList<Point> VisibleVertices,

        /// <summary>
        /// The geometry a Region bound to this Component stands for: its
        /// closed region mesh where it has one, its ordinary mesh otherwise.
        /// Separate from the visible vertices because a stroke outline is
        /// something to look at, not something to collide with.
        /// </summary>
        IReadOnlyList<Point> RegionVertices,
        bool IsAssetReference,
        bool HasMesh,
        bool HasClosedRegionMesh);

    private abstract record RuntimeRegion(
        string RegionId,
        string Name,
        string Role,
        string SourceComponentId)
    {
        public const string CollisionRole = "collision";
    }

    private sealed record AuthoredRuntimeRegion(
        string RegionId,
        string Name,
        string Role,
        string SourceComponentId,
        IReadOnlyList<Point> Vertices,
        IReadOnlyList<int> Indices)
        : RuntimeRegion(RegionId, Name, Role, SourceComponentId);

    private sealed record ComponentBoundRuntimeRegion(
        string RegionId,
        string Name,
        string Role,
        string SourceComponentId)
        : RuntimeRegion(RegionId, Name, Role, SourceComponentId);

    private sealed record IndexedGeometry(
        IReadOnlyList<Point> Vertices,
        IReadOnlyList<int> Indices);

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
