using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorHost : IDisposable
{
    private const int _completionDelayMilliseconds = 80;

    private readonly string _cellId;
    private readonly World _world;
    private readonly BrowserEditorView _view;
    private readonly CSharpCompletionProvider _completionProvider;
    private readonly ReactiveMount<EditorViewProps> _mount;
    private readonly List<(DomElement Element, string Label)> _completionItems = [];

    private State<EditorState>? _state;
    private CompletionResult? _completion;
    private DomElement? _completionPopup;
    private CancellationTokenSource? _completionCancellation;
    private int _completionIndex;
    private int _completionGeneration;
    private bool _completionPending;
    private bool _disposed;

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
            "muttext" => HandleTextMutation(arguments),
            "mutline" => HandleLineMutation(arguments),
            "mutall" => HandleDocumentMutation(arguments),
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
        if (!TryReadCommand(arguments, out var sequence, out var encodedText)) {
            return false;
        }

        try {
            var before = State.Value;
            var after = before;
            var text = Uri.UnescapeDataString(encodedText);
            TextCommands.Insert(new(before, state => after = state), text);
            Commit(after);
            ScheduleCompletion(before, after);
            return true;
        } finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleLineMutation(string arguments)
    {
        var separator = arguments.IndexOf('\0');
        if (separator < 0
            || !int.TryParse(arguments[..separator], out var lineIndex)) {
            return false;
        }

        var before = State.Value;
        if (lineIndex < 0 || lineIndex >= before.Doc.Lines) {
            return false;
        }
        var line = before.Doc.Line(lineIndex + 1);
        var replacement = arguments[(separator + 1)..];
        if (TextDiff.Minimal(line.Text, replacement) is not { } difference) {
            return true;
        }

        var (from, to, insert) = difference;
        var documentFrom = line.From + from;
        var change = new ChangeSpec(documentFrom, line.From + to, insert);
        var after = before.Apply(new() {
            Changes = [change],
            Selection = EditorSelection.Single(documentFrom + insert.Length),
        });
        _view.SuppressNextSelectionUpdate();
        _view.PreserveNativeEdit(before.LineIdentities.Values[lineIndex]);
        Commit(after);
        ScheduleCompletion(before, after);
        return true;
    }

    private bool HandleTextMutation(string arguments)
    {
        var parts = arguments.Split(':', 4);
        if (parts.Length < 4
            || !int.TryParse(parts[0], out var lineIndex)
            || !int.TryParse(parts[1], out var from)
            || !int.TryParse(parts[2], out var to)) {
            return false;
        }

        var before = State.Value;
        if (lineIndex < 0 || lineIndex >= before.Doc.Lines) {
            return false;
        }
        var line = before.Doc.Line(lineIndex + 1);
        if (from < 0 || from > to || to > line.Length) {
            return false;
        }

        var after = before.Apply(new() {
            Selection = EditorSelection.Single(line.From + from, line.From + to),
        });
        var text = Uri.UnescapeDataString(parts[3]);
        TextCommands.Insert(new(after, state => after = state), text);
        _view.SuppressNextSelectionUpdate();
        _view.PreserveNativeEdit(before.LineIdentities.Values[lineIndex]);
        Commit(after);
        ScheduleCompletion(before, after);
        return true;
    }

    private bool HandleDocumentMutation(string arguments)
    {
        var separator = arguments.IndexOf('\0');
        var replacement = separator < 0 ? arguments : arguments[(separator + 1)..];
        var before = State.Value;
        if (TextDiff.Minimal(before.Doc.SliceDoc(), replacement) is not { } difference) {
            return true;
        }

        var (from, to, insert) = difference;
        var after = before.Apply(new() {
            Changes = [new(from, to, insert)],
            Selection = EditorSelection.Single(from + insert.Length),
        });
        _view.RestoreDocumentStructure();
        Commit(after);
        ScheduleCompletion(before, after);
        return true;
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
        var parts = arguments.Split(':');
        if (parts.Length < 4
            || !bool.TryParse(parts[1], out var control)
            || !bool.TryParse(parts[2], out var shift)) {
            return false;
        }
        var key = parts[0];
        if (_completionPending
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            CancelCompletion();
            if (key == "Escape") {
                return true;
            }
        }

        if (_completion is not null
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            HandleCompletionKey(key);
            return true;
        }

        var command = GetCommand(key, control, shift);
        if (command is null) {
            return false;
        }

        var before = State.Value;
        var after = before;
        if (!command(new(before, state => after = state))) {
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
        State.Set(state);
        _world.FlushReactive();
    }

    private void ScheduleCompletion(EditorState before, EditorState after)
    {
        if (!after.Selection.Main.Empty) {
            CancelCompletion();
            return;
        }

        var position = after.Selection.Main.Head;
        var shouldQuery = false;
        if (after.Doc.Length > before.Doc.Length
            && position > 0
            && position <= after.Doc.Length) {
            var insertedCharacter = after.SliceDoc(position - 1, position)[0];
            shouldQuery = insertedCharacter is '.' or '_'
                || char.IsLetterOrDigit(insertedCharacter);
        } else if (after.Doc.Length < before.Doc.Length) {
            shouldQuery = true;
        }

        if (!shouldQuery) {
            CancelCompletion();
            return;
        }

        CancelCompletionQuery();
        _completionPending = true;
        _completion = null;
        var cancellation = new CancellationTokenSource();
        _completionCancellation = cancellation;
        var generation = _completionGeneration;
        DomRuntime.ScheduleEvent(
            CompletionScheduleKey,
            $"complete:{_cellId}:{generation}",
            _completionDelayMilliseconds);
    }

    private bool HandleCompletionRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation)
            || generation != _completionGeneration
            || _completionCancellation is not { } cancellation
            || cancellation.IsCancellationRequested) {
            return false;
        }

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

            _completionPending = false;
            _completionIndex = 0;
            if (!result.HasItems) {
                CloseCompletionPopup();
                return;
            }

            _completion = result;
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
                cancellation.Dispose();
            }
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
        if (isNew) {
            _view.MountOverlay(popup);
        } else {
            _view.PlaceOverlay(popup);
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
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        _completionCancellation = null;
    }

    private void CloseCompletionPopup()
    {
        ClearCompletionItems();
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

    private State<EditorState> State
        => _state ??= _mount.GetState<EditorState>();

    private string CompletionScheduleKey => $"completion:{_cellId}";
}
