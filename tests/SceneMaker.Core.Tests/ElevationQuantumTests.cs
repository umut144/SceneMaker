using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// The Workspace quantizes heights an author chooses directly. Values derived
/// between those anchors stay continuous; the quantum is not a voxel grid.
/// </summary>
public sealed class ElevationQuantumTests
{
    [Fact]
    public void EveryKindOfAuthoredAbsoluteHeightCanShareTheWorkspaceQuantum()
    {
        using var workspace = TestWorkspace.Create();
        var scene = TestScenes.Instance(workspace) with { DefaultElevationMeters = 1.125m };
        scene = scene with
        {
            TerrainCells = scene.TerrainCells.Select((cell, index) =>
                index == 0 ? cell with { ElevationMeters = 1.25m } : cell).ToList(),
        };
        scene = PropEditing.Place(scene, workspace.Props, 32, 32, "stone", 1.375m);
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(
                    32, 96, WaterPointMode.Linear,
                    elevationMeters: 1.5m,
                    channelDepthMeters: 0.3m,
                    clearanceAboveMeters: 0.2m,
                    widthMeters: 1.0m),
                WaterEditing.Point(
                    160, 96, WaterPointMode.Linear,
                    elevationMeters: 1.625m,
                    channelDepthMeters: 0.3m,
                    clearanceAboveMeters: 0.2m,
                    widthMeters: 1.0m),
            ],
            "river");

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
    }

    [Theory]
    [InlineData("scene", "scene")]
    [InlineData("terrain", "terrain")]
    [InlineData("prop", "placement")]
    [InlineData("water", "water")]
    public void AnOffQuantumAuthoredAbsoluteHeightNamesItsOwner(
        string owner,
        string expectedLabel)
    {
        using var workspace = TestWorkspace.Create();
        var scene = SceneWithOffQuantumHeight(workspace, owner);

        var exception = Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.ValidateGrid(scene, workspace.Metrics));

        Assert.Contains(expectedLabel, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.125 m", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1.1 m", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpolatedWaterCellsDoNotHaveToLieOnTheAuthoringQuantum()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(0, 96, WaterPointMode.Linear, 4.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 96, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

        DocumentValidation.ValidateGrid(scene, workspace.Metrics);
        var cells = WaterGeometry.Corridor(scene, workspace.Metrics, scene.WaterBodies[0]);

        Assert.Contains(cells, cell => cell.SurfaceMeters == 3.9m);
        Assert.False(workspace.Metrics.IsElevationAligned(3.9m));
    }

    private static SceneDocument SceneWithOffQuantumHeight(
        TestWorkspace workspace,
        string owner)
    {
        var scene = TestScenes.Instance(workspace);
        return owner switch
        {
            "scene" => scene with { DefaultElevationMeters = 1.1m },
            "terrain" => scene with
            {
                TerrainCells = scene.TerrainCells.Select((cell, index) =>
                    index == 0 ? cell with { ElevationMeters = 1.1m } : cell).ToList(),
            },
            "prop" => PropEditing.Place(scene, workspace.Props, 32, 32, "stone", 1.1m),
            "water" => WaterEditing.PlaceRiver(
                scene,
                workspace.Terrain,
                [
                    WaterEditing.Point(32, 96, WaterPointMode.Linear, 1.1m),
                    WaterEditing.Point(160, 96, WaterPointMode.Linear, 1.0m),
                ],
                "river"),
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };
    }
}
