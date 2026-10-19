namespace Sia_Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private CancellationTokenSource? _languageCancellation;
    private int _languageGeneration;
    private EditorCommandId _languageCommand;
    internal event Action<BrowserEditorHost, SymbolDefinition?>? DefinitionReady;
    internal event Action<BrowserEditorHost, SymbolReferences?>? ReferencesReady;

    internal void NavigateTo(int start, int length)
    {
        var state = State.Value;
        start = Math.Clamp(start, 0, state.Doc.Length);
        Commit(state.Apply(new() { Selection = EditorSelection.Single(start, Math.Clamp(start + length, start, state.Doc.Length)) }));
        _view.RevealSelection();
        _view.Focus();
    }

    internal void NavigateToLine(int line, int column, int length = 0)
    {
        var document = State.Value.Doc;
        var target = document.Line(Math.Clamp(line, 1, document.Lines));
        NavigateTo(target.From + Math.Clamp(column - 1, 0, target.Length), length);
    }

    private bool StartLanguageCommand(EditorCommand command, BrowserSelection nativeSelection)
    {
        Commit(ApplyNativeSelection(State.Value, nativeSelection));
        CancelLanguageCommand();
        CancelCompletion();
        CancelSignature();
        _languageCommand = command.Id;
        var cancellation = new CancellationTokenSource();
        _languageCancellation = cancellation;
        _ = RunLanguageCommandAsync(State.Value, command.Id, cancellation, _languageGeneration);
        return true;
    }

    private async Task RunLanguageCommandAsync(EditorState state, EditorCommandId command, CancellationTokenSource cancellation, int generation)
    {
        try {
            if (command == EditorCommandId.GoToDefinition) {
                var definition = await _completionProvider.QueryDefinitionAsync(_cellId, state.Doc.SliceDoc(), state.Selection.Main.Head, cancellation.Token);
                if (generation == _languageGeneration && !cancellation.IsCancellationRequested
                    && ReferenceEquals(state.Doc, State.Value.Doc) && state.Selection.Eq(State.Value.Selection)) DefinitionReady?.Invoke(this, definition);
            }
            else if (command == EditorCommandId.FindReferences) {
                var references = await _completionProvider.QueryReferencesAsync(_cellId, state.Doc.SliceDoc(), state.Selection.Main.Head, cancellation.Token);
                if (generation == _languageGeneration && !cancellation.IsCancellationRequested
                    && ReferenceEquals(state.Doc, State.Value.Doc) && state.Selection.Eq(State.Value.Selection)) ReferencesReady?.Invoke(this, references);
            }
            else {
                var changes = await _completionProvider.FormatAsync(_cellId, state.Doc.SliceDoc(), EditorState.TabSize, cancellation.Token);
                if (generation == _languageGeneration && !cancellation.IsCancellationRequested
                    && ReferenceEquals(state.Doc, State.Value.Doc) && changes.Length > 0) Commit(State.Value.Apply(new() { Changes = changes }));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested && generation == _languageGeneration) Sia_Examples.Dom.DomRuntime.ReportError(error.ToString()); }
        finally {
            if (ReferenceEquals(_languageCancellation, cancellation)) _languageCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelLanguageCommand()
    {
        _languageGeneration++;
        _languageCancellation?.Cancel();
        _languageCancellation = null;
    }
}
