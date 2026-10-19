namespace Sia.Examples.Editor;

internal sealed record WorkspaceEditBuffer(string Source, EditorMemento Position, bool Editable = true);
internal sealed record WorkspaceBufferChange(string DocumentId, WorkspaceEditBuffer Before, WorkspaceEditBuffer After, ChangeSpec[] Changes);
internal sealed class WorkspaceEditTransaction(Func<WorkspaceEditTransaction, bool, bool> replay)
{
    internal bool Replay(bool redo) => replay(this, redo);
}

internal sealed class EditorWorkspaceEdits(
    Func<RenamePlan, IReadOnlyDictionary<string, WorkspaceEditBuffer>> capture,
    Action<IReadOnlyList<WorkspaceBufferChange>> commit, Action<string> report) : IDisposable
{
    private bool _disposed;
    private bool _busy;

    internal bool Apply(RenamePlan plan)
    {
        if (_disposed || _busy || !plan.CanApply) return false;
        _busy = true;
        try {
            var current = capture(plan);
            if (current.Count != plan.Context.Count || plan.Context.Any(source => !current.TryGetValue(source.Id, out var buffer)
                || buffer.Source != source.Source)) return Reject("The workspace changed. Preview the rename again.");
            var changes = new List<WorkspaceBufferChange>();
            var positions = new List<(string Id, EditorMemento Before, EditorMemento After)>();
            var scope = plan with { Documents = [], Context = plan.Context.Select(source => source with { Source = "" }).ToArray() };
            var transaction = new WorkspaceEditTransaction((step, redo) => Replay(scope, positions, step, redo));
            foreach (var document in plan.Documents) {
                if (!current.TryGetValue(document.DocumentId, out var buffer) || !buffer.Editable || buffer.Source != document.Before)
                    return Reject("A source document changed or is read only. Preview the rename again.");
                var text = Text.OfString(buffer.Source);
                var forward = ChangeSet.Of(document.Changes, text.Length);
                var source = forward.Apply(text).SliceDoc();
                var beforeSelection = buffer.Position.Selection.Ranges.All(range => range.To <= text.Length) ? buffer.Position.Selection
                    : EditorSelection.Create([.. buffer.Position.Selection.Ranges.Select(range => range.Clamp(text.Length))], buffer.Position.Selection.MainIndex);
                var afterSelection = beforeSelection.Map(forward);
                long size = 0;
                forward.IterateChangedRanges((a, b, c, d) => size += (long)b - a + d - c);
                var edit = new BrowserEditorHost.HistoryEdit(forward, forward.Invert(text), beforeSelection, afterSelection, size, transaction);
                var history = CurrentHistory(buffer);
                var undo = history.Undo.Add(edit);
                var total = undo.Sum(static item => item.Size);
                while (undo.Length > 1 && (undo.Length > BrowserEditorHost.k_HistoryLimit || total > BrowserEditorHost.k_HistoryTextLimit)) {
                    total -= undo[0].Size; undo = undo.RemoveAt(0);
                }
                var position = buffer.Position with { Selection = afterSelection, History = new(source, undo, []) };
                changes.Add(new(document.DocumentId, buffer, new(source, position, buffer.Editable), document.Changes));
                positions.Add((document.DocumentId, buffer.Position with { History = null }, position with { History = null }));
            }
            return Commit(changes);
        }
        finally { _busy = false; }
    }

    private bool Replay(RenamePlan scope, IReadOnlyList<(string Id, EditorMemento Before, EditorMemento After)> positions,
        WorkspaceEditTransaction transaction, bool redo)
    {
        if (_disposed || _busy) return false;
        _busy = true;
        try {
            var current = capture(scope);
            var changes = new List<WorkspaceBufferChange>(positions.Count);
            foreach (var document in positions) {
                if (!current.TryGetValue(document.Id, out var buffer) || !buffer.Editable)
                    return Reject("Undo newer changes in the affected files before this workspace edit.");
                var history = CurrentHistory(buffer);
                var from = redo ? history.Redo : history.Undo;
                var to = redo ? history.Undo : history.Redo;
                if (from.IsEmpty || !ReferenceEquals(from[^1].WorkspaceEdit, transaction))
                    return Reject("Undo newer changes in the affected files before this workspace edit.");
                var edit = from[^1];
                var operation = redo ? edit.Forward : edit.Backward;
                if (operation.Length != buffer.Source.Length) return Reject("The source history no longer matches this workspace edit.");
                var specs = operation.ToSpecs();
                var source = operation.Apply(Text.OfString(buffer.Source)).SliceDoc();
                var position = (redo ? document.After : document.Before) with {
                    Selection = redo ? edit.After : edit.Before,
                    History = new(source, redo ? to.Add(edit) : from.RemoveAt(from.Length - 1),
                        redo ? from.RemoveAt(from.Length - 1) : to.Add(edit))
                };
                changes.Add(new(document.Id, buffer, new(source, position, buffer.Editable), specs));
            }
            return Commit(changes);
        }
        finally { _busy = false; }
    }

    private bool Commit(IReadOnlyList<WorkspaceBufferChange> changes)
    {
        try { commit(changes); return true; }
        catch (Exception failure) {
            var reverse = changes.Reverse().Select(change => new WorkspaceBufferChange(change.DocumentId, change.After, change.Before,
                ChangeSet.Of(change.Changes, change.Before.Source.Length).Invert(Text.OfString(change.Before.Source)).ToSpecs())).ToArray();
            try { commit(reverse); }
            catch (Exception rollback) { throw new AggregateException("The workspace edit and its rollback both failed.", failure, rollback); }
            throw;
        }
    }

    private static BrowserEditorHost.HistorySnapshot CurrentHistory(WorkspaceEditBuffer buffer)
        => buffer.Position.History is { } history && history.Source == buffer.Source ? history : new(buffer.Source, [], []);
    private bool Reject(string message) { report(message); return false; }
    public void Dispose() => _disposed = true;
}
