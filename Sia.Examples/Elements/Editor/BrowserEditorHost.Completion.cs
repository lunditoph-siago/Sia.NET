using Microsoft.CodeAnalysis.Completion;
using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private const int k_CompletionDelayMilliseconds = 80;
    private CompletionResult? _completion;
    private CompletionResult? _completionSource;
    private Text? _completionDocument;
    private int _completionPosition;
    private CancellationTokenSource? _completionCancellation;
    private int _completionIndex;
    private bool _completionSoft;
    private CompletionCandidate? _completionChoice;
    private int _completionGeneration;
    private int _completionQueryAnchor = -1;
    private bool _completionPending;
    private EditorState? _completionQueryState;

    private void ScheduleCompletion(EditorState before, EditorState after)
    {
        var isActive = _completionPending || _completionSource is not null;
        if (!CompletionTrigger.ShouldActivate(before, after, isActive)) {
            CancelCompletion();
            return;
        }

        StartCompletion(after, k_CompletionDelayMilliseconds);
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
        _view.RevealSelection();
        StartCompletion(state, delayMilliseconds: 0);
        return true;
    }

    private void StartCompletion(EditorState state, int delayMilliseconds)
    {
        var position = state.Selection.Main.Head;
        var queryAnchor = CompletionIdentifier.FindStart(state.Doc, position);
        if (_completionPending && queryAnchor == _completionQueryAnchor
            && (_completionQueryState is not { } queryState
                || ReferenceEquals(queryState.Doc, state.Doc) && queryState.Selection.Eq(state.Selection))) {
            if (_completionQueryState is null) {
                DomRuntime.ScheduleEvent(
                    CompletionScheduleKey,
                    $"complete:{_cellId}:{_completionGeneration}",
                    delayMilliseconds);
            }
            return;
        }

        CancelCompletionQuery();
        var cached = _completionSource;
        _completionQueryAnchor = queryAnchor;
        var cancellation = new CancellationTokenSource();
        _completionCancellation = cancellation;
        var generation = _completionGeneration;
        _completionPending = true;
        if (cached is { HasItems: true } source
            && (source.Items[0].ReplaceStart == queryAnchor || source.Items[0].ReplaceStart == queryAnchor - 1
                && queryAnchor > 0 && state.Doc.SliceDoc(queryAnchor - 1, queryAnchor) == "@")) {
            _completionDocument = null;
            RenderCompletionPopup();
        }
        else {
            HideCompletionPopup();
            _completion = null;
            _completionSource = null;
        }
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
        if (_completionQueryState is not null) {
            return true;
        }

        var state = State.Value;
        _completionQueryState = state;
        _ = QueryCompletionAsync(state, cancellation, generation, _completionSource);
        return true;
    }

    private async Task QueryCompletionAsync(
        EditorState state,
        CancellationTokenSource cancellation,
        int generation,
        CompletionResult? cached = null)
    {
        try {
            cancellation.Token.ThrowIfCancellationRequested();

            await RefreshCompletionAsync(state, cancellation, generation, cached);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
        catch (Exception error) {
            if (generation == _completionGeneration) {
                _completionPending = false;
                CloseCompletionPopup();
                DomRuntime.ReportError(error.ToString());
            }
        }
        finally {
            if (ReferenceEquals(_completionCancellation, cancellation)) {
                _completionCancellation = null;
                _completionQueryState = null;
                PrepareCompletionWindow();
            }
            cancellation.Dispose();
        }
    }

    private async Task RefreshCompletionAsync(
        EditorState state, CancellationTokenSource cancellation, int generation, CompletionResult? cached)
    {
        var refresh = cached is null
            ? (source, position, token) => _completionProvider.QueryAsync(_cellId, source, position, token)
            : cached.Query?.Refresh;
        var result = refresh is null ? cached! : await refresh(state.Doc.SliceDoc(), state.Selection.Main.Head, cancellation.Token);
        if (IsRetired()) {
            return;
        }

        var queryDocument = state.Doc;
        var queryPosition = state.Selection.Main.Head;
        while (!IsRetired()) {
            var current = State.Value;
            var position = current.Selection.Main.Head;
            if (CompletionIdentifier.FindStart(current.Doc, position) != _completionQueryAnchor
                || !current.Selection.Main.Empty) {
                CancelCompletion();
                return;
            }
            if (refresh is not null && (!ReferenceEquals(queryDocument, current.Doc) || queryPosition != position)) {
                result = await refresh(current.Doc.SliceDoc(), position, cancellation.Token);
                if (IsRetired()) return;
                queryDocument = current.Doc;
                queryPosition = position;
                if (SourceChanged(current)) continue;
            }
            if (!result.HasItems) {
                _completionPending = false;
                CloseCompletionPopup();
                return;
            }
            _completionSource = result;
            CompletionResult? Filter() => result.TryFilter(current.Doc, position, int.MaxValue, out var value) ? value : null;
            var filtered = result.Items.Count <= k_MaximumRenderedCompletions ? Filter() : await Task.Run(Filter, cancellation.Token);
            if (IsRetired()) return;
            if (SourceChanged(current)) continue;
            if (filtered is null) {
                _completionPending = false;
                CloseCompletionPopup();
                return;
            }
            if (!filtered.HasItems) {
                _completionPending = false;
                HideCompletionPopup();
                return;
            }
            if (cached is not null) PublishCompletion(filtered, current);
            var selectedIndex = CompletionSelection(filtered);
            var prepared = await CSharpCompletionProvider.PrepareWindowAsync(filtered.Window(selectedIndex, 1),
                current.Doc.SliceDoc(), position, -1, cancellation.Token);
            if (IsRetired()) return;
            if (SourceChanged(current)) continue;
            if (!prepared.HasItems) { _completionPending = false; HideCompletionPopup(); return; }
            _completionPending = false;
            PublishCompletion(filtered.WithPreparedWindow(selectedIndex, 1, prepared), current);
            return;
        }

        bool IsRetired() => generation != _completionGeneration || cancellation.IsCancellationRequested;
        bool SourceChanged(EditorState expected) => !ReferenceEquals(expected.Doc, State.Value.Doc)
            || !expected.Selection.Eq(State.Value.Selection);
    }

    private void AcceptCompletion(CompletionResult completion)
    {
        if (!ReferenceEquals(_completionDocument, State.Value.Doc)
            || !State.Value.Selection.Main.Empty || State.Value.Selection.Main.Head != _completionPosition) {
            CancelCompletion();
            return;
        }
        var candidate = completion.Items[_completionIndex];
        if (candidate.Edit is not { } edit) return;
        CancelCompletion();
        var state = State.Value;
        Commit(state.Apply(new() {
            Changes = edit.Changes,
            Selection = EditorSelection.Single(edit.Position),
        }));
    }

    private bool TryCommitCompletionCharacter(EditorState state, string text)
    {
        if (text.Length != 1 || _completionSoft || _completion is not { HasItems: true } completion
            || !ReferenceEquals(state.Doc, _completionDocument) || state.Selection.Ranges.Count != 1
            || !state.Selection.Main.Empty || state.Selection.Main.Head != _completionPosition) return false;
        var candidate = completion.Items[_completionIndex];
        if (candidate.Edit is null || candidate.CommitEdits is null
            || !candidate.CommitEdits.TryGetValue(text[0], out var edit)) return false;
        CancelCompletion();
        var after = state.Apply(new() { Changes = edit.Changes, Selection = EditorSelection.Single(edit.Position) });
        Commit(after, historySelection: state.Selection);
        ScheduleCompletion(state, after);
        return true;
    }

    private bool HandleCompletionClick(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var generation)
            || generation != _completionGeneration || !int.TryParse(parts[1], out var index)
            || _completion is not { } completion || index < 0 || index >= completion.Items.Count) return false;
        _completionIndex = index;
        AcceptCompletion(completion);
        return true;
    }

    private bool CompletionMakesEdit(CompletionCandidate candidate)
        => candidate.Edit is { } edit && edit.Changes.Any(change => State.Value.Doc.SliceDoc(change.From, change.To ?? change.From) != change.Insert);

    private int CompletionSelection(CompletionResult completion)
    {
        if (_completionChoice is { } choice) {
            for (var index = 0; index < completion.Items.Count; index++) if (completion.Items[index].SameIdentity(choice)) return index;
            _completionChoice = null;
        }
        return 0;
    }

    private void PublishCompletion(CompletionResult completion, EditorState state)
    {
        _completionIndex = CompletionSelection(completion);
        _completionWindowStart = CompletionWindowStart(_completionIndex, completion.Items.Count);
        _completion = completion;
        var behavior = completion.Items[_completionIndex].Item?.Rules.SelectionBehavior;
        _completionSoft = _completionChoice is null && (completion.SuggestionMode || behavior == CompletionItemSelectionBehavior.SoftSelection);
        _completionDocument = state.Doc;
        _completionPosition = state.Selection.Main.Head;
        RenderCompletionPopup();
    }

    private void CancelCompletion()
    {
        _completionChoice = null;
        CancelCompletionQuery();
        _completionPending = false;
        CloseCompletionPopup();
    }

    private void CancelCompletionQuery()
    {
        _completionWindowCancellation?.Cancel();
        _completionWindowCancellation = null;
        _completionGeneration++;
        DomRuntime.CancelScheduledEvent(CompletionScheduleKey);
        var cancellation = _completionCancellation;
        var queryWasRunning = _completionQueryState is not null;
        _completionCancellation = null;
        _completionQueryAnchor = -1;
        _completionQueryState = null;
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

}
