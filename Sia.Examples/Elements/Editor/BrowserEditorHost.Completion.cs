using System.Globalization;
using Microsoft.CodeAnalysis.Completion;
using Sia_Examples.Dom;

namespace Sia_Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private const int _completionDelayMilliseconds = 80;
    private const int _maximumRenderedCompletions = 20;
    private readonly List<(DomElement Element, string Label)> _completionItems = [];
    private CompletionResult? _completion;
    private CompletionResult? _completionSource;
    private DomElement? _completionPopup;
    private DomElement? _completionList;
    private DomElement? _completionDescription;
    private CancellationTokenSource? _completionDescriptionCancellation;
    private CompletionCandidate? _completionDescriptionCandidate;
    private Text? _completionDescriptionDocument;
    private Text? _completionDocument;
    private int _completionPosition;
    private CancellationTokenSource? _completionCancellation;
    private int _completionIndex;
    private int _completionWindowStart;
    private int _completionPageSize = 8;
    private bool _completionSoft;
    private CompletionCandidate? _completionChoice;
    private CancellationTokenSource? _completionWindowCancellation;
    private DomElement? _completionBefore;
    private DomElement? _completionAfter;
    private DomElement? _completionCount;
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

            Func<string, int, CancellationToken, Task<CompletionResult>>? refresh = cached is null
                ? (source, position, token) => _completionProvider.QueryAsync(_cellId, source, position, token)
                : cached.Query?.Refresh;
            var result = refresh is null ? cached! : await refresh(state.Doc.SliceDoc(), state.Selection.Main.Head, cancellation.Token);
            if (generation != _completionGeneration || cancellation.IsCancellationRequested) {
                return;
            }

            var queryDocument = state.Doc;
            var queryPosition = state.Selection.Main.Head;
            while (generation == _completionGeneration && !cancellation.IsCancellationRequested) {
                var current = State.Value;
                var position = current.Selection.Main.Head;
                if (CompletionIdentifier.FindStart(current.Doc, position) != _completionQueryAnchor
                    || !current.Selection.Main.Empty) {
                    CancelCompletion();
                    return;
                }
                if (refresh is not null && (!ReferenceEquals(queryDocument, current.Doc) || queryPosition != position)) {
                    result = await refresh(current.Doc.SliceDoc(), position, cancellation.Token);
                    if (generation != _completionGeneration || cancellation.IsCancellationRequested) return;
                    queryDocument = current.Doc;
                    queryPosition = position;
                    if (!ReferenceEquals(current.Doc, State.Value.Doc) || !current.Selection.Eq(State.Value.Selection)) continue;
                }
                if (!result.HasItems) {
                    _completionPending = false;
                    CloseCompletionPopup();
                    return;
                }
                _completionSource = result;
                CompletionResult? Filter() => result.TryFilter(current.Doc, position, int.MaxValue, out var value) ? value : null;
                var filtered = result.Items.Count <= _maximumRenderedCompletions ? Filter() : await Task.Run(Filter, cancellation.Token);
                if (generation != _completionGeneration || cancellation.IsCancellationRequested) return;
                if (!ReferenceEquals(current.Doc, State.Value.Doc) || !current.Selection.Eq(State.Value.Selection)) continue;
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
                if (generation != _completionGeneration || cancellation.IsCancellationRequested) return;
                if (!ReferenceEquals(current.Doc, State.Value.Doc) || !current.Selection.Eq(State.Value.Selection)) continue;
                if (!prepared.HasItems) { _completionPending = false; HideCompletionPopup(); return; }
                _completionPending = false;
                PublishCompletion(filtered.WithPreparedWindow(selectedIndex, 1, prepared), current);
                return;
            }
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

    private static int CompletionWindowStart(int selectedIndex, int count)
        => Math.Clamp(selectedIndex - _maximumRenderedCompletions / 2, 0, Math.Max(0, count - _maximumRenderedCompletions));

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

    private void RenderCompletionPopup(bool revealSelection = true)
    {
        if (_completion is not { HasItems: true } completion) { HideCompletionPopup(); return; }
        _view.BeginUpdate();
        try {
            var isNew = _completionPopup is null;
            var popup = _completionPopup ??= DomElement.Create("div").Class("completion-popup");
            if (isNew) {
                _completionList = DomElement.Create("div").Class("completion-list")
                    .Id($"{_cellId}-suggestions").Attr("role", "listbox").Attr("aria-label", "C# suggestions");
                _completionBefore = DomElement.Create("div").Class("completion-spacer").Attr("aria-hidden", "true");
                _completionAfter = DomElement.Create("div").Class("completion-spacer").Attr("aria-hidden", "true");
                _completionList.Append(_completionBefore).Append(_completionAfter);
                _completionCount = DomElement.Create("div").Class("completion-count");
                _completionDescription = DomElement.Create("div").Class("completion-description");
                popup.Append(_completionList).Append(_completionCount).Append(_completionDescription);
            }
            var start = _completionWindowStart;
            var length = Math.Min(_maximumRenderedCompletions, completion.Items.Count - start);
            _completionList!.Attr("data-completion-binding", _cellId)
                .Attr("aria-busy", _completionPending ? "true" : "false")
                .Attr("data-completion-generation", _completionGeneration.ToString(CultureInfo.InvariantCulture))
                .Attr("data-completion-start", start.ToString(CultureInfo.InvariantCulture))
                .Attr("data-completion-total", completion.Items.Count.ToString(CultureInfo.InvariantCulture));
            if (revealSelection) _completionList.Attr("data-completion-reveal", "true");
            _completionBefore!.Attr("style", $"height: calc(var(--completion-row-height, 1.42rem) * {start})");
            _completionAfter!.Attr("style", $"height: calc(var(--completion-row-height, 1.42rem) * {completion.Items.Count - start - length})");
            for (var row = 0; row < length; row++) {
                var index = start + row;
                var candidate = completion.Items[index];
                DomElement item;
                if (row < _completionItems.Count) {
                    var rendered = _completionItems[row];
                    item = rendered.Element;
                    if (rendered.Label != candidate.DisplayLabel) {
                        item.Text(candidate.DisplayLabel);
                        _completionItems[row] = (item, candidate.DisplayLabel);
                    }
                }
                else {
                    item = DomElement.Create("div").Class("completion-item").Text(candidate.DisplayLabel);
                    _completionList.InsertBefore(item, _completionAfter);
                    _completionItems.Add((item, candidate.DisplayLabel));
                }
                var selected = index == _completionIndex;
                item.ToggleClass("selected", selected && !_completionSoft);
                item.ToggleClass("soft-selected", selected && _completionSoft);
                item.Id($"{_cellId}-suggestion-{index}").Attr("role", "option")
                    .Attr("aria-selected", selected && !_completionSoft ? "true" : "false")
                    .Attr("aria-disabled", candidate.Edit is null || !ReferenceEquals(_completionDocument, State.Value.Doc)
                        || State.Value.Selection.Main.Head != _completionPosition ? "true" : "false")
                    .Attr("aria-posinset", (index + 1).ToString(CultureInfo.InvariantCulture))
                    .Attr("aria-setsize", completion.Items.Count.ToString(CultureInfo.InvariantCulture))
                    .Attr("data-completion-generation", _completionGeneration.ToString(CultureInfo.InvariantCulture))
                    .Attr("data-completion-index", index.ToString(CultureInfo.InvariantCulture));
            }
            while (_completionItems.Count > length) {
                var item = _completionItems[^1].Element;
                item.Remove(); item.Dispose(); _completionItems.RemoveAt(_completionItems.Count - 1);
            }
            var selectedRow = _completionIndex - start;
            _completionCount!.Text($"{_completionIndex + 1} / {completion.Items.Count}");
            var position = completion.Items[0].ReplaceStart;
            var line = State.Value.Doc.LineAt(position);
            if (isNew) _view.MountOverlay(popup, line.Number - 1, position - line.From);
            else _view.PlaceOverlay(popup, line.Number - 1, position - line.From);
            if (revealSelection && selectedRow >= 0 && selectedRow < length)
                DomRuntime.EnsureVisible(_completionList, _completionItems[selectedRow].Element);
            _view.SetCompletionSelection($"{_cellId}-suggestions", selectedRow >= 0 && selectedRow < length
                ? $"{_cellId}-suggestion-{_completionIndex}" : "");
            PlaceSignature();
            UpdateCompletionDescription();
        }
        finally { _view.EndUpdate(); }
    }

    private void SelectCompletion(int index)
    {
        if (_completion is not { } completion) return;
        _completionIndex = index;
        _completionChoice = completion.Items[index];
        _completionSoft = false;
        if (index < _completionWindowStart || index >= _completionWindowStart + _maximumRenderedCompletions)
            _completionWindowStart = CompletionWindowStart(index, completion.Items.Count);
        RenderCompletionPopup();
        PrepareCompletionWindow();
    }

    private bool HandleCompletionWindow(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length is not (2 or 3) || !int.TryParse(parts[0], out var generation) || generation != _completionGeneration
            || !int.TryParse(parts[1], out var start) || _completion is not { } completion) return false;
        if (parts.Length == 3 && int.TryParse(parts[2], out var pageSize)) _completionPageSize = Math.Clamp(pageSize, 1, _maximumRenderedCompletions);
        start = Math.Clamp(start, 0, Math.Max(0, completion.Items.Count - _maximumRenderedCompletions));
        if (start == _completionWindowStart) return true;
        _completionWindowStart = start;
        RenderCompletionPopup(revealSelection: false);
        PrepareCompletionWindow();
        return true;
    }

    private int _completionWindowPreparingStart = -1;
    private int _completionWindowPreparingIndex = -1;

    private void PrepareCompletionWindow()
    {
        if (_completionQueryState is not null || _completion is not { } completion || _completionDocument is not { } document) return;
        var start = _completionWindowStart;
        var index = _completionIndex;
        if (_completionWindowCancellation is not null && (_completionWindowPreparingStart != start || _completionWindowPreparingIndex != index)) {
            _completionWindowCancellation.Cancel();
            _completionWindowCancellation = null;
        }
        var pending = completion.Items.Skip(start).Take(_maximumRenderedCompletions).Any(static item => item.Edit is null);
        var characters = completion.Items[index] is { Item: not null, CommitEditsComplete: false };
        if (!pending && !characters || _completionWindowCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _completionWindowCancellation = cancellation;
        _completionWindowPreparingStart = start;
        _completionWindowPreparingIndex = index;
        _ = PrepareCompletionWindowAsync(completion, document, _completionPosition, start, index, cancellation, _completionGeneration);
    }

    private async Task PrepareCompletionWindowAsync(CompletionResult completion, Text document, int position, int start, int index,
        CancellationTokenSource cancellation, int generation)
    {
        try {
            var source = document.SliceDoc();
            if (completion.Items[index] is { Edit: null } pendingSelection) {
                var prepared = await CSharpCompletionProvider.PrepareWindowAsync(completion with { Items = [pendingSelection] }, source, position, -1, cancellation.Token);
                if (!IsCurrent(completion)) return;
                _completion = completion = completion.WithPreparedWindow(index, 1, prepared);
                RenderCompletionPopup(revealSelection: false);
            }
            if (completion.Items[index] is { Item: not null, CommitEditsComplete: false } candidate) {
                var selected = completion with { Items = [candidate], Query = candidate.PreparedQuery ?? completion.Query };
                var common = await CSharpCompletionProvider.PrepareWindowAsync(selected, source, position, 0, cancellation.Token);
                if (!IsCurrent(completion)) return;
                _completion = completion = completion.WithPreparedWindow(index, 1, common);
                RenderCompletionPopup(revealSelection: false);
            }
            for (var row = start; row < Math.Min(completion.Items.Count, start + _maximumRenderedCompletions); row++) {
                if (completion.Items[row].Edit is not null) continue;
                var pending = completion with { Items = [completion.Items[row]] };
                var prepared = await CSharpCompletionProvider.PrepareWindowAsync(pending, source, position, -1, cancellation.Token);
                if (!IsCurrent(completion)) return;
                _completion = completion = completion.WithPreparedWindow(row, 1, prepared);
                RenderCompletionPopup(revealSelection: false);
            }
            if (completion.Items[index] is { Item: not null, CommitEditsComplete: false }) {
                var selected = completion with { Items = [completion.Items[index]], Query = completion.Items[index].PreparedQuery ?? completion.Query };
                var characters = await CSharpCompletionProvider.PrepareAsync(selected, source, position, cancellation.Token);
                if (!IsCurrent(completion)) return;
                _completion = completion.WithPreparedWindow(index, 1, characters);
                RenderCompletionPopup(revealSelection: false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested && generation == _completionGeneration) DomRuntime.ReportError(error.ToString()); }
        finally {
            if (ReferenceEquals(_completionWindowCancellation, cancellation)) _completionWindowCancellation = null;
            cancellation.Dispose();
        }
        bool IsCurrent(CompletionResult expected) => !cancellation.IsCancellationRequested && generation == _completionGeneration
            && ReferenceEquals(_completion, expected) && _completionIndex == index
            && ReferenceEquals(State.Value.Doc, document) && State.Value.Selection.Main.Head == position;
    }

    private void UpdateCompletionDescription()
    {
        if (_completion is not { } completion || _completionDescription is not { } description) return;
        var candidate = completion.Items[_completionIndex];
        if (candidate.Edit is null || _completionDocument is null) {
            _completionDescriptionCancellation?.Cancel();
            _completionDescriptionCancellation = null;
            _completionDescriptionCandidate = null;
            _completionDescriptionDocument = null;
            description.Text("").Attr("aria-busy", "true");
            return;
        }
        if (_completionDescriptionCandidate is { } previous && previous.SameIdentity(candidate)
            && ReferenceEquals(previous.PreparedQuery, candidate.PreparedQuery)
            && ReferenceEquals(_completionDescriptionDocument, _completionDocument)) return;
        _completionDescriptionCancellation?.Cancel();
        _completionDescriptionCandidate = candidate;
        _completionDescriptionDocument = _completionDocument;
        description.Attr("aria-busy", "true");
        var cancellation = new CancellationTokenSource();
        _completionDescriptionCancellation = cancellation;
        _ = ShowCompletionDescriptionAsync(completion, candidate, description, cancellation);
    }

    private async Task ShowCompletionDescriptionAsync(CompletionResult completion, CompletionCandidate candidate,
        DomElement description, CancellationTokenSource cancellation)
    {
        try {
            var text = await CSharpCompletionProvider.GetDescriptionAsync(completion, candidate, cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_completionDescription, description)) {
                description.Text(text).Attr("aria-busy", "false");
                RenderCompletionPlacement(completion);
                RevealCompletionSelection();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested) DomRuntime.ReportError(error.ToString()); }
        finally {
            if (ReferenceEquals(_completionDescriptionCancellation, cancellation)) _completionDescriptionCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RenderCompletionPlacement(CompletionResult completion)
    {
        if (_completionPopup is not { } popup) return;
        var position = completion.Items[0].ReplaceStart;
        var line = State.Value.Doc.LineAt(position);
        _view.PlaceOverlay(popup, line.Number - 1, position - line.From);
    }

    private void RevealCompletionSelection()
    {
        var row = _completionIndex - _completionWindowStart;
        if (_completionList is not null && row >= 0 && row < _completionItems.Count)
            DomRuntime.EnsureVisible(_completionList, _completionItems[row].Element);
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

    private void HideCompletionPopup()
    {
        _completionWindowCancellation?.Cancel();
        _completionWindowCancellation = null;
        if (_signaturePopup is not null) {
            _signaturePopup.Remove();
            _signaturePopup.ToggleClass("combined", false);
        }
        _completionDescriptionCancellation?.Cancel();
        _completionDescriptionCancellation = null;
        _completionDescriptionCandidate = null;
        _completionDescriptionDocument = null;
        _completionDocument = null;
        _view.SetCompletionSelection("", "");
        ClearCompletionItems();
        if (_completionPopup is { } popup) {
            DomRuntime.ClearOverlayPlacement(popup);
        }
        _completionPopup?.Remove();
        _completionPopup?.Dispose();
        _completionPopup = null;
        PlaceSignature();
        _completionList?.Dispose();
        _completionList = null;
        _completionDescription?.Dispose();
        _completionDescription = null;
        _completionBefore?.Dispose(); _completionBefore = null;
        _completionAfter?.Dispose(); _completionAfter = null;
        _completionCount?.Dispose(); _completionCount = null;
        _completion = null;
    }

}
