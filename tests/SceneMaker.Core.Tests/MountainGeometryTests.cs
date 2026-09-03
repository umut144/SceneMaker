using System.Text.Json;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A mountain keeps its closed contour as authoring truth and contributes a
/// level top to Terrain's solid column. These tests pin the fold before a UI
/// starts producing bodies.
/// </summary>
public sealed class MountainGeometryTests
{
    [Fact]
    public void ClosedAlignedDraftResolvesHandlesAtEveryPointIncludingTheSeam()
    {
        var points = MountainEditing.ResolveContour(
        [
            new MountainDraftPoint(0, 0, MountainPointMode.Aligned),
            new MountainDraftPoint(96, 0, MountainPointMode.Aligned),
            new MountainDraftPoint(96, 96, MountainPointMode.Aligned),
        ]);

        Assert.All(points, point =>
        {
            Assert.False(point.HandleInAuthoringPx.IsZero());
            Assert.False(point.HandleOutAuthoringPx.IsZero());
        });
    }

    [Fact]
    public void PickingAnOverlapFindsTheHighestMountain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            2m);
        scene = MountainEditing.Place(
            scene,
            workspace.Metrics,
            workspace.Terrain,
            Square(64, 64, 128, 128),
            "grass",
            4m);

        Assert.Equal("mountain_0002", MountainEditing.FindAt(scene, 96, 96)?.MountainBodyId);
        Assert.Equal("mountain_0001", MountainEditing.FindAt(scene, 48, 48)?.MountainBodyId);
        Assert.Null(MountainEditing.FindAt(scene, 8, 8));
    }

    [Fact]
    public void ASquareMountainRaisesExactlyTheCellsInsideItsContour()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(16, terrain.Count);
        Assert.Equal(
            Enumerable.Range(1, 4).SelectMany(y =>
                Enumerable.Range(1, 4).Select(x => new TerrainCellCoordinate(x, y))),
            terrain.Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y)));
        Assert.All(terrain, cell => Assert.Equal(10.0m, cell.ElevationMeters));
    }

    [Fact]
    public void ACurvedMountainFollowsItsBezierRatherThanItsControlPolygon()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            [
                MountainEditing.Point(
                    32,
                    96,
                    MountainPointMode.Aligned,
                    new AuthoringPixelOffset { X = 0, Y = -96 },
                    new AuthoringPixelOffset { X = 0, Y = 96 }),
                MountainEditing.Point(
                    160,
                    96,
                    MountainPointMode.Aligned,
                    new AuthoringPixelOffset { X = 0, Y = 96 },
                    new AuthoringPixelOffset { X = 0, Y = -96 }),
            ],
            "grass",
            10.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal([1, 2, 3, 4], terrain.Select(static cell => cell.Y).Distinct().Order());
        Assert.DoesNotContain(terrain, cell => cell.Y is 0 or 5);
    }

    [Fact]
    public void ASecondMountainCanSitOnTheFirstAtAnAbsoluteHigherTop()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);
        scene = MountainEditing.Place(
            scene,
            workspace.Metrics,
            workspace.Terrain,
            Square(64, 64, 128, 128),
            "sand",
            15.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(16, terrain.Count);
        Assert.Equal(12, terrain.Count(cell => cell.ElevationMeters == 10.0m));
        Assert.Equal(4, terrain.Count(cell =>
            cell.ElevationMeters == 15.0m && cell.AssetKey == "sand"));
    }

    [Fact]
    public void ALowerMountainNeverCutsDownExistingTerrain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "sand",
            0.5m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(36, terrain.Count);
        Assert.All(terrain, cell =>
        {
            Assert.Equal(1.0m, cell.ElevationMeters);
            Assert.Equal("grass", cell.AssetKey);
        });
    }

    [Fact]
    public void AContourAtThePaintedHeightDeliberatelyOwnsTheSurface()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "sand",
            1.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(16, terrain.Count(cell => cell.AssetKey == "sand"));
        Assert.Equal(20, terrain.Count(cell => cell.AssetKey == "grass"));
    }

    [Fact]
    public void EqualMountainTopsWithDifferentAssetsAreAmbiguous()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);
        scene = MountainEditing.Place(
            scene,
            workspace.Metrics,
            workspace.Terrain,
            Square(64, 64, 128, 128),
            "sand",
            10.0m);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(scene, workspace.Metrics));

        Assert.Contains("mountain_0001", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mountain_0002", exception.Message, StringComparison.Ordinal);
        Assert.Contains("different Assets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MountainElevationUsesTheWorkspaceQuantum()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainEditing.Place(
                TestScenes.EmptyInstance(),
                workspace.Metrics,
                workspace.Terrain,
                Square(32, 32, 160, 160),
                "grass",
                1.1m));

        Assert.Contains("0.125", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACurveAuthoredTerrainAssetCannotSurfaceAMountain()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainEditing.Place(
                TestScenes.EmptyInstance(),
                workspace.Metrics,
                workspace.Terrain,
                Square(32, 32, 160, 160),
                "river",
                10.0m));

        Assert.Contains("authored as a curve", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASelfIntersectingMountainIsRefusedBeforeItEntersTheDocument()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainEditing.Place(
                TestScenes.EmptyInstance(),
                workspace.Metrics,
                workspace.Terrain,
                [
                    MountainEditing.Point(32, 32),
                    MountainEditing.Point(160, 160),
                    MountainEditing.Point(160, 32),
                    MountainEditing.Point(32, 192),
                ],
                "grass",
                10.0m));

        Assert.Contains("contact with itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MountainSourceSurvivesAnAuthoringRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));

        var body = Assert.Single(restored.MountainBodies);
        Assert.Equal("mountain_0001", body.MountainBodyId);
        Assert.Equal(10.0m, body.ElevationMeters);
        Assert.Equal(4, body.Points.Count);
    }

    [Fact]
    public void ExportFoldsMountainsWithoutLeakingTheirAuthoringSource()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);
        // A Prop rides along: the fold must leave the Props beside it alone.
        scene = PropEditing.Place(scene, workspace.Props, 64, 64, "stone", 10.0m);
        var session = WorkspaceSession.Load(workspace.RootPath);
        var loaded = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            scene);

        var written = SceneExport.Write(session, loaded);
        var json = File.ReadAllText(written.Path);
        using var parsed = JsonDocument.Parse(json);
        var exportedScene = parsed.RootElement.GetProperty("scene");

        Assert.Equal(10, exportedScene.GetProperty("version").GetInt32());
        Assert.False(exportedScene.TryGetProperty("mountain_bodies", out _));
        Assert.Equal(16, exportedScene.GetProperty("terrain_cells").GetArrayLength());
        Assert.All(
            exportedScene.GetProperty("terrain_cells").EnumerateArray(),
            cell => Assert.Equal(10.0m, cell.GetProperty("elevation_meters").GetDecimal()));
    }

    [Fact]
    public void SceneTemplatesRefuseMountainBodiesUntilCompositionCanMoveTheirContours()
    {
        using var workspace = TestWorkspace.Create();
        var instance = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            workspace.Terrain,
            Square(32, 32, 160, 160),
            "grass",
            10.0m);
        var template = SceneDocument.CreateTemplate("mountain", 6, 6, 1, 0, 0) with
        {
            MountainBodies = instance.MountainBodies,
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(template));

        Assert.Contains("Template cannot own mountain bodies", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tie that the old step-by-step fold could not see: a third, higher
    /// body covers the same cell, so comparing each body only against the
    /// winner so far never brought the two lower ones together.
    /// </summary>
    [Fact]
    public void EqualTopsWithDifferentAssetsAreAmbiguousEvenUnderAHigherMountain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(
            Body("mountain_0001", "grass", 15.0m, Square(32, 32, 160, 160)),
            Body("mountain_0002", "grass", 10.0m, Square(32, 32, 160, 160)),
            Body("mountain_0003", "sand", 10.0m, Square(64, 64, 128, 128)));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(scene, workspace.Metrics));

        Assert.Contains("mountain_0002", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mountain_0003", exception.Message, StringComparison.Ordinal);
        Assert.Contains("different Assets", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same three bodies in any list order are the same Scene, so they are
    /// refused with the same message. The order a body was drawn or named in is
    /// not a geometric fact and must not decide whether a tie is seen.
    /// </summary>
    [Fact]
    public void TheAmbiguityAndItsMessageDoNotDependOnTheOrderOfTheBodies()
    {
        using var workspace = TestWorkspace.Create();
        var tall = Body("mountain_0001", "grass", 15.0m, Square(32, 32, 160, 160));
        var grass = Body("mountain_0002", "grass", 10.0m, Square(32, 32, 160, 160));
        var sand = Body("mountain_0003", "sand", 10.0m, Square(64, 64, 128, 128));

        var messages = new[]
        {
            Refusal(workspace, tall, grass, sand),
            Refusal(workspace, sand, grass, tall),
            Refusal(workspace, grass, sand, tall),
            Refusal(workspace, sand, tall, grass),
        };

        Assert.Single(messages.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void EqualTopsSharingOneAssetMayOverlap()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(
            Body("mountain_0001", "grass", 10.0m, Square(32, 32, 160, 160)),
            Body("mountain_0002", "grass", 10.0m, Square(64, 64, 192, 192)));

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);
        Assert.All(terrain, cell => Assert.Equal(10.0m, cell.ElevationMeters));
        Assert.All(terrain, cell => Assert.Equal("grass", cell.AssetKey));
    }

    /// <summary>
    /// Checking the ties changes nothing about which contribution shows: the
    /// highest top still wins, over a tie and over painted Terrain alike.
    /// </summary>
    [Fact]
    public void HighestTopStillWinsOverATiedPairBeneathIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 3, 3, "sand", 1.0m);
        scene = scene with
        {
            MountainBodies =
            [
                Body("mountain_0001", "grass", 15.0m, Square(64, 64, 128, 128)),
                Body("mountain_0002", "grass", 10.0m, Square(32, 32, 160, 160)),
                Body("mountain_0003", "grass", 10.0m, Square(32, 32, 160, 160)),
            ],
        };

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        var covered = terrain.Single(cell => cell is { X: 3, Y: 3 });
        Assert.Equal(15.0m, covered.ElevationMeters);
        Assert.Equal("grass", covered.AssetKey);
        Assert.Equal(16, terrain.Count);
        Assert.Equal(4, terrain.Count(cell => cell.ElevationMeters == 15.0m));
        Assert.Equal(12, terrain.Count(cell => cell.ElevationMeters == 10.0m));
        // The painted cell was covered, so its own height is gone from the fold.
        Assert.DoesNotContain(terrain, cell => cell.ElevationMeters == 1.0m);
    }

    [Fact]
    public void AMountainAnchorOutsideTheSceneIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(
            Body("mountain_0001", "grass", 10.0m, Square(32, 32, 224, 160)));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(scene, workspace.Metrics));

        Assert.Contains(
            "Mountain body 'mountain_0001' point 1 lies outside Scene bounds",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMountainAnchorBetweenGridLinesIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(
            Body("mountain_0001", "grass", 10.0m, Square(32, 32, 160, 144)));

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(scene, workspace.Metrics));

        Assert.Contains(
            "Mountain body 'mountain_0001' point 2 must align to the 32-authoring-pixel grid",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Only the anchor is a place. A handle shapes the curve, so it stays
    /// unsnapped and may push the curve past the edge of the map, where the
    /// raster stops rather than the document failing.
    /// </summary>
    [Fact]
    public void UnsnappedHandlesReachingBeyondTheSceneStayValid()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(Body("mountain_0001", "grass", 10.0m, Lens(200)));

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.NotEmpty(MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics));
    }

    /// <summary>
    /// Two authored anchors are enough when the handles bow the closing edges
    /// apart, so the document rule stays at two points and the contour rule
    /// decides whether they enclose anything.
    /// </summary>
    [Fact]
    public void TheCurvedTwoAnchorLensStaysValidAtTheIoBoundary()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(Body("mountain_0001", "grass", 10.0m, Lens(96)));

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.Equal(2, Assert.Single(scene.MountainBodies).Points.Count);
    }

    [Fact]
    public void AGridAlignedMountainStaysValid()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(
            Body("mountain_0001", "grass", 10.0m, Square(0, 0, 192, 192)),
            Body("mountain_0002", "sand", 12.0m, Square(64, 64, 128, 128)));

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.Equal(36, MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics).Count);
    }

    private static string Refusal(TestWorkspace workspace, params MountainBodyDocument[] bodies) =>
        Assert.Throws<SceneMakerDocumentException>(() =>
            MountainGeometry.EffectiveTerrainCells(
                TestScenes.EmptyInstance() with { MountainBodies = [.. bodies] },
                workspace.Metrics)).Message;

    private static SceneDocument Bodies(params MountainBodyDocument[] bodies) =>
        TestScenes.EmptyInstance() with { MountainBodies = [.. bodies] };

    private static MountainBodyDocument Body(
        string mountainBodyId,
        string assetKey,
        decimal elevationMeters,
        IReadOnlyList<MountainCurvePointDocument> points) => new()
    {
        MountainBodyId = mountainBodyId,
        AssetKey = assetKey,
        ElevationMeters = elevationMeters,
        Points = [.. points],
    };

    /// <summary>
    /// Two anchors whose handles bow the two closing edges apart into a lens.
    /// <paramref name="reach"/> is the handle length in authoring pixels; large
    /// values push the curve outside the Scene without moving an anchor.
    /// </summary>
    private static IReadOnlyList<MountainCurvePointDocument> Lens(int reach) =>
    [
        MountainEditing.Point(
            32,
            96,
            MountainPointMode.Aligned,
            new AuthoringPixelOffset { X = 0, Y = -reach },
            new AuthoringPixelOffset { X = 0, Y = reach }),
        MountainEditing.Point(
            160,
            96,
            MountainPointMode.Aligned,
            new AuthoringPixelOffset { X = 0, Y = reach },
            new AuthoringPixelOffset { X = 0, Y = -reach }),
    ];

    private static IReadOnlyList<MountainCurvePointDocument> Square(
        int left,
        int bottom,
        int right,
        int top) =>
    [
        MountainEditing.Point(left, bottom),
        MountainEditing.Point(right, bottom),
        MountainEditing.Point(right, top),
        MountainEditing.Point(left, top),
    ];
}
