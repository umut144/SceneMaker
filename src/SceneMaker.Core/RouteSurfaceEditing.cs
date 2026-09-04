namespace SceneMaker.Core;

/// <summary>
/// Document operations for independently materialized route surfaces. Drawing
/// and reshaping arrive with the editor slice; the IO boundary already needs a
/// single place to validate their Asset references.
/// </summary>
public static class RouteSurfaceEditing
{
    /// <summary>
    /// Requires every route to name an enabled Asset whose SceneMaker role is
    /// Terrain. The catalog embodies that role filter; the Asset's cell/curve
    /// authoring style is deliberately irrelevant to a route.
    /// </summary>
    public static void ValidateAssetReferences(
        SceneDocument scene,
        TerrainDisplayCatalog terrainAssets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        DocumentValidation.Validate(scene);
        foreach (var route in scene.RouteSurfaces)
            _ = terrainAssets.Resolve(route.AssetKey);
    }
}
