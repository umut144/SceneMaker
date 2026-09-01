using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A surface belongs to a Terrain Asset, not to a cell, so one Asset cannot
/// present land in one place and water in another. These tests pin the rules
/// that make that hold.
/// </summary>
public sealed class TerrainSurfaceTests
{
    [Fact]
    public void TerrainCarriesItsSurfaceAndPropsCarryNone()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Equal("land", workspace.Configuration.ResolveAssetProfile("grass").Surface);
        Assert.Equal("sand", workspace.Configuration.ResolveAssetProfile("sand").Surface);
        Assert.Null(workspace.Configuration.ResolveAssetProfile("stone").Surface);
        Assert.Null(workspace.Configuration.ResolveAssetProfile("portal").Surface);
    }

    [Fact]
    public void ATerrainAssetWithoutASurfaceIsRefused()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            workspace.Configuration.WithAssetProfiles(
                [new WorkspaceAssetProfile("grass", "#99E550")],
                workspace.Catalog));

        Assert.Contains("requires a surface", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APropWithASurfaceIsRefused()
    {
        using var workspace = TestWorkspace.Create();

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            workspace.Configuration.WithAssetProfiles(
                [new WorkspaceAssetProfile("stone", "#808080", "land")],
                workspace.Catalog));

        Assert.Contains("must not declare a surface", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("land")]
    [InlineData("water")]
    [InlineData("deep_water")]
    [InlineData("lava")]
    public void TheSurfaceSetIsOpen(string surface)
    {
        using var workspace = TestWorkspace.Create();

        var narrowed = workspace.Configuration.WithAssetProfiles(
            [new WorkspaceAssetProfile("grass", "#99E550", surface)],
            workspace.Catalog);

        Assert.Equal(surface, narrowed.ResolveAssetProfile("grass").Surface);
    }

    [Theory]
    [InlineData("Land")]
    [InlineData("deep water")]
    [InlineData("_land")]
    [InlineData("land_")]
    [InlineData("deep__water")]
    [InlineData("")]
    public void AMalformedSurfaceTokenIsRefused(string surface)
    {
        using var workspace = TestWorkspace.Create();

        Assert.Throws<SceneMakerDocumentException>(() =>
            workspace.Configuration.WithAssetProfiles(
                [new WorkspaceAssetProfile("grass", "#99E550", surface)],
                workspace.Catalog));
    }

    [Fact]
    public void TheSurfaceSurvivesAWriteAndReadRoundTrip()
    {
        using var workspace = TestWorkspace.Create();
        var narrowed = workspace.Configuration.WithAssetProfiles(
            [new WorkspaceAssetProfile("grass", "#99E550", "swamp")],
            workspace.Catalog);

        WorkspaceConfigurationStore.Save(workspace.RootPath, narrowed);
        var reloaded = WorkspaceConfigurationStore.Load(workspace.RootPath, workspace.Catalog);

        Assert.Equal("swamp", reloaded.ResolveAssetProfile("grass").Surface);
    }
}
