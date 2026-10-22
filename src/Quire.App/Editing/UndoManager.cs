using Quire.Models;
using Quire.Storage;

namespace Quire.Editing;

/// <summary>
/// Document-wide undo and redo using snapshots of the manifest (assets are immutable and never
/// removed from the store while the document is open, so they need no snapshot).
///
/// Every burst of changes that happens in one turn of the UI (for example a paste that inserts
/// several elements) becomes one step. Consecutive typing in the same field within
/// <see cref="TypingMergeWindow"/> is merged into one step too.
/// </summary>
public sealed class UndoManager
{
    public static readonly TimeSpan TypingMergeWindow = TimeSpan.FromSeconds(1.5);
    private const int MaxSteps = 500;

    private readonly QuireDocument _document;
    private readonly Action<Action> _whenIdle;
    private readonly Func<DateTime> _clock;
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();

    private string _baseline;
    private bool _batchOpen;
    private bool _restoring;
    private object? _lastTypingTarget;
    private string? _lastTypingProperty;
    private DateTime _lastTypingTime;

    /// <param name="whenIdle">Runs an action once the current burst of changes is over (the UI's idle callback).</param>
    /// <param name="clock">Current time, replaceable in tests.</param>
    public UndoManager(QuireDocument document, Action<Action> whenIdle, Func<DateTime>? clock = null)
    {
        _document = document;
        _whenIdle = whenIdle;
        _clock = clock ?? (() => DateTime.UtcNow);
        _baseline = Snapshot();
    }

    public bool CanUndo => _undo.Count > 0 || _batchOpen;

    public bool CanRedo => _redo.Count > 0 && !_batchOpen;

    /// <summary>Raised after an undo or redo has changed the document.</summary>
    public event EventHandler? Restored;

    /// <summary>Records that the document is about to change, or just changed. Call for every change notification.</summary>
    public void OnChanged(object source, string? property, bool isTyping)
    {
        if (_restoring)
            return;

        var now = _clock();
        var mergesWithTyping = isTyping
            && ReferenceEquals(source, _lastTypingTarget)
            && property == _lastTypingProperty
            && now - _lastTypingTime <= TypingMergeWindow;

        _lastTypingTarget = isTyping ? source : null;
        _lastTypingProperty = isTyping ? property : null;
        _lastTypingTime = now;

        if (_batchOpen)
            return;

        if (!mergesWithTyping || _undo.Count == 0)
            Push(_baseline);
        _redo.Clear();
        _batchOpen = true;
        _whenIdle(CloseBatch);
    }

    /// <summary>Ends the current step now instead of waiting for idle. Used before saving and in tests.</summary>
    public void CloseBatch()
    {
        if (!_batchOpen)
            return;
        _batchOpen = false;
        _baseline = Snapshot();
    }

    public bool Undo()
    {
        CloseBatch();
        if (_undo.Last is not { } node)
            return false;
        _undo.RemoveLast();
        _redo.Push(_baseline);
        Restore(node.Value);
        return true;
    }

    public bool Redo()
    {
        CloseBatch();
        if (_redo.Count == 0)
            return false;
        Push(_baseline);
        Restore(_redo.Pop());
        return true;
    }

    /// <summary>The manifest JSON of the current state. Comparing it with the saved state tells whether the document is dirty.</summary>
    public string CurrentState
    {
        get
        {
            CloseBatch();
            return _baseline;
        }
    }

    private void Push(string state)
    {
        _undo.AddLast(state);
        if (_undo.Count > MaxSteps)
            _undo.RemoveFirst();
    }

    private void Restore(string state)
    {
        _restoring = true;
        try
        {
            DocumentReconciler.Apply(_document, DocumentSerializer.FromJson(state));
        }
        finally
        {
            _restoring = false;
        }
        _baseline = state;
        _lastTypingTarget = null;
        Restored?.Invoke(this, EventArgs.Empty);
    }

    private string Snapshot() => DocumentSerializer.ToJson(_document);
}
