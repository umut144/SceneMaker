using System.Collections.ObjectModel;

namespace SceneMaker.Core;

/// <summary>
/// One part of a Placement that the Workspace named, as a box measured from
/// the Placement's own anchor. A bridge sets its posts by it, and the Canvas
/// draws it where that part sits inside the whole Asset. Offsets are signed
/// because a part need not surround the anchor.
/// </summary>
public sealed record PropAnchorComponent(
    string Name,
    int OffsetXAuthoringPixels,
    int OffsetYAuthoringPixels,
    int WidthAuthoringPixels,
    int HeightAuthoringPixels);

/// <summary>
/// What a Placement occupies where it stands, as a box measured from its own
/// anchor. It comes from the collision Regions the PolyTools model authored,
/// not from what the model looks like: a lamp post is a pole to walk into and
/// a crown of light to see, and only the first decides whether something else
/// fits beside it.
/// </summary>
public sealed record PropCollisionBox(
    int OffsetXAuthoringPixels,
    int OffsetYAuthoringPixels,
    int WidthAuthoringPixels,
    int HeightAuthoringPixels);

public sealed record PropDisplayAsset(
    string AssetKey,
    string Name,
    string Color,
    decimal WidthMeters,
    decimal HeightMeters,
    decimal AnchorXMeters,
    decimal AnchorYMeters,
    int FootprintWidthAuthoringPixels,
    int FootprintHeightAuthoringPixels,
    int AnchorXAuthoringPixels,
    int AnchorYAuthoringPixels,
    PropCollisionBox Collision,
    PropAnchorComponent? AnchorComponent = null);

public sealed class PropDisplayCatalog
{
    private readonly IReadOnlyDictionary<string, PropDisplayAsset> _byKey;

    internal PropDisplayCatalog(
        WorkspaceMetrics metrics,
        SortedDictionary<string, PropDisplayAsset> byKey)
    {
        Metrics = metrics;
        _byKey = new ReadOnlyDictionary<string, PropDisplayAsset>(byKey);
    }

    public WorkspaceMetrics Metrics { get; }
    public IReadOnlyList<PropDisplayAsset> Assets => [.. _byKey.Values];

    public PropDisplayAsset Resolve(string assetKey) =>
        _byKey.TryGetValue(assetKey, out var asset)
            ? asset
            : throw new SceneMakerDocumentException(
                $"Placement asset_key '{assetKey}' is not enabled in this Workspace.");
}

public static class PropDisplayCatalogLoader
{
    public static PropDisplayCatalog Load(PolyToolsCatalog catalog, WorkspaceConfiguration workspace)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(workspace);
        SortedDictionary<string, PropDisplayAsset> assets = new(StringComparer.Ordinal);
        foreach (var profile in workspace.AssetProfiles)
        {
            if (profile.Role != WorkspaceAssetRole.Placement) continue;
            var catalogAsset = catalog.Resolve(profile.AssetKey);
            assets.Add(profile.AssetKey, Create(
                profile, catalogAsset, workspace.Grid.AuthoringPixelsPerMeter));
        }
        return new PropDisplayCatalog(workspace.Metrics, assets);
    }

    internal static PropDisplayAsset Create(
        WorkspaceAssetProfile profile,
        PolyToolsCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        var bounds = catalogAsset.BoundsMeters;
        var anchorComponent = ResolveAnchorComponent(
            profile, catalogAsset, authoringPixelsPerMeter);
        var collision = ResolveCollision(profile, catalogAsset, authoringPixelsPerMeter);
        var left = checked((int)decimal.Floor(bounds.MinimumX * authoringPixelsPerMeter));
        var bottom = checked((int)decimal.Floor(bounds.MinimumY * authoringPixelsPerMeter));
        var right = checked((int)decimal.Ceiling(bounds.MaximumX * authoringPixelsPerMeter));
        var top = checked((int)decimal.Ceiling(bounds.MaximumY * authoringPixelsPerMeter));
        var width = checked(right - left);
        var height = checked(top - bottom);
        var anchorX = checked(-left);
        var anchorY = checked(-bottom);
        if (width <= 0 || height <= 0)
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' has an empty authoring footprint.");
        if (anchorX < 0 || anchorY < 0 || anchorX > width || anchorY > height)
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' PolyTools pivot lies outside its visible authoring footprint.");
        return new PropDisplayAsset(
            profile.AssetKey,
            profile.DisplayName,
            profile.Color,
            width / authoringPixelsPerMeter,
            height / authoringPixelsPerMeter,
            anchorX / authoringPixelsPerMeter,
            anchorY / authoringPixelsPerMeter,
            width,
            height,
            anchorX,
            anchorY,
            collision,
            anchorComponent);
    }

    /// <summary>
    /// A Placement must say what it occupies. Falling back to its visible
    /// footprint would be a hidden default of exactly the kind this Workspace
    /// forbids, and it would be the wrong one: a model whose collision the
    /// author has not drawn yet would quietly claim all the space it is drawn
    /// with. Refusing names the Asset, and adding the Region in PolyTools is
    /// the fix.
    /// </summary>
    private static PropCollisionBox ResolveCollision(
        WorkspaceAssetProfile profile,
        PolyToolsCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        if (catalogAsset.CollisionBoundsMeters is not { } bounds)
        {
            throw new SceneMakerDocumentException(
                $"Placement Asset '{profile.AssetKey}' has no PolyTools Region with the collision role, so nothing says what it occupies.");
        }

        // Rounded outward like the footprint: a Placement never occupies less
        // than the geometry it was authored with.
        var left = checked((int)decimal.Floor(bounds.MinimumX * authoringPixelsPerMeter));
        var bottom = checked((int)decimal.Floor(bounds.MinimumY * authoringPixelsPerMeter));
        var right = checked((int)decimal.Ceiling(bounds.MaximumX * authoringPixelsPerMeter));
        var top = checked((int)decimal.Ceiling(bounds.MaximumY * authoringPixelsPerMeter));
        var width = checked(right - left);
        var height = checked(top - bottom);
        if (width <= 0 || height <= 0)
        {
            throw new SceneMakerDocumentException(
                $"Placement Asset '{profile.AssetKey}' has an empty collision footprint.");
        }
        return new PropCollisionBox(left, bottom, width, height);
    }

    /// <summary>
    /// Resolves the Component the Workspace named on this Placement. A name
    /// that matches nothing, or more than one part, is refused rather than
    /// quietly ignored: something authored it deliberately, and drawing the
    /// wrong part - or none - is worse than a load that says why.
    /// </summary>
    private static PropAnchorComponent? ResolveAnchorComponent(
        WorkspaceAssetProfile profile,
        PolyToolsCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        if (profile.AnchorComponent is not { } name) return null;
        var matches = catalogAsset.Components
            .Where(component => StringComparer.Ordinal.Equals(component.Name, name))
            .ToList();
        if (matches.Count == 0)
        {
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' names anchor_component '{name}', which its PolyTools geometry does not contain.");
        }
        if (matches.Count > 1)
        {
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' names anchor_component '{name}', which its PolyTools geometry contains {matches.Count} times.");
        }

        // Rounded outward on the same grid as the visible footprint, so a part
        // never reads as smaller than the geometry it stands for.
        var bounds = matches[0].BoundsMeters;
        var left = checked((int)decimal.Floor(bounds.MinimumX * authoringPixelsPerMeter));
        var bottom = checked((int)decimal.Floor(bounds.MinimumY * authoringPixelsPerMeter));
        var right = checked((int)decimal.Ceiling(bounds.MaximumX * authoringPixelsPerMeter));
        var top = checked((int)decimal.Ceiling(bounds.MaximumY * authoringPixelsPerMeter));
        var width = checked(right - left);
        var height = checked(top - bottom);
        if (width <= 0 || height <= 0)
        {
            throw new SceneMakerDocumentException(
                $"Asset '{profile.AssetKey}' anchor_component '{name}' has an empty authoring footprint.");
        }
        return new PropAnchorComponent(name, left, bottom, width, height);
    }
}
