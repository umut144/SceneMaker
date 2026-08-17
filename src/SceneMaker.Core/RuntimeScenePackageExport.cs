using MMORPG.Simulation.Scenes;
using MMORPG.Simulation.WorldAssets;

namespace SceneMaker.Core;

public static class RuntimeScenePackageExporter
{
    public static RuntimeScenePackageDocument Create(
        IEnumerable<SceneDocument> scenes,
        TerrainDisplayCatalog terrainAssets,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        LoadedWorldAssetCatalog worldAssets)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(placementAssets);
        ArgumentNullException.ThrowIfNull(transitionAssets);
        ArgumentNullException.ThrowIfNull(worldAssets);

        var orderedScenes = scenes
            .OrderBy(static scene => scene.SceneId, StringComparer.Ordinal)
            .ToList();
        var issues = RuntimeScenePackageExportValidator.Validate(
            orderedScenes,
            terrainAssets,
            placementAssets,
            transitionAssets);
        if (issues.Count > 0)
        {
            throw new SceneMakerDocumentException(
                "Runtime Scene Package export validation failed:\n- "
                + string.Join("\n- ", issues.Select(static issue => issue.Message)));
        }

        var package = new RuntimeScenePackageDocument
        {
            Schema = RuntimeScenePackageContract.Schema,
            Version = RuntimeScenePackageContract.Version,
            CoordinateSpace = RuntimeScenePackageContract.CoordinateSpace,
            Scenes = orderedScenes.Select(CreateScene).ToList(),
        };
        try
        {
            _ = RuntimeScenePackageValidator.Validate(package, worldAssets);
            return package;
        }
        catch (RuntimeScenePackageException exception)
        {
            throw new SceneMakerDocumentException(
                $"Runtime Scene Package validation failed: {exception.Message}",
                exception);
        }
    }

    private static RuntimeSceneDocument CreateScene(SceneDocument scene) => new()
    {
        SceneId = scene.SceneId,
        SceneKind = scene.SceneKind == SceneKind.Instance
            ? RuntimeSceneKind.Instance
            : RuntimeSceneKind.Template,
        SizeCells = new RuntimeSceneSizeCells
        {
            Width = scene.SizeCells.Width,
            Height = scene.SizeCells.Height,
        },
        TerrainCells = scene.TerrainCells.Select(static cell => new RuntimeTerrainCell
        {
            X = cell.X,
            Y = cell.Y,
            AssetId = cell.AssetId,
        }).ToList(),
        Placements = scene.Placements.Select(CreateSpatialInstance).ToList(),
        Transitions = scene.Transitions.Select(CreateSpatialInstance).ToList(),
        TemplateDefinition = scene.TemplateDefinition is null
            ? null
            : new RuntimeTemplateDefinition
            {
                GroupNumber = scene.TemplateDefinition.GroupNumber,
                InsertionAnchorAuthoringPx = CreatePosition(
                    scene.TemplateDefinition.InsertionAnchorAuthoringPx),
            },
        TemplateAnchors = scene.TemplateAnchors.Select(anchor => new RuntimeTemplateAnchor
        {
            AnchorId = anchor.AnchorId,
            GroupNumber = anchor.GroupNumber,
            PositionAuthoringPx = CreatePosition(anchor.PositionAuthoringPx),
        }).ToList(),
    };

    private static RuntimeSpatialInstance CreateSpatialInstance(PlacementDocument placement) => new()
    {
        InstanceId = placement.InstanceId,
        AssetId = placement.AssetId,
        PositionAuthoringPx = CreatePosition(placement.PositionAuthoringPx),
    };

    private static RuntimeSpatialInstance CreateSpatialInstance(TransitionDocument transition) => new()
    {
        InstanceId = transition.InstanceId,
        AssetId = transition.AssetId,
        PositionAuthoringPx = CreatePosition(transition.PositionAuthoringPx),
    };

    private static RuntimeAuthoringPosition CreatePosition(AuthoringPixelPosition position) => new()
    {
        X = position.X,
        Y = position.Y,
    };
}

public sealed record RuntimeScenePackageExportIssue(string Message);

public static class RuntimeScenePackageExportValidator
{
    public static IReadOnlyList<RuntimeScenePackageExportIssue> Validate(
        IEnumerable<SceneDocument> scenes,
        TerrainDisplayCatalog terrainAssets,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(terrainAssets);
        ArgumentNullException.ThrowIfNull(placementAssets);
        ArgumentNullException.ThrowIfNull(transitionAssets);
        var ordered = scenes.OrderBy(static scene => scene.SceneId, StringComparer.Ordinal).ToList();
        List<RuntimeScenePackageExportIssue> issues = [];
        foreach (var scene in ordered)
        {
            try
            {
                DocumentValidation.Validate(scene);
                TerrainEditing.ValidateAssetReferences(scene, terrainAssets);
                PlacementEditing.ValidateAssetReferences(
                    scene,
                    placementAssets,
                    transitionAssets);
                TransitionEditing.ValidateAssetReferences(
                    scene,
                    placementAssets,
                    transitionAssets);
            }
            catch (SceneMakerDocumentException exception)
            {
                issues.Add(new RuntimeScenePackageExportIssue(
                    $"Scene '{scene.SceneId}': {exception.Message}"));
                continue;
            }

            foreach (var placement in scene.Placements)
            {
                var asset = placementAssets.Resolve(placement.AssetId);
                AddCoverageIssue(
                    issues,
                    scene,
                    "Placement",
                    placement.InstanceId,
                    PlacementEditing.BoundsFor(
                        asset,
                        placement.PositionAuthoringPx.X,
                        placement.PositionAuthoringPx.Y));
            }
            foreach (var transition in scene.Transitions)
            {
                var asset = transitionAssets.Resolve(transition.AssetId);
                AddCoverageIssue(
                    issues,
                    scene,
                    "Transition",
                    transition.InstanceId,
                    TransitionEditing.BoundsFor(
                        asset,
                        transition.PositionAuthoringPx.X,
                        transition.PositionAuthoringPx.Y));
            }
        }

        var portalAssetIds = transitionAssets.Assets
            .Where(static asset => asset.Key == "portal")
            .Select(static asset => asset.AssetId)
            .ToHashSet();
        var portalSceneIds = ordered
            .Where(static scene => scene.SceneKind == SceneKind.Instance)
            .Where(scene => scene.Transitions.Any(transition =>
                portalAssetIds.Contains(transition.AssetId)))
            .Select(static scene => scene.SceneId)
            .ToList();
        if (portalSceneIds.Count != 1)
        {
            issues.Add(new RuntimeScenePackageExportIssue(
                portalSceneIds.Count == 0
                    ? "Workspace requires exactly one Portal-bearing startup Scene, but none was found."
                    : $"Workspace requires exactly one Portal-bearing startup Scene, but Portals occur in multiple Scenes: {string.Join(", ", portalSceneIds)}."));
        }
        return issues;
    }

    private static void AddCoverageIssue(
        ICollection<RuntimeScenePackageExportIssue> issues,
        SceneDocument scene,
        string label,
        string instanceId,
        PlacementBoundsAuthoringPixels bounds)
    {
        var missing = TerrainCoverage.MissingCells(scene, bounds);
        if (missing.Count == 0) return;
        issues.Add(new RuntimeScenePackageExportIssue(
            $"Scene '{scene.SceneId}' {label} '{instanceId}' lacks Terrain under its footprint at {TerrainCoverage.FormatMissingCells(missing)}."));
    }
}

public static class RuntimeScenePackageStore
{
    public const string ExportsDirectoryName = "exports";
    public const string FileSuffix = RuntimeScenePackageContract.FileSuffix;

    public static string Export(
        LoadedWorkspace workspace,
        TerrainDisplayCatalog terrainAssets,
        PlacementDisplayCatalog placementAssets,
        TransitionDisplayCatalog transitionAssets,
        string worldAssetCatalogPath,
        string? outputDirectoryPath = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldAssetCatalogPath);
        try
        {
            var scenes = SceneStore
                .EnumeratePaths(workspace)
                .Select(path => SceneStore.Load(workspace, path).Document)
                .ToList();
            var worldAssets = WorldAssetCatalogLoader.Load(worldAssetCatalogPath);
            var package = RuntimeScenePackageExporter.Create(
                scenes,
                terrainAssets,
                placementAssets,
                transitionAssets,
                worldAssets);
            var directory = outputDirectoryPath is null
                ? Path.Combine(workspace.DirectoryPath, ExportsDirectoryName)
                : Path.GetFullPath(outputDirectoryPath);
            var path = Path.Combine(
                directory,
                workspace.Document.WorkspaceId + FileSuffix);
            AtomicTextFile.Write(
                path,
                RuntimeScenePackageLoader.Serialize(package, worldAssets));
            return path;
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (WorldAssetCatalogException exception)
        {
            throw new SceneMakerDocumentException(
                $"World Asset catalog is invalid: {exception.Message}",
                exception);
        }
        catch (RuntimeScenePackageException exception)
        {
            throw new SceneMakerDocumentException(
                $"Runtime Scene Package export failed: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not export Runtime Scene Package: {exception.Message}",
                exception);
        }
    }
}
