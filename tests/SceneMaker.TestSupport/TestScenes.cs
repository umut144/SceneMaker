using SceneMaker.Core;

namespace SceneMaker.TestSupport;

/// <summary>
/// Builders for the Scene documents the Core tests operate on. Most are fully
/// covered with Terrain simply because that is the ordinary map; a Prop needs
/// no ground, so <see cref="EmptyInstance"/> is an equally valid Scene rather
/// than a deliberately broken one.
/// </summary>
public static class TestScenes
{
    /// <summary>
    /// A square Scene Instance covered with <paramref name="terrainAssetKey"/>.
    /// The default 6 x 6 cells span 192 x 192 authoring pixels.
    /// </summary>
    public static SceneDocument Instance(
        TestWorkspace workspace,
        string sceneId = "base",
        int sizeCells = 6,
        string terrainAssetKey = "grass") =>
        FillTerrain(
            SceneDocument.CreateInstance(sceneId, sizeCells, sizeCells),
            workspace,
            terrainAssetKey,
            sizeCells);

    /// <summary>
    /// A square Scene Template covered with <paramref name="terrainAssetKey"/>,
    /// whose insertion anchor sits at its lower left corner (0, 0). The default
    /// 2 x 2 cells span 64 x 64 authoring pixels.
    /// </summary>
    public static SceneDocument Template(
        TestWorkspace workspace,
        string sceneId,
        int groupNumber,
        int sizeCells = 2,
        string terrainAssetKey = "sand") =>
        FillTerrain(
            SceneDocument.CreateTemplate(
                sceneId,
                sizeCells,
                sizeCells,
                groupNumber,
                insertionAnchorX: 0,
                insertionAnchorY: 0),
            workspace,
            terrainAssetKey,
            sizeCells);

    /// <summary>A square Scene Instance with no Terrain at all.</summary>
    public static SceneDocument EmptyInstance(string sceneId = "base", int sizeCells = 6) =>
        SceneDocument.CreateInstance(sceneId, sizeCells, sizeCells);

    private static SceneDocument FillTerrain(
        SceneDocument scene,
        TestWorkspace workspace,
        string terrainAssetKey,
        int sizeCells)
    {
        for (var y = 0; y < sizeCells; y++)
        {
            for (var x = 0; x < sizeCells; x++)
                scene = TerrainEditing.Paint(scene, workspace.Terrain, x, y, terrainAssetKey);
        }
        return scene;
    }
}
