using System.Text.Json;
using Sia.Examples.Dom;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private int _lastNativeSequence;
    private int _documentRevision;
    private int _lastInputSequence;
    private readonly Dictionary<int, DomDocument> _domDocuments = [];
    private readonly bool _nativeInput = DomRuntime.Backend.SupportsNativeInput;

    public void SynchronizeNativeInput(bool sourceBoundary = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = _view.TakePendingInput(sourceBoundary);
        if (input.Length > 0) {
            _view.BeginUpdate();
            try {
                foreach (var pending in JsonSerializer.Deserialize<string[]>(input)!) {
                    HandleInput(pending);
                }
            }
            finally {
                _view.EndUpdate();
            }
        }
    }

    private bool HandleQueuedInput(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out _) || sequence <= _lastInputSequence) {
            return HandleInput(arguments);
        }
        if (!HandleInput(arguments)) {
            return false;
        }
        var pending = _view.TakePendingInput(sourceBoundary: false);
        if (pending.Length > 0) {
            foreach (var input in JsonSerializer.Deserialize<string?[]>(pending)!) {
                if (input is null) {
                    break;
                }
                HandleInput(input);
            }
        }
        return true;
    }

    private bool HandleInput(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var input)) {
            return false;
        }
        var separator = input.IndexOf(':');
        if (separator < 0 || input[..separator] is not ("key" or "command" or "text" or "paste" or "mut" or "sel" or "clipboard" or "noop" or "suggestion")) {
            return false;
        }
        if (sequence <= _lastInputSequence) {
            return sequence > 0;
        }
        if (sequence != _lastInputSequence + 1) {
            return false;
        }
        _lastInputSequence = sequence;
        try {
            Route(input[..separator], input[(separator + 1)..]);
            return true;
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleClipboard(string arguments)
    {
        if (!int.TryParse(arguments, out var requestId) || requestId <= 0) {
            return false;
        }
        var state = State.Value;
        var ranges = state.Selection.Ranges.Where(static range => !range.Empty).ToArray();
        var text = ranges.Length == 0
            ? null
            : string.Join('\n', ranges.Select(range => state.Doc.SliceDoc(range.From, range.To)));
        DomRuntime.ResolveEditorClipboard(_cellId, requestId, text);
        return true;
    }

    private bool HandleTextInput(string arguments, bool isolateHistory = false)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            var before = State.Value;
            if (!TryReadTextCommand(
                commandArguments,
                out var nativeSelection,
                out var text)) {
                return false;
            }

            var editBefore = ApplyNativeSelection(before, nativeSelection);
            if (!isolateHistory && TryCommitCompletionCharacter(editBefore, text)) return true;
            var after = editBefore;
            TextCommands.Insert(new(after, state => after = state), text);
            Commit(after, historySelection: editBefore.Selection, historyGroup:
                !isolateHistory && text.Length > 0 && !text.Contains('\n') && !text.Contains('\r')
                    && editBefore.Selection.Ranges.All(static range => range.Empty) ? EditGroup.Typing : EditGroup.None);
            ScheduleCompletion(before, after);
            return true;
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleDomMutation(string arguments)
    {
        if (!TryReadDomMutation(arguments, out var mutation)
            || mutation.Sequence <= _lastNativeSequence
            || !_domDocuments.TryGetValue(mutation.DocumentRevision, out var basis)
            || basis.NativeSequence != mutation.BaseSequence) {
            return false;
        }
        var before = State.Value;
        var nativeSelection = TryResolveSelection(basis.Document, mutation.Before, out var resolved)
            ? resolved
            : EditorSelection.Single(System.Math.Clamp(before.Selection.Main.Head, 0, basis.Document.Length));
        if (!TryCreateDomChange(basis.Document, nativeSelection, mutation, out var change)) {
            return false;
        }
        var (after, nativeDocument, pendingChanges, historySelection) = ReconcileDomMutation(
            before, basis, mutation, nativeSelection, change);
        _view.PrepareReconciliation(mutation.Sequence);
        _lastNativeSequence = mutation.Sequence;
        CancelCompletion();
        _view.RestoreDocumentStructure();
        Commit(after, nativeCommit: true, historySelection: historySelection,
            historyGroup: historySelection.Main.Empty
                ? NativeHistoryGroup(mutation.InputType, nativeSelection, change) : EditGroup.None);
        _domDocuments[mutation.DocumentRevision] = new(
            nativeDocument, pendingChanges, mutation.Sequence, basis.PendingNavigation);
        ScheduleCompletion(before, after);
        return true;
    }

    private static (EditorState State, Text NativeDocument, ChangeSet PendingChanges, EditorSelection HistorySelection) ReconcileDomMutation(
        EditorState before, DomDocument basis, DomMutation mutation, EditorSelection nativeSelection, ChangeSpec? change)
    {
        var nativeChanges = change is { } edit
            ? ChangeSet.Of([edit], basis.Document.Length)
            : ChangeSet.Empty(basis.Document.Length);
        var nativeDocument = nativeChanges.Apply(basis.Document);
        var transformed = ChangeSet.Transform(nativeChanges, basis.PendingChanges);
        var selection = TryResolveSelection(nativeDocument, mutation.After, out var nativeAfter)
            ? nativeAfter
            : nativeSelection.Map(nativeChanges);
        var modelChanges = transformed.Left;
        var pendingChanges = transformed.Right;
        var mappedSelection = selection.Map(pendingChanges, assoc: 1);
        if ((basis.PendingNavigation || mutation.SelectionSource == NativeSelectionSource.Model)
            && mutation.SelectionSource != NativeSelectionSource.Pointer) {
            var replacement = change;
            if (mutation.SelectionSource == NativeSelectionSource.Model && mutation.ReplacesSelection
                && !nativeSelection.Main.Empty) {
                var range = nativeSelection.Main;
                var replacementEnd = range.To + nativeDocument.Length - basis.Document.Length;
                if (replacementEnd >= range.From && replacementEnd <= nativeDocument.Length) {
                    replacement = new(range.From, range.To, nativeDocument.SliceDoc(range.From, replacementEnd));
                }
            }
            if (replacement is { } nativeEdit) {
                var nativeRange = nativeSelection.Main;
                var modelRange = before.Selection.Main;
                var from = mutation.SelectionSource == NativeSelectionSource.Model
                    ? modelRange.From
                    : System.Math.Clamp(
                        basis.PendingChanges.MapPos(nativeEdit.From, 1)!.Value
                            + modelRange.From - basis.PendingChanges.MapPos(nativeRange.From, 1)!.Value,
                        0, before.Doc.Length);
                var to = mutation.SelectionSource == NativeSelectionSource.Model
                    ? modelRange.To
                    : System.Math.Clamp(
                        basis.PendingChanges.MapPos(nativeEdit.To ?? nativeEdit.From, 1)!.Value
                            + modelRange.To - basis.PendingChanges.MapPos(nativeRange.To, 1)!.Value,
                        from, before.Doc.Length);
                var retargeted = ChangeSet.Of([new(from, to, nativeEdit.Insert ?? string.Empty)], before.Doc.Length);
                pendingChanges = ChangeSet.Compose(
                    ChangeSet.Compose(pendingChanges, modelChanges.Invert(before.Doc)), retargeted);
                modelChanges = retargeted;
                var insertedLength = nativeEdit.Insert?.Length ?? 0;
                int MapPoint(int position, int association)
                    => position >= nativeEdit.From && position <= nativeEdit.From + insertedLength
                        ? from + position - nativeEdit.From
                        : pendingChanges.MapPos(position, association)!.Value;
                mappedSelection = mutation.SelectionSource == NativeSelectionSource.Model && selection.Main.Empty
                    ? EditorSelection.Single(from + insertedLength)
                    : EditorSelection.Create([.. selection.Ranges.Select(range =>
                        EditorSelection.Range(MapPoint(range.Anchor, 1), MapPoint(range.Head, range.Empty ? 1 : -1)))],
                        selection.MainIndex);
            }
            else {
                mappedSelection = before.Selection;
            }
        }
        var edits = modelChanges.ToSpecs();
        var historySelection = (basis.PendingNavigation || mutation.SelectionSource == NativeSelectionSource.Model)
            && mutation.SelectionSource != NativeSelectionSource.Pointer
            ? before.Selection : nativeSelection.Map(basis.PendingChanges);
        var after = before.Apply(new() { Changes = edits, Selection = mappedSelection });
        return (after, nativeDocument, pendingChanges, historySelection);
    }

    private static bool TryCreateDomChange(
        Text document,
        EditorSelection beforeSelection,
        DomMutation mutation,
        out ChangeSpec? change)
    {
        if (mutation.LineIndex < 0 || mutation.WindowEnd <= mutation.LineIndex
            || mutation.WindowEnd > document.Lines) {
            change = null;
            return false;
        }
        var windowStart = document.Line(mutation.LineIndex + 1);
        var windowEnd = document.Line(mutation.WindowEnd);
        var windowLength = windowEnd.To - windowStart.From;
        var from = System.Math.Clamp(beforeSelection.Main.From - windowStart.From, 0, windowLength);
        var to = System.Math.Clamp(beforeSelection.Main.To - windowStart.From, from, windowLength);
        var window = document.Slice(windowStart.From, windowEnd.To);
        var difference = TextDiff.FindForSelection(
            window.Layout.SliceDoc(), Text.OfString(mutation.Replacement).Layout.SliceDoc(), window.ToLayoutPosition(from), window.ToLayoutPosition(to),
            mutation.InputType.Contains("Backward", StringComparison.Ordinal)
                ? TextDiff.Preference.End : TextDiff.Preference.None);
        change = difference is { } edit
            ? new(windowStart.From + window.FromLayoutPosition(edit.From), windowStart.From + window.FromLayoutPosition(edit.To),
                edit.Insert.Replace("\n", document.NewlineAt(windowStart.From + window.FromLayoutPosition(edit.From)), StringComparison.Ordinal))
            : null;
        return true;
    }

    private static bool TryReadDomMutation(string arguments, out DomMutation mutation)
    {
        var prefix = arguments.Split(':', 7);
        if (prefix.Length != 7 || prefix[0] != "c"
            || !int.TryParse(prefix[1], out var sequence) || sequence <= 0
            || !int.TryParse(prefix[2], out var revision) || revision < 0
            || !int.TryParse(prefix[3], out var baseSequence) || baseSequence < 0 || baseSequence >= sequence
            || prefix[4] is not ("0" or "1" or "2")
            || prefix[5] is not ("0" or "1")) {
            mutation = default;
            return false;
        }
        var parts = prefix[6].Split(':', 12);
        if (parts.Length != 12
            || !int.TryParse(parts[0], out var lineIndex)
            || !int.TryParse(parts[1], out var windowEnd)
            || !TryReadSelection(parts, 2, out var before)
            || !TryReadSelection(parts, 6, out var after)) {
            mutation = default;
            return false;
        }
        mutation = new(lineIndex, windowEnd, before, after,
            Uri.UnescapeDataString(parts[10]), Uri.UnescapeDataString(parts[11]),
            sequence, revision, baseSequence, prefix[4] switch {
                "1" => NativeSelectionSource.Pointer,
                "2" => NativeSelectionSource.Model,
                _ => NativeSelectionSource.Dom,
            }, prefix[5] == "1");
        return true;
    }

    private static bool TryReadTextCommand(
        string arguments,
        out BrowserSelection selection,
        out string text)
    {
        var parts = arguments.Split(':', 5);
        selection = default;
        if (parts.Length < 5 || !TryReadSelection(parts, 0, out selection)) {
            text = string.Empty;
            return false;
        }
        text = Uri.UnescapeDataString(parts[4]);
        return true;
    }

    private static bool TryReadSelection(
        string[] parts,
        int offset,
        out BrowserSelection selection)
    {
        if (parts.Length < offset + 4
            || !int.TryParse(parts[offset], out var anchorLineIndex)
            || !int.TryParse(parts[offset + 1], out var anchorColumn)
            || !int.TryParse(parts[offset + 2], out var headLineIndex)
            || !int.TryParse(parts[offset + 3], out var headColumn)) {
            selection = default;
            return false;
        }
        selection = new(
            anchorLineIndex,
            anchorColumn,
            headLineIndex,
            headColumn);
        return true;
    }

    private static bool TryResolveSelection(
        Text document,
        BrowserSelection native,
        out EditorSelection selection)
    {
        if (native.AnchorLineIndex < 0
            || native.AnchorLineIndex >= document.Lines
            || native.HeadLineIndex < 0
            || native.HeadLineIndex >= document.Lines
            || native.AnchorColumn < 0
            || native.HeadColumn < 0) {
            selection = null!;
            return false;
        }

        var anchorLine = document.Line(native.AnchorLineIndex + 1);
        var headLine = document.Line(native.HeadLineIndex + 1);
        selection = EditorSelection.Single(
            System.Math.Clamp(
                anchorLine.From + native.AnchorColumn,
                anchorLine.From,
                anchorLine.To),
            System.Math.Clamp(
                headLine.From + native.HeadColumn,
                headLine.From,
                headLine.To));
        return true;
    }

    private static EditorState ApplyNativeSelection(
        EditorState state,
        BrowserSelection native)
    {
        if (!TryResolveSelection(state.Doc, native, out var selection)
            || selection.Main.Anchor == state.Selection.Main.Anchor
                && selection.Main.Head == state.Selection.Main.Head) {
            return state;
        }
        return state.Apply(new() { Selection = selection });
    }

    private bool HandleSelection(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length is not (4 or 6) || !TryReadSelection(parts, 0, out var native)) {
            return false;
        }

        var state = State.Value;
        var document = state.Doc;
        ChangeSet? pendingChanges = null;
        if (parts.Length == 6) {
            if (!int.TryParse(parts[4], out var revision)
                || !int.TryParse(parts[5], out var nativeSequence)
                || !_domDocuments.TryGetValue(revision, out var basis)
                || basis.NativeSequence != nativeSequence) {
                return false;
            }
            document = basis.Document;
            pendingChanges = basis.PendingChanges;
        }
        if (!TryResolveSelection(document, native, out var selection)) {
            return false;
        }
        if (pendingChanges is not null) {
            selection = selection.Map(pendingChanges, assoc: 1);
        }
        foreach (var revision in _domDocuments.Keys.ToArray()) {
            _domDocuments[revision] = _domDocuments[revision] with { PendingNavigation = false };
        }
        _view.CancelSelectionReveal();
        if (_view.ReadScrollPosition() is { } position) {
            _lastScrollTop = position.Top;
            ApplyViewport(_viewState.ComputeViewport(position.Top, position.Top + _lastClientHeight));
        }
        if (selection.Eq(state.Selection, true)) {
            _world.FlushReactive();
            return false;
        }

        if (pendingChanges is null || pendingChanges.IsEmpty) {
            _view.SuppressNextSelectionUpdate();
        }
        Commit(state.Apply(new() { Selection = selection }), revealSelection: false);
        CancelCompletion();
        return true;
    }

    private static bool TryReadCommand(
        string arguments,
        out int sequence,
        out string commandArguments)
    {
        var separator = arguments.IndexOf(':');
        sequence = 0;
        if (separator < 0 || !int.TryParse(arguments[..separator], out sequence)) {
            commandArguments = string.Empty;
            return false;
        }

        commandArguments = arguments[(separator + 1)..];
        return true;
    }

    private enum NativeSelectionSource
    {
        Dom,
        Pointer,
        Model,
    }

    private readonly record struct BrowserSelection(
        int AnchorLineIndex,
        int AnchorColumn,
        int HeadLineIndex,
        int HeadColumn);

    private readonly record struct DomMutation(
        int LineIndex,
        int WindowEnd,
        BrowserSelection Before,
        BrowserSelection After,
        string InputType,
        string Replacement,
        int Sequence,
        int DocumentRevision,
        int BaseSequence,
        NativeSelectionSource SelectionSource,
        bool ReplacesSelection);

    private readonly record struct DomDocument(
        Text Document, ChangeSet PendingChanges, int NativeSequence, bool PendingNavigation = false);
}
