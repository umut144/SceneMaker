using SceneMaker.Core;
using SceneMaker.Editor;
using Xunit;

namespace SceneMaker.Editor.Tests;

public sealed class VoxelCrossSectionViewTests
{
    [Fact]
    public void ViewCutsAtCurrentPointerElevationInsideVolume()
    {
        var grid = SolidBox();
        var view = new VoxelCrossSectionView();

        var slice = view.Follow(grid, new VoxelPointMeters(1.5m, 0.5m, 1.5m));

        Assert.Equal(VoxelViewportMode.CrossSection, slice.Mode);
        Assert.Equal(0, slice.CutVoxelY);
        Assert.NotNull(Column(slice, 1, 1).VisibleCell);
        Assert.True(Column(slice, 1, 1).HasSolidAbovePlane);
    }

    [Fact]
    public void EmptyTunnelRemainsVisibleInsideOverheadMountainOutline()
    {
        var grid = SolidBox().Apply(
            [new VoxelCoordinate(1, 0, 1)],
            VoxelEditMode.Subtractive);
        var view = new VoxelCrossSectionView();

        var slice = view.Follow(grid, new VoxelPointMeters(1.5m, 0.5m, 1.5m));

        Assert.Equal(VoxelViewportMode.CrossSection, slice.Mode);
        Assert.Null(Column(slice, 1, 1).VisibleCell);
        Assert.True(Column(slice, 1, 1).HasSolidAbovePlane);
        Assert.NotEmpty(slice.OverheadOutline);
    }

    [Fact]
    public void CutPlaneFollowsTheEditedElevation()
    {
        var view = new VoxelCrossSectionView();
        var grid = SolidBox();

        Assert.Equal(0, view.Follow(
            grid, new VoxelPointMeters(1.5m, 0.5m, 1.5m)).CutVoxelY);
        Assert.Equal(2, view.Follow(
            grid, new VoxelPointMeters(1.5m, 2.5m, 1.5m)).CutVoxelY);
    }

    [Fact]
    public void ReachingSurfaceReturnsToTopDownView()
    {
        var view = new VoxelCrossSectionView();

        var slice = view.Follow(
            SolidBox(), new VoxelPointMeters(1.5m, 4m, 1.5m));

        Assert.Equal(VoxelViewportMode.TopDown, slice.Mode);
        Assert.Null(slice.CutVoxelY);
        Assert.Equal(3, Column(slice, 1, 1).VisibleCell!.Coordinate.Y);
    }

    [Fact]
    public void PointerOutsideVolumeUsesTopDownView()
    {
        var slice = new VoxelCrossSectionView().Follow(
            SolidBox(), new VoxelPointMeters(3.5m, 0.5m, 3.5m));

        Assert.Equal(VoxelViewportMode.TopDown, slice.Mode);
    }

    [Fact]
    public void OverheadOutlineContainsOnlyBoundaryEdges()
    {
        var slice = new VoxelCrossSectionView().Follow(
            SolidBox(), new VoxelPointMeters(1.5m, 0.5m, 1.5m));

        Assert.Equal(8, slice.OverheadOutline.Count);
    }

    private static VoxelColumnView Column(VoxelViewportSlice slice, int x, int z) =>
        slice.Columns.Single(column => column.X == x && column.Z == z);

    private static VoxelGrid SolidBox()
    {
        var grid = VoxelGrid.Empty(4, 4, 1m, 0.2m);
        return grid.Apply(
            VoxelPrimitiveLibrary.Box(
                grid,
                new VoxelPointMeters(0m, 0m, 0m),
                new VoxelSizeMeters(2m, 4m, 2m)),
            VoxelEditMode.Additive,
            "grass");
    }
}
