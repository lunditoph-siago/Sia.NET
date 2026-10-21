namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private readonly EditorLatestRequest<EditorState> _languageRequests = new();
    private EditorCommandId _languageCommand;
    internal event Action<BrowserEditorHost, SymbolDefinition?>? DefinitionReady;
    internal event Action<BrowserEditorHost, SymbolReferences?>? ReferencesReady;
    internal event Action<BrowserEditorHost, RenameTarget, string, int>? RenameReady;

    internal void NavigateTo(int start, int length)
    {
        var state = State.Value;
        start = System.Math.Clamp(start, 0, state.Doc.Length);
        Commit(state.Apply(new() { Selection = EditorSelection.Single(start, System.Math.Clamp(start + length, start, state.Doc.Length)) }));
        _view.RevealSelection();
        _view.Focus();
    }

    internal void NavigateToLine(int line, int column, int length = 0)
    {
        var document = State.Value.Doc;
        var target = document.Line(System.Math.Clamp(line, 1, document.Lines));
        NavigateTo(target.From + System.Math.Clamp(column - 1, 0, target.Length), length);
    }

    private bool StartLanguageCommand(EditorCommand command, BrowserSelection nativeSelection)
    {
        Commit(ApplyNativeSelection(State.Value, nativeSelection));
        CancelLanguageCommand();
        CancelCompletion();
        CancelSignature();
        _languageCommand = command.Id;
        var request = _languageRequests.Start(State.Value);
        _ = RunLanguageCommandAsync(request, command.Id);
        return true;
    }

    private async Task RunLanguageCommandAsync(EditorLatestRequest<EditorState>.Request request, EditorCommandId command)
    {
        var state = request.Context;
        try {
            var source = state.Doc.SliceDoc();
            var position = state.Selection.Main.Head;
            if (command == EditorCommandId.GoToDefinition) {
                var definition = await _completionProvider.QueryDefinitionAsync(_cellId, source, position, request.Token);
                if (request.IsCurrent
                    && ReferenceEquals(state.Doc, State.Value.Doc) && state.Selection.Eq(State.Value.Selection)) DefinitionReady?.Invoke(this, definition);
            }
            else if (command == EditorCommandId.FindReferences) {
                var references = await _completionProvider.QueryReferencesAsync(_cellId, source, position, request.Token);
                if (request.IsCurrent
                    && ReferenceEquals(state.Doc, State.Value.Doc) && state.Selection.Eq(State.Value.Selection)) ReferencesReady?.Invoke(this, references);
            }
            else if (command == EditorCommandId.RenameSymbol) {
                var target = await _completionProvider.QueryRenameTargetAsync(_cellId, source, position, request.Token);
                if (request.IsCurrent
                    && ReferenceEquals(state.Doc, State.Value.Doc) && state.Selection.Eq(State.Value.Selection)) RenameReady?.Invoke(this, target, source, position);
            }
            else {
                var changes = await _completionProvider.FormatAsync(_cellId, source, EditorState.TabSize, request.Token);
                if (request.IsCurrent
                    && ReferenceEquals(state.Doc, State.Value.Doc) && changes.Length > 0) Commit(State.Value.Apply(new() { Changes = changes }));
            }
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception error) { if (request.IsCurrent) Sia.Examples.Dom.DomRuntime.ReportError(error.ToString()); }
        finally {
            request.Dispose();
        }
    }

    private void CancelLanguageCommand() => _languageRequests.Cancel();
}
