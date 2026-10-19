using System.Collections.Immutable;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    internal const int k_HistoryLimit = 128;
    internal const long k_HistoryTextLimit = 2_000_000;
    private readonly List<HistoryEdit> _undoHistory = [];
    private readonly List<HistoryEdit> _redoHistory = [];
    private EditGroup _historyGroup;
    private long _lastHistoryEdit;

    private HistorySnapshot? CaptureHistory()
        => _undoHistory.Count == 0 && _redoHistory.Count == 0 ? null
            : new(Source, [.. _undoHistory], [.. _redoHistory]);

    private void RestoreHistory(HistorySnapshot? snapshot)
    {
        EndHistoryGroup();
        if (snapshot is null) return;
        _undoHistory.Clear();
        _redoHistory.Clear();
        if (snapshot.Source != Source) return;
        _undoHistory.AddRange(snapshot.Undo);
        _redoHistory.AddRange(snapshot.Redo);
    }

    private void ClearHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        EndHistoryGroup();
    }

    internal void ApplyWorkspaceChange(WorkspaceBufferChange change)
    {
        if (Source != change.Before.Source && Source != change.After.Source)
            throw new InvalidOperationException("The workspace edit no longer matches the mounted editor.");
        if (Source != change.After.Source) {
            var state = State.Value.Apply(new() { Changes = change.Changes, Selection = change.After.Position.Selection });
            if (state.Doc.SliceDoc() != change.After.Source) throw new InvalidOperationException("The workspace edit produced inconsistent text.");
            Commit(state, recordHistory: false, revealSelection: false);
        }
        ClearHistory();
        RestorePosition(change.After.Position);
    }

    private static EditGroup NativeHistoryGroup(
        string inputType, EditorSelection selection, ChangeSpec? change)
    {
        if (!selection.Main.Empty || change is not { } edit) return EditGroup.None;
        var from = edit.From;
        var to = edit.To ?? from;
        var text = edit.Insert ?? string.Empty;
        var head = selection.Main.Head;
        return inputType switch {
            "insertText" when from == to && from == head && text.Length > 0
                && !text.Contains('\n') && !text.Contains('\r') => EditGroup.Typing,
            "deleteContentBackward" when from < to && to == head && text.Length == 0 => EditGroup.Backward,
            "deleteContentForward" when from < to && from == head && text.Length == 0 => EditGroup.Forward,
            _ => EditGroup.None,
        };
    }

    private void EndHistoryGroup() => _historyGroup = EditGroup.None;

    private void RecordHistory(ChangeSet changes, EditorState before, EditorState after,
        EditorSelection selection, EditGroup group)
    {
        var now = Environment.TickCount64;
        long size = 0;
        changes.IterateChangedRanges((a, b, c, d) => size += (long)b - a + d - c);
        var edit = new HistoryEdit(changes, changes.Invert(before.Doc), selection, after.Selection, size);
        if (group != EditGroup.None && group == _historyGroup
            && now - _lastHistoryEdit <= 750 && _undoHistory.Count > 0 && _redoHistory.Count == 0
            && _undoHistory[^1].After.Eq(selection, true)) {
            var previous = _undoHistory[^1];
            edit = new(ChangeSet.Compose(previous.Forward, changes),
                ChangeSet.Compose(edit.Backward, previous.Backward), previous.Before, after.Selection,
                previous.Size + size);
            _undoHistory[^1] = edit;
        }
        else {
            _undoHistory.Add(edit);
        }
        _redoHistory.Clear();
        _historyGroup = group;
        _lastHistoryEdit = now;
        var totalSize = _undoHistory.Sum(static item => item.Size);
        while (_undoHistory.Count > 1 && (_undoHistory.Count > k_HistoryLimit || totalSize > k_HistoryTextLimit)) {
            totalSize -= _undoHistory[0].Size;
            _undoHistory.RemoveAt(0);
        }
    }

    private bool ReplayHistory(bool redo)
    {
        CancelCompletion();
        EndHistoryGroup();
        var source = redo ? _redoHistory : _undoHistory;
        if (source.Count == 0) return true;
        var destination = redo ? _undoHistory : _redoHistory;
        var edit = source[^1];
        if (edit.WorkspaceEdit is { } workspaceEdit) { workspaceEdit.Replay(redo); return true; }
        var before = State.Value;
        var after = before.Apply(new() {
            Changes = (redo ? edit.Forward : edit.Backward).ToSpecs(),
            Selection = redo ? edit.After : edit.Before,
        });
        source.RemoveAt(source.Count - 1);
        destination.Add(edit);
        try {
            Commit(after, recordHistory: false);
        }
        catch {
            if (!ReferenceEquals(State.Value.Doc, after.Doc)) {
                destination.RemoveAt(destination.Count - 1);
                source.Add(edit);
            }
            throw;
        }
        return true;
    }

    internal sealed record HistorySnapshot(string Source,
        ImmutableArray<HistoryEdit> Undo, ImmutableArray<HistoryEdit> Redo);

    internal sealed record HistoryEdit(ChangeSet Forward, ChangeSet Backward,
        EditorSelection Before, EditorSelection After, long Size, WorkspaceEditTransaction? WorkspaceEdit = null);

    private enum EditGroup { None, Typing, Backward, Forward }
}
