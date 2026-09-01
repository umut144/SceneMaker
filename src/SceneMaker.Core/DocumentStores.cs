using System.Text;

namespace SceneMaker.Core;

public static class WorkspaceStore
{
    public const string ScenesDirectoryName = "scenes";
    public const string TemplatesDirectoryName = "templates";

    /// <summary>
    /// Creates a Workspace whole: both document directories, the PolyTools
    /// import boundary and a default configuration. If any step fails the
    /// directory is removed again, because a half-built Workspace would only
    /// fail to load later without saying why.
    /// </summary>
    public static LoadedWorkspace Create(string parentDirectoryPath, string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectoryPath);
        DocumentValidation.ValidateStableId("workspace_key", workspaceId);

        var fullParentDirectory = Path.GetFullPath(parentDirectoryPath);
        var fullDirectory = Path.Combine(fullParentDirectory, workspaceId);
        if (Directory.Exists(fullDirectory))
        {
            throw new SceneMakerDocumentException(
                $"Refusing to overwrite existing Workspace directory '{fullDirectory}'.");
        }

        var complete = false;
        try
        {
            Directory.CreateDirectory(fullParentDirectory);
            Directory.CreateDirectory(fullDirectory);
            Directory.CreateDirectory(Path.Combine(fullDirectory, ScenesDirectoryName));
            Directory.CreateDirectory(Path.Combine(fullDirectory, TemplatesDirectoryName));
            Directory.CreateDirectory(Path.Combine(
                fullDirectory,
                PolyToolsCatalogImporter.ImportDirectoryName,
                PolyToolsCatalogImporter.PolyToolsDirectoryName));
            WorkspaceConfigurationStore.CreateDefault(fullDirectory, workspaceId);
            complete = true;
            return new LoadedWorkspace(fullDirectory, workspaceId);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not create Workspace '{fullDirectory}': {exception.Message}", exception);
        }
        finally
        {
            if (!complete) RemoveIncompleteWorkspace(fullDirectory);
        }
    }

    private static void RemoveIncompleteWorkspace(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The failure that got us here is the one worth reporting; leaving
            // the directory behind is the lesser problem.
        }
    }

    public static LoadedWorkspace Load(string workspaceDirectory, PolyToolsCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentNullException.ThrowIfNull(catalog);
        var directory = Path.GetFullPath(workspaceDirectory);

        try
        {
            if (!Directory.Exists(directory))
                throw new SceneMakerDocumentException($"Workspace directory '{directory}' does not exist.");
            var configuration = WorkspaceConfigurationStore.Load(directory, catalog);
            var directoryName = Path.GetFileName(directory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (!string.Equals(directoryName, configuration.WorkspaceKey, StringComparison.Ordinal))
            {
                throw new SceneMakerDocumentException(
                    $"Workspace '{configuration.WorkspaceKey}' must use directory '{configuration.WorkspaceKey}'.");
            }

            var scenesDirectory = Path.Combine(directory, ScenesDirectoryName);
            if (!Directory.Exists(scenesDirectory))
            {
                throw new SceneMakerDocumentException(
                    $"Workspace '{configuration.WorkspaceKey}' requires its '{ScenesDirectoryName}' directory.");
            }
            var templatesDirectory = Path.Combine(directory, TemplatesDirectoryName);
            if (!Directory.Exists(templatesDirectory))
            {
                throw new SceneMakerDocumentException(
                    $"Workspace '{configuration.WorkspaceKey}' requires its '{TemplatesDirectoryName}' directory.");
            }
            return new LoadedWorkspace(directory, configuration.WorkspaceKey);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load Workspace '{directory}': {exception.Message}", exception);
        }
    }
}

public static class SceneStore
{
    public const string FileSuffix = ".scene.json";

    public static IEnumerable<string> EnumeratePaths(LoadedWorkspace workspace) =>
        new[] { workspace.ScenesDirectoryPath, workspace.TemplatesDirectoryPath }
            .SelectMany(directory => Directory.EnumerateFiles(
                directory,
                $"*{FileSuffix}",
                SearchOption.TopDirectoryOnly))
            .OrderBy(static path => path, StringComparer.Ordinal);

    /// <summary>
    /// Finds the file of <paramref name="sceneId"/> in the Workspace, whether it
    /// is a Scene Instance or a Scene Template. A Scene id names one Scene, so
    /// the same id in both directories is an error rather than a preference.
    /// </summary>
    public static string ResolvePath(LoadedWorkspace workspace, string sceneId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentValidation.ValidateStableId("scene_id", sceneId);
        var candidates = ExistingPaths(workspace, sceneId);
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new SceneMakerDocumentException(
                $"Workspace '{workspace.WorkspaceKey}' has no Scene '{sceneId}'."),
            _ => throw new SceneMakerDocumentException(
                $"Scene id '{sceneId}' names both a Scene Instance and a Scene Template."),
        };
    }

    /// <summary>Every file in the Workspace that carries this Scene id.</summary>
    private static List<string> ExistingPaths(LoadedWorkspace workspace, string sceneId) =>
        new[] { workspace.ScenesDirectoryPath, workspace.TemplatesDirectoryPath }
            .Select(directory => Path.Combine(directory, sceneId + FileSuffix))
            .Where(File.Exists)
            .ToList();

    public static LoadedScene CreateInstance(
        LoadedWorkspace workspace,
        string sceneId,
        int widthCells,
        int heightCells,
        decimal defaultElevationMeters = SceneDocument.GroundElevationMeters) =>
        WriteNewScene(workspace, SceneDocument.CreateInstance(
            sceneId, widthCells, heightCells, defaultElevationMeters));

    public static LoadedScene CreateTemplate(
        LoadedWorkspace workspace,
        string sceneId,
        int widthCells,
        int heightCells,
        int groupNumber,
        int insertionAnchorX,
        int insertionAnchorY,
        decimal defaultElevationMeters = SceneDocument.GroundElevationMeters) =>
        WriteNewScene(workspace, SceneDocument.CreateTemplate(
            sceneId,
            widthCells,
            heightCells,
            groupNumber,
            insertionAnchorX,
            insertionAnchorY,
            defaultElevationMeters));

    private static LoadedScene WriteNewScene(LoadedWorkspace workspace, SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentValidation.Validate(document);
        // A Scene id names one Scene in the whole Workspace, not one per
        // directory: the exports live in a single flat directory named by id,
        // and consumers name a Template by that id across a reconnect.
        if (ExistingPaths(workspace, document.SceneId).Count > 0)
        {
            throw new SceneMakerDocumentException(
                $"Scene '{document.SceneId}' already exists in this Workspace.");
        }
        var directory = DirectoryFor(workspace, document.SceneKind);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, document.SceneId + FileSuffix);

        AtomicTextFile.WriteNew(filePath, DocumentJson.Serialize(document));
        return new LoadedScene(filePath, document);
    }

    public static LoadedScene Load(LoadedWorkspace workspace, string filePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        RequireDirectSceneChild(workspace, fullPath);
        if (!fullPath.EndsWith(FileSuffix, StringComparison.Ordinal))
            throw new SceneMakerDocumentException($"Scene file must end with '{FileSuffix}'.");

        try
        {
            var document = DocumentJson.DeserializeScene(File.ReadAllText(fullPath));
            var expectedFileName = document.SceneId + FileSuffix;
            if (Path.GetFileName(fullPath) != expectedFileName)
            {
                throw new SceneMakerDocumentException(
                    $"Scene '{document.SceneId}' must use filename '{expectedFileName}'.");
            }
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    DirectoryFor(workspace, document.SceneKind),
                    StringComparison.Ordinal))
            {
                throw new SceneMakerDocumentException(
                    $"{document.SceneKind} '{document.SceneId}' must be stored in its Workspace {DirectoryNameFor(document.SceneKind)} directory.");
            }
            return new LoadedScene(fullPath, document);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load Scene '{fullPath}': {exception.Message}", exception);
        }
    }

    public static void Save(LoadedWorkspace workspace, LoadedScene scene)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scene);
        var fullPath = Path.GetFullPath(scene.FilePath);
        RequireDirectSceneChild(workspace, fullPath, scene.Document.SceneKind);
        if (Path.GetFileName(fullPath) != scene.Document.SceneId + FileSuffix)
            throw new SceneMakerDocumentException("Scene ID and filename must remain identical.");
        AtomicTextFile.Write(fullPath, DocumentJson.Serialize(scene.Document));
    }

    private static void RequireDirectSceneChild(
        LoadedWorkspace workspace,
        string fullPath,
        SceneKind? kind = null)
    {
        var actualParent = Path.GetDirectoryName(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var expectedParents = kind is null
            ? new[] { workspace.ScenesDirectoryPath, workspace.TemplatesDirectoryPath }
            : new[] { DirectoryFor(workspace, kind.Value) };
        if (!expectedParents.Any(directory => string.Equals(
                actualParent,
                Path.GetFullPath(directory).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException(
                "Scene file must be directly inside the Workspace scenes or templates directory.");
        }
    }

    public static string DirectoryFor(LoadedWorkspace workspace, SceneKind kind) => kind switch
    {
        SceneKind.Instance => workspace.ScenesDirectoryPath,
        SceneKind.Template => workspace.TemplatesDirectoryPath,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string DirectoryNameFor(SceneKind kind) => kind switch
    {
        SceneKind.Instance => WorkspaceStore.ScenesDirectoryName,
        SceneKind.Template => WorkspaceStore.TemplatesDirectoryName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal static class AtomicTextFile
{
    public static void WriteNew(string path, string contents)
    {
        if (File.Exists(path))
            throw new SceneMakerDocumentException($"Refusing to overwrite existing file '{path}'.");
        WriteInternal(path, contents, overwrite: false);
    }

    public static void Write(string path, string contents) =>
        WriteInternal(path, contents, overwrite: true);

    private static void WriteInternal(string path, string contents, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new SceneMakerDocumentException("Document path requires a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not save SceneMaker document '{path}': {exception.Message}", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
