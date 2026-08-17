using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public sealed record RecentSessionDocument
{
    public required string Schema { get; init; }
    public required int Version { get; init; }
    public required string WorkspaceManifestPath { get; init; }
    public required string? SceneRelativePath { get; init; }
}

public static class RecentSessionStore
{
    public const string Schema = "srt.scene_maker_recent_session";
    public const int Version = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static void Save(string statePath, LoadedWorkspace workspace, LoadedScene? scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(workspace);
        if (scene is not null)
        {
            var fullWorkspaceDirectory = Path.GetFullPath(workspace.DirectoryPath);
            var sceneRelativePath = Path.GetRelativePath(fullWorkspaceDirectory, scene.FilePath);
            if (Path.IsPathRooted(sceneRelativePath)
                || sceneRelativePath == ".."
                || sceneRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new SceneMakerDocumentException("Recent Scene must belong to its Workspace.");
            if (Path.GetFileName(scene.FilePath) != scene.Document.SceneId + SceneStore.FileSuffix)
                throw new SceneMakerDocumentException("Recent Scene ID and filename must match.");
        }

        var document = new RecentSessionDocument
        {
            Schema = Schema,
            Version = Version,
            WorkspaceManifestPath = Path.Combine(
                Path.GetFullPath(workspace.DirectoryPath),
                WorkspaceStore.ManifestFileName),
            SceneRelativePath = scene is null
                ? null
                : Path.GetRelativePath(workspace.DirectoryPath, scene.FilePath),
        };
        Validate(document);
        AtomicTextFile.Write(Path.GetFullPath(statePath), Serialize(document));
    }

    public static RecentSessionDocument? Load(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        var fullPath = Path.GetFullPath(statePath);
        if (!File.Exists(fullPath)) return null;
        try
        {
            var json = File.ReadAllText(fullPath);
            using var root = JsonDocument.Parse(json);
            var version = root.RootElement.TryGetProperty("version", out var versionElement)
                ? versionElement.GetInt32()
                : 0;
            var document = version == 1
                ? MigrateVersionOne(json)
                : JsonSerializer.Deserialize<RecentSessionDocument>(json, JsonOptions)
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

    private static string Serialize(RecentSessionDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions) + "\n";

    private static void Validate(RecentSessionDocument document)
    {
        if (document.Schema != Schema || document.Version != Version)
        {
            throw new SceneMakerDocumentException(
                $"Recent session must use {Schema} version {Version}.");
        }
        if (string.IsNullOrWhiteSpace(document.WorkspaceManifestPath)
            || !Path.IsPathFullyQualified(document.WorkspaceManifestPath)
            || Path.GetFileName(document.WorkspaceManifestPath) != WorkspaceStore.ManifestFileName)
        {
            throw new SceneMakerDocumentException(
                $"Recent session requires an absolute '{WorkspaceStore.ManifestFileName}' path.");
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
    }

    private static RecentSessionDocument MigrateVersionOne(string json)
    {
        var legacy = JsonSerializer.Deserialize<VersionOneRecentSessionDocument>(json, JsonOptions)
            ?? throw new SceneMakerDocumentException("Recent session must not contain JSON null.");
        if (legacy.Schema != Schema || legacy.Version != 1)
            throw new SceneMakerDocumentException("Recent session version 1 is invalid.");
        return new RecentSessionDocument
        {
            Schema = Schema,
            Version = Version,
            WorkspaceManifestPath = legacy.WorkspaceManifestPath,
            SceneRelativePath = legacy.SceneFileName is null
                ? null
                : Path.Combine(WorkspaceStore.ScenesDirectoryName, legacy.SceneFileName),
        };
    }

    private sealed record VersionOneRecentSessionDocument
    {
        public required string Schema { get; init; }
        public required int Version { get; init; }
        public required string WorkspaceManifestPath { get; init; }
        public required string? SceneFileName { get; init; }
    }
}
