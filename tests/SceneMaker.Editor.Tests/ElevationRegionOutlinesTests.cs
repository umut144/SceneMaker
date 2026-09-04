using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// The contours the canvas draws for finished hill bodies, the colour slot
/// each body wears, and the one thing the cache is allowed to depend on.
/// </summary>
public sealed class ElevationRegionOutlinesTests
{
    [Fact]
    public void EachBodyGetsOneContour()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);

        var outline = Assert.Single(new ElevationRegionOutlineCache().For(scene));

        Assert.Equal("mountain_0001", outline.ElevationRegionId);
        Assert.NotEmpty(outline.Points);
    }

    [Fact]
    public void ContoursKeepTheDocumentsCanonicalOrder()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 0, 0, 64, 64, 2m);
        scene = ElevationRegion(scene, workspace, 96, 0, 160, 64, 4m);
        scene = ElevationRegion(scene, workspace, 0, 96, 64, 160, 6m);

        var outlines = new ElevationRegionOutlineCache().For(scene);

        Assert.Equal(
            ["mountain_0001", "mountain_0002", "mountain_0003"],
            outlines.Select(static outline => outline.ElevationRegionId));
    }

    /// <summary>
    /// The same ring the Core flattens, handed on unchanged - a contour drawn
    /// from anything else could disagree with the cells the body fills.
    /// </summary>
    [Fact]
    public void TheContourIsTheFlattenedRingAndDoesNotRepeatItsFirstPoint()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);

        var outline = Assert.Single(new ElevationRegionOutlineCache().For(scene));

        Assert.Equal(ElevationRegionGeometry.Flatten(scene.ElevationRegions[0]).Points, outline.Points);
        Assert.NotEqual(outline.Points[0], outline.Points[^1]);
    }

    /// <summary>
    /// Pinned on purpose. The derivation must not quietly become
    /// <see cref="string.GetHashCode()"/>, which is randomised per process and
    /// would repaint every hill on restart.
    /// </summary>
    [Fact]
    public void ThePaletteIndexIsPinnedForKnownIds()
    {
        Assert.Equal(0, ElevationRegionPalette.IndexOf("mountain_0001"));
        Assert.Equal(5, ElevationRegionPalette.IndexOf("mountain_0006"));
        // Not the canonical form, so the hash answers - and answers this.
        Assert.Equal(0, ElevationRegionPalette.IndexOf("ridge"));
        Assert.Equal(1, ElevationRegionPalette.IndexOf("mountain_x"));
        Assert.Equal(3, ElevationRegionPalette.IndexOf("mountain_0000"));
        Assert.Equal(4, ElevationRegionPalette.IndexOf("MOUNTAIN_0001"));
    }

    /// <summary>
    /// Six bodies authored one after another wear six different colours, and the
    /// seventh starts the cycle again. Neighbours never share a colour, which is
    /// the whole point of colouring them.
    /// </summary>
    [Fact]
    public void SixConsecutiveIdsUseTheWholePalette()
    {
        var indices = Enumerable.Range(1, ElevationRegionPalette.Size)
            .Select(static ordinal => ElevationRegionPalette.IndexOf($"mountain_{ordinal:0000}"))
            .ToList();

        Assert.Equal(Enumerable.Range(0, ElevationRegionPalette.Size), indices);
        Assert.Equal(
            ElevationRegionPalette.IndexOf("mountain_0001"),
            ElevationRegionPalette.IndexOf($"mountain_{ElevationRegionPalette.Size + 1:0000}"));
    }

    [Fact]
    public void EveryIdLandsOnAColourThePaletteHas()
    {
        for (var ordinal = 1; ordinal <= 200; ordinal++)
        {
            Assert.InRange(ElevationRegionPalette.IndexOf($"mountain_{ordinal:0000}"), 0, ElevationRegionPalette.Size - 1);
            Assert.InRange(ElevationRegionPalette.IndexOf($"ridge_{ordinal}"), 0, ElevationRegionPalette.Size - 1);
        }
    }

    [Fact]
    public void TheSameDocumentIsAnsweredFromTheCache()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new ElevationRegionOutlineCache();

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
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new ElevationRegionOutlineCache();
        var before = cache.For(scene);

        var painted = TerrainEditing.Paint(scene, workspace.Terrain, 0, 0, "grass");

        Assert.Same(scene.ElevationRegions, painted.ElevationRegions);
        Assert.Same(before, cache.For(painted));
    }

    [Fact]
    public void AuthoringOrRemovingABodyInvalidatesTheContours()
    {
        using var workspace = TestWorkspace.Create();
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 0, 0, 64, 64, 2m);
        var cache = new ElevationRegionOutlineCache();
        var first = cache.For(scene);

        var added = ElevationRegion(scene, workspace, 96, 0, 160, 64, 4m);
        var afterAdd = cache.For(added);
        var afterRemove = cache.For(ElevationRegionEditing.Remove(added, "mountain_0001"));

        Assert.NotSame(first, afterAdd);
        Assert.Equal(2, afterAdd.Count);
        Assert.Equal("mountain_0002", Assert.Single(afterRemove).ElevationRegionId);
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
        var scene = ElevationRegion(TestScenes.EmptyInstance(), workspace, 32, 32, 160, 160, 4m);
        var cache = new ElevationRegionOutlineCache();
        var outlines = cache.For(scene);

        var baseScene = TemplateEditing.PlaceAnchor(scene, workspace.Metrics, 64, 64, 1);
        var composed = TemplateComposition.Compose(
            baseScene,
            [TestScenes.Template(workspace, "tpl_a", 1)],
            workspace.Props,
            seed: 7UL).ComposedScene;

        Assert.Empty(composed.ElevationRegions);
        Assert.Empty(cache.For(composed));
        Assert.Empty(cache.For(TestScenes.EmptyInstance()));
        Assert.Same(outlines, cache.For(scene));
    }

    private static SceneDocument ElevationRegion(
        SceneDocument scene,
        TestWorkspace workspace,
        int left,
        int bottom,
        int right,
        int top,
        decimal elevationMeters) =>
        ElevationRegionEditing.Place(
            scene,
            workspace.Metrics,
            [
                ElevationRegionEditing.Point(left, bottom),
                ElevationRegionEditing.Point(right, bottom),
                ElevationRegionEditing.Point(right, top),
                ElevationRegionEditing.Point(left, top),
            ],
            elevationMeters);
}
