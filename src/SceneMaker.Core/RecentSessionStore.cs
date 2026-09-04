using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public sealed record RecentSessionDocument
{
    public required string Schema { get; init; }
    public required int Version { get; init; }
    public required string WorkspaceDirectoryPath { get; init; }
    public required string? SceneRelativePath { get; init; }
    public required RecentCameraDocument Camera { get; init; }
}

public sealed record RecentCameraDocument
{
    public required double PanX { get; init; }
    public required double PanY { get; init; }
    public required double Zoom { get; init; }
}

public static class RecentSessionStore
{
    public const string Schema = "scene_maker_recent_session";
    public const int Version = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static void Save(
        string statePath,
        LoadedWorkspace workspace,
        LoadedScene? scene,
        double cameraPanX,
        double cameraPanY,
        double cameraZoom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(workspace);
        string? sceneRelativePath = null;
        if (scene is not null)
        {
            sceneRelativePath = Path.GetRelativePath(workspace.DirectoryPath, scene.FilePath);
            if (Path.IsPathRooted(sceneRelativePath)
                || sceneRelativePath == ".."
                || sceneRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new SceneMakerDocumentException("Recent Scene must belong to its Workspace.");
            }
        }
        var document = new RecentSessionDocument
        {
            Schema = Schema,
            Version = Version,
            WorkspaceDirectoryPath = Path.GetFullPath(workspace.DirectoryPath),
            SceneRelativePath = sceneRelativePath,
            Camera = new RecentCameraDocument
            {
                PanX = cameraPanX,
                PanY = cameraPanY,
                Zoom = cameraZoom,
            },
        };
        Validate(document);
        AtomicTextFile.Write(Path.GetFullPath(statePath), JsonSerializer.Serialize(document, JsonOptions) + "\n");
    }

    public static RecentSessionDocument? Load(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        var fullPath = Path.GetFullPath(statePath);
        if (!File.Exists(fullPath)) return null;
        try
        {
            var document = JsonSerializer.Deserialize<RecentSessionDocument>(File.ReadAllText(fullPath), JsonOptions)
                ?? throw new SceneMakerDocumentException("Recent session must not contain JSON null.");
            Validate(document);
            return document;
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new SceneMakerDocumentException(
                $"Could not load recent SceneMaker session: {exception.Message}", exception);
        }
    }

    private static void Validate(RecentSessionDocument document)
    {
        if (document.Schema != Schema || document.Version != Version)
            throw new SceneMakerDocumentException($"Recent session must use {Schema} version {Version}.");
        if (string.IsNullOrWhiteSpace(document.WorkspaceDirectoryPath)
            || !Path.IsPathFullyQualified(document.WorkspaceDirectoryPath))
        {
            throw new SceneMakerDocumentException("Recent session requires an absolute workspace_directory_path.");
        }
        if (document.SceneRelativePath is not null
            && (!document.SceneRelativePath.EndsWith(SceneStore.FileSuffix, StringComparison.Ordinal)
                || Path.IsPathRooted(document.SceneRelativePath)
                || document.SceneRelativePath == ".."
                || document.SceneRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            throw new SceneMakerDocumentException(
                $"Recent session Scene must be a Workspace-relative path ending with '{SceneStore.FileSuffix}'.");
        }
        if (document.Camera is null
            || !double.IsFinite(document.Camera.PanX)
            || !double.IsFinite(document.Camera.PanY)
            || !double.IsFinite(document.Camera.Zoom)
            || document.Camera.Zoom <= 0.0)
        {
            throw new SceneMakerDocumentException(
                "Recent session camera requires finite pan coordinates and a positive finite zoom.");
        }
    }
}
