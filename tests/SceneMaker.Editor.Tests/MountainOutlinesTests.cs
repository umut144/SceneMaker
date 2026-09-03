using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The contours the canvas draws for finished mountain bodies, the colour slot
/// each body wears, and the one thing the cache is allowed to depend on.
/// </summary>
public sealed class MountainOutlinesTests
{
    [Fact]
    public void EachBodyGetsOneContour()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);

        var outline = Assert.Single(new MountainOutlineCache().For(scene));

        Assert.Equal("mountain_0001", outline.MountainBodyId);
        Assert.NotEmpty(outline.Points);
    }

    [Fact]
    public void ContoursKeepTheDocumentsCanonicalOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 0, 0, 64, 64, 2m);
        scene = Mountain(scene, workspace, 96, 0, 160, 64, 4m);
        scene = Mountain(scene, workspace, 0, 96, 64, 160, 6m);

        var outlines = new MountainOutlineCache().For(scene);

        Assert.Equal(
            ["mountain_0001", "mountain_0002", "mountain_0003"],
            outlines.Select(static outline => outline.MountainBodyId));
    }

    /// <summary>
    /// The same ring the Core flattens, handed on unchanged - a contour drawn
    /// from anything else could disagree with the cells the body fills.
    /// </summary>
    [Fact]
    public void TheContourIsTheFlattenedRingAndDoesNotRepeatItsFirstPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);

        var outline = Assert.Single(new MountainOutlineCache().For(scene));

        Assert.Equal(MountainGeometry.Flatten(scene.MountainBodies[0]).Points, outline.Points);
        Assert.NotEqual(outline.Points[0], outline.Points[^1]);
    }

    /// <summary>
    /// Pinned on purpose. The derivation must not quietly become
    /// <see cref="string.GetHashCode()"/>, which is randomised per process and
    /// would repaint every mountain on restart.
    /// </summary>
    [Fact]
    public void ThePaletteIndexIsPinnedForKnownIds()
    {
        Assert.Equal(0, MountainPalette.IndexOf("mountain_0001"));
        Assert.Equal(5, MountainPalette.IndexOf("mountain_0006"));
        // Not the canonical form, so the hash answers - and answers this.
        Assert.Equal(0, MountainPalette.IndexOf("ridge"));
        Assert.Equal(1, MountainPalette.IndexOf("mountain_x"));
        Assert.Equal(3, MountainPalette.IndexOf("mountain_0000"));
        Assert.Equal(4, MountainPalette.IndexOf("MOUNTAIN_0001"));
    }

    /// <summary>
    /// Six bodies authored one after another wear six different colours, and the
    /// seventh starts the cycle again. Neighbours never share a colour, which is
    /// the whole point of colouring them.
    /// </summary>
    [Fact]
    public void SixConsecutiveIdsUseTheWholePalette()
    {
        var indices = Enumerable.Range(1, MountainPalette.Size)
            .Select(static ordinal => MountainPalette.IndexOf($"mountain_{ordinal:0000}"))
            .ToList();

        Assert.Equal(Enumerable.Range(0, MountainPalette.Size), indices);
        Assert.Equal(
            MountainPalette.IndexOf("mountain_0001"),
            MountainPalette.IndexOf($"mountain_{MountainPalette.Size + 1:0000}"));
    }

    [Fact]
    public void EveryIdLandsOnAColourThePaletteHas()
    {
        for (var ordinal = 1; ordinal <= 200; ordinal++)
        {
            Assert.InRange(MountainPalette.IndexOf($"mountain_{ordinal:0000}"), 0, MountainPalette.Size - 1);
            Assert.InRange(MountainPalette.IndexOf($"ridge_{ordinal}"), 0, MountainPalette.Size - 1);
        }
    }

    [Fact]
    public void TheSameDocumentIsAnsweredFromTheCache()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new MountainOutlineCache();

        Assert.Same(cache.For(scene), cache.For(scene));
    }

    /// <summary>
    /// The invariant that keeps this cache small: a Terrain stroke makes a new
    /// document but carries the same body list, so painting must not cost a
    /// re-flatten. If this ever fails, the key stopped being the bodies.
    /// </summary>
    [Fact]
    public void PaintingTerrainDoesNotInvalidateTheContours()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new MountainOutlineCache();
        var before = cache.For(scene);

        var painted = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass");

        Assert.Same(scene.MountainBodies, painted.MountainBodies);
        Assert.Same(before, cache.For(painted));
    }

    [Fact]
    public void AuthoringOrRemovingABodyInvalidatesTheContours()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 0, 0, 64, 64, 2m);
        var cache = new MountainOutlineCache();
        var first = cache.For(scene);

        var added = Mountain(scene, workspace, 96, 0, 160, 64, 4m);
        var afterAdd = cache.For(added);
        var afterRemove = cache.For(MountainEditing.Remove(added, "mountain_0001"));

        Assert.NotSame(first, afterAdd);
        Assert.Equal(2, afterAdd.Count);
        Assert.Equal("mountain_0002", Assert.Single(afterRemove).MountainBodyId);
    }

    /// <summary>
    /// Composition folds the bodies away, so a Template preview carries none -
    /// and being answered without touching the cache is what stops the preview
    /// from evicting the contours of the Scene it is drawn over.
    /// </summary>
    [Fact]
    public void ASceneWithoutBodiesHasNoContoursAndLeavesTheCacheAlone()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Mountain(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new MountainOutlineCache();
        var outlines = cache.For(scene);

        var baseScene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 64, 64, 1);
        var composed = TemplateComposition.Compose(
            baseScene,
            [TestScenes.Template(workspace, "tpl_a", 1)],
            workspace.Props,
            seed: 7UL).ComposedScene;

        Assert.Empty(composed.MountainBodies);
        Assert.Empty(cache.For(composed));
        Assert.Empty(cache.For(TestScenes.EmptyInstance()));
        Assert.Same(outlines, cache.For(scene));
    }

    private static SceneDocument Mountain(
        SceneDocument scene,
        TestWorkspace workspace,
        int left,
        int bottom,
        int right,
        int top,
        decimal elevationMeters) =>
        MountainEditing.Place(
            scene,
            workspace.Metrics,
            [
                MountainEditing.Point(left, bottom),
                MountainEditing.Point(right, bottom),
                MountainEditing.Point(right, top),
                MountainEditing.Point(left, top),
            ],
            elevationMeters);
}
