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

    [Fact]
    public void TerrainToolsAndPropToolsOverlapOnlyWhereIntended()
    {
        Assert.True(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.Fill));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.Fill));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.Selector));
        Assert.True(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.Selector));
        Assert.True(EditorToolRegistry.Supports(EditorMode.Templates, EditorTool.AnchorMove));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Templates, EditorTool.Pencil));
        // Water is Terrain: a River is authored where Terrain is authored.
        Assert.True(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.DrawRiver));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.DrawRiver));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Templates, EditorTool.DrawRiver));
        Assert.True(EditorToolRegistry.Supports(EditorMode.Terrain, EditorTool.DrawMountain));
        Assert.False(EditorToolRegistry.Supports(EditorMode.Props, EditorTool.DrawMountain));
    }

    [Fact]
    public void TerrainToolsAreOfferedByHowTheChosenAssetIsAuthored()
    {
        // A painted Asset has no Bezier, a drawn one has no Pencil and no Fill.
        Assert.True(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.Pencil, TerrainAuthoring.Cells));
        Assert.False(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.DrawRiver, TerrainAuthoring.Cells));
        Assert.True(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.DrawMountain, TerrainAuthoring.Cells));
        Assert.False(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.DrawMountain, TerrainAuthoring.Curve));
        Assert.True(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.DrawRiver, TerrainAuthoring.Curve));
        Assert.False(EditorToolRegistry.Offers(
            EditorMode.Terrain, EditorTool.Fill, TerrainAuthoring.Curve));

        // Outside Terrain the authoring says nothing at all.
        Assert.True(EditorToolRegistry.Offers(
            EditorMode.Props, EditorTool.Pencil, TerrainAuthoring.Curve));
        // And while no Asset is chosen, nothing is narrowed away.
        Assert.True(EditorToolRegistry.Offers(EditorMode.Terrain, EditorTool.Pencil, null));
        Assert.True(EditorToolRegistry.Offers(EditorMode.Terrain, EditorTool.DrawRiver, null));
    }

    [Fact]
    public void EveryAuthoringHasAToolToFallBackTo()
    {
        Assert.Equal(
            EditorTool.Pencil,
            EditorToolRegistry.DefaultTool(EditorMode.Terrain, TerrainAuthoring.Cells));
        Assert.Equal(
            EditorTool.DrawRiver,
            EditorToolRegistry.DefaultTool(EditorMode.Terrain, TerrainAuthoring.Curve));
        Assert.Equal(
            EditorToolRegistry.DefaultTool(EditorMode.Props),
            EditorToolRegistry.DefaultTool(EditorMode.Props, TerrainAuthoring.Curve));

        foreach (var mode in Enum.GetValues<EditorMode>())
        {
            foreach (var authoring in Enum.GetValues<TerrainAuthoring>())
            {
                var tool = EditorToolRegistry.DefaultTool(mode, authoring);
                Assert.True(
                    EditorToolRegistry.Offers(mode, tool, authoring),
                    $"Mode {mode} with {authoring} falls back to {tool}, which it does not offer.");
            }
        }
    }
}
