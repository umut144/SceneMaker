namespace SceneMaker.Core;

/// <summary>
/// The switches a Scene has, as editing operations. Pure
/// <c>SceneDocument -&gt; SceneDocument</c> like every other editing operation
/// here; validation runs at the IO boundaries.
///
/// <para>A switch is not water's, which is why this file is not named after it -
/// it relates several authored elements to each other, and today only water
/// bodies name one.</para>
/// </summary>
public static class SwitchEditing
{
    /// <summary>
    /// Declares a switch. A Scene may not declare the same name twice: the name
    /// is what a consumer binds a trigger to, so two of them would be two
    /// triggers that cannot be told apart.
    /// </summary>
    public static SceneDocument AddSwitch(
        SceneDocument scene,
        string name,
        bool initiallyOn)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DocumentValidation.ValidateStableId("switch", name);
        if (scene.Switches.Any(candidate =>
                string.Equals(candidate.Switch, name, StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException($"The Scene already declares a switch '{name}'.");
        }

        return scene with
        {
            Switches = scene.Switches
                .Append(new SwitchDocument { Switch = name, InitiallyOn = initiallyOn })
                .OrderBy(static candidate => candidate.Switch, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>
    /// Where the Scene stands before anything has flipped it. Its own operation
    /// because it belongs to the switch and not to any body on it - two bodies
    /// on one switch must not be able to disagree about where the map starts.
    /// </summary>
    public static SceneDocument SetInitiallyOn(SceneDocument scene, string name, bool initiallyOn)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.Switches.Any(candidate =>
                string.Equals(candidate.Switch, name, StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException($"The Scene declares no switch '{name}'.");
        }

        return scene with
        {
            Switches = scene.Switches
                .Select(candidate => string.Equals(candidate.Switch, name, StringComparison.Ordinal)
                    ? candidate with { InitiallyOn = initiallyOn }
                    : candidate)
                .ToList(),
        };
    }

    /// <summary>
    /// Takes a switch away, and with it the switch of every body that named it -
    /// those bodies are then always there, which is what a body with no switch
    /// is.
    ///
    /// <para>Leaving them pointing at a switch that is gone would be a document
    /// no reader could resolve, and clearing them quietly would be the silent
    /// kind of edit this project keeps out. So it happens here, in one
    /// operation, and the caller says how many it touched.</para>
    /// </summary>
    public static SceneDocument RemoveSwitch(SceneDocument scene, string name)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var remaining = scene.Switches
            .Where(candidate => !string.Equals(candidate.Switch, name, StringComparison.Ordinal))
            .ToList();
        if (remaining.Count == scene.Switches.Count) return scene;

        return scene with
        {
            Switches = remaining,
            WaterBodies = scene.WaterBodies
                .Select(body => Names(body, name) ? body with { Switch = null } : body)
                .ToList(),
        };
    }

    /// <summary>Every water body on this switch.</summary>
    public static IReadOnlyList<WaterBodyDocument> Members(SceneDocument scene, string name)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.WaterBodies.Where(body => Names(body, name)).ToList();
    }

    private static bool Names(WaterBodyDocument body, string name) =>
        string.Equals(body.Switch, name, StringComparison.Ordinal);
}
