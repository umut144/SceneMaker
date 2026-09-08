namespace SceneMaker.Core;

/// <summary>
/// The states a Scene can be in, as editing operations. Pure
/// <c>SceneDocument -&gt; SceneDocument</c> like every other editing operation
/// here; validation runs at the IO boundaries.
///
/// <para>A group is not water's, which is why this file is not named after it -
/// it relates several authored elements to each other, and today only water
/// bodies name one. What the states are called is the caller's business: a
/// river offers "dry" and "flowing" because that is what a river does, and this
/// file has no opinion about it.</para>
/// </summary>
public static class ActivationEditing
{
    /// <summary>
    /// Declares a group. Two states at least, because a group with one switches
    /// nothing and would be a name a consumer binds a trigger to for no effect.
    /// </summary>
    public static SceneDocument AddGroup(
        SceneDocument scene,
        string group,
        IReadOnlyList<string> states,
        string initialState)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(states);
        DocumentValidation.ValidateStableId("activation group", group);
        if (scene.ActivationGroups.Any(candidate =>
                string.Equals(candidate.Group, group, StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException($"The Scene already declares an activation group '{group}'.");
        }
        if (states.Count < 2)
            throw new SceneMakerDocumentException($"Activation group '{group}' requires at least two states.");
        if (states.Distinct(StringComparer.Ordinal).Count() != states.Count)
            throw new SceneMakerDocumentException($"Activation group '{group}' lists a state twice.");
        foreach (var state in states)
            DocumentValidation.ValidateStableId($"Activation group '{group}' state", state);
        if (!states.Contains(initialState, StringComparer.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Activation group '{group}' requires an initial_state that is one of its states.");
        }

        return scene with
        {
            ActivationGroups = scene.ActivationGroups
                .Append(new ActivationGroupDocument
                {
                    Group = group,
                    States = [.. states],
                    InitialState = initialState,
                })
                .OrderBy(static candidate => candidate.Group, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>
    /// Takes a group away, and with it the activation of every body that named
    /// it - those bodies then exist in every state, which is what a body with no
    /// group is.
    ///
    /// <para>Leaving them pointing at a group that is gone would be a document
    /// no reader could resolve, and clearing them quietly would be the silent
    /// kind of edit this project keeps out. So it happens here, in one
    /// operation, and the caller says how many it touched.</para>
    /// </summary>
    public static SceneDocument RemoveGroup(SceneDocument scene, string group)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.ActivationGroups
            .Where(candidate => !string.Equals(candidate.Group, group, StringComparison.Ordinal))
            .ToList();
        if (remaining.Count == scene.ActivationGroups.Count) return scene;

        return scene with
        {
            ActivationGroups = remaining,
            WaterBodies = scene.WaterBodies
                .Select(body => Names(body, group) ? body with { Activation = null } : body)
                .ToList(),
        };
    }

    /// <summary>Every water body that names this group.</summary>
    public static IReadOnlyList<WaterBodyDocument> Members(SceneDocument scene, string group)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.WaterBodies.Where(body => Names(body, group)).ToList();
    }

    private static bool Names(WaterBodyDocument body, string group) =>
        body.Activation is { } activation
        && string.Equals(activation.Group, group, StringComparison.Ordinal);
}
