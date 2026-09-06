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
    /// The kit a Set's members describe, or the reason this Workspace has none.
    ///
    /// <para>Nothing here refuses a Workspace. Every way this can fail - a Set
    /// missing a role, a member the Workspace enables under no name - is either
    /// an ordinary edit or the other project's business, and a Workspace that
    /// stopped opening because of one would be a Workspace an author could edit
    /// shut, with no way back in. So the reason travels to where a bridge is
    /// actually authored and is said there.</para>
    /// </summary>
    public static BridgeKitResolution Resolve(
        string setAssetKey,
        IReadOnlyDictionary<string, string> assetIdsByRole,
        IReadOnlyDictionary<string, string> workspaceKeysByAssetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setAssetKey);
        ArgumentNullException.ThrowIfNull(assetIdsByRole);
        ArgumentNullException.ThrowIfNull(workspaceKeysByAssetId);
        if (Resolve(setAssetKey, assetIdsByRole, workspaceKeysByAssetId, PlankRole)
            is not { } plank)
        {
            return Missing(setAssetKey, assetIdsByRole, workspaceKeysByAssetId, PlankRole);
        }
        if (Resolve(setAssetKey, assetIdsByRole, workspaceKeysByAssetId, AnchorRole)
            is not { } anchor)
        {
            return Missing(setAssetKey, assetIdsByRole, workspaceKeysByAssetId, AnchorRole);
        }
        return new BridgeKitResolution.Resolved(new BridgeKit(plank, anchor));
    }

    /// <summary>
    /// One role, resolved twice: the Set says which PolyTools Asset fills it,
    /// and the Workspace says what it calls that Asset. Null when either half
    /// is missing; <see cref="Missing"/> then says which.
    /// </summary>
    private static string? Resolve(
        string setAssetKey,
        IReadOnlyDictionary<string, string> assetIdsByRole,
        IReadOnlyDictionary<string, string> workspaceKeysByAssetId,
        string role) =>
        assetIdsByRole.TryGetValue(role, out var assetId)
        && workspaceKeysByAssetId.TryGetValue(assetId, out var workspaceKey)
            ? workspaceKey
            : null;

    /// <summary>
    /// Both halves are named apart, because "the Set has no such role" and
    /// "this Workspace does not enable what the Set names" are different
    /// problems with different fixes - one is PolyTools', one is the author's.
    /// </summary>
    private static BridgeKitResolution.Unavailable Missing(
        string setAssetKey,
        IReadOnlyDictionary<string, string> assetIdsByRole,
        IReadOnlyDictionary<string, string> workspaceKeysByAssetId,
        string role) =>
        new(assetIdsByRole.TryGetValue(role, out var assetId)
            ? $"PolyTools Set '{setAssetKey}' fills the role '{role}' with asset "
              + $"'{assetId}', which this Workspace enables under no name."
            : $"PolyTools Set '{setAssetKey}' has no member with the role '{role}', "
              + "so it cannot be a bridge kit.");
}

/// <summary>
/// Either the kit a Workspace builds bridges from, or the sentence saying why
/// it has none. Closed hierarchy: a Workspace without a kit always knows the
/// reason, so no caller has to invent one, and the Bridge tool can say the
/// thing the author can act on rather than a generic refusal.
/// </summary>
public abstract record BridgeKitResolution
{
    private BridgeKitResolution() { }

    /// <summary>This Workspace builds bridges from these two Assets.</summary>
    public sealed record Resolved(BridgeKit Kit) : BridgeKitResolution;

    /// <summary>
    /// No kit here, and why. <paramref name="Reason"/> is a whole sentence: it
    /// is shown to the author as it stands.
    /// </summary>
    public sealed record Unavailable(string Reason) : BridgeKitResolution;
}
