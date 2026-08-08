using Sia;
using Sia.Reactive;
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

    private State<EditorState>? _state;
    private CompletionResult? _completion;
    private BrowserElement? _completionPopup;
    private CancellationTokenSource? _completionCancellation;
    private Task<CompletionResult>? _activeCompletionQuery;
    private int _completionIndex;
    private int _completionGeneration;
    private bool _disposed;

    public BrowserEditorHost(
        World world,
        BrowserElement container,
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
            "mutline" => HandleLineMutation(arguments),
            "mutall" => HandleDocumentMutation(arguments),
            "sel" => HandleSelection(arguments),
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
        _view.SuppressNextSelectionUpdate();
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
        var parts = arguments.Split(':');
        if (parts.Length < 4
            || !bool.TryParse(parts[1], out var control)
            || !bool.TryParse(parts[2], out var shift)) {
            return false;
        }
        var key = parts[0];
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

        CancelCompletion();
        var cancellation = new CancellationTokenSource();
        _completionCancellation = cancellation;
        _ = QueryCompletionAsync(after, cancellation, ++_completionGeneration);
    }

    private async Task QueryCompletionAsync(
        EditorState state,
        CancellationTokenSource cancellation,
        int generation)
    {
        try {
            await Task.Delay(_completionDelayMilliseconds, cancellation.Token);
            if (_activeCompletionQuery is { } previousQuery) {
                try {
                    await previousQuery;
                }
                catch (OperationCanceledException) {
                }
                catch {
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();

            var query = _completionProvider.QueryAsync(
                state.Doc.SliceDoc(),
                state.Selection.Main.Head,
                cancellation.Token);
            _activeCompletionQuery = query;
            var result = await query;
            if (generation != _completionGeneration || cancellation.IsCancellationRequested) {
                return;
            }

            _completionIndex = 0;
            _completion = result.HasItems ? result : null;
            RenderCompletionPopup();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
        catch {
            if (generation == _completionGeneration) {
                CloseCompletionPopup();
            }
        } finally {
            if (ReferenceEquals(_completionCancellation, cancellation)) {
                _completionCancellation = null;
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
                _completionIndex = (_completionIndex + 1) % completion.Items.Count;
                RenderCompletionPopup();
                break;
            case "ArrowUp":
                _completionIndex = (_completionIndex - 1 + completion.Items.Count)
                    % completion.Items.Count;
                RenderCompletionPopup();
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
        CloseCompletionPopup(removeResult: false);
        if (_completion is not { } completion) {
            return;
        }

        var popup = BrowserElement.Create("div").Class("completion-popup");
        for (var index = 0; index < completion.Items.Count; index++) {
            using var item = BrowserElement.Create("div")
                .Class("completion-item")
                .ToggleClass("selected", index == _completionIndex)
                .Text(completion.Items[index].Label);
            popup.Append(item);
        }
        using var container = BrowserElement.Find(NotebookElementIds.Editor(_cellId));
        container.Append(popup);
        _completionPopup = popup;
    }

    private void CancelCompletion()
    {
        _completionGeneration++;
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        _completionCancellation = null;
        CloseCompletionPopup();
    }

    private void CloseCompletionPopup(bool removeResult = true)
    {
        _completionPopup?.Remove();
        _completionPopup?.Dispose();
        _completionPopup = null;
        if (removeResult) {
            _completion = null;
        }
    }

    private static StateCommand? GetCommand(string key, bool control, bool shift)
        => (key, control, shift) switch {
            ("ArrowLeft", false, _) => CursorCommands.CharLeft,
            ("ArrowRight", false, _) => CursorCommands.CharRight,
            ("ArrowUp", false, _) => CursorCommands.LineUp,
            ("ArrowDown", false, _) => CursorCommands.LineDown,
            ("Home", false, _) => CursorCommands.LineStart,
            ("End", false, _) => CursorCommands.LineEnd,
            ("Home", true, _) => CursorCommands.DocumentStart,
            ("End", true, _) => CursorCommands.DocumentEnd,
            ("PageUp", false, _) => CursorCommands.PageUp,
            ("PageDown", false, _) => CursorCommands.PageDown,
            ("ArrowLeft", true, _) => CursorCommands.GroupLeft,
            ("ArrowRight", true, _) => CursorCommands.GroupRight,
            ("Enter", false, _) => LineCommands.InsertNewlineAndIndent,
            ("Backspace", false, _) => DeleteCommands.CharBackward,
            ("Delete", false, _) => DeleteCommands.CharForward,
            ("Tab", false, false) => LineCommands.InsertTab,
            ("Tab", false, true) => LineCommands.IndentLess,
            ("a", true, _) => SelectionCommands.SelectAll,
            ("k", true, _) => DeleteCommands.ToLineEnd,
            ("Escape", false, _) => SelectionCommands.SimplifySelection,
            _ => null,
        };

    private State<EditorState> State
        => _state ??= _mount.GetState<EditorState>();
}
