using System.Text.Json;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A mountain keeps its closed contour and one absolute top as authoring truth
/// and raises painted Terrain to it. It carries no material of its own: the
/// painted cell decides whether there is a column at all and what its surface
/// is made of. These tests pin that fold.
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
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            2m);
        scene = MountainEditing.Place(scene, workspace.Metrics, Square(64, 64, 128, 128), 4m);

        Assert.Equal(
            "mountain_0002",
            MountainEditing.FindAtCell(scene, workspace.Metrics, new TerrainCellCoordinate(3, 3))
                ?.MountainBodyId);
        Assert.Equal(
            "mountain_0001",
            MountainEditing.FindAtCell(scene, workspace.Metrics, new TerrainCellCoordinate(1, 1))
                ?.MountainBodyId);
        Assert.Null(
            MountainEditing.FindAtCell(scene, workspace.Metrics, new TerrainCellCoordinate(0, 0)));
    }

    [Fact]
    public void ReshapingChangesOnlyTheRequestedContour()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            2m);
        scene = MountainEditing.Place(
            scene,
            workspace.Metrics,
            Square(64, 64, 128, 128),
            4m);
        var firstBefore = scene.MountainBodies[0];
        var secondBefore = scene.MountainBodies[1];
        var points = firstBefore.Points.ToList();
        points[1] = points[1] with
        {
            PositionAuthoringPx = new AuthoringPixelPosition { X = 192, Y = 32 },
        };

        var reshaped = MountainEditing.Reshape(scene, firstBefore.MountainBodyId, points);

        var firstAfter = reshaped.MountainBodies[0];
        Assert.Equal(firstBefore.MountainBodyId, firstAfter.MountainBodyId);
        Assert.Equal(firstBefore.ElevationMeters, firstAfter.ElevationMeters);
        Assert.Equal(192, firstAfter.Points[1].PositionAuthoringPx.X);
        Assert.Same(secondBefore, reshaped.MountainBodies[1]);
    }

    [Fact]
    public void TryReshapeRefusesSelfContactWithoutChangingTheScene()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            4m);
        var points = scene.MountainBodies[0].Points.ToList();
        points[1] = points[1] with
        {
            PositionAuthoringPx = new AuthoringPixelPosition { X = 32, Y = 192 },
        };

        var result = MountainEditing.TryReshape(scene, "mountain_0001", points);

        Assert.Null(result.Scene);
        Assert.Null(result.Body);
        Assert.Contains("contact with itself", result.Reason!, StringComparison.Ordinal);
        Assert.Equal(160, scene.MountainBodies[0].Points[1].PositionAuthoringPx.X);
    }

    [Fact]
    public void ASquareMountainRaisesExactlyThePaintedCellsInsideItsContour()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(36, terrain.Count);
        Assert.Equal(
            Enumerable.Range(1, 4).SelectMany(y =>
                Enumerable.Range(1, 4).Select(x => new TerrainCellCoordinate(x, y))),
            terrain.Where(static cell => cell.ElevationMeters == 10.0m)
                .Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y)));
        Assert.Equal(20, terrain.Count(static cell => cell.ElevationMeters == 1.0m));
    }

    /// <summary>
    /// The point of dropping the Asset from the body: a contour is a shape and
    /// a height, and the surface it lifts is whatever was painted there. Sand
    /// and grass come up as sand and grass, in the same places.
    /// </summary>
    [Fact]
    public void AContourLiftsThePaintedMaterialsUnderItUnchanged()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 1, 1, "sand");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 1, "sand");
        scene = MountainEditing.Place(scene, workspace.Metrics, Square(32, 32, 160, 160), 10.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        var raised = terrain.Where(static cell => cell.ElevationMeters == 10.0m).ToList();
        Assert.Equal(16, raised.Count);
        Assert.Equal(
            [new TerrainCellCoordinate(1, 1), new TerrainCellCoordinate(2, 1)],
            raised.Where(static cell => cell.AssetKey == "sand")
                .Select(static cell => new TerrainCellCoordinate(cell.X, cell.Y)));
        Assert.Equal(14, raised.Count(static cell => cell.AssetKey == "grass"));
    }

    /// <summary>
    /// A mountain has no material to make ground out of, so it creates no cell
    /// where nobody painted. The body is still a valid document and starts
    /// working the moment Terrain appears under it - the same rule read from
    /// the other side.
    /// </summary>
    [Fact]
    public void AContourOverUnpaintedGroundIsValidAndRaisesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.EmptyInstance(),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
        Assert.Empty(MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics));

        var painted = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "sand");
        var cell = Assert.Single(
            MountainGeometry.EffectiveTerrainCells(painted, workspace.Metrics));
        Assert.Equal(10.0m, cell.ElevationMeters);
        Assert.Equal("sand", cell.AssetKey);
    }

    /// <summary>
    /// The walk over the bodies is unconditional. A Scene with nothing to raise
    /// still has to refuse an unusable contour, so the check may not be skipped
    /// when the fold would produce no cell anyway.
    /// </summary>
    [Fact]
    public void AnUnusableContourIsRefusedEvenWithoutAnyTerrainToRaise()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(Body("mountain_0001", 10.0m,
        [
            MountainEditing.Point(32, 32),
            MountainEditing.Point(160, 160),
            MountainEditing.Point(160, 32),
            MountainEditing.Point(32, 192),
        ]));

        Assert.Empty(scene.TerrainCells);
        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics));

        Assert.Contains("contact with itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACurvedMountainFollowsItsBezierRatherThanItsControlPolygon()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Lens(96),
            10.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics)
            .Where(static cell => cell.ElevationMeters == 10.0m)
            .ToList();

        Assert.Equal([1, 2, 3, 4], terrain.Select(static cell => cell.Y).Distinct().Order());
        Assert.DoesNotContain(terrain, static cell => cell.Y is 0 or 5);
    }

    [Fact]
    public void ASecondMountainCanSitOnTheFirstAtAnAbsoluteHigherTop()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);
        scene = MountainEditing.Place(scene, workspace.Metrics, Square(64, 64, 128, 128), 15.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(36, terrain.Count);
        Assert.Equal(12, terrain.Count(static cell => cell.ElevationMeters == 10.0m));
        Assert.Equal(4, terrain.Count(static cell => cell.ElevationMeters == 15.0m));
        Assert.All(terrain, static cell => Assert.Equal("grass", cell.AssetKey));
    }

    [Fact]
    public void ALowerMountainNeverCutsDownExistingTerrain()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            0.5m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(36, terrain.Count);
        Assert.All(terrain, static cell =>
        {
            Assert.Equal(1.0m, cell.ElevationMeters);
            Assert.Equal("grass", cell.AssetKey);
        });
    }

    /// <summary>
    /// A contour at exactly the painted height changes nothing at all now. It
    /// used to take the surface over, because the body carried an Asset that
    /// could differ from the paint; with no Asset on the body there is no
    /// second answer to prefer.
    /// </summary>
    [Fact]
    public void AContourAtThePaintedHeightLeavesTheCellExactlyAsItWas()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.Instance(workspace), workspace.Terrain, 2, 2, "sand");
        scene = MountainEditing.Place(scene, workspace.Metrics, Square(32, 32, 160, 160), 1.0m);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        Assert.Equal(scene.TerrainCells, terrain);
        Assert.Equal(1, terrain.Count(static cell => cell.AssetKey == "sand"));
    }

    /// <summary>
    /// Two bodies meeting at one height used to be refused when they named
    /// different Assets, because geometry supplied no winner for the material.
    /// A height is a number and both name the same one, so the question is gone
    /// with the material.
    /// </summary>
    [Fact]
    public void TwoBodiesMayShareOneCellAtOneHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            MountainBodies =
            [
                Body("mountain_0001", 10.0m, Square(32, 32, 160, 160)),
                Body("mountain_0002", 10.0m, Square(64, 64, 192, 192)),
            ],
        };

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);
        // Sixteen cells each, nine of them shared.
        Assert.Equal(23, terrain.Count(static cell => cell.ElevationMeters == 10.0m));
        Assert.All(terrain, static cell => Assert.Equal("grass", cell.AssetKey));
    }

    /// <summary>
    /// The fold does not depend on the order the bodies happen to be listed in,
    /// which is their ID order and therefore the order they were drawn in.
    /// </summary>
    [Fact]
    public void TheFoldDoesNotDependOnTheOrderOfTheBodies()
    {
        using var workspace = TestWorkspace.Create();
        var tall = Body("mountain_0001", 15.0m, Square(64, 64, 128, 128));
        var wide = Body("mountain_0002", 10.0m, Square(32, 32, 160, 160));
        var same = Body("mountain_0003", 10.0m, Square(32, 32, 160, 160));

        var folds = new[]
        {
            Fold(workspace, tall, wide, same),
            Fold(workspace, same, wide, tall),
            Fold(workspace, wide, same, tall),
            Fold(workspace, same, tall, wide),
        };

        Assert.All(folds, fold => Assert.Equal(folds[0], fold));
        Assert.Equal(4, folds[0].Count(static cell => cell.ElevationMeters == 15.0m));
        Assert.Equal(12, folds[0].Count(static cell => cell.ElevationMeters == 10.0m));
    }

    /// <summary>
    /// The highest top wins over a lower body and over painted Terrain alike.
    /// </summary>
    [Fact]
    public void HighestTopWinsOverALowerBodyBeneathIt()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.Instance(workspace), workspace.Terrain, 3, 3, "sand");
        scene = scene with
        {
            MountainBodies =
            [
                Body("mountain_0001", 15.0m, Square(64, 64, 128, 128)),
                Body("mountain_0002", 10.0m, Square(32, 32, 160, 160)),
            ],
        };

        var terrain = MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics);

        var covered = terrain.Single(static cell => cell is { X: 3, Y: 3 });
        Assert.Equal(15.0m, covered.ElevationMeters);
        Assert.Equal("sand", covered.AssetKey);
        Assert.Equal(4, terrain.Count(static cell => cell.ElevationMeters == 15.0m));
        Assert.Equal(12, terrain.Count(static cell => cell.ElevationMeters == 10.0m));
        Assert.Equal(20, terrain.Count(static cell => cell.ElevationMeters == 1.0m));
    }

    /// <summary>
    /// One definition read backwards: what a body lifts is what removing it
    /// would drop. Cells another body holds higher, and cells nobody painted,
    /// are not this body's to lose.
    /// </summary>
    [Fact]
    public void ABodyOnlyOwnsTheCellsItActuallyLifts()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 1, 1, "sand");
        scene = TerrainEditing.Paint(scene, workspace.Terrain, 2, 2, "grass");
        scene = scene with
        {
            MountainBodies =
            [
                Body("mountain_0001", 10.0m, Square(32, 32, 160, 160)),
                Body("mountain_0002", 15.0m, Square(64, 64, 128, 128)),
            ],
        };

        var wide = MountainGeometry.CellsRaisedBy(
            scene, workspace.Metrics, scene.MountainBodies[0]);
        var tall = MountainGeometry.CellsRaisedBy(
            scene, workspace.Metrics, scene.MountainBodies[1]);

        // (1, 1) is painted and this body's alone; (2, 2) belongs to the higher
        // body; the other fourteen covered coordinates were never painted.
        var lifted = Assert.Single(wide);
        Assert.Equal(new TerrainCellCoordinate(1, 1), new TerrainCellCoordinate(lifted.X, lifted.Y));
        Assert.Equal(10.0m, lifted.ElevationMeters);
        Assert.Equal("sand", lifted.AssetKey);

        var top = Assert.Single(tall);
        Assert.Equal(new TerrainCellCoordinate(2, 2), new TerrainCellCoordinate(top.X, top.Y));
        Assert.Equal(15.0m, top.ElevationMeters);
        Assert.Equal("grass", top.AssetKey);
    }

    /// <summary>
    /// Two bodies at one height each raise nothing over the other, and removing
    /// either leaves the height where it is. That falls out of the definition
    /// rather than being a case of its own.
    /// </summary>
    [Fact]
    public void NeitherOfTwoTiedBodiesOwnsTheCellTheyShare()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TerrainEditing.Paint(
            TestScenes.EmptyInstance(), workspace.Terrain, 2, 2, "grass");
        scene = scene with
        {
            MountainBodies =
            [
                Body("mountain_0001", 10.0m, Square(32, 32, 160, 160)),
                Body("mountain_0002", 10.0m, Square(32, 32, 160, 160)),
            ],
        };

        Assert.Empty(MountainGeometry.CellsRaisedBy(
            scene, workspace.Metrics, scene.MountainBodies[0]));
        Assert.Empty(MountainGeometry.CellsRaisedBy(
            scene, workspace.Metrics, scene.MountainBodies[1]));
    }

    /// <summary>
    /// The same definition answers for a body that is not in the Scene yet,
    /// because a body is told apart by ID and never compared against itself.
    /// </summary>
    [Fact]
    public void TheSameDefinitionAnswersWhatPlacingABodyWouldRaise()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace);
        var candidate = Body("mountain_0001", 10.0m, Square(32, 32, 160, 160));

        var raised = MountainGeometry.CellsRaisedBy(scene, workspace.Metrics, candidate);

        Assert.Equal(16, raised.Count);
        Assert.All(raised, static cell =>
        {
            Assert.Equal(10.0m, cell.ElevationMeters);
            Assert.Equal("grass", cell.AssetKey);
        });
    }

    [Fact]
    public void MountainElevationUsesTheWorkspaceQuantum()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainEditing.Place(
                TestScenes.Instance(workspace),
                workspace.Metrics,
                Square(32, 32, 160, 160),
                1.1m));

        Assert.Contains("0.125", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASelfIntersectingMountainIsRefusedBeforeItEntersTheDocument()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            MountainEditing.Place(
                TestScenes.Instance(workspace),
                workspace.Metrics,
                [
                    MountainEditing.Point(32, 32),
                    MountainEditing.Point(160, 160),
                    MountainEditing.Point(160, 32),
                    MountainEditing.Point(32, 192),
                ],
                10.0m));

        Assert.Contains("contact with itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MountainSourceSurvivesAnAuthoringRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));

        var body = Assert.Single(restored.MountainBodies);
        Assert.Equal("mountain_0001", body.MountainBodyId);
        Assert.Equal(10.0m, body.ElevationMeters);
        Assert.Equal(4, body.Points.Count);
    }

    /// <summary>
    /// The body is a shape and a height, and the serialized document says so:
    /// there is no material on it to read back.
    /// </summary>
    [Fact]
    public void AMountainBodyCarriesNoAssetInTheDocument()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);

        using var parsed = JsonDocument.Parse(DocumentJson.Serialize(scene));
        var body = parsed.RootElement.GetProperty("mountain_bodies")[0];

        Assert.Equal(12, parsed.RootElement.GetProperty("version").GetInt32());
        Assert.False(body.TryGetProperty("asset_key", out _));
        Assert.Equal(10.0m, body.GetProperty("elevation_meters").GetDecimal());
    }

    [Fact]
    public void ExportFoldsMountainsWithoutLeakingTheirAuthoringSource()
    {
        using var workspace = TestWorkspace.Create();
        var scene = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
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
        var cells = exportedScene.GetProperty("terrain_cells").EnumerateArray().ToList();
        Assert.Equal(36, cells.Count);
        Assert.Equal(
            16,
            cells.Count(cell => cell.GetProperty("elevation_meters").GetDecimal() == 10.0m));
        Assert.All(cells, cell => Assert.Equal("grass", cell.GetProperty("asset_key").GetString()));
    }

    [Fact]
    public void SceneTemplatesRefuseMountainBodiesUntilCompositionCanMoveTheirContours()
    {
        using var workspace = TestWorkspace.Create();
        var instance = MountainEditing.Place(
            TestScenes.Instance(workspace),
            workspace.Metrics,
            Square(32, 32, 160, 160),
            10.0m);
        var template = SceneDocument.CreateTemplate("mountain", 6, 6, 1, 0, 0) with
        {
            MountainBodies = instance.MountainBodies,
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(template));

        Assert.Contains("Template cannot own mountain bodies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMountainAnchorOutsideTheSceneIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Bodies(Body("mountain_0001", 10.0m, Square(32, 32, 224, 160)));

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
        var scene = Bodies(Body("mountain_0001", 10.0m, Square(32, 32, 160, 144)));

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
        var scene = TestScenes.Instance(workspace) with
        {
            MountainBodies = [Body("mountain_0001", 10.0m, Lens(200))],
        };

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.Contains(
            MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics),
            static cell => cell.ElevationMeters == 10.0m);
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
        var scene = Bodies(Body("mountain_0001", 10.0m, Lens(96)));

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.Equal(2, Assert.Single(scene.MountainBodies).Points.Count);
    }

    [Fact]
    public void AGridAlignedMountainStaysValid()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            MountainBodies =
            [
                Body("mountain_0001", 10.0m, Square(0, 0, 192, 192)),
                Body("mountain_0002", 12.0m, Square(64, 64, 128, 128)),
            ],
        };

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);

        Assert.Equal(36, MountainGeometry.EffectiveTerrainCells(scene, workspace.Metrics).Count);
    }

    private static IReadOnlyList<TerrainCellDocument> Fold(
        TestWorkspace workspace,
        params MountainBodyDocument[] bodies) =>
        MountainGeometry.EffectiveTerrainCells(
            TestScenes.Instance(workspace) with { MountainBodies = [.. bodies] },
            workspace.Metrics);

    private static SceneDocument Bodies(params MountainBodyDocument[] bodies) =>
        TestScenes.EmptyInstance() with { MountainBodies = [.. bodies] };

    private static MountainBodyDocument Body(
        string mountainBodyId,
        decimal elevationMeters,
        IReadOnlyList<MountainCurvePointDocument> points) => new()
    {
        MountainBodyId = mountainBodyId,
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
