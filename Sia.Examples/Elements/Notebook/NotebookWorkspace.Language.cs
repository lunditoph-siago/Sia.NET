using Sia.Examples.Editor;

namespace Sia.Examples.Notebook;

public sealed partial class NotebookWorkspace
{
    private void RestoreReferenceFocus()
    {
        if (!_view.Editors.TryResolveCellId(_referenceReturnBinding ?? "", out var script)) return;
        var layout = _mount.GetState<NotebookCellState>().Value;
        var tab = layout.GetTabForWindow(layout.GetScriptWindow(script).Id);
        if (NotebookCellLayout.FindGroupContaining(layout, tab.Id) is null) return;
        _view.RevealEditor(script);
        _view.Editors.Focus(script);
    }

    private void RestoreRenameFocus()
    {
        if (!_view.Editors.TryResolveCellId(_renameReturnBinding ?? "", out var script)) return;
        _view.RevealEditor(script);
        _view.Editors.Focus(script);
    }

    private void OpenScriptChoice(EditorNavigationChoice choice)
    {
        if (_disposed || !_session.ContainsScript(choice.Id)) return;
        UpdateCell(state => NotebookCellLayout.OpenTabIntoHome(state,
            state.GetTabForWindow(state.GetScriptWindow(choice.Id).Id).Id));
        _view.RevealEditor(choice.Id);
        _view.Editors.Focus(choice.Id);
    }

    private ValueTask<CSharpProjectSnapshot> CaptureLanguageSnapshot(
        string binding, string source, CSharpQueryKind kind, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_disposed || !_view.Editors.TryResolveCellId(binding, out var active)) {
            throw new OperationCanceledException(cancellation);
        }
        if (kind is CSharpQueryKind.DocumentSymbols or CSharpQueryKind.WorkspaceSymbols) {
            return ValueTask.FromResult(CaptureSymbolDocuments(active, source, kind));
        }

        string SourceFor(string id)
            => id == active ? source
                : _view.Editors.TryGetSource(id, out var live) ? live : _session.GetState(id).Source;

        var cells = _session.GetProgramCells(
            active, SourceFor, includeFollowing: kind == CSharpQueryKind.References);
        var readOnly = cells.SelectMany(static cell => cell.Files)
            .Where(file => !_session.IsScriptEditable(file.Id))
            .Select(static file => file.Id)
            .ToHashSet(StringComparer.Ordinal);
        return new(CSharpCompilationEngine.RunRoslynAsync(
            () => CSharpProjectSnapshot.ForNotebook(active, cells, cancellation, readOnly), cancellation));
    }

    private CSharpProjectSnapshot CaptureSymbolDocuments(string active, string source, CSharpQueryKind kind)
    {
        var documents = _session.GetScriptFiles()
            .Where(file => kind == CSharpQueryKind.WorkspaceSymbols || file.Id == active)
            .Select(file => new CSharpSourceDocument(
                file.Id, file.Name, file.Path,
                file.Id == active ? source : ReadScriptSource(file.Id), _session.IsScriptEditable(file.Id)))
            .ToArray();
        return new(active, documents);
    }

    private void HandleEditorSourceChanged(string binding)
    {
        if (!_disposed && _view.Editors.TryResolveCellId(binding, out var cellId)) {
            _renamePanel.Invalidate();
            _referenceResults.Clear();
            _view.UpdateEditorDirty(cellId);
        }
    }

    private void HandleEditorDefinition(string binding, SymbolDefinition? definition)
    {
        if (_disposed || definition?.Locations.FirstOrDefault() is not { } target) return;
        _referenceReturnBinding = binding;
        _referenceResults.Clear();
        NavigateEditorSource(target);
    }

    private void HandleEditorReferences(string binding, SymbolReferences? references)
    {
        if (_disposed || !_view.Editors.TryResolveCellId(binding, out _)) return;
        _referenceReturnBinding = binding;
        _referenceResults.Show(references);
        _view.RevealLanguageResults(_referenceResults.Element);
        _referenceResults.Focus();
    }

    internal bool RouteReferences(string argument) => !_disposed && _referenceResults.Route(argument);
    internal bool RouteNavigationPicker(string argument) => !_disposed && _navigationPicker.Route(argument);

    private IReadOnlyList<EditorNavigationChoice> CaptureFileChoices()
        => [.._session.GetScriptFiles().Select(static file => new EditorNavigationChoice(file.Id, file.Name, file.Path))];

    private string ReadScriptSource(string id)
    {
        _view.Editors.SynchronizeNativeInput(id);
        return _view.Editors.TryGetSource(id, out var live) ? live : _session.GetState(id).Source;
    }

    internal void RouteRename(string argument) => Track(_renamePanel.RouteAsync(argument));
    private void ReportRename(string message) => _renamePanel.Report(message);

    private IReadOnlyDictionary<string, WorkspaceEditBuffer> CaptureWorkspaceBuffers(RenamePlan plan)
    {
        var buffers = new Dictionary<string, WorkspaceEditBuffer>(StringComparer.Ordinal);
        if (!_session.ContainsScript(plan.Origin)) return buffers;
        var cells = _session.GetProgramCells(
            plan.Origin, id => _session.GetState(id).Source, includeFollowing: true);
        foreach (var file in cells.SelectMany(static cell => cell.Files)) {
            if (_view.Editors.TryCaptureWorkspaceBuffer(file.Id, out var buffer)) {
                buffers.Add(file.Id, buffer with { Editable = _session.IsScriptEditable(file.Id) });
            }
            else {
                buffers.Add(file.Id, new(file.Source, new(EditorSelection.Single(0), 0, 0, 0, 0, 0), false));
            }
        }
        return buffers;
    }

    private void CommitWorkspaceBuffers(IReadOnlyList<WorkspaceBufferChange> changes)
    {
        foreach (var change in changes) {
            if (!_view.Editors.ApplyWorkspaceChange(change.DocumentId, change)) {
                throw new InvalidOperationException("An affected editor is no longer available.");
            }
            _view.UpdateEditorDirty(change.DocumentId);
        }
    }

    private void NavigateEditorSource(SourcePosition target)
    {
        if (_disposed) return;
        if (target.Generated) {
            _referenceResults.ShowSource(target);
            _view.RevealLanguageResults(_referenceResults.Element);
            return;
        }
        if (!_session.ContainsScript(target.DocumentId)) return;
        UpdateCell(state => NotebookCellLayout.OpenTabIntoHome(state,
            state.GetTabForWindow(state.GetScriptWindow(target.DocumentId).Id).Id));
        var targetBinding = _view.Editors.GetBindingId(target.DocumentId) ?? target.DocumentId;
        if (_view.Editors.TryResolveCellId(targetBinding, out var cellId)
            && _view.Editors.GetSource(cellId) == target.Source) {
            _view.Editors.NavigateToLine(cellId, target.Line, target.Column, target.Length);
            _view.RevealEditor(cellId);
            _referenceReturnBinding = targetBinding;
        }
        else if (!_session.IsScriptEditable(target.DocumentId)
            && _session.GetState(target.DocumentId).Source == target.Source) {
            _referenceResults.ShowSource(target);
            _view.RevealLanguageResults(_referenceResults.Element);
        }
    }
}
