namespace SceneMaker.Core;

/// <summary>
/// Which water bodies still hang on something. A branch is fed by the body its
/// source sits on, so a branch whose feeder is gone - or no longer touches it -
/// has nothing running through it, and neither has anything hanging under that
/// branch.
///
/// <para>The walk runs the same direction as <see cref="WaterActivation"/>'s:
/// down the source claims until a body has none, which is a river with a spring
/// of its own. Only the source end decides it. A mouth that has come off its
/// partner is a break in the body that states it and says nothing about where
/// the water comes from, so it leaves attachment alone.</para>
///
/// <para>This is what the Canvas paints red, and it is deliberately not the
/// same question as <see cref="WaterEditing.BrokenJunctions"/>. That one lists
/// the claims a document states and cannot keep, which is what a status line
/// after an edit should say. This one answers, for one body, whether it is
/// still part of the river - so deleting a branch turns its children red and
/// leaves the river they all hung on alone.</para>
/// </summary>
public static class WaterAttachment
{
    /// <summary>Whether this body's source still leads back to a spring.</summary>
    public static bool IsAttached(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(body);
        return IsAttached(scene, metrics, body, []);
    }

    /// <summary>Every body that has come off the network, in document order.</summary>
    public static IReadOnlyList<WaterBodyDocument> DetachedBodies(
        SceneDocument scene,
        WorkspaceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(metrics);
        return scene.WaterBodies.Where(body => !IsAttached(scene, metrics, body)).ToList();
    }

    private static bool IsAttached(
        SceneDocument scene,
        WorkspaceMetrics metrics,
        WaterBodyDocument body,
        HashSet<string> walking)
    {
        var sources = body.Junctions
            .Where(static claim => claim.End == WaterEnd.Source)
            .ToList();
        if (sources.Count == 0) return true;

        // A malformed document could name a ring of bodies. Validation refuses
        // one, and this refuses to hang on it in the meantime.
        if (!walking.Add(body.WaterBodyId)) return false;
        try
        {
            foreach (var claim in sources)
            {
                var partner = scene.WaterBodies.FirstOrDefault(candidate =>
                    string.Equals(candidate.WaterBodyId, claim.WaterBodyId, StringComparison.Ordinal));
                if (partner is null) continue;
                if (!Meets(metrics, body, partner)) continue;
                if (IsAttached(scene, metrics, partner, walking)) return true;
            }

            return false;
        }
        finally
        {
            walking.Remove(body.WaterBodyId);
        }
    }

    /// <summary>
    /// Whether the source still sits on that body, at the height that body has
    /// there - the same two questions the export asks before it writes a
    /// junction, so the Canvas and the export break at the same moment.
    /// </summary>
    private static bool Meets(
        WorkspaceMetrics metrics,
        WaterBodyDocument body,
        WaterBodyDocument partner)
    {
        if (WaterGeometry.SurfaceAtJunction(metrics, body, WaterEnd.Source, partner)
            is not { } surface)
        {
            return false;
        }

        return Math.Abs(body.Points[0].ElevationMeters - surface)
            <= metrics.ElevationQuantumMeters;
    }
}
