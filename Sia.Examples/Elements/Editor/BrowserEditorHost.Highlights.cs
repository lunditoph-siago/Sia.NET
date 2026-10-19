using Sia.Examples.Dom;
using Sia.Examples.Notebook;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private const int k_HighlightDelayMilliseconds = 150;
    private string _highlightedSource;
    private int _highlightGeneration;
    private string? _highlightPendingSource;
    private CancellationTokenSource? _highlightCancellation;
    private Task<RangeSet<Decoration>>? _highlightWork;

    internal void UpdateHighlights(IReadOnlyList<HighlightRun> highlights)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelHighlight();
        _view.BeginUpdate();
        try {
            _highlightedSource = Source;
            Commit(State.Value.WithDecorations(EditorDecorations.FromHighlights(highlights)), revealSelection: false);
        }
        finally {
            _view.EndUpdate();
        }
    }

    private void ScheduleHighlight(string source)
    {
        if (source == _highlightPendingSource) {
            return;
        }
        if (source == _highlightedSource) {
            if (_highlightPendingSource is not null) {
                CancelHighlight();
            }
            return;
        }
        CancelHighlight();
        _highlightPendingSource = source;
        DomRuntime.ScheduleEvent(
            HighlightScheduleKey,
            $"highlight:{_cellId}:{_highlightGeneration}",
            k_HighlightDelayMilliseconds);
    }

    private bool HandleHighlightRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation) || generation != _highlightGeneration
            || _highlightPendingSource is not { } source) {
            return false;
        }
        if (_highlightWork is not null) {
            return true;
        }
        var cancellation = new CancellationTokenSource();
        _highlightCancellation = cancellation;
        var token = cancellation.Token;
        var work = CSharpCompilationEngine.RunRoslynAsync(() => {
            var highlights = CSharpHighlighter.Classify(source, token);
            token.ThrowIfCancellationRequested();
            var decorations = EditorDecorations.FromHighlights(highlights);
            token.ThrowIfCancellationRequested();
            return decorations;
        }, token);
        _highlightWork = work;
        _ = PublishHighlightAsync(work, token, _cellId, generation);
        return true;
    }

    private static async Task PublishHighlightAsync(
        Task<RangeSet<Decoration>> work, CancellationToken cancellation, string binding, int generation)
    {
        try { await work; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch { }
        if (cancellation.IsCancellationRequested) {
            return;
        }
        try {
            DomRuntime.ScheduleEvent($"highlight:{binding}", $"highlight-ready:{binding}:{generation}", 0);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
    }

    private bool HandleHighlightReady(string arguments)
    {
        if (!int.TryParse(arguments, out var generation) || generation != _highlightGeneration
            || _highlightWork is not { IsCompleted: true } work || _highlightPendingSource is not { } source) {
            return false;
        }
        _highlightPendingSource = null;
        _highlightWork = null;
        _highlightCancellation?.Dispose();
        _highlightCancellation = null;
        var decorations = work.GetAwaiter().GetResult();
        if (source != Source) {
            return false;
        }
        State.Set(State.Value.WithDecorations(decorations));
        _world.FlushReactive();
        _highlightedSource = source;
        return true;
    }

    private void CancelHighlight()
    {
        ++_highlightGeneration;
        DomRuntime.CancelScheduledEvent(HighlightScheduleKey);
        _highlightPendingSource = null;
        var cancellation = _highlightCancellation;
        var work = _highlightWork;
        _highlightCancellation = null;
        _highlightWork = null;
        if (cancellation is not null) {
            cancellation.Cancel();
            _ = ReleaseHighlightAsync(work!, cancellation);
        }
    }

    private static async Task ReleaseHighlightAsync(Task work, CancellationTokenSource cancellation)
    {
        try { await work.ConfigureAwait(false); }
        catch { }
        finally { cancellation.Dispose(); }
    }

    private string HighlightScheduleKey => $"highlight:{_cellId}";
}
