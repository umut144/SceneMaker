using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class VoxelExportTests
{
    [Fact]
    public void HeightfieldKeepsOnlyHighestMaterializedSurfacePerColumn()
    {
        var grid = Grid().Add(
        [
            new VoxelCell(new VoxelCoordinate(1, 0, 2), "grass"),
            new VoxelCell(new VoxelCoordinate(1, 3, 2), "sand"),
            new VoxelCell(new VoxelCoordinate(0, -2, 0), "grass"),
        ]);

        var export = VoxelExportPipeline.Heightfield(grid);

        Assert.True(export.Lossy);
        Assert.Equal(2, export.Cells.Count);
        Assert.Equal(
            new VoxelHeightfieldCell(1, 2, 4m, "sand"),
            export.Cells[1]);
    }

    [Fact]
    public void SurfaceMeshOmitsFacesBetweenAdjacentVoxels()
    {
        var grid = Grid().Apply(
            [new VoxelCoordinate(0, 0, 0), new VoxelCoordinate(1, 0, 0)],
            VoxelEditMode.Additive,
            "grass");

        var export = VoxelExportPipeline.SurfaceMesh(grid);

        Assert.Equal(10, export.Faces.Count);
        Assert.Equal(12, export.Vertices.Count);
        Assert.All(export.Faces, face => Assert.Equal("grass", face.AssetKey));
    }

    [Fact]
    public void SurfaceMeshPreservesTunnelSurfaces()
    {
        var grid = Grid().Apply(
            VoxelPrimitiveLibrary.Box(
                Grid(),
                new VoxelPointMeters(0m, 0m, 0m),
                new VoxelSizeMeters(3m, 3m, 3m)),
            VoxelEditMode.Additive,
            "grass");
        grid = grid.Apply(
            [new VoxelCoordinate(1, 1, 0), new VoxelCoordinate(1, 1, 1)],
            VoxelEditMode.Subtractive);

        var export = VoxelExportPipeline.SurfaceMesh(grid);

        Assert.Contains(export.Vertices, vertex => vertex.Y == 1m && vertex.Z == 1m);
        Assert.Contains(export.Vertices, vertex => vertex.Y == 2m && vertex.Z == 2m);
    }

    [Fact]
    public void CompressedVoxelsRunAlongXAndSplitAtMaterialsAndGaps()
    {
        var grid = Grid().Add(
        [
            new VoxelCell(new VoxelCoordinate(0, 0, 0), "grass"),
            new VoxelCell(new VoxelCoordinate(1, 0, 0), "grass"),
            new VoxelCell(new VoxelCoordinate(2, 0, 0), "sand"),
            new VoxelCell(new VoxelCoordinate(4, 0, 0), "sand"),
        ]);

        var export = VoxelExportPipeline.CompressedVoxels(grid);

        Assert.Equal(
            [
                new VoxelRun(0, 0, 0, 2, "grass"),
                new VoxelRun(2, 0, 0, 1, "sand"),
                new VoxelRun(4, 0, 0, 1, "sand"),
            ],
            export.Runs);
    }

    [Theory]
    [InlineData(VoxelExportFormat.Heightfield, "world_vox_maker.heightfield")]
    [InlineData(VoxelExportFormat.SurfaceMesh, "world_vox_maker.surface_mesh")]
    [InlineData(VoxelExportFormat.CompressedVoxels, "world_vox_maker.compressed_voxels")]
    public void PipelineBuildsAndSerializesEveryVersionedContract(
        VoxelExportFormat format,
        string expectedContract)
    {
        var export = VoxelExportPipeline.Build(Grid(), format);
        var json = VoxelExportPipeline.Serialize(export);

        Assert.Equal(expectedContract, export.Format);
        Assert.Contains($"\"format\": \"{expectedContract}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
    }

    private static VoxelGrid Grid() => VoxelGrid.Empty(8, 8, 1m, 0.2m);
}
