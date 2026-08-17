using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class MapEditingTests
{
    [Fact]
    public void NorthAndEastExtensionPreserveAuthoredCoordinates()
    {
        var scene = SceneDocument.Create("world", 4, 3) with
        {
            TerrainCells =
            [
                new TerrainCellDocument { X = 3, Y = 2, AssetId = 1003 },
            ],
            Placements =
            [
                new PlacementDocument
                {
                    InstanceId = "tree_0001",
                    AssetId = 2,
                    PositionAuthoringPx = new AuthoringPixelPosition { X = 16, Y = 16 },
                },
            ],
        };

        var expanded = MapEditing.ExtendEast(MapEditing.ExtendNorth(scene, 5), 7);

        Assert.Equal((11, 8), (expanded.SizeCells.Width, expanded.SizeCells.Height));
        Assert.Equal(scene.TerrainCells, expanded.TerrainCells);
        Assert.Equal(scene.Placements, expanded.Placements);
        Assert.Equal(176, AuthoringMetrics.SceneWidthAuthoringPixels(expanded));
        Assert.Equal(128, AuthoringMetrics.SceneHeightAuthoringPixels(expanded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExtensionRequiresPositiveCellCount(int cells)
    {
        var scene = SceneDocument.Create("world", 4, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendNorth(scene, cells));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapEditing.ExtendEast(scene, cells));
    }
}
