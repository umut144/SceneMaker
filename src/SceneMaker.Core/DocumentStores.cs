using System.Text;

namespace SceneMaker.Core;

public static class WorkspaceStore
{
    /// <summary>
    /// Creates a World whole: its PolyTools import boundary and a default
    /// configuration. If any step fails the directory is removed again,
    /// because a half-built Workspace would only fail to load later without
    /// saying why.
    ///
    /// <para>It creates no Game. A World with no Game is ordinary right after
    /// creation - there is nothing yet to put a Scene in - and
    /// <see cref="GameStore.Create"/> is the separate, later step that adds
    /// one.</para>
    /// </summary>
    public static LoadedWorkspace Create(
        string parentDirectoryPath,
        string workspaceId,
        decimal minimumChannelDepthMeters)
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
            Directory.CreateDirectory(Path.Combine(
                fullDirectory,
                PolyToolsCatalogImporter.ImportDirectoryName,
                PolyToolsCatalogImporter.PolyToolsDirectoryName));
            WorkspaceConfigurationStore.CreateDefault(fullDirectory, workspaceId, minimumChannelDepthMeters);
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
            if (!complete) RemoveIncompleteDirectory(fullDirectory);
        }
    }

    internal static void RemoveIncompleteDirectory(string directory)
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

    public static LoadedWorkspace Load(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var directory = Path.GetFullPath(workspaceDirectory);

        try
        {
            if (!Directory.Exists(directory))
                throw new SceneMakerDocumentException($"Workspace directory '{directory}' does not exist.");
            var configuration = WorkspaceConfigurationStore.Load(directory);
            var directoryName = Path.GetFileName(directory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (!string.Equals(directoryName, configuration.WorkspaceKey, StringComparison.Ordinal))
            {
                throw new SceneMakerDocumentException(
                    $"Workspace '{configuration.WorkspaceKey}' must use directory '{configuration.WorkspaceKey}'.");
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

/// <summary>
/// One named Game inside a World: its own `scenes/` and `templates/`
/// directories, holding the map sets built from the World's shared Assets and
/// PolyTools import. See <see cref="LoadedGame"/> for why a Game carries no
/// file of its own.
/// </summary>
public static class GameStore
{
    public const string ScenesDirectoryName = "scenes";
    public const string TemplatesDirectoryName = "templates";

    public static LoadedGame Create(LoadedWorkspace workspace, string gameKey)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentValidation.ValidateStableId("game_key", gameKey);

        var fullDirectory = Path.Combine(workspace.DirectoryPath, gameKey);
        if (Directory.Exists(fullDirectory))
        {
            throw new SceneMakerDocumentException(
                $"Refusing to overwrite existing Game directory '{fullDirectory}'.");
        }

        var complete = false;
        try
        {
            Directory.CreateDirectory(fullDirectory);
            Directory.CreateDirectory(Path.Combine(fullDirectory, ScenesDirectoryName));
            Directory.CreateDirectory(Path.Combine(fullDirectory, TemplatesDirectoryName));
            complete = true;
            return new LoadedGame(workspace, gameKey);
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SceneMakerDocumentException(
                $"Could not create Game '{fullDirectory}': {exception.Message}", exception);
        }
        finally
        {
            if (!complete) WorkspaceStore.RemoveIncompleteDirectory(fullDirectory);
        }
    }

    public static LoadedGame Load(LoadedWorkspace workspace, string gameKey)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentValidation.ValidateStableId("game_key", gameKey);
        var game = new LoadedGame(workspace, gameKey);
        RequireDirectories(game);
        return game;
    }

    /// <summary>
    /// Opens the Game at <paramref name="gameDirectory"/>, a direct child of
    /// the Workspace directory. Throws if it does not belong to this Workspace
    /// or is missing either document directory.
    /// </summary>
    public static LoadedGame LoadAt(LoadedWorkspace workspace, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var fullDirectory = Path.GetFullPath(gameDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var expectedParent = Path.GetFullPath(workspace.DirectoryPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var actualParent = Path.GetDirectoryName(fullDirectory)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.Equals(actualParent, expectedParent, StringComparison.Ordinal))
        {
            throw new SceneMakerDocumentException(
                $"Game directory '{fullDirectory}' does not belong to Workspace '{workspace.WorkspaceKey}'.");
        }
        return Load(workspace, Path.GetFileName(fullDirectory));
    }

    /// <summary>Every Game directory under the Workspace, in name order.</summary>
    public static IEnumerable<string> EnumerateGameKeys(LoadedWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Directory.EnumerateDirectories(workspace.DirectoryPath)
            .Select(directory => Path.GetFileName(directory)!)
            .Where(gameKey =>
                Directory.Exists(Path.Combine(workspace.DirectoryPath, gameKey, ScenesDirectoryName))
                && Directory.Exists(Path.Combine(workspace.DirectoryPath, gameKey, TemplatesDirectoryName)))
            .OrderBy(static gameKey => gameKey, StringComparer.Ordinal);
    }

    private static void RequireDirectories(LoadedGame game)
    {
        if (!Directory.Exists(game.DirectoryPath))
        {
            throw new SceneMakerDocumentException(
                $"Workspace '{game.Workspace.WorkspaceKey}' has no Game '{game.GameKey}'.");
        }
        if (!Directory.Exists(game.ScenesDirectoryPath))
        {
            throw new SceneMakerDocumentException(
                $"Game '{game.GameKey}' requires its '{ScenesDirectoryName}' directory.");
        }
        if (!Directory.Exists(game.TemplatesDirectoryPath))
        {
            throw new SceneMakerDocumentException(
                $"Game '{game.GameKey}' requires its '{TemplatesDirectoryName}' directory.");
        }
    }
}

public static class SceneStore
{
    public const string FileSuffix = ".scene.json";

    public static IEnumerable<string> EnumeratePaths(LoadedGame game) =>
        new[] { game.ScenesDirectoryPath, game.TemplatesDirectoryPath }
            .SelectMany(directory => Directory.EnumerateFiles(
                directory,
                $"*{FileSuffix}",
                SearchOption.TopDirectoryOnly))
            .OrderBy(static path => path, StringComparer.Ordinal);

    /// <summary>
    /// Finds the file of <paramref name="sceneId"/> in the Game, whether it is
    /// a Scene Instance or a Scene Template. A Scene id names one Scene, so
    /// the same id in both directories is an error rather than a preference.
    /// </summary>
    public static string ResolvePath(LoadedGame game, string sceneId)
    {
        ArgumentNullException.ThrowIfNull(game);
        DocumentValidation.ValidateStableId("scene_id", sceneId);
        var candidates = ExistingPaths(game, sceneId);
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new SceneMakerDocumentException(
                $"Game '{game.GameKey}' has no Scene '{sceneId}'."),
            _ => throw new SceneMakerDocumentException(
                $"Scene id '{sceneId}' names both a Scene Instance and a Scene Template."),
        };
    }

    /// <summary>Every file in the Game that carries this Scene id.</summary>
    private static List<string> ExistingPaths(LoadedGame game, string sceneId) =>
        new[] { game.ScenesDirectoryPath, game.TemplatesDirectoryPath }
            .Select(directory => Path.Combine(directory, sceneId + FileSuffix))
            .Where(File.Exists)
            .ToList();

    public static LoadedScene CreateInstance(
        LoadedGame game,
        string sceneId,
        int widthCells,
        int heightCells,
        decimal defaultElevationMeters = SceneDocument.GroundElevationMeters) =>
        WriteNewScene(game, SceneDocument.CreateInstance(
            sceneId, widthCells, heightCells, defaultElevationMeters));

    public static LoadedScene CreateTemplate(
        LoadedGame game,
        string sceneId,
        int widthCells,
        int heightCells,
        int groupNumber,
        int insertionAnchorX,
        int insertionAnchorY,
        decimal defaultElevationMeters = SceneDocument.GroundElevationMeters) =>
        WriteNewScene(game, SceneDocument.CreateTemplate(
            sceneId,
            widthCells,
            heightCells,
            groupNumber,
            insertionAnchorX,
            insertionAnchorY,
            defaultElevationMeters));

    private static LoadedScene WriteNewScene(LoadedGame game, SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(game);
        DocumentValidation.Validate(document);
        // A Scene id names one Scene in the whole Game, not one per directory:
        // the exports live in a single flat directory named by id, and
        // consumers name a Template by that id across a reconnect.
        if (ExistingPaths(game, document.SceneId).Count > 0)
        {
            throw new SceneMakerDocumentException(
                $"Scene '{document.SceneId}' already exists in this Game.");
        }
        var directory = DirectoryFor(game, document.SceneKind);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, document.SceneId + FileSuffix);

        AtomicTextFile.WriteNew(filePath, DocumentJson.Serialize(document));
        return new LoadedScene(filePath, document);
    }

    public static LoadedScene Load(LoadedGame game, string filePath)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        RequireDirectSceneChild(game, fullPath);
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
                    DirectoryFor(game, document.SceneKind),
                    StringComparison.Ordinal))
            {
                throw new SceneMakerDocumentException(
                    $"{document.SceneKind} '{document.SceneId}' must be stored in its Game {DirectoryNameFor(document.SceneKind)} directory.");
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

    public static void Save(LoadedGame game, LoadedScene scene)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(scene);
        var fullPath = Path.GetFullPath(scene.FilePath);
        RequireDirectSceneChild(game, fullPath, scene.Document.SceneKind);
        if (Path.GetFileName(fullPath) != scene.Document.SceneId + FileSuffix)
            throw new SceneMakerDocumentException("Scene ID and filename must remain identical.");
        AtomicTextFile.Write(fullPath, DocumentJson.Serialize(scene.Document));
    }

    private static void RequireDirectSceneChild(
        LoadedGame game,
        string fullPath,
        SceneKind? kind = null)
    {
        var actualParent = Path.GetDirectoryName(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var expectedParents = kind is null
            ? new[] { game.ScenesDirectoryPath, game.TemplatesDirectoryPath }
            : new[] { DirectoryFor(game, kind.Value) };
        if (!expectedParents.Any(directory => string.Equals(
                actualParent,
                Path.GetFullPath(directory).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException(
                "Scene file must be directly inside the Game scenes or templates directory.");
        }
    }

    public static string DirectoryFor(LoadedGame game, SceneKind kind) => kind switch
    {
        SceneKind.Instance => game.ScenesDirectoryPath,
        SceneKind.Template => game.TemplatesDirectoryPath,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string DirectoryNameFor(SceneKind kind) => kind switch
    {
        SceneKind.Instance => GameStore.ScenesDirectoryName,
        SceneKind.Template => GameStore.TemplatesDirectoryName,
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
