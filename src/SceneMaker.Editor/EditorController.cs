using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// What a command did, in words the interface can show. Whether a failure
/// belongs in the status line or in a dialog is a presentation decision and
/// stays with the caller; the controller only says what happened.
/// </summary>
public sealed record EditorReport(string Message, bool Succeeded)
{
    /// <summary>Nothing worth saying happened. The status line keeps what it had.</summary>
    public static EditorReport Silent { get; } = new(string.Empty, true);

    public static EditorReport Ok(string message) => new(message, true);
    public static EditorReport Failed(string message) => new(message, false);
    public bool HasMessage => Message.Length > 0;
}

/// <summary>
/// What one edit did. <see cref="Before"/> and <see cref="After"/> are set only
/// when the document actually changed, which is what a caller needs in order to
/// refresh anything that caches the Scene.
/// </summary>
public sealed record SceneEdit(EditorReport Report, SceneDocument? Before, SceneDocument? After)
{
    public static SceneEdit Nothing { get; } = new(EditorReport.Silent, null, null);

    public static SceneEdit Refused(string message) => new(EditorReport.Failed(message), null, null);

    public bool Changed => Before is not null && After is not null;
}

/// <summary>
/// The editor's Workspace lifecycle, without Godot. Commands take plain
/// absolute paths, change state only when they succeed, and answer with an
/// <see cref="EditorReport"/> instead of touching an interface.
///
/// It owns the open Workspace, the open Scene and its edit history. What stays
/// in SceneMakerMain is drawing, layout and the autosave timer - Godot things.
/// </summary>
public sealed class EditorController
{
    private SceneEditHistory? _history;

    /// <summary>The open Workspace, or null when none is open.</summary>
    public WorkspaceSession? Session { get; private set; }

    /// <summary>The open Scene, or null when none is open.</summary>
    public LoadedScene? Scene { get; private set; }

    public SceneDocument? Document => Scene?.Document;
    public bool CanUndo => _history?.CanUndo ?? false;
    public bool CanRedo => _history?.CanRedo ?? false;

    /// <summary>Null when no Scene is open, so the interface can say nothing at all.</summary>
    public bool? IsDirty => _history?.IsDirty;

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
    /// checked against the open Scene in full before anything is
    /// written, so a profile set that would invalidate the open Scene changes
    /// nothing at all.
    /// </summary>
    public EditorReport SaveAssetProfiles(IEnumerable<WorkspaceAssetProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (Session is not { } session)
            return EditorReport.Failed("No Workspace is open.");
        try
        {
            var candidate = session.WithAssetProfiles(profiles);
            if (Document is { } openScene)
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

    /// <summary>Closes the open Workspace and Scene without touching anything on disk.</summary>
    public void CloseWorkspace()
    {
        Session = null;
        CloseScene();
    }

    /// <summary>Drops the open Scene. Unsaved edits are lost; save first.</summary>
    public void CloseScene()
    {
        Scene = null;
        _history = null;
    }

    /// <summary>Creates a Scene Instance in the open Workspace and opens it.</summary>
    public EditorReport CreateInstance(string sceneId, int widthCells, int heightCells) =>
        CreateScene(
            "Scene Instance",
            workspace => SceneStore.CreateInstance(workspace, sceneId, widthCells, heightCells));

    /// <summary>Creates a Scene Template in the open Workspace and opens it.</summary>
    public EditorReport CreateTemplate(
        string sceneId,
        int widthCells,
        int heightCells,
        int groupNumber,
        int insertionAnchorX,
        int insertionAnchorY) =>
        CreateScene(
            "Scene Template",
            workspace => SceneStore.CreateTemplate(
                workspace,
                sceneId,
                widthCells,
                heightCells,
                groupNumber,
                insertionAnchorX,
                insertionAnchorY));

    /// <summary>
    /// Opens a Scene out of the current Workspace, after checking that it still
    /// fits the Workspace grid and that every Asset it names is enabled.
    /// </summary>
    public EditorReport OpenScene(string absolutePath)
    {
        if (Session is not { } session) return EditorReport.Failed("No Workspace is open.");
        var pending = SaveScene();
        if (!pending.Succeeded) return pending;
        try
        {
            var loaded = SceneStore.Load(session.Workspace, absolutePath);
            DocumentValidation.ValidateGrid(loaded.Document, session.Metrics);
            TerrainEditing.ValidateAssetReferences(loaded.Document, session.TerrainAssets);
            PropEditing.ValidateAssetReferences(loaded.Document, session.PropAssets);
            Open(loaded);
            return EditorReport.Ok($"Loaded Scene '{loaded.Document.SceneId}'.");
        }
        catch (Exception exception) when (IsDocumentFailure(exception))
        {
            return EditorReport.Failed(exception.Message);
        }
    }

    /// <summary>
    /// Writes the Scene to its Workspace if it differs from the stored copy.
    /// Safe to call at any time; it says nothing when there is nothing to write.
    /// </summary>
    public EditorReport SaveScene()
    {
        if (Session is not { } session || Scene is not { } scene || _history is null)
            return EditorReport.Silent;
        if (!_history.IsDirty) return EditorReport.Silent;
        try
        {
            SceneStore.Save(session.Workspace, scene);
            _history.MarkSaved();
            return EditorReport.Silent;
        }
        catch (Exception exception) when (IsDocumentFailure(exception))
        {
            return EditorReport.Failed($"Save blocked: {exception.Message}");
        }
    }

    /// <summary>
    /// Applies one edit and records it for undo. Every document change in the
    /// editor goes through here. A stroke key collapses a whole drag into one
    /// undo step.
    /// </summary>
    public SceneEdit Apply(ToolOutcome.Edit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (Session is null || Scene is not { } scene || _history is null) return SceneEdit.Nothing;
        var before = scene.Document;
        SceneDocument after;
        try
        {
            after = edit.Apply(before);
        }
        catch (Exception exception)
            when (exception is SceneMakerDocumentException or OverflowException)
        {
            return SceneEdit.Refused($"{edit.Name} blocked: {exception.Message}");
        }

        if (ReferenceEquals(after, before))
        {
            return edit.NoChangeText is null
                ? SceneEdit.Nothing
                : new SceneEdit(EditorReport.Ok(edit.NoChangeText), null, null);
        }

        _history.Push(after, edit.StrokeKey);
        Scene = scene with { Document = after };
        var message = edit.Describe is null ? EditorReport.Silent : EditorReport.Ok(edit.Describe(before, after));
        return new SceneEdit(message, before, after);
    }

    /// <summary>Ends the current stroke, so the next edit starts a new undo step.</summary>
    public void EndEditStroke() => _history?.BreakStroke();

    public EditorReport Undo() => Step(undo: true);

    public EditorReport Redo() => Step(undo: false);

    private EditorReport Step(bool undo)
    {
        var name = undo ? "undo" : "redo";
        if (_history is null || Scene is not { } scene || (undo ? !_history.CanUndo : !_history.CanRedo))
            return EditorReport.Failed($"Nothing to {name}.");
        Scene = scene with { Document = undo ? _history.Undo() : _history.Redo() };
        var depth = undo ? _history.UndoDepth : _history.RedoDepth;
        return EditorReport.Ok(
            $"{char.ToUpperInvariant(name[0])}{name[1..]}. {depth} further step{(depth == 1 ? string.Empty : "s")} available.");
    }

    /// <summary>
    /// Adopts a Scene that was written to disk behind the editor's back, which
    /// happens when the Templates popup changes the group of the Scene that is
    /// currently open. Returns false when a different Scene is open, in which
    /// case there is nothing to catch up with.
    /// </summary>
    public bool AdoptStoredScene(LoadedScene stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (Scene is null || !string.Equals(Scene.FilePath, stored.FilePath, StringComparison.Ordinal))
            return false;
        // The stored copy is the new baseline, so the undo history starts over.
        Open(stored);
        return true;
    }

    /// <summary>Records the open Workspace and Scene for the next start.</summary>
    public void SaveRecentSession(string recentSessionPath)
    {
        if (Session is not { } session) return;
        RecentSessionStore.Save(recentSessionPath, session.Workspace, Scene);
    }

    /// <summary>
    /// Reopens whatever was open when the editor last closed. Anything that no
    /// longer fits - a moved Workspace, a Scene naming an Asset that has since
    /// been disabled - leaves the editor closed rather than half-open, and the
    /// caller should discard the recorded session.
    /// </summary>
    public EditorReport RestoreRecentSession(string recentSessionPath)
    {
        try
        {
            var recent = RecentSessionStore.Load(recentSessionPath);
            if (recent is null) return EditorReport.Silent;
            if (!OpenWorkspaceDirectory(recent.WorkspaceDirectoryPath).Succeeded)
                throw new SceneMakerDocumentException("The recorded Workspace could not be opened.");
            var session = Session!;
            if (recent.SceneRelativePath is null)
            {
                CloseScene();
                return EditorReport.Ok($"Restored Workspace '{session.WorkspaceKey}'.");
            }

            var loaded = SceneStore.Load(
                session.Workspace,
                Path.Combine(session.DirectoryPath, recent.SceneRelativePath));
            DocumentValidation.ValidateGrid(loaded.Document, session.Metrics);
            TerrainEditing.ValidateAssetReferences(loaded.Document, session.TerrainAssets);
            PropEditing.ValidateAssetReferences(loaded.Document, session.PropAssets);
            Open(loaded);
            return EditorReport.Ok(
                $"Restored Workspace '{session.WorkspaceKey}' and Scene '{loaded.Document.SceneId}'.");
        }
        catch (Exception exception) when (IsDocumentFailure(exception))
        {
            CloseWorkspace();
            return EditorReport.Failed("No compatible recent session was restored.");
        }
    }

    private EditorReport CreateScene(string kind, Func<LoadedWorkspace, LoadedScene> create)
    {
        if (Session is not { } session) return EditorReport.Failed("No Workspace is open.");
        var pending = SaveScene();
        if (!pending.Succeeded) return pending;
        try
        {
            var created = create(session.Workspace);
            Open(created);
            return EditorReport.Ok($"Created {kind} '{created.Document.SceneId}'.");
        }
        catch (Exception exception)
            when (IsDocumentFailure(exception) || exception is ArgumentException)
        {
            return EditorReport.Failed(exception.Message);
        }
    }

    private void Open(LoadedScene scene)
    {
        Scene = scene;
        _history = new SceneEditHistory(scene.Document);
    }

    private static EditorReport Blocked(string reason) =>
        EditorReport.Failed($"Workspace load blocked: {reason}");

    private static bool IsDocumentFailure(Exception exception) =>
        exception is SceneMakerDocumentException
            or IOException
            or UnauthorizedAccessException
            or OverflowException;
}
