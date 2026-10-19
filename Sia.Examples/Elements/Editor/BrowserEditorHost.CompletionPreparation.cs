using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private CancellationTokenSource? _completionWindowCancellation;

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
        var pending = completion.Items.Skip(start).Take(k_MaximumRenderedCompletions).Any(static item => item.Edit is null);
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
            for (var row = start; row < System.Math.Min(completion.Items.Count, start + k_MaximumRenderedCompletions); row++) {
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
}
