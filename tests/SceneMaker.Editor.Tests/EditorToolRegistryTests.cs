using SceneMaker.Core;
using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class EditorToolRegistryTests
{
    [Fact]
    public void EveryModeHasADefaultToolThatItSupports()
    {
        foreach (var mode in Enum.GetValues<EditorMode>())
        {
            var tool = EditorToolRegistry.DefaultTool(mode);
            Assert.True(
                EditorToolRegistry.Supports(mode, tool),
                $"Mode {mode} defaults to {tool}, which it does not support.");
        }
    }

    [Fact]
    public void EveryModeHasADisplayName()
    {
        foreach (var mode in Enum.GetValues<EditorMode>())
            Assert.False(string.IsNullOrWhiteSpace(EditorToolRegistry.ModeDisplayName(mode)));
    }

    [Fact]
    public void TheInternalPropsModeIsPresentedToAuthorsAsPlacements()
    {
        Assert.Equal("Placement", EditorToolRegistry.ModeDisplayName(EditorMode.Props));
    }

    [Fact]
    public void EveryToolResolvesToItsOwnDefinition()
    {
        foreach (var tool in Enum.GetValues<EditorTool>())
        {
            var definition = EditorToolRegistry.Resolve(tool);
            Assert.Equal(tool, definition.Tool);
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.NotEmpty(definition.SupportedModes);
        }
    }

    [Fact]
    public void EveryToolIsReachableFromAtLeastOneMode()
    {
        var reachable = Enum.GetValues<EditorMode>()
            .SelectMany(mode => Enum.GetValues<EditorTool>()
                .Where(tool => EditorToolRegistry.Supports(mode, tool)))
            .ToHashSet();

        Assert.Equal(
            Enum.GetValues<EditorTool>().Order().ToArray(),
            reachable.Order().ToArray());
    }

    [Fact]
    public void ToolBarShowsOnlyToolsMarkedForItAndEachHasAnIcon()
    {
        Assert.All(EditorToolRegistry.ToolBarDefinitions, definition =>
        {
            Assert.True(definition.ShowInToolBar);
            Assert.False(string.IsNullOrWhiteSpace(definition.IconFileName));
        });
        Assert.DoesNotContain(
            EditorToolRegistry.ToolBarDefinitions,
            definition => definition.Tool == EditorTool.AnchorPlace);
    }

    /// <summary>
    /// Each area owns the tools that draw its own thing. A River is no longer a
    /// tool inside Terrain that a chosen Asset switches to; it is an area, and
    /// so is Mountain.
    /// </summary>
    [Fact]
    public void EachAreaOwnsTheToolsThatDrawItsOwnThing()
    {
        Assert.True(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.Fill));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.Fill));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.Selector));
        Assert.True(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.Selector));
        Assert.True(EditorToolRegistry.Supports(EditorMode.Templates, EditorTool.AnchorMove));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Templates, EditorTool.Pencil));

        Assert.True(EditorToolRegistry.Supports(EditorMode.River, EditorTool.DrawRiver));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.DrawRiver));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Mountain, EditorTool.DrawRiver));

        Assert.True(EditorToolRegistry.Supports(EditorMode.Mountain, EditorTool.DrawMountain));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.DrawMountain));
        Assert.False(EditorToolRegistry.Supports(EditorMode.River, EditorTool.DrawMountain));

        // The cell tools stay where cells are painted.
        Assert.False(EditorToolRegistry.Supports(EditorMode.Mountain, EditorTool.Pencil));
        Assert.False(EditorToolRegistry.Supports(EditorMode.River, EditorTool.Pencil));
    }

    /// <summary>
    /// The direction the whole slice turned around: the area says how its Assets
    /// are authored, instead of the Asset saying which tool the author holds.
    /// </summary>
    [Fact]
    public void AnAreaSaysHowItsTerrainAssetsAreAuthored()
    {
        Assert.Equal(
            TerrainAuthoring.Cells, EditorToolRegistry.TerrainAuthoringFor(EditorMode.Terrain));
        // A mountain is a shape and a height. The painted Terrain under the
        // contour is the material, so this area has nothing to offer.
        Assert.Null(EditorToolRegistry.TerrainAuthoringFor(EditorMode.Mountain));
        Assert.Equal(
            TerrainAuthoring.Curve, EditorToolRegistry.TerrainAuthoringFor(EditorMode.River));
        Assert.Null(EditorToolRegistry.TerrainAuthoringFor(EditorMode.Props));
        Assert.Null(EditorToolRegistry.TerrainAuthoringFor(EditorMode.Templates));
    }

    [Fact]
    public void EveryAreaThatAuthorsTerrainHasAToolForIt()
    {
        foreach (var mode in Enum.GetValues<EditorMode>())
        {
            if (EditorToolRegistry.TerrainAuthoringFor(mode) is null) continue;
            Assert.Contains(
                EditorToolRegistry.ToolBarDefinitions,
                definition => EditorToolRegistry.Supports(mode, definition.Tool));
        }
    }

    /// <summary>
    /// Offering no Asset is not the same as having nothing to draw with. The
    /// Mountain area authors geometry, so its tool is a real tool - the guard
    /// that disables an area without a usable Asset must not catch it.
    /// </summary>
    [Fact]
    public void MountainOffersNoAssetAndStillHasItsDrawingTool()
    {
        Assert.Null(EditorToolRegistry.TerrainAuthoringFor(EditorMode.Mountain));
        Assert.Equal(EditorTool.DrawMountain, EditorToolRegistry.DefaultTool(EditorMode.Mountain));
        Assert.Contains(
            EditorToolRegistry.ToolBarDefinitions,
            static definition => definition.Tool == EditorTool.DrawMountain);
    }
}
