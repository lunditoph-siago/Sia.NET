using System.Globalization;
using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private const int k_MaximumRenderedCompletions = 20;
    private readonly List<(DomElement Element, string Label)> _completionItems = [];
    private DomElement? _completionPopup;
    private DomElement? _completionList;
    private DomElement? _completionDescription;
    private CancellationTokenSource? _completionDescriptionCancellation;
    private CompletionCandidate? _completionDescriptionCandidate;
    private Text? _completionDescriptionDocument;
    private int _completionWindowStart;
    private int _completionPageSize = 8;
    private DomElement? _completionBefore;
    private DomElement? _completionAfter;
    private DomElement? _completionCount;

    private static int CompletionWindowStart(int selectedIndex, int count)
        => System.Math.Clamp(selectedIndex - k_MaximumRenderedCompletions / 2, 0, System.Math.Max(0, count - k_MaximumRenderedCompletions));

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
            var length = System.Math.Min(k_MaximumRenderedCompletions, completion.Items.Count - start);
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
            var caret = State.Value.Doc.LineAt(_completionPosition);
            if (isNew) _view.MountOverlay(popup, line.Number - 1, position - line.From, caret.Number - 1, _completionPosition - caret.From);
            else _view.PlaceOverlay(popup, line.Number - 1, position - line.From, caret.Number - 1, _completionPosition - caret.From);
            if (revealSelection && selectedRow >= 0 && selectedRow < length)
                DomRuntime.EnsureVisible(_completionList, _completionItems[selectedRow].Element);
            _view.SetCompletionSelection($"{_cellId}-suggestions", selectedRow >= 0 && selectedRow < length
                ? $"{_cellId}-suggestion-{_completionIndex}" : "", _completionGeneration);
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
        if (index < _completionWindowStart || index >= _completionWindowStart + k_MaximumRenderedCompletions)
            _completionWindowStart = CompletionWindowStart(index, completion.Items.Count);
        RenderCompletionPopup();
        PrepareCompletionWindow();
    }

    private bool HandleCompletionWindow(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length is not (2 or 3) || !int.TryParse(parts[0], out var generation) || generation != _completionGeneration
            || !int.TryParse(parts[1], out var start) || _completion is not { } completion) return false;
        if (parts.Length == 3 && int.TryParse(parts[2], out var pageSize)) _completionPageSize = System.Math.Clamp(pageSize, 1, k_MaximumRenderedCompletions);
        start = System.Math.Clamp(start, 0, System.Math.Max(0, completion.Items.Count - k_MaximumRenderedCompletions));
        if (start == _completionWindowStart) return true;
        _completionWindowStart = start;
        RenderCompletionPopup(revealSelection: false);
        PrepareCompletionWindow();
        return true;
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
        var caret = State.Value.Doc.LineAt(_completionPosition);
        _view.PlaceOverlay(popup, line.Number - 1, position - line.From, caret.Number - 1, _completionPosition - caret.From);
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
