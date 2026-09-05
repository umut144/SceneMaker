using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A bridge is authored whole and derived from six numbers. These tests pin
/// what follows from that: that its posts are computed rather than kept, that
/// it is refused whole when one corner cannot stand, and that "occupied" means
/// the same thing whether a Placement or a bridge asks.
/// </summary>
public sealed class BridgeEditingTests
{
    [Fact]
    public void ABridgeSetsAPostAtEachCornerOfItsDeck()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Place(workspace, Map(), 320, 320, 640, 320);
        var bridge = Assert.Single(scene.Bridges);

        var corners = BridgeGeometry.Corners(workspace.Metrics, bridge);

        // Left is a quarter turn counter-clockwise from start towards end, so
        // a bridge running east has its left corners to the north.
        Assert.Equal(
            [
                BridgeCornerKind.StartLeft,
                BridgeCornerKind.StartRight,
                BridgeCornerKind.EndLeft,
                BridgeCornerKind.EndRight,
            ],
            corners.Select(static corner => corner.Kind));
        Assert.Equal(10m, corners[0].XMeters);
        Assert.Equal(12m, corners[0].YMeters);
        Assert.Equal(10m, corners[1].XMeters);
        Assert.Equal(8m, corners[1].YMeters);
        Assert.Equal(20m, corners[2].XMeters);
        Assert.Equal(12m, corners[2].YMeters);
        Assert.Equal(20m, corners[3].XMeters);
        Assert.Equal(8m, corners[3].YMeters);
    }

    /// <summary>
    /// The posts are not stored, so nothing has to be kept in step with the
    /// bridge: widening it moves them, and deleting it takes them with it.
    /// </summary>
    [Fact]
    public void WideningABridgeMovesItsPostsAndDeletingItTakesThemAlong()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Place(workspace, Map(), 320, 320, 640, 320);
        var bridge = Assert.Single(scene.Bridges);

        var narrow = BridgeGeometry.Corners(workspace.Metrics, bridge);
        var wide = BridgeGeometry.Corners(
            workspace.Metrics, bridge with { WidthMeters = 8m });

        Assert.Equal(12m, narrow[0].YMeters);
        Assert.Equal(14m, wide[0].YMeters);
        Assert.Empty(BridgeEditing.Remove(scene, bridge.BridgeId).Bridges);
    }

    /// <summary>
    /// A bridge with both ends in one place has no direction, and without a
    /// direction there is no left and no right for a post to stand on.
    /// </summary>
    [Fact]
    public void ABridgeNeedsTwoDifferentEnds()
    {
        using var workspace = TestWorkspace.Create();

        var validation = Validate(workspace, Map(), 320, 320, 320, 320);

        Assert.False(validation.IsValid);
        Assert.Contains("two different ends", validation.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ABridgeHeightFollowsTheWorkspaceElevationQuantum()
    {
        using var workspace = TestWorkspace.Create();

        var validation = BridgeEditing.ValidateCandidate(
            Map(),
            workspace.Terrain,
            workspace.Props,
            320,
            320,
            640,
            320,
            "grass",
            "stone",
            BridgeEditing.DefaultWidthMeters,
            1.1m);

        Assert.False(validation.IsValid);
        Assert.Contains("elevation quantum", validation.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bridge is one thing. If a corner cannot stand where it would stand,
    /// what the author asked for cannot be authored, so nothing is - rather
    /// than a bridge with three posts.
    /// </summary>
    [Fact]
    public void ABridgeIsRefusedWholeWhenOneCornerIsOccupied()
    {
        using var workspace = TestWorkspace.Create();
        var corner = BridgeGeometry.Corners(
            workspace.Metrics,
            new BridgeDocument
            {
                BridgeId = "bridge_0001",
                AssetKey = "grass",
                AnchorAssetKey = "stone",
                StartAuthoringPx = new AuthoringPixelPosition { X = 320, Y = 320 },
                EndAuthoringPx = new AuthoringPixelPosition { X = 640, Y = 320 },
                WidthMeters = BridgeEditing.DefaultWidthMeters,
                ElevationMeters = 1m,
            })[0];
        var occupied = PropEditing.Place(
            Map(),
            workspace.Props,
            (int)(corner.XMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            (int)(corner.YMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            "stone");

        var validation = Validate(workspace, occupied, 320, 320, 640, 320);

        Assert.False(validation.IsValid);
        Assert.Contains("collides", validation.Reason!, StringComparison.Ordinal);
        Assert.Throws<SceneMakerDocumentException>(() =>
            Place(workspace, occupied, 320, 320, 640, 320));
    }

    /// <summary>
    /// The same question from the other side. A post is not a Prop in the
    /// document, but it stands in the same map, so a Placement cannot be set
    /// into one either.
    /// </summary>
    [Fact]
    public void APlacementCannotBeSetIntoABridgePost()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Place(workspace, Map(), 320, 320, 640, 320);
        var corner = BridgeGeometry.Corners(workspace.Metrics, scene.Bridges[0])[0];

        var validation = PropEditing.ValidateCandidate(
            scene,
            workspace.Props,
            (int)(corner.XMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            (int)(corner.YMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            "stone");

        Assert.False(validation.IsValid);
        Assert.Contains("post of bridge", validation.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Placement offered whole has no part to stand at a corner: setting
    /// four of those would set four bridges rather than four posts.
    /// </summary>
    [Fact]
    public void AnAnchorAssetWithoutANamedPartCannotStandAtACorner()
    {
        using var workspace = TestWorkspace.Create();

        var validation = BridgeEditing.ValidateCandidate(
            Map(),
            workspace.Terrain,
            workspace.Props,
            320,
            320,
            640,
            320,
            "grass",
            "portal",
            BridgeEditing.DefaultWidthMeters,
            1m);

        Assert.False(validation.IsValid);
        Assert.Contains("names no anchor_component", validation.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deck is the same band a Path is, built by the same geometry rather
    /// than by a second idea of what a band looks like.
    /// </summary>
    [Fact]
    public void TheDeckIsAnOrdinaryTwoPointBandAtOneHeight()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Place(workspace, Map(), 320, 320, 640, 320);

        var deck = BridgeGeometry.Deck(workspace.Metrics, scene.Bridges[0]);

        Assert.Equal(10.0, deck.TotalLengthMeters, 6);
        Assert.Equal(0.0, deck.MaximumAbsoluteRisePerMeter, 6);
        Assert.Equal(2, deck.Points.Count);
    }

    /// <summary>
    /// Composition moves Terrain cells and Props and nothing else, so a
    /// Template holding a bridge would lose it at every Anchor. Refusing to
    /// author one says so out loud instead.
    /// </summary>
    [Fact]
    public void ASceneTemplateCannotCarryABridge()
    {
        using var workspace = TestWorkspace.Create();
        var template = SceneDocument.CreateTemplate("grove", 30, 30, 1, 0, 0);
        var scene = Place(workspace, template, 320, 320, 640, 320);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(scene));

        Assert.Contains("cannot own bridges", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportRefusesABridgeRatherThanDroppingIt()
    {
        using var workspace = TestWorkspace.Create();
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = Place(
            workspace, TestScenes.Instance(workspace, sizeCells: 30), 320, 320, 640, 320);

        var exception = Assert.Throws<SceneMakerDocumentException>(() => SceneExport.Write(
            session,
            new LoadedScene(
                Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
                scene)));

        Assert.Contains("no representation for a bridge", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A Scene wide enough to hold a ten-metre span with room around it.</summary>
    private static SceneDocument Map() => TestScenes.EmptyInstance(sizeCells: 30);

    private static SceneDocument Place(
        TestWorkspace workspace,
        SceneDocument scene,
        int startX,
        int startY,
        int endX,
        int endY) => BridgeEditing.Place(
            scene,
            workspace.Terrain,
            workspace.Props,
            startX,
            startY,
            endX,
            endY,
            "grass",
            "stone",
            BridgeEditing.DefaultWidthMeters,
            1m);

    private static BridgeValidationResult Validate(
        TestWorkspace workspace,
        SceneDocument scene,
        int startX,
        int startY,
        int endX,
        int endY) => BridgeEditing.ValidateCandidate(
            scene,
            workspace.Terrain,
            workspace.Props,
            startX,
            startY,
            endX,
            endY,
            "grass",
            "stone",
            BridgeEditing.DefaultWidthMeters,
            1m);
}
