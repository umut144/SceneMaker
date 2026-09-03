using System.Globalization;
using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// The closed contour of one authored mountain body, ready for the canvas.
///
/// <para>Derived, never stored. The Scene keeps the body's Bezier points; this
/// is what they flatten to, plus which of the editor's mountain colours the body
/// wears. Neither the contour nor the colour is document data, so nothing here
/// reaches the export or means anything to a consumer.</para>
///
/// <para><see cref="Points"/> is a ring: the first point is not repeated at the
/// end, exactly as <see cref="BezierChain.FlattenClosed"/> returns it, and
/// whoever draws it closes it.</para>
/// </summary>
public sealed record MountainOutline(
    string MountainBodyId,
    int PaletteIndex,
    IReadOnlyList<ChainPoint> Points);

/// <summary>
/// Which of the editor's mountain colours a body wears. The palette itself is a
/// drawing concern and lives with the canvas; what belongs here is the rule that
/// picks a slot, because it has to answer the same way every time.
/// </summary>
public static class MountainPalette
{
    /// <summary>How many colours the canvas offers.</summary>
    public const int Size = 6;

    private const string CanonicalPrefix = "mountain_";
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    /// <summary>
    /// The colour slot for a body ID.
    ///
    /// <para>A Scene numbers its bodies in order, so the ordinary case is made to
    /// cycle rather than left to a hash: <c>mountain_0001</c> to
    /// <c>mountain_0006</c> wear all six colours and no two bodies authored one
    /// after the other share one. That is the case an author actually looks at -
    /// two neighbouring mountains in the same colour is exactly the confusion
    /// this slice exists to remove.</para>
    ///
    /// <para>An ID that is not of that form - hand-written, or from some later
    /// naming scheme - falls back to a hash. That spreads, and promises nothing
    /// about neighbours, which is the honest answer when there is no order to
    /// read.</para>
    /// </summary>
    public static int IndexOf(string mountainBodyId)
    {
        ArgumentNullException.ThrowIfNull(mountainBodyId);
        if (mountainBodyId.StartsWith(CanonicalPrefix, StringComparison.Ordinal)
            && int.TryParse(
                mountainBodyId.AsSpan(CanonicalPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ordinal)
            && ordinal > 0)
        {
            return (ordinal - 1) % Size;
        }
        return (int)(Hash(mountainBodyId) % Size);
    }

    /// <summary>
    /// FNV-1a over the ID's UTF-16 code units, written out rather than taken
    /// from <see cref="string.GetHashCode()"/>: that one is randomised per
    /// process, and a mountain that changes colour every time the editor starts
    /// would be worse than no colour at all.
    /// </summary>
    private static uint Hash(string value)
    {
        var hash = OffsetBasis;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= Prime;
        }
        return hash;
    }
}

/// <summary>
/// The contours of the mountain bodies a Scene holds, kept until those bodies
/// change.
///
/// <para>A contour depends on one thing only: the body's own points. Not on the
/// Workspace metrics, not on the Scene's size, not on painted cells, and not on
/// the other bodies. That is what separates it from
/// <see cref="MountainGeometry.TerrainCells"/>, which clips to the Scene, and
/// from the fold, which reads every contribution a cell receives. So the key is
/// the <see cref="SceneDocument.MountainBodies"/> reference and nothing
/// else.</para>
///
/// <para>That key works because a <see cref="SceneDocument"/> is a record and
/// nobody mutates the list in place: painting a cell produces a new document
/// that carries the same list, so a Terrain stroke costs nothing here, while
/// authoring or removing a body builds a new list and invalidates.</para>
/// </summary>
public sealed class MountainOutlineCache
{
    private IReadOnlyList<MountainBodyDocument>? _bodies;
    private IReadOnlyList<MountainOutline> _outlines = [];

    public IReadOnlyList<MountainOutline> For(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        // A Scene without bodies is answered without touching the cache, so the
        // Template preview - which is folded and carries none - cannot evict the
        // contours of the Scene it is previewed over.
        if (scene.MountainBodies.Count == 0) return [];
        if (ReferenceEquals(_bodies, scene.MountainBodies)) return _outlines;

        List<MountainOutline> outlines = new(scene.MountainBodies.Count);
        foreach (var body in scene.MountainBodies)
        {
            outlines.Add(new MountainOutline(
                body.MountainBodyId,
                MountainPalette.IndexOf(body.MountainBodyId),
                MountainGeometry.Flatten(body).Points));
        }
        _bodies = scene.MountainBodies;
        _outlines = outlines;
        return _outlines;
    }
}
