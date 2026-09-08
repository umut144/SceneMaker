namespace SceneMaker.Core;

/// <summary>
/// Which water bodies a Scene has while it stands in one state.
///
/// <para>A body says which states of its group it exists in. That alone is not
/// the whole answer, because a branch is fed by the body it leaves: a branch of
/// a river that is not there has nothing running through it. So activation
/// runs down the junctions - a body is there when its own activation says so
/// <em>and</em> the body it leaves is there - and switching a river off takes
/// everything hanging under it with it.</para>
///
/// <para>Nothing about that is authored. The tree is the junctions, which are
/// derived from the curves, and this rule reads it; a document that repeated a
/// parent's states on every child would be a document where the two could
/// disagree.</para>
/// </summary>
public static class WaterActivation
{
    /// <summary>
    /// Whether <paramref name="body"/> is there while each named group stands
    /// in the given state. A group the caller says nothing about stands in its
    /// <c>initial_state</c>, so passing nothing asks about the Scene as it
    /// starts.
    /// </summary>
    public static bool IsActive(
        SceneDocument scene,
        WaterBodyDocument body,
        IReadOnlyDictionary<string, string>? states = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(body);
        return IsActive(scene, body, states, []);
    }

    /// <summary>Every body the Scene has in that state, in document order.</summary>
    public static IReadOnlyList<WaterBodyDocument> ActiveBodies(
        SceneDocument scene,
        IReadOnlyDictionary<string, string>? states = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.WaterBodies.Where(body => IsActive(scene, body, states)).ToList();
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
        IReadOnlyDictionary<string, string>? states,
        HashSet<string> walking)
    {
        if (!SwitchedOn(scene, body, states)) return false;

        var feeders = Feeders(scene, body);
        if (feeders.Count == 0) return true;

        // A malformed document could name a ring of bodies. Validation refuses
        // one, and this refuses to hang on it in the meantime.
        if (!walking.Add(body.WaterBodyId)) return false;
        try
        {
            return feeders.Any(feeder => IsActive(scene, feeder, states, walking));
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
        IReadOnlyDictionary<string, string>? states)
    {
        if (body.Activation is not { } activation) return true;
        var group = scene.ActivationGroups.FirstOrDefault(candidate =>
            string.Equals(candidate.Group, activation.Group, StringComparison.Ordinal));
        if (group is null) return false;

        var current = states is not null && states.TryGetValue(group.Group, out var chosen)
            ? chosen
            : group.InitialState;
        return activation.ActiveIn.Contains(current, StringComparer.Ordinal);
    }
}
