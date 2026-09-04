using System.Text.Json;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class RouteSurfaceDocumentTests
{
    [Fact]
    public void AuthoredRouteSourceSurvivesAJsonRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithRoute(TestScenes.Instance(workspace));

        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(scene));

        Assert.Equal(16, restored.Version);
        var route = Assert.Single(restored.RouteSurfaces);
        Assert.Equal("route_0001", route.RouteSurfaceId);
        Assert.Equal("grass", route.AssetKey);
        Assert.Equal(scene.RouteSurfaces[0].Points, route.Points);
        Assert.Equal(scene.RouteSurfaces[0].Segments, route.Segments);
        Assert.Equal(1.0625, RouteSurfaceGeometry.Prepare(workspace.Metrics, route).ElevationAt(16));
    }

    [Fact]
    public void RouteBodiesHaveExactlyTheAuthoredFields()
    {
        using var workspace = TestWorkspace.Create();
        using var parsed = JsonDocument.Parse(
            DocumentJson.Serialize(WithRoute(TestScenes.Instance(workspace))));

        var route = parsed.RootElement.GetProperty("route_surfaces")[0];
        Assert.Equal(
            ["route_surface_id", "asset_key", "points", "segments"],
            route.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(
            [
                "position_authoring_px", "mode", "handle_in_authoring_px",
                "handle_out_authoring_px", "elevation_meters", "width_meters",
            ],
            route.GetProperty("points")[0]
                .EnumerateObject()
                .Select(static property => property.Name));
        Assert.Equal(
            ["segment_id", "grade_percent", "operation", "clearance_above_meters"],
            route.GetProperty("segments")[0]
                .EnumerateObject()
                .Select(static property => property.Name));
    }

    [Fact]
    public void RouteIdsStayUniqueAndCanonicallyOrdered()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with
        {
            RouteSurfaces = [Route("route_0002"), Route("route_0001")],
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));

        Assert.Contains("canonical ordinal order", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPointPairNeedsOneStableAllowedGradeSegment()
    {
        using var workspace = TestWorkspace.Create();
        var missing = WithRoute(TestScenes.Instance(workspace));
        missing.RouteSurfaces[0].Segments.Clear();
        var countError = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(missing));
        Assert.Contains("one segment", countError.Message, StringComparison.Ordinal);

        var unsupported = WithRoute(TestScenes.Instance(workspace));
        unsupported.RouteSurfaces[0].Segments[0] = unsupported.RouteSurfaces[0].Segments[0] with
        {
            GradePercent = 49,
        };
        var gradeError = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(unsupported));
        Assert.Contains("grade_percent 49", gradeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentOperationAndClearanceAreAClosedAuthoredPair()
    {
        using var workspace = TestWorkspace.Create();
        var additiveWithClearance = WithRoute(TestScenes.Instance(workspace));
        additiveWithClearance.RouteSurfaces[0].Segments[0] =
            additiveWithClearance.RouteSurfaces[0].Segments[0] with
            {
                ClearanceAboveMeters = 1m,
            };
        var additiveError = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(additiveWithClearance));
        Assert.Contains("additive", additiveError.Message, StringComparison.Ordinal);

        var subtractiveWithoutClearance = WithRoute(TestScenes.Instance(workspace));
        subtractiveWithoutClearance.RouteSurfaces[0].Segments[0] =
            subtractiveWithoutClearance.RouteSurfaces[0].Segments[0] with
            {
                Operation = RouteSegmentOperation.Subtractive,
            };
        var subtractiveError = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(subtractiveWithoutClearance));
        Assert.Contains("positive clearance", subtractiveError.Message, StringComparison.Ordinal);

        var subtractive = WithRoute(TestScenes.Instance(workspace));
        subtractive.RouteSurfaces[0].Segments[0] = subtractive.RouteSurfaces[0].Segments[0] with
        {
            Operation = RouteSegmentOperation.Subtractive,
            ClearanceAboveMeters = 1.5m,
        };
        var restored = DocumentJson.DeserializeScene(DocumentJson.Serialize(subtractive));
        var segment = Assert.Single(Assert.Single(restored.RouteSurfaces).Segments);
        Assert.Equal(RouteSegmentOperation.Subtractive, segment.Operation);
        Assert.Equal(1.5m, segment.ClearanceAboveMeters);
    }

    [Fact]
    public void SegmentIdsAreUniqueAcrossAllPaths()
    {
        using var workspace = TestWorkspace.Create();
        var first = Route("route_0001");
        var second = Route("route_0002") with
        {
            Segments = [first.Segments[0]],
        };
        var scene = TestScenes.Instance(workspace) with
        {
            RouteSurfaces = [first, second],
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));
        Assert.Contains("duplicated across", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRoutePointNeedsPositiveWidth()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithRoute(
            TestScenes.Instance(workspace),
            points: [Point(32, 32, 1m, 0m), Point(96, 32, 1.125m)]);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));

        Assert.Contains("positive width_meters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinearRoutePointCannotCarryHandles()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithRoute(
            TestScenes.Instance(workspace),
            points:
            [
                Point(
                    32,
                    32,
                    1m,
                    handleOut: new AuthoringPixelOffset { X = 32, Y = 0 }),
                Point(96, 32, 1.125m),
            ]);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));

        Assert.Contains("linear point carrying handles", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrulyCollapsedAuthoredIntervalIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithRoute(
            TestScenes.Instance(workspace),
            points: [Point(32, 32, 1m), Point(32, 32, 1.125m)]);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));

        Assert.Contains("no horizontal run", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EqualEndpointPositionsRemainValidWhenHandlesCreateARun()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithRoute(
            TestScenes.Instance(workspace),
            points:
            [
                Point(
                    64,
                    64,
                    1m,
                    mode: RoutePointMode.Aligned,
                    handleIn: new AuthoringPixelOffset { X = -64, Y = 0 },
                    handleOut: new AuthoringPixelOffset { X = 64, Y = 0 }),
                Point(
                    64,
                    64,
                    1.125m,
                    mode: RoutePointMode.Aligned,
                    handleIn: new AuthoringPixelOffset { X = 64, Y = 0 },
                    handleOut: new AuthoringPixelOffset { X = -64, Y = 0 }),
            ]);

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
        Assert.True(
            RouteSurfaceGeometry.Prepare(workspace.Metrics, scene.RouteSurfaces[0])
                .TotalLengthMeters > 0.0);
    }

    [Fact]
    public void RouteAnchorsAreBoundedButNotTiedToAHorizontalGrid()
    {
        using var workspace = TestWorkspace.Create();
        var free = WithRoute(
            TestScenes.Instance(workspace),
            points: [Point(33, 47, 1m), Point(159, 101, 1.125m)]);

        DocumentValidation.ValidateGrid(free, workspace.Metrics);

        var outside = WithRoute(
            TestScenes.Instance(workspace),
            points: [Point(33, 47, 1m), Point(193, 101, 1.125m)]);
        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(outside, workspace.Metrics));
        Assert.Contains("outside Scene bounds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyTerrainRoleAssetCanMaterializeARoute()
    {
        using var workspace = TestWorkspace.Create();

        RouteSurfaceEditing.ValidateAssetReferences(
            WithRoute(TestScenes.Instance(workspace), assetKey: "grass"),
            workspace.Terrain);
        RouteSurfaceEditing.ValidateAssetReferences(
            WithRoute(TestScenes.Instance(workspace), assetKey: "river"),
            workspace.Terrain);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            RouteSurfaceEditing.ValidateAssetReferences(
                WithRoute(TestScenes.Instance(workspace), assetKey: "stone"),
                workspace.Terrain));
        Assert.Contains("stone", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesMayPersistRoutesForAConsumerToAcceptOrReject()
    {
        using var workspace = TestWorkspace.Create();
        var template = WithRoute(TestScenes.Template(workspace, "route_template", 1));

        DocumentValidation.Validate(template);
        Assert.Single(template.RouteSurfaces);
        var session = WorkspaceSession.Load(workspace.RootPath);
        var written = SceneExport.Write(
            session,
            new LoadedScene(
                Path.Combine(session.Workspace.TemplatesDirectoryPath, "route_template.scene.json"),
                template));
        using var parsed = JsonDocument.Parse(File.ReadAllText(written.Path));
        Assert.Single(parsed.RootElement.GetProperty("scene")
            .GetProperty("route_surfaces").EnumerateArray());
        Assert.Single(parsed.RootElement.GetProperty("route_surface_bakes").EnumerateArray());
    }

    [Fact]
    public void ExportTenCarriesAuthoredRoutesAndTheirBakeWithoutAWarning()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        var loaded = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            WithRoute(TestScenes.Instance(workspace)));

        var result = SceneExport.Write(session, loaded);

        Assert.Empty(result.Warnings);
        using var parsed = JsonDocument.Parse(File.ReadAllText(result.Path));
        Assert.Single(parsed.RootElement.GetProperty("scene")
            .GetProperty("route_surfaces").EnumerateArray());
        Assert.Single(parsed.RootElement.GetProperty("route_surface_bakes").EnumerateArray());
    }

    [Fact]
    public void ExportTenRefusesASubtractiveSegmentInsteadOfSilentlyLosingIt()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        var document = WithRoute(TestScenes.Instance(workspace));
        document.RouteSurfaces[0].Segments[0] = document.RouteSurfaces[0].Segments[0] with
        {
            Operation = RouteSegmentOperation.Subtractive,
            ClearanceAboveMeters = 1m,
        };

        var exception = Assert.Throws<SceneMakerDocumentException>(() => SceneExport.Write(
            session,
            new LoadedScene(
                Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
                document)));

        Assert.Contains("export schema 10", exception.Message, StringComparison.Ordinal);
    }

    private static SceneDocument WithRoute(
        SceneDocument scene,
        string assetKey = "grass",
        IReadOnlyList<RouteSurfacePointDocument>? points = null) =>
        scene with
        {
            RouteSurfaces =
            [
                Route(
                    "route_0001",
                    assetKey,
                    points ?? [Point(0, 32, 1m), Point(32, 32, 1.125m)]),
            ],
        };

    private static RouteSurfaceDocument Route(
        string routeSurfaceId,
        string assetKey = "grass",
        IReadOnlyList<RouteSurfacePointDocument>? points = null) => new()
    {
        RouteSurfaceId = routeSurfaceId,
        AssetKey = assetKey,
        Points = [.. points ?? [Point(0, 32, 1m), Point(32, 32, 1.125m)]],
        Segments =
        [
            new RouteSurfaceSegmentDocument
            {
                SegmentId = $"{routeSurfaceId}.segment_0001",
                GradePercent = 25,
                Operation = RouteSegmentOperation.Additive,
            },
        ],
    };

    private static RouteSurfacePointDocument Point(
        int x,
        int y,
        decimal elevationMeters,
        decimal widthMeters = 1m,
        RoutePointMode mode = RoutePointMode.Linear,
        AuthoringPixelOffset? handleIn = null,
        AuthoringPixelOffset? handleOut = null) => new()
    {
        PositionAuthoringPx = new AuthoringPixelPosition { X = x, Y = y },
        Mode = mode,
        HandleInAuthoringPx = handleIn ?? AuthoringPixelOffset.Zero,
        HandleOutAuthoringPx = handleOut ?? AuthoringPixelOffset.Zero,
        ElevationMeters = elevationMeters,
        WidthMeters = widthMeters,
    };
}
