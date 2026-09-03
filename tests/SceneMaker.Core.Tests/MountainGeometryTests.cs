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
        // A Prop over mountain-only Terrain proves coverage also reads the fold.
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
