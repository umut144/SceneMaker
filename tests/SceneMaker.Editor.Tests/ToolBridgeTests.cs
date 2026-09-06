using SceneMaker.Core;
using SceneMaker.Editor;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Editor.Tests;

/// <summary>
/// A bridge is two ends and nothing else, so the tool has no draft to grow and
/// no Enter to press. These pin that shape, and that the preview answers with
/// the same call the commit makes.
/// </summary>
public sealed class ToolBridgeTests
{
    [Fact]
    public void TheFirstClickFixesAnEndAndTheSecondBuildsTheBridge()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);

        var first = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(320, 320), Cell(10, 10)));
        Assert.Contains("start set", first.Text, StringComparison.Ordinal);
        Assert.True(interaction.HasUnfinishedDraft);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(640, 320), Cell(20, 10)));
        var bridge = Assert.Single(edit.Apply(scene).Bridges);

        Assert.Equal("bridge_0001", bridge.BridgeId);
        Assert.Equal("portal", bridge.PlankAssetKey);
        Assert.Equal("stone", bridge.AnchorAssetKey);
        Assert.Equal(320, bridge.StartAuthoringPx.X);
        Assert.Equal(640, bridge.EndAuthoringPx.X);
        Assert.Equal(BridgeEditing.DefaultWidthMeters, bridge.WidthMeters);
        Assert.Equal(BridgeEditing.DefaultPlankCount, bridge.PlankCount);
        Assert.Equal(BridgeEditing.DefaultPlankGapMeters, bridge.PlankGapMeters);

        // Nothing is left holding a half-authored bridge: the second click is
        // the whole of it.
        Assert.False(interaction.HasUnfinishedDraft);
        Assert.Null(interaction.BridgeStart);
    }

    [Fact]
    public void EscapeGivesTheFixedEndBack()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);
        interaction.PointerPressed(Context(workspace, scene), Point(320, 320), Cell(10, 10));

        var outcome = Assert.IsType<ToolOutcome.Message>(
            interaction.KeyPressed(Context(workspace, scene), ToolKey.Escape));

        Assert.Contains("discarded", outcome.Text, StringComparison.Ordinal);
        Assert.Null(interaction.BridgeStart);
    }

    /// <summary>There is nothing for Enter to finish; the second click already did.</summary>
    [Fact]
    public void EnterDoesNothing()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);
        interaction.PointerPressed(Context(workspace, scene), Point(320, 320), Cell(10, 10));

        Assert.IsType<ToolOutcome.Idle>(
            interaction.KeyPressed(Context(workspace, scene), ToolKey.Enter));
        Assert.NotNull(interaction.BridgeStart);
    }

    [Fact]
    public void TheEraserTakesTheWholeBridge()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WithBridge(workspace);
        var interaction = Bridge(workspace);
        interaction.SetEraserEnabled(true);

        var edit = Assert.IsType<ToolOutcome.Edit>(
            interaction.PointerPressed(Context(workspace, scene), Point(480, 320), Cell(15, 10)));

        Assert.Empty(edit.Apply(scene).Bridges);
    }

    [Fact]
    public void TheEraserSaysSoWhereThereIsNoBridge()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);
        interaction.SetEraserEnabled(true);

        var outcome = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(480, 320), Cell(15, 10)));

        Assert.Contains("no bridge here", outcome.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Ready draft is a promise: the same call the commit makes has already
    /// succeeded. A Blocked one carries the reason instead of leaving the
    /// refusal to be discovered by clicking.
    /// </summary>
    [Fact]
    public void ThePreviewAnswersWithTheSameCallTheCommitMakes()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);

        var ready = Preview(workspace, scene, new AuthoringPoint(320, 320), new AuthoringPoint(640, 320));

        Assert.Equal(BridgeDraftKind.Ready, ready.Kind);
        Assert.Null(ready.Explanation);
        Assert.Equal(10m, ready.LengthMeters);
        Assert.Equal(4, ready.Corners.Count);
        Assert.Equal(4, ready.Posts.Count);

        // The planks are in the draft too, because the count is a number an
        // author turns while looking at it.
        Assert.Equal(BridgeEditing.DefaultPlankCount, ready.Planks.Count);
        Assert.Equal(0.741667m, ready.Planks[0].DepthMeters);
    }

    [Fact]
    public void APreviewOverAnOccupiedCornerIsBlockedWithItsReason()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var corner = BridgeGeometry.Corners(
            workspace.Metrics,
            new BridgeDocument
            {
                BridgeId = "bridge_preview",
                PlankAssetKey = "portal",
                AnchorAssetKey = "stone",
                StartAuthoringPx = new AuthoringPixelPosition { X = 320, Y = 320 },
                EndAuthoringPx = new AuthoringPixelPosition { X = 640, Y = 320 },
                WidthMeters = BridgeEditing.DefaultWidthMeters,
                ElevationMeters = 1m,
                PlankCount = BridgeEditing.DefaultPlankCount,
                PlankGapMeters = BridgeEditing.DefaultPlankGapMeters,
            })[0];
        var occupied = PropEditing.Place(
            scene,
            workspace.Props,
            (int)(corner.XMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            (int)(corner.YMeters * workspace.Metrics.AuthoringPixelsPerMeter),
            "stone");

        var blocked = Preview(
            workspace, occupied, new AuthoringPoint(320, 320), new AuthoringPoint(640, 320));

        Assert.Equal(BridgeDraftKind.Blocked, blocked.Kind);
        Assert.Contains("collides", blocked.Explanation!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two ends in one place is a bridge nobody has finished asking for, not
    /// one that was refused, so it is drawn as a draft rather than in red.
    /// </summary>
    [Fact]
    public void AnEndOnTopOfTheStartStaysIncomplete()
    {
        using var workspace = TestWorkspace.Create();

        var preview = Preview(
            workspace,
            Map(workspace),
            new AuthoringPoint(320, 320),
            new AuthoringPoint(320, 320));

        Assert.Equal(BridgeDraftKind.Incomplete, preview.Kind);
        Assert.Empty(preview.Posts);
    }

    [Fact]
    public void WithoutAPlankAssetTheToolSaysSoRatherThanInventingOne()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);
        interaction.State.SelectBridgePlankAsset(null);

        var outcome = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(320, 320), Cell(10, 10)));

        Assert.Contains(
            "no Placement Asset to plank a deck with",
            outcome.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnAnchorAssetTheToolSaysSoRatherThanInventingAPost()
    {
        using var workspace = TestWorkspace.Create();
        var scene = Map(workspace);
        var interaction = Bridge(workspace);
        interaction.State.SelectBridgeAnchorAsset(null);

        var outcome = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, scene), Point(320, 320), Cell(10, 10)));

        Assert.Contains("no Placement Asset offering a post", outcome.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASceneTemplateCannotCarryABridge()
    {
        using var workspace = TestWorkspace.Create();
        var template = SceneDocument.CreateTemplate("grove", 30, 30, 1, 0, 0);
        var interaction = Bridge(workspace);

        var outcome = Assert.IsType<ToolOutcome.Message>(
            interaction.PointerPressed(Context(workspace, template), Point(320, 320), Cell(10, 10)));

        Assert.Contains("Scene Template cannot carry", outcome.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A post is an Asset of its own, so every enabled Placement can stand at
    /// a corner. A composition that could not stand anywhere - a Set, a
    /// Palette - never reaches the catalog, so the field needs no rule of its
    /// own beyond remembering what was chosen.
    /// </summary>
    [Fact]
    public void EveryEnabledPlacementIsOfferedAsAnAnchor()
    {
        using var workspace = TestWorkspace.Create();

        var offered = BridgeAnchorAssets.Offered(workspace.Props)
            .Select(static asset => asset.AssetKey)
            .ToList();

        Assert.Equal(["portal", "stone"], offered);
        Assert.Equal("portal", BridgeAnchorAssets.Choose(workspace.Props, null));
        Assert.Equal("stone", BridgeAnchorAssets.Choose(workspace.Props, "stone"));
    }

    /// <summary>
    /// The same for the deck. What makes something a plank is that a bridge
    /// repeats it, not a property the Asset carries, so every enabled
    /// Placement is offered.
    /// </summary>
    [Fact]
    public void EveryEnabledPlacementIsOfferedAsAPlank()
    {
        using var workspace = TestWorkspace.Create();

        var offered = BridgePlankAssets.Offered(workspace.Props)
            .Select(static asset => asset.AssetKey)
            .ToList();

        Assert.Equal(["portal", "stone"], offered);
        Assert.Equal("portal", BridgePlankAssets.Choose(workspace.Props, null));
        Assert.Equal("stone", BridgePlankAssets.Choose(workspace.Props, "stone"));
    }

    private static BridgeDraftPreview Preview(
        TestWorkspace workspace,
        SceneDocument scene,
        AuthoringPoint start,
        AuthoringPoint pointer) => ToolPreviewBuilder.BuildBridgeDraft(
            scene,
            workspace.Props,
            EditorTool.DrawBridge,
            start,
            pointer,
            "portal",
            "stone",
            BridgeEditing.DefaultWidthMeters,
            1m,
            BridgeEditing.DefaultPlankCount,
            BridgeEditing.DefaultPlankGapMeters);

    private static SceneDocument Map(TestWorkspace workspace) =>
        TestScenes.Instance(workspace, sizeCells: 30);

    private static SceneDocument WithBridge(TestWorkspace workspace) => BridgeEditing.Place(
        Map(workspace),
        workspace.Props,
        320,
        320,
        640,
        320,
        "portal",
        "stone",
        BridgeEditing.DefaultWidthMeters,
        1m,
        BridgeEditing.DefaultPlankCount,
        BridgeEditing.DefaultPlankGapMeters);

    private static ToolInteraction Bridge(TestWorkspace workspace)
    {
        var interaction = new ToolInteraction();
        interaction.SelectMode(EditorMode.Bridge);
        interaction.SelectTool(EditorTool.DrawBridge);
        interaction.State.SelectBridgePlankAsset("portal");
        interaction.State.SelectBridgeAnchorAsset("stone");
        interaction.State.SetBridgeElevation(1m);
        return interaction;
    }

    private static ToolContext Context(TestWorkspace workspace, SceneDocument scene) => new(
        scene,
        workspace.Terrain,
        workspace.Props,
        workspace.Metrics,
        SelectedTerrainAssetKey: "grass",
        SelectedPropAssetKey: "stone",
        TemplateAnchorGroupNumber: 1,
        ElevationMeters: 1m);

    private static AuthoringPoint Point(int x, int y) => new(x, y);

    private static TerrainCellCoordinate Cell(int x, int y) => new(x, y);
}
