using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// One line of the Outliner. The panel is generic over modes, so a line says
/// only what every kind of object can say: which object it is, what to call it,
/// how deep it hangs, and a short note beside the name.
/// </summary>
/// <param name="ObjectId">
/// The document id. It is what a click selects and what editor visibility is
/// keyed by, so it has to be the document's own id and not a display string.
/// </param>
/// <param name="Depth">
/// How far it is indented. Only water nests today - a branch under the body it
/// leaves - and every other mode is a flat list of zeroes.
/// </param>
/// <param name="Note">Beside the name, dimmer: what a body is switched by.</param>
/// <param name="Loose">
/// A water body that no longer hangs on anything. The Canvas paints it red; the
/// list says the same thing in words, because the reason it is red is a
/// relationship and the list is where relationships are visible.
/// </param>
public sealed record OutlinerEntry(
    string ObjectId,
    string Label,
    int Depth,
    string? Note = null,
    bool Loose = false);

/// <summary>
/// What the Outliner shows for the mode being worked in. Pure
/// <c>SceneDocument -&gt; lines</c>: no Godot, no selection, no visibility - the
/// panel asks this what there is and decides the rest itself.
/// </summary>
public static class OutlinerModel
{
    public static IReadOnlyList<OutlinerEntry> Build(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        EditorMode mode)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        return mode switch
        {
            EditorMode.River => Rivers(scene, metrics),
            EditorMode.ElevationRegion => scene.ElevationRegions
                .Select(region => new OutlinerEntry(
                    region.ElevationRegionId,
                    region.ElevationRegionId,
                    0,
                    FormattableString.Invariant($"{region.ElevationMeters:0.###} m")))
                .ToList(),
            EditorMode.Path => scene.RouteSurfaces
                .Select(route => new OutlinerEntry(
                    route.RouteSurfaceId, route.RouteSurfaceId, 0, route.AssetKey))
                .ToList(),
            EditorMode.Bridge => scene.Bridges
                .Select(bridge => new OutlinerEntry(
                    bridge.BridgeId, bridge.BridgeId, 0, bridge.PlankAssetKey))
                .ToList(),
            EditorMode.Props => scene.Props
                .Select(prop => new OutlinerEntry(
                    prop.InstanceId, prop.InstanceId, 0, prop.AssetKey))
                .ToList(),
            EditorMode.Templates => scene.TemplateAnchors
                .Select(anchor => new OutlinerEntry(anchor.AnchorId, anchor.AnchorId, 0))
                .ToList(),
            _ => [],
        };
    }

    /// <summary>
    /// The junction tree, roots first. A body hangs under the body its source
    /// sits on, which is the one relationship water has and the one an author
    /// draws on paper.
    /// </summary>
    private static IReadOnlyList<OutlinerEntry> Rivers(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        Dictionary<string, List<WaterBodyDocument>> children = new(StringComparer.Ordinal);
        List<WaterBodyDocument> roots = [];
        foreach (var body in scene.WaterBodies)
        {
            if (Parent(scene, body) is not { } parent)
            {
                roots.Add(body);
                continue;
            }

            if (!children.TryGetValue(parent, out var siblings))
                children[parent] = siblings = [];
            siblings.Add(body);
        }

        List<OutlinerEntry> entries = [];
        HashSet<string> placed = new(StringComparer.Ordinal);
        foreach (var root in roots) Walk(root, 0);

        // A ring of bodies would leave its members unreachable from any root.
        // Validation refuses one; until it runs, they are still the Scene's and
        // are listed flat rather than quietly left out of the panel.
        foreach (var body in scene.WaterBodies)
        {
            if (!placed.Contains(body.WaterBodyId)) Walk(body, 0);
        }

        return entries;

        void Walk(WaterBodyDocument body, int depth)
        {
            if (!placed.Add(body.WaterBodyId)) return;
            entries.Add(new OutlinerEntry(
                body.WaterBodyId,
                body.WaterBodyId,
                depth,
                Switches(body),
                !WaterAttachment.IsAttached(scene, metrics, body)));
            if (!children.TryGetValue(body.WaterBodyId, out var siblings)) return;
            foreach (var child in siblings) Walk(child, depth + 1);
        }
    }

    /// <summary>
    /// The body this one's source sits on, when the Scene still has it. A claim
    /// naming a body that is gone leaves the branch at the top level: there is
    /// nothing to nest it under, and the line says it is loose instead.
    /// </summary>
    private static string? Parent(SceneDocument scene, WaterBodyDocument body)
    {
        foreach (var claim in body.Junctions)
        {
            if (claim.End != WaterEnd.Source) continue;
            if (scene.WaterBodies.Any(candidate => string.Equals(
                candidate.WaterBodyId, claim.WaterBodyId, StringComparison.Ordinal)))
            {
                return claim.WaterBodyId;
            }
        }

        return null;
    }

    private static string? Switches(WaterBodyDocument body) => body.Switch;
}
