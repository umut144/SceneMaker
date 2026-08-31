using SceneMaker.Core;

namespace SceneMaker.Editor;

/// <summary>
/// Bounded undo/redo history over immutable Scene documents, plus the dirty
/// flag that decides whether the Workspace copy still matches what is being
/// edited.
///
/// Editing operations are pure <c>SceneDocument -&gt; SceneDocument</c>
/// functions, so a history entry is simply the document after one step and
/// undo is a move along the list rather than an inverse operation.
///
/// Continuous input coalesces: pushing repeatedly under the same stroke key
/// replaces the top entry instead of adding one, so dragging the pencil across
/// forty cells stays a single undo step. <see cref="BreakStroke"/> ends the
/// open stroke, which the editor calls when the pointer is released.
/// </summary>
public sealed class SceneEditHistory
{
    public const int DefaultCapacity = 128;

    private readonly List<SceneDocument> _entries;
    private readonly int _capacity;
    private int _index;
    private int _savedIndex;
    private string? _openStrokeKey;

    public SceneEditHistory(SceneDocument document, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity), "History needs room for at least two states.");
        _capacity = capacity;
        _entries = [document];
        _index = 0;
        _savedIndex = 0;
    }

    /// <summary>The document currently being edited.</summary>
    public SceneDocument Current => _entries[_index];

    public bool CanUndo => _index > 0;
    public bool CanRedo => _index < _entries.Count - 1;

    /// <summary>
    /// True while <see cref="Current"/> differs from the state last written to
    /// the Workspace. A saved state that has been trimmed out of the history
    /// counts as dirty, because the editor can no longer prove they match.
    /// </summary>
    public bool IsDirty => _index != _savedIndex;

    public int UndoDepth => _index;
    public int RedoDepth => _entries.Count - 1 - _index;

    /// <summary>
    /// Records one edit. A non-null <paramref name="strokeKey"/> that matches
    /// the open stroke replaces the top entry rather than adding one.
    /// </summary>
    public void Push(SceneDocument document, string? strokeKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (strokeKey is not null
            && string.Equals(strokeKey, _openStrokeKey, StringComparison.Ordinal)
            && _index > 0)
        {
            _entries[_index] = document;
            return;
        }

        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(document);
        _index = _entries.Count - 1;
        _openStrokeKey = strokeKey;
        Trim();
    }

    /// <summary>Ends the open stroke, so the next push starts a new entry.</summary>
    public void BreakStroke() => _openStrokeKey = null;

    public SceneDocument Undo()
    {
        if (!CanUndo)
            throw new InvalidOperationException("There is nothing to undo.");
        _index--;
        _openStrokeKey = null;
        return Current;
    }

    public SceneDocument Redo()
    {
        if (!CanRedo)
            throw new InvalidOperationException("There is nothing to redo.");
        _index++;
        _openStrokeKey = null;
        return Current;
    }

    /// <summary>Records that <see cref="Current"/> is now the Workspace copy.</summary>
    public void MarkSaved() => _savedIndex = _index;

    /// <summary>Starts over from <paramref name="document"/>, which counts as saved.</summary>
    public void Reset(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _entries.Clear();
        _entries.Add(document);
        _index = 0;
        _savedIndex = 0;
        _openStrokeKey = null;
    }

    private void Trim()
    {
        var excess = _entries.Count - _capacity;
        if (excess <= 0) return;
        _entries.RemoveRange(0, excess);
        _index -= excess;
        _savedIndex -= excess;
    }
}
