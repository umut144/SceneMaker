namespace SceneMaker.Core;

/// <summary>
/// Changes only the north/east Scene boundary. Existing local coordinates are
/// deliberately preserved, so every authored anchor and Terrain cell remains
/// exactly where it was.
/// </summary>
public static class MapEditing
{
    public static SceneDocument ExtendNorth(SceneDocument scene, int cells) =>
        Extend(scene, widthCells: 0, heightCells: cells);

    public static SceneDocument ExtendEast(SceneDocument scene, int cells) =>
        Extend(scene, widthCells: cells, heightCells: 0);

    private static SceneDocument Extend(SceneDocument scene, int widthCells, int heightCells)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (widthCells < 0 || heightCells < 0 || widthCells == 0 && heightCells == 0)
            throw new ArgumentOutOfRangeException(nameof(widthCells),
                "Map extension requires a positive north or east Cell count.");
        try
        {
            var expanded = scene with
            {
                SizeCells = new SceneSizeCells
                {
                    Width = checked(scene.SizeCells.Width + widthCells),
                    Height = checked(scene.SizeCells.Height + heightCells),
                },
            };
            DocumentValidation.Validate(expanded);
            return expanded;
        }
        catch (OverflowException exception)
        {
            throw new SceneMakerDocumentException(
                "Map extension exceeds the integer authoring coordinate range.", exception);
        }
    }
}
