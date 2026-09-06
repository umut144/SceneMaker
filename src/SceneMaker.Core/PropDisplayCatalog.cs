using System.Collections.ObjectModel;

namespace SceneMaker.Core;

/// <summary>
/// What a Placement occupies where it stands, as a box measured from its own
/// anchor. It comes from the collision Regions the PolyTools model authored,
/// not from what the model looks like: a lamp post is a pole to walk into and
/// a crown of light to see, and only the first decides whether something else
/// fits beside it.
///
/// <para>A model may author none, and then the Placement occupies nothing at
/// all. That is a real answer rather than a gap: a bridge plank is walked on,
/// not walked into, and what takes space there is the bridge.</para>
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
    PropCollisionBox? Collision);

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
            collision);
    }

    /// <summary>
    /// What a Placement occupies, or null when its model authored no collision
    /// Region at all.
    ///
    /// <para>Refusing such an Asset was tried and taken back. It sounded like
    /// the strict reading - a model that has not said what it occupies should
    /// say so - until the first real case arrived: a bridge plank occupies
    /// nothing, because what takes space there is the bridge. Demanding a
    /// Region would have meant inventing one in PolyTools to satisfy an import
    /// rule. Occupying nothing is the answer, and it is said out loud rather
    /// than defaulted to a footprint.</para>
    /// </summary>
    private static PropCollisionBox? ResolveCollision(
        WorkspaceAssetProfile profile,
        PolyToolsCatalogAsset catalogAsset,
        decimal authoringPixelsPerMeter)
    {
        if (catalogAsset.CollisionBoundsMeters is not { } bounds) return null;
        return Box(
            bounds,
            authoringPixelsPerMeter,
            $"Placement Asset '{profile.AssetKey}' collision");
    }

    /// <summary>
    /// Metre bounds as a box measured from the Asset's anchor, rounded outward
    /// so nothing ever reads as smaller than the geometry it stands for.
    /// </summary>
    private static PropCollisionBox Box(
        AssetBoundsMeters bounds,
        decimal authoringPixelsPerMeter,
        string label)
    {
        var left = checked((int)decimal.Floor(bounds.MinimumX * authoringPixelsPerMeter));
        var bottom = checked((int)decimal.Floor(bounds.MinimumY * authoringPixelsPerMeter));
        var right = checked((int)decimal.Ceiling(bounds.MaximumX * authoringPixelsPerMeter));
        var top = checked((int)decimal.Ceiling(bounds.MaximumY * authoringPixelsPerMeter));
        var width = checked(right - left);
        var height = checked(top - bottom);
        if (width <= 0 || height <= 0)
            throw new SceneMakerDocumentException($"{label} has an empty authoring footprint.");
        return new PropCollisionBox(left, bottom, width, height);
    }

}
