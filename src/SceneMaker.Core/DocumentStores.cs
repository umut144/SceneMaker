using System.Text;

namespace SceneMaker.Core;

public static class WorkspaceStore
{
    public const string ScenesDirectoryName = "scenes";
    public const string TemplatesDirectoryName = "templates";

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

        try
        {
            Directory.CreateDirectory(fullParentDirectory);
            Directory.CreateDirectory(fullDirectory);
            Directory.CreateDirectory(Path.Combine(fullDirectory, ScenesDirectoryName));
            Directory.CreateDirectory(Path.Combine(fullDirectory, TemplatesDirectoryName));
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
    }

    public static LoadedWorkspace Load(string workspaceDirectory, SceneMakerCatalog catalog)
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

    public static LoadedScene Create(
        LoadedWorkspace workspace,
        string sceneId,
        int widthCells,
        int heightCells,
        SceneKind sceneKind = SceneKind.Instance,
        int templateGroupNumber = 1,
        int insertionAnchorX = 0,
        int insertionAnchorY = 0)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var document = SceneDocument.Create(
            sceneId,
            widthCells,
            heightCells,
            sceneKind,
            templateGroupNumber,
            insertionAnchorX,
            insertionAnchorY);
        DocumentValidation.Validate(document);
        var directory = DirectoryFor(workspace, sceneKind);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, sceneId + FileSuffix);
        if (File.Exists(filePath))
            throw new SceneMakerDocumentException($"Scene '{sceneId}' already exists in this Workspace.");

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
