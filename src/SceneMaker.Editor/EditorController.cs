using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// What a command did, in words the interface can show. Whether a failure
/// belongs in the status line or in a dialog is a presentation decision and
/// stays with the caller; the controller only says what happened.
/// </summary>
public sealed record EditorReport(string Message, bool Succeeded)
{
    public static EditorReport Ok(string message) => new(message, true);
    public static EditorReport Failed(string message) => new(message, false);
}

/// <summary>
/// The editor's Workspace lifecycle, without Godot. Commands take plain
/// absolute paths, change state only when they succeed, and answer with an
/// <see cref="EditorReport"/> instead of touching an interface.
///
/// The Scene and its edit history still live in SceneMakerMain; this type owns
/// the Workspace only.
/// </summary>
public sealed class EditorController
{
    /// <summary>The open Workspace, or null when none is open.</summary>
    public WorkspaceSession? Session { get; private set; }

    /// <summary>
    /// The last Workspace directory this controller touched, whether or not it
    /// could be opened. A Workspace that was just created has no session yet,
    /// and the file dialogs should still start there.
    /// </summary>
    public string? LastWorkspaceDirectory { get; private set; }

    /// <summary>
    /// Opens whatever the user picked: a Workspace directory, or the
    /// config.json inside one. A failure leaves the open Workspace untouched.
    /// </summary>
    public EditorReport OpenWorkspaceAt(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (Directory.Exists(absolutePath))
        {
            var configPath = Path.Combine(absolutePath, WorkspaceConfigurationStore.FileName);
            return File.Exists(configPath)
                ? OpenWorkspaceDirectory(absolutePath)
                : Blocked($"no {WorkspaceConfigurationStore.FileName} in the selected folder.");
        }

        if (!File.Exists(absolutePath))
            return Blocked("selected path does not exist.");
        if (!string.Equals(
                Path.GetFileName(absolutePath),
                WorkspaceConfigurationStore.FileName,
                StringComparison.Ordinal))
        {
            return Blocked("select a workspace config.json file.");
        }

        var workspaceDirectory = Path.GetDirectoryName(absolutePath);
        return workspaceDirectory is null
            ? Blocked("Workspace config requires a parent directory.")
            : OpenWorkspaceDirectory(workspaceDirectory);
    }

    /// <summary>
    /// Opens the Workspace in <paramref name="workspaceDirectory"/>. The
    /// session is built completely before it is adopted, so a load that fails
    /// leaves the Workspace currently open exactly as it was.
    /// </summary>
    public EditorReport OpenWorkspaceDirectory(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        try
        {
            var session = WorkspaceSession.Load(workspaceDirectory);
            Session = session;
            LastWorkspaceDirectory = session.DirectoryPath;
            return EditorReport.Ok(
                $"Loaded Workspace '{session.WorkspaceKey}'. Load a Scene to start editing.");
        }
        catch (Exception exception) when (IsDocumentFailure(exception))
        {
            return Blocked(exception.Message);
        }
    }

    /// <summary>
    /// Creates a Workspace but does not open it: a new Workspace has no
    /// PolyTools import yet, so there is nothing to build a session from. The
    /// editor stays closed until the import is synchronized and the Workspace
    /// is loaded.
    /// </summary>
    public EditorReport CreateWorkspace(string parentDirectory, string workspaceId)
    {
        try
        {
            var created = WorkspaceStore.Create(parentDirectory, workspaceId);
            Session = null;
            LastWorkspaceDirectory = created.DirectoryPath;
            return EditorReport.Ok(
                $"Created Workspace '{created.WorkspaceKey}'. Synchronize its PolyTools import before editing.");
        }
        catch (Exception exception)
            when (IsDocumentFailure(exception) || exception is ArgumentException)
        {
            return EditorReport.Failed(exception.Message);
        }
    }

    /// <summary>
    /// Persists a new set of Asset profiles. The candidate session is built and
    /// checked against <paramref name="openScene"/> in full before anything is
    /// written, so a profile set that would invalidate the open Scene changes
    /// nothing at all.
    /// </summary>
    public EditorReport SaveAssetProfiles(
        IEnumerable<WorkspaceAssetProfile> profiles,
        SceneDocument? openScene)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (Session is not { } session)
            return EditorReport.Failed("No Workspace is open.");
        try
        {
            var candidate = session.WithAssetProfiles(profiles);
            if (openScene is not null)
            {
                DocumentValidation.ValidateGrid(openScene, candidate.Metrics);
                TerrainEditing.ValidateAssetReferences(openScene, candidate.TerrainAssets);
                PropEditing.ValidateAssetReferences(openScene, candidate.PropAssets);
            }
            WorkspaceConfigurationStore.Save(candidate.DirectoryPath, candidate.Configuration);
            Session = candidate;
            return EditorReport.Ok("Saved Workspace asset profiles.");
        }
        catch (Exception exception) when (IsDocumentFailure(exception))
        {
            return EditorReport.Failed(exception.Message);
        }
    }

    /// <summary>Closes the open Workspace without touching anything on disk.</summary>
    public void CloseWorkspace() => Session = null;

    private static EditorReport Blocked(string reason) =>
        EditorReport.Failed($"Workspace load blocked: {reason}");

    private static bool IsDocumentFailure(Exception exception) =>
        exception is SceneMakerDocumentException
            or IOException
            or UnauthorizedAccessException
            or OverflowException;
}
