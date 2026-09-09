using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// The Scene as the author has asked to see it. Hiding an object in the
/// Outliner is a way of looking, exactly like the height and section views: it
/// never reaches the document, never reaches the export, and is gone when the
/// Scene is closed.
///
/// <para>What it does reach is both drawing <em>and</em> picking. Those have to
/// agree - an object that is hidden and still pickable would let a press take
/// hold of something that is not on screen - so the same filtered Scene answers
/// both questions, and the unfiltered one is what every edit is applied to.
/// </para>
/// </summary>
public static class SceneVisibility
{
    /// <summary>
    /// The Scene without the objects these ids name. The same instance when
    /// nothing is hidden, which is the ordinary case and costs nothing.
    /// </summary>
    public static SceneDocument Without(
        SceneDocument scene,
        IReadOnlyCollection<string> hiddenObjectIds)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(hiddenObjectIds);
        if (hiddenObjectIds.Count == 0) return scene;

        HashSet<string> hidden = new(hiddenObjectIds, StringComparer.Ordinal);
        return scene with
        {
            WaterBodies = scene.WaterBodies
                .Where(body => !hidden.Contains(body.WaterBodyId)).ToList(),
            ElevationRegions = scene.ElevationRegions
                .Where(region => !hidden.Contains(region.ElevationRegionId)).ToList(),
            RouteSurfaces = scene.RouteSurfaces
                .Where(route => !hidden.Contains(route.RouteSurfaceId)).ToList(),
            Bridges = scene.Bridges
                .Where(bridge => !hidden.Contains(bridge.BridgeId)).ToList(),
            Props = scene.Props
                .Where(prop => !hidden.Contains(prop.InstanceId)).ToList(),
            TemplateAnchors = scene.TemplateAnchors
                .Where(anchor => !hidden.Contains(anchor.AnchorId)).ToList(),
        };
    }
}
