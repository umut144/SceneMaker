namespace SceneMaker.Core;

/// <summary>
/// Which Assets a bridge in this Workspace is built from: the plank its deck is
/// a row of, and the post standing at each corner.
///
/// <para>Nobody authors this in SceneMaker, and asking an author was tried and
/// taken back. It is a fact about a world rather than a decision per bridge, so
/// a field offering it made every bridge re-answer a settled question - and
/// offered a tree as a plank while doing so.</para>
///
/// <para>PolyTools publishes it: a Set says which Assets belong together, and
/// each member carries a role. SceneMaker's Workspace only says which Set is
/// the bridge kit; what is in it belongs to the other project. The resolved
/// keys are still written into the bridge record, because the export has to
/// name what to build without a consumer resolving a Set.</para>
/// </summary>
public sealed record BridgeKit(string PlankAssetKey, string AnchorAssetKey)
{
    /// <summary>
    /// The two roles SceneMaker looks for, spelled as PolyTools authors them.
    ///
    /// <para>They are tokens in another project's vocabulary, never anything
    /// derived here, so they sit together and a rename over there is a change
    /// of these two lines. A role is deliberately not a name: the reference
    /// name follows the Asset it points at - the place was called "plank" while
    /// the Asset was called "deck", and today it would have moved with it - so
    /// only a role can say what a member is for.</para>
    /// </summary>
    public const string PlankRole = "plank";

    // "post" rather than "rope_post": a role that names one execution ages
    // badly, and a stone post at the corners of the same bridge would have had
    // to keep answering to a rope. PolyTools renamed it for that reason.
    public const string AnchorRole = "post";

    /// <summary>
    /// The kit a Set's members describe, or an exception naming what is
    /// missing. A Set carrying neither role is not a bridge kit, and saying so
    /// beats resolving to whichever member happened to come first.
    /// </summary>
    public static BridgeKit From(
        string setAssetKey,
        IReadOnlyDictionary<string, string> assetKeysByRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setAssetKey);
        ArgumentNullException.ThrowIfNull(assetKeysByRole);
        return new BridgeKit(
            Require(setAssetKey, assetKeysByRole, PlankRole),
            Require(setAssetKey, assetKeysByRole, AnchorRole));
    }

    private static string Require(
        string setAssetKey,
        IReadOnlyDictionary<string, string> assetKeysByRole,
        string role) =>
        assetKeysByRole.TryGetValue(role, out var assetKey)
            ? assetKey
            : throw new SceneMakerDocumentException(
                $"PolyTools Set '{setAssetKey}' has no member with the role '{role}', "
                + "so it cannot be a bridge kit.");
}
