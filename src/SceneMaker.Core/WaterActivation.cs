namespace SceneMaker.Core;

/// <summary>
/// Which water bodies a Scene has while its switches stand a certain way.
///
/// <para>A body names the switch that decides whether it exists. That alone is
/// not the whole answer, because a branch is fed by the body it leaves: a
/// branch of a river that is not there has nothing running through it. So
/// existence runs down the junctions - a body is there when its own switch is
/// on <em>and</em> the body it leaves is there - and switching a river off
/// takes everything hanging under it with it.</para>
///
/// <para>Nothing about that is authored. The tree is the junctions, which are
/// derived from the curves, and this rule reads it; a document that repeated a
/// parent's switch on every child would be a document where the two could
/// disagree.</para>
///
/// <para>Whether a body that exists is carrying water is a different question
/// and not one this file answers. A dry bed is weather; the switch is
/// existence.</para>
/// </summary>
public static class WaterActivation
{
    /// <summary>
    /// Whether <paramref name="body"/> is there while the named switches stand
    /// as given. A switch the caller says nothing about stands where the Scene
    /// starts it, so passing nothing asks about the Scene as it opens.
    /// </summary>
    public static bool IsActive(
        SceneDocument scene,
        WaterBodyDocument body,
        IReadOnlyDictionary<string, bool>? switches = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(body);
        return IsActive(scene, body, switches, []);
    }

    /// <summary>Every body the Scene has that way, in document order.</summary>
    public static IReadOnlyList<WaterBodyDocument> ActiveBodies(
        SceneDocument scene,
        IReadOnlyDictionary<string, bool>? switches = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.WaterBodies.Where(body => IsActive(scene, body, switches)).ToList();
    }

    /// <summary>
    /// The bodies this one's <em>source</em> sits on - what feeds it. More than
    /// one is two rivers running together into a third, and that third has water
    /// as soon as either of them does.
    /// </summary>
    public static IReadOnlyList<WaterBodyDocument> Feeders(
        SceneDocument scene,
        WaterBodyDocument body)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(body);
        return body.Junctions
            .Where(static claim => claim.End == WaterEnd.Source)
            .Select(claim => scene.WaterBodies.FirstOrDefault(candidate =>
                string.Equals(candidate.WaterBodyId, claim.WaterBodyId, StringComparison.Ordinal)))
            .OfType<WaterBodyDocument>()
            .ToList();
    }

    private static bool IsActive(
        SceneDocument scene,
        WaterBodyDocument body,
        IReadOnlyDictionary<string, bool>? switches,
        HashSet<string> walking)
    {
        if (!SwitchedOn(scene, body, switches)) return false;

        var feeders = Feeders(scene, body);
        if (feeders.Count == 0) return true;

        // A malformed document could name a ring of bodies. Validation refuses
        // one, and this refuses to hang on it in the meantime.
        if (!walking.Add(body.WaterBodyId)) return false;
        try
        {
            return feeders.Any(feeder => IsActive(scene, feeder, switches, walking));
        }
        finally
        {
            walking.Remove(body.WaterBodyId);
        }
    }

    /// <summary>What the body says about itself, before what feeds it is asked.</summary>
    private static bool SwitchedOn(
        SceneDocument scene,
        WaterBodyDocument body,
        IReadOnlyDictionary<string, bool>? switches)
    {
        if (body.Switch is not { } name) return true;
        var declared = scene.Switches.FirstOrDefault(candidate =>
            string.Equals(candidate.Switch, name, StringComparison.Ordinal));
        if (declared is null) return false;
        return switches is not null && switches.TryGetValue(name, out var on)
            ? on
            : declared.InitiallyOn;
    }
}
