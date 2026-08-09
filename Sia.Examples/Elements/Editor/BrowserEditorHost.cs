using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorHost : IDisposable
{
    private const int _completionDelayMilliseconds = 80;
    private const int _maximumRenderedCompletions = 20;

    private readonly string _cellId;
    private readonly World _world;
    private readonly BrowserEditorView _view;
    private readonly CSharpCompletionProvider _completionProvider;
    private readonly ReactiveMount<EditorViewProps> _mount;
    private readonly List<(DomElement Element, string Label)> _completionItems = [];

    private State<EditorState>? _state;
    private CompletionResult? _completion;
    private CompletionResult? _completionSource;
    private DomElement? _completionPopup;
    private CancellationTokenSource? _completionCancellation;
    private int _completionIndex;
    private int _completionGeneration;
    private int _completionQueryAnchor = -1;
    private bool _completionPending;
    private bool _completionQueryRunning;
    private bool _disposed;
    private string _highlightedSource;

    public BrowserEditorHost(
        World world,
        DomElement container,
        string cellId,
        string source,
        IReadOnlyList<HighlightRun> highlights,
        ICompilationReferenceResolver references)
    {
        _cellId = cellId;
        _world = world;
        _view = new(cellId, container);
        _completionProvider = new(references);
        _highlightedSource = source;
        var initialState = EditorState.Create(
            source,
            EditorDecorations.FromHighlights(highlights));
        _mount = _world.Mount(EditorViewComponent.Definition, new(_view, initialState));
    }

    public string Source => State.Value.Doc.SliceDoc();

    public bool Route(string eventType, string arguments)
        => eventType switch {
            "key" => HandleKey(arguments),
            "text" => HandleTextInput(arguments),
            "mut" => HandleDomMutation(arguments),
            "sel" => HandleSelection(arguments),
            "complete" => HandleCompletionRequest(arguments),
            _ => false,
        };

    public void Update(string source, IReadOnlyList<HighlightRun> highlights)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = State.Value;
        var decorations = EditorDecorations.FromHighlights(highlights);
        EditorState next;
        if (current.Doc.SliceDoc() == source) {
            next = current.WithDecorations(decorations);
        } else {
            var head = Math.Min(current.Selection.Main.Head, source.Length);
            next = current.Apply(new() {
                Changes = [new(0, current.Doc.Length, source)],
                Selection = EditorSelection.Single(head),
                Decorations = decorations,
            });
        }
        _highlightedSource = source;
        Commit(next);
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        CancelCompletion();
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _view.Dispose();
    }

    private bool HandleTextInput(string arguments)
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

            var after = ApplyNativeSelection(before, nativeSelection);
            TextCommands.Insert(new(after, state => after = state), text);
            Commit(after);
            ScheduleCompletion(before, after);
            return true;
        } finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleDomMutation(string arguments)
    {
        if (!TryReadDomMutation(arguments, out var mutation)) {
            return false;
        }

        var before = State.Value;
        var beforeSelection = TryResolveSelection(
            before.Doc,
            mutation.Before,
            out var resolvedBeforeSelection)
            ? resolvedBeforeSelection
            : before.Selection;
        if (!TryCreateDomChange(
            before,
            beforeSelection,
            mutation,
            out var change,
            out var preservedLineIdentity)) {
            return false;
        }

        var changes = change is { } edit
            ? ChangeSet.Of([edit], before.Doc.Length)
            : ChangeSet.Empty(before.Doc.Length);
        var document = changes.Apply(before.Doc);
        var hasNativeSelection = TryResolveSelection(
            document,
            mutation.After,
            out var nativeSelection);
        var selection = hasNativeSelection
            ? nativeSelection
            : beforeSelection.Map(changes);
        if (change is null && selection.Eq(before.Selection, true)) {
            return true;
        }

        var after = before.Apply(new() {
            Changes = change is { } documentChange ? [documentChange] : null,
            Selection = selection,
        });
        if (mutation.Scope == DomMutationScope.Document) {
            _view.RestoreDocumentStructure();
        } else {
            if (hasNativeSelection) {
                _view.SuppressNextSelectionUpdate();
            }
            if (preservedLineIdentity is { } lineIdentity) {
                _view.PreserveNativeEdit(lineIdentity);
            }
        }
        Commit(after);
        ScheduleCompletion(before, after);
        return true;
    }

    private static bool TryCreateDomChange(
        EditorState state,
        EditorSelection beforeSelection,
        DomMutation mutation,
        out ChangeSpec? change,
        out int? preservedLineIdentity)
    {
        TextDifference? difference;
        var offset = 0;
        preservedLineIdentity = null;
        switch (mutation.Scope) {
            case DomMutationScope.Text:
                if (!TryGetMutationLine(state, mutation.LineIndex, out var textLine)) {
                    change = null;
                    return false;
                }
                var selection = beforeSelection.Main;
                if (selection.From < textLine.From || selection.To > textLine.To) {
                    change = null;
                    return false;
                }
                difference = new(
                    selection.From - textLine.From,
                    selection.To - textLine.From,
                    mutation.Replacement);
                offset = textLine.From;
                preservedLineIdentity = state.LineIdentities.Values[mutation.LineIndex];
                break;

            case DomMutationScope.Line:
                if (!TryGetMutationLine(state, mutation.LineIndex, out var line)) {
                    change = null;
                    return false;
                }
                var localFrom = Math.Clamp(
                    beforeSelection.Main.From - line.From,
                    0,
                    line.Length);
                var localTo = Math.Clamp(
                    beforeSelection.Main.To - line.From,
                    localFrom,
                    line.Length);
                difference = TextDiff.FindForSelection(
                    line.Text,
                    mutation.Replacement,
                    localFrom,
                    localTo,
                    MutationPreference(mutation.InputType));
                offset = line.From;
                preservedLineIdentity = state.LineIdentities.Values[mutation.LineIndex];
                break;

            case DomMutationScope.Document:
                difference = TextDiff.FindForSelection(
                    state.Doc.SliceDoc(),
                    mutation.Replacement,
                    beforeSelection.Main.From,
                    beforeSelection.Main.To,
                    MutationPreference(mutation.InputType));
                break;

            default:
                change = null;
                return false;
        }

        change = difference is { } edit
            ? new(offset + edit.From, offset + edit.To, edit.Insert)
            : null;
        return true;
    }

    private static TextDiff.Preference MutationPreference(string inputType)
        => inputType.Contains("Backward", StringComparison.Ordinal)
            ? TextDiff.Preference.End
            : TextDiff.Preference.None;

    private static bool TryGetMutationLine(
        EditorState state,
        int lineIndex,
        out Line line)
    {
        if (lineIndex < 0 || lineIndex >= state.Doc.Lines) {
            line = default;
            return false;
        }
        line = state.Doc.Line(lineIndex + 1);
        return true;
    }

    private static bool TryReadDomMutation(string arguments, out DomMutation mutation)
    {
        var parts = arguments.Split(':', 12);
        if (parts.Length < 12
            || !TryReadMutationScope(parts[0], out var scope)
            || !int.TryParse(parts[1], out var lineIndex)
            || !TryReadSelection(parts, 2, out var before)
            || !TryReadSelection(parts, 6, out var after)) {
            mutation = default;
            return false;
        }

        mutation = new(
            scope,
            lineIndex,
            before,
            after,
            Uri.UnescapeDataString(parts[10]),
            Uri.UnescapeDataString(parts[11]));
        return true;
    }

    private static bool TryReadMutationScope(string value, out DomMutationScope scope)
    {
        scope = value switch {
            "t" => DomMutationScope.Text,
            "l" => DomMutationScope.Line,
            "d" => DomMutationScope.Document,
            _ => DomMutationScope.Invalid,
        };
        return scope != DomMutationScope.Invalid;
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
            Math.Clamp(
                anchorLine.From + native.AnchorColumn,
                anchorLine.From,
                anchorLine.To),
            Math.Clamp(
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
            || selection.Eq(state.Selection, true)) {
            return state;
        }
        return state.Apply(new() { Selection = selection });
    }

    private bool HandleSelection(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length < 4
            || !int.TryParse(parts[0], out var anchorLineIndex)
            || !int.TryParse(parts[1], out var anchorColumn)
            || !int.TryParse(parts[2], out var headLineIndex)
            || !int.TryParse(parts[3], out var headColumn)) {
            return false;
        }

        var state = State.Value;
        if (anchorLineIndex < 0
            || anchorLineIndex >= state.Doc.Lines
            || headLineIndex < 0
            || headLineIndex >= state.Doc.Lines) {
            return false;
        }

        var anchorLine = state.Doc.Line(anchorLineIndex + 1);
        var headLine = state.Doc.Line(headLineIndex + 1);
        var selection = EditorSelection.Single(
            Math.Clamp(anchorLine.From + anchorColumn, anchorLine.From, anchorLine.To),
            Math.Clamp(headLine.From + headColumn, headLine.From, headLine.To));
        if (selection.Eq(state.Selection, true)) {
            return false;
        }

        _view.SuppressNextSelectionUpdate();
        Commit(state.Apply(new() { Selection = selection }));
        CancelCompletion();
        return true;
    }

    private bool HandleKey(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            return HandleKeyCommand(commandArguments);
        } finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleKeyCommand(string arguments)
    {
        var parts = arguments.Split(':', 8);
        if (parts.Length < 8
            || !TryReadSelection(parts, 0, out var nativeSelection)
            || !bool.TryParse(parts[5], out var control)
            || !bool.TryParse(parts[6], out var shift)) {
            return false;
        }
        var key = parts[4];
        if (key == "Tab" && shift && (_completionPending || _completion is not null)) {
            CancelCompletion();
        }

        if (_completion is not null
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            HandleCompletionKey(key);
            return true;
        }

        if (_completionPending
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            if (key == "Tab"
                && !control
                && !shift
                && HandleManualCompletion(nativeSelection)) {
                return true;
            }
            CancelCompletion();
            if (key == "Escape") {
                return true;
            }
        }

        if (key == "Tab"
            && !control
            && !shift
            && HandleManualCompletion(nativeSelection)) {
            return true;
        }

        var command = GetCommand(key, control, shift);
        if (command is null) {
            return false;
        }

        var before = State.Value;
        var after = ApplyNativeSelection(before, nativeSelection);
        if (!command(new(after, state => after = state))) {
            return false;
        }
        Commit(after);
        ScheduleCompletion(before, after);
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

    private void Commit(EditorState state)
    {
        if (state == State.Value) {
            return;
        }
        var source = state.Doc.SliceDoc();
        if (source != _highlightedSource) {
            state = state.WithDecorations(
                EditorDecorations.FromHighlights(CSharpHighlighter.Classify(source)));
            _highlightedSource = source;
        }
        State.Set(state);
        _world.FlushReactive();
    }

    private void ScheduleCompletion(EditorState before, EditorState after)
    {
        var isActive = _completionPending || _completionSource is not null;
        if (!CompletionTrigger.ShouldActivate(before, after, isActive)) {
            CancelCompletion();
            return;
        }

        StartCompletion(after, _completionDelayMilliseconds);
    }

    private bool HandleManualCompletion(BrowserSelection nativeSelection)
    {
        var before = State.Value;
        var state = ApplyNativeSelection(before, nativeSelection);
        if (!state.Selection.Main.Empty) {
            return false;
        }
        if (state != before) {
            _view.SuppressNextSelectionUpdate();
            Commit(state);
        }
        StartCompletion(state, delayMilliseconds: 0);
        return true;
    }

    private void StartCompletion(EditorState state, int delayMilliseconds)
    {
        var position = state.Selection.Main.Head;
        var queryAnchor = CompletionIdentifier.FindStart(state.Doc, position);
        if (_completionPending && queryAnchor == _completionQueryAnchor) {
            if (!_completionQueryRunning) {
                DomRuntime.ScheduleEvent(
                    CompletionScheduleKey,
                    $"complete:{_cellId}:{_completionGeneration}",
                    delayMilliseconds);
            }
            return;
        }

        CancelCompletionQuery();
        if (_completionSource is { } source
            && source.TryFilter(
                state.Doc,
                position,
                _maximumRenderedCompletions,
                out var filtered)) {
            _completionPending = false;
            _completionIndex = 0;
            _completion = filtered;
            if (filtered.HasItems) {
                RenderCompletionPopup();
            } else {
                HideCompletionPopup();
            }
            return;
        }

        _completionPending = true;
        _completion = null;
        _completionSource = null;
        _completionQueryAnchor = queryAnchor;
        var cancellation = new CancellationTokenSource();
        _completionCancellation = cancellation;
        var generation = _completionGeneration;
        DomRuntime.ScheduleEvent(
            CompletionScheduleKey,
            $"complete:{_cellId}:{generation}",
            delayMilliseconds);
    }

    private bool HandleCompletionRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation)
            || generation != _completionGeneration
            || _completionCancellation is not { } cancellation
            || cancellation.IsCancellationRequested) {
            return false;
        }
        if (_completionQueryRunning) {
            return true;
        }

        _completionQueryRunning = true;
        _ = QueryCompletionAsync(State.Value, cancellation, generation);
        return true;
    }

    private async Task QueryCompletionAsync(
        EditorState state,
        CancellationTokenSource cancellation,
        int generation)
    {
        try {
            cancellation.Token.ThrowIfCancellationRequested();

            var result = await _completionProvider.QueryAsync(
                state.Doc.SliceDoc(),
                state.Selection.Main.Head,
                cancellation.Token);
            if (generation != _completionGeneration || cancellation.IsCancellationRequested) {
                return;
            }

            var current = State.Value;
            var currentPosition = current.Selection.Main.Head;
            if (CompletionIdentifier.FindStart(current.Doc, currentPosition)
                != _completionQueryAnchor) {
                CancelCompletion();
                return;
            }

            _completionPending = false;
            _completionIndex = 0;
            if (!result.HasItems) {
                CloseCompletionPopup();
                return;
            }

            _completionSource = result;
            if (!result.TryFilter(
                current.Doc,
                currentPosition,
                _maximumRenderedCompletions,
                out var filtered)) {
                CloseCompletionPopup();
                return;
            }
            _completion = filtered;
            RenderCompletionPopup();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
        catch (Exception error) {
            if (generation == _completionGeneration) {
                _completionPending = false;
                CloseCompletionPopup();
                DomRuntime.ReportError(error.ToString());
            }
        } finally {
            if (ReferenceEquals(_completionCancellation, cancellation)) {
                _completionCancellation = null;
                _completionQueryRunning = false;
            }
            cancellation.Dispose();
        }
    }

    private void HandleCompletionKey(string key)
    {
        if (_completion is not { } completion) {
            return;
        }
        switch (key) {
            case "ArrowDown":
                SelectCompletion((_completionIndex + 1) % completion.Items.Count);
                break;
            case "ArrowUp":
                SelectCompletion(
                    (_completionIndex - 1 + completion.Items.Count) % completion.Items.Count);
                break;
            case "Enter":
            case "Tab":
                AcceptCompletion(completion);
                break;
            case "Escape":
                CancelCompletion();
                break;
        }
    }

    private void AcceptCompletion(CompletionResult completion)
    {
        var candidate = completion.Items[_completionIndex];
        CancelCompletion();
        var state = State.Value;
        Commit(state.Apply(new() {
            Changes = [new(
                candidate.ReplaceStart,
                candidate.ReplaceEnd,
                candidate.InsertText)],
            Selection = EditorSelection.Single(
                candidate.ReplaceStart + candidate.InsertText.Length),
        }));
    }

    private void RenderCompletionPopup()
    {
        if (_completion is not { } completion) {
            CloseCompletionPopup();
            return;
        }

        var isNew = _completionPopup is null;
        var popup = _completionPopup ??= DomElement.Create("div").Class("completion-popup");
        for (var index = 0; index < completion.Items.Count; index++) {
            var label = completion.Items[index].Label;
            DomElement item;
            if (index < _completionItems.Count) {
                var rendered = _completionItems[index];
                item = rendered.Element;
                if (rendered.Label != label) {
                    item.Text(label);
                    _completionItems[index] = (item, label);
                }
            } else {
                item = DomElement.Create("div").Class("completion-item").Text(label);
                popup.Append(item);
                _completionItems.Add((item, label));
            }
            item.ToggleClass("selected", index == _completionIndex);
        }
        while (_completionItems.Count > completion.Items.Count) {
            var item = _completionItems[^1].Element;
            item.Remove();
            item.Dispose();
            _completionItems.RemoveAt(_completionItems.Count - 1);
        }
        var anchorPosition = completion.Items.Min(static item => item.ReplaceStart);
        var anchorLine = State.Value.Doc.LineAt(anchorPosition);
        var anchorLineIndex = anchorLine.Number - 1;
        var anchorColumn = anchorPosition - anchorLine.From;
        if (isNew) {
            _view.MountOverlay(popup, anchorLineIndex, anchorColumn);
        } else {
            _view.PlaceOverlay(popup, anchorLineIndex, anchorColumn);
        }
        DomRuntime.EnsureVisible(popup, _completionItems[_completionIndex].Element);
    }

    private void SelectCompletion(int index)
    {
        _completionItems[_completionIndex].Element.ToggleClass("selected", false);
        _completionIndex = index;
        var selected = _completionItems[_completionIndex].Element;
        selected.ToggleClass("selected", true);
        if (_completionPopup is { } popup) {
            DomRuntime.EnsureVisible(popup, selected);
        }
    }

    private void ClearCompletionItems()
    {
        foreach (var (item, _) in _completionItems) {
            item.Remove();
            item.Dispose();
        }
        _completionItems.Clear();
    }

    private void CancelCompletion()
    {
        CancelCompletionQuery();
        _completionPending = false;
        CloseCompletionPopup();
    }

    private void CancelCompletionQuery()
    {
        _completionGeneration++;
        DomRuntime.CancelScheduledEvent(CompletionScheduleKey);
        var cancellation = _completionCancellation;
        var queryWasRunning = _completionQueryRunning;
        _completionCancellation = null;
        _completionQueryAnchor = -1;
        _completionQueryRunning = false;
        cancellation?.Cancel();
        if (!queryWasRunning) {
            cancellation?.Dispose();
        }
    }

    private void CloseCompletionPopup()
    {
        _completionSource = null;
        HideCompletionPopup();
    }

    private void HideCompletionPopup()
    {
        ClearCompletionItems();
        if (_completionPopup is { } popup) {
            DomRuntime.ClearOverlayPlacement(popup);
        }
        _completionPopup?.Remove();
        _completionPopup?.Dispose();
        _completionPopup = null;
        _completion = null;
    }

    private static StateCommand? GetCommand(string key, bool control, bool shift)
        => (key, control, shift) switch {
            ("ArrowLeft", false, false) => CursorCommands.CharLeft,
            ("ArrowLeft", false, true) => CursorCommands.SelectCharLeft,
            ("ArrowRight", false, false) => CursorCommands.CharRight,
            ("ArrowRight", false, true) => CursorCommands.SelectCharRight,
            ("ArrowUp", false, false) => CursorCommands.LineUp,
            ("ArrowUp", false, true) => CursorCommands.SelectLineUp,
            ("ArrowDown", false, false) => CursorCommands.LineDown,
            ("ArrowDown", false, true) => CursorCommands.SelectLineDown,
            ("Home", false, false) => CursorCommands.LineStart,
            ("Home", false, true) => CursorCommands.SelectLineStart,
            ("End", false, false) => CursorCommands.LineEnd,
            ("End", false, true) => CursorCommands.SelectLineEnd,
            ("Home", true, false) => CursorCommands.DocumentStart,
            ("Home", true, true) => CursorCommands.SelectDocumentStart,
            ("End", true, false) => CursorCommands.DocumentEnd,
            ("End", true, true) => CursorCommands.SelectDocumentEnd,
            ("PageUp", false, false) => CursorCommands.PageUp,
            ("PageUp", false, true) => CursorCommands.SelectPageUp,
            ("PageDown", false, false) => CursorCommands.PageDown,
            ("PageDown", false, true) => CursorCommands.SelectPageDown,
            ("ArrowLeft", true, false) => CursorCommands.GroupLeft,
            ("ArrowLeft", true, true) => CursorCommands.SelectGroupLeft,
            ("ArrowRight", true, false) => CursorCommands.GroupRight,
            ("ArrowRight", true, true) => CursorCommands.SelectGroupRight,
            ("Enter", false, _) => LineCommands.InsertNewlineAndIndent,
            ("Backspace", false, _) => DeleteCommands.CharBackward,
            ("Backspace", true, _) => DeleteCommands.GroupBackward,
            ("Delete", false, _) => DeleteCommands.CharForward,
            ("Delete", true, _) => DeleteCommands.GroupForward,
            ("Tab", false, false) => LineCommands.InsertTab,
            ("Tab", false, true) => LineCommands.IndentLess,
            ("a", true, _) => SelectionCommands.SelectAll,
            ("k", true, _) => DeleteCommands.ToLineEnd,
            ("Escape", false, _) => SelectionCommands.SimplifySelection,
            _ => null,
        };

    private enum DomMutationScope
    {
        Invalid,
        Text,
        Line,
        Document,
    }

    private readonly record struct BrowserSelection(
        int AnchorLineIndex,
        int AnchorColumn,
        int HeadLineIndex,
        int HeadColumn);

    private readonly record struct DomMutation(
        DomMutationScope Scope,
        int LineIndex,
        BrowserSelection Before,
        BrowserSelection After,
        string InputType,
        string Replacement);

    private State<EditorState> State
        => _state ??= _mount.GetState<EditorState>();

    private string CompletionScheduleKey => $"completion:{_cellId}";
}
