using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorRegistry(
    World world,
    ICompilationReferenceResolver references) : IDisposable, IAsyncDisposable
{
    private readonly World _world = world;
    private readonly CSharpCompletionProvider _completionProvider = new(references);
    private readonly Dictionary<string, (DomElement Container, BrowserEditorHost Editor)> _editors = [];
    internal event Action<string>? SourceChanged;
    internal event Action<string, SymbolDefinition?>? DefinitionReady;
    internal event Action<string, SymbolReferences?>? ReferencesReady;
    internal event Action<string, RenameTarget, string, int>? RenameReady;

    private void OnSourceChanged(BrowserEditorHost editor) { InvalidateLanguageContext(editor); SourceChanged?.Invoke(editor.BindingId); }
    private void OnDefinitionReady(BrowserEditorHost editor, SymbolDefinition? definition) => DefinitionReady?.Invoke(editor.BindingId, definition);
    private void OnReferencesReady(BrowserEditorHost editor, SymbolReferences? references) => ReferencesReady?.Invoke(editor.BindingId, references);
    private void OnRenameReady(BrowserEditorHost editor, RenameTarget target, string source, int position) => RenameReady?.Invoke(editor.BindingId, target, source, position);

    internal Func<string, string, CSharpQueryKind, CancellationToken, ValueTask<CSharpProjectSnapshot>>? LanguageContext {
        set {
            _completionProvider.CaptureSnapshot = value;
            InvalidateLanguageContext();
        }
    }

    internal void InvalidateLanguageContext(BrowserEditorHost? changed = null)
    {
        foreach (var entry in _editors.Values) {
            if (entry.Editor != changed) entry.Editor.InvalidateLanguageContext();
        }
    }

    internal void InvalidateLanguageContext(string cellId)
    {
        if (_editors.TryGetValue(cellId, out var entry)) entry.Editor.InvalidateLanguageContext();
    }

    public BrowserEditorHost Add(
        DomElement container,
        string cellId,
        string source,
        IReadOnlyList<HighlightRun> highlights)
    {
        if (_editors.ContainsKey(cellId)) {
            throw new InvalidOperationException($"Editor '{cellId}' is already mounted.");
        }
        var editor = new BrowserEditorHost(
            _world,
            container,
            cellId,
            source,
            highlights,
            _completionProvider);
        _editors.Add(cellId, (container, editor));
        editor.SourceChanged += OnSourceChanged;
        editor.DefinitionReady += OnDefinitionReady;
        editor.ReferencesReady += OnReferencesReady;
        editor.RenameReady += OnRenameReady;
        InvalidateLanguageContext(editor);
        return editor;
    }

    public bool Route(string payload)
    {
        var typeSeparator = payload.IndexOf(':');
        if (typeSeparator < 0) {
            return false;
        }
        var cellSeparator = payload.IndexOf(':', typeSeparator + 1);
        if (cellSeparator < 0) {
            return false;
        }

        var eventType = payload[..typeSeparator];
        var bindingId = payload[(typeSeparator + 1)..cellSeparator];
        return TryResolveCellId(bindingId, out var cellId)
            && _editors[cellId].Editor.Route(eventType, payload[(cellSeparator + 1)..]);
    }

    public bool TryResolveCellId(string bindingId, out string cellId)
    {
        var separator = bindingId.LastIndexOf('~');
        cellId = separator < 0 ? string.Empty : bindingId[..separator];
        return _editors.TryGetValue(cellId, out var entry)
            && entry.Editor.BindingId == bindingId;
    }

    internal bool CanExecuteCommand(string bindingId, EditorCommand command)
        => TryResolveCellId(bindingId, out var cellId) && _editors[cellId].Editor.CanExecuteCommand(command);

    internal bool ExecuteCommand(string bindingId, EditorCommand command)
    {
        if (!CanExecuteCommand(bindingId, command) || !TryResolveCellId(bindingId, out var cellId)) return false;
        var editor = _editors[cellId].Editor;
        var result = editor.ExecuteWorkbenchCommand(command);
        editor.Focus();
        return result;
    }

    public string GetSource(string cellId)
        => _editors.TryGetValue(cellId, out var entry)
            ? entry.Editor.Source
            : throw new KeyNotFoundException($"Editor '{cellId}' is not mounted.");

    internal bool TryCaptureWorkspaceBuffer(string cellId, out WorkspaceEditBuffer buffer)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            entry.Editor.SynchronizeNativeInput();
            if (entry.Editor.TryGetPosition(out var position)) { buffer = new(entry.Editor.Source, position); return true; }
        }
        buffer = null!; return false;
    }

    internal bool ApplyWorkspaceChange(string cellId, WorkspaceBufferChange change)
    {
        if (!_editors.TryGetValue(cellId, out var entry)) return false;
        entry.Editor.ApplyWorkspaceChange(change); return true;
    }

    internal Task<RenamePlan> PrepareRenameAsync(string binding, string source, int position, string name, CancellationToken cancellation)
    {
        if (!TryResolveCellId(binding, out var cellId)) throw new OperationCanceledException(cancellation);
        _editors[cellId].Editor.SynchronizeNativeInput();
        if (_editors[cellId].Editor.Source != source) throw new OperationCanceledException(cancellation);
        return _completionProvider.PrepareRenameAsync(binding, source, position, name, cancellation);
    }

    internal Task<DocumentSymbol[]> QuerySymbolsAsync(string binding, bool workspace, CancellationToken cancellation)
    {
        if (!TryResolveCellId(binding, out var cellId)) throw new OperationCanceledException(cancellation);
        var editor = _editors[cellId].Editor;
        editor.SynchronizeNativeInput();
        return _completionProvider.QuerySymbolsAsync(binding, editor.Source, workspace, cancellation);
    }

    internal string? GetBindingId(string cellId)
        => _editors.TryGetValue(cellId, out var entry) ? entry.Editor.BindingId : null;

    internal void Focus(string cellId)
    {
        if (_editors.TryGetValue(cellId, out var entry)) entry.Editor.Focus();
    }

    internal bool NavigateTo(string cellId, int start, int length)
    {
        if (!_editors.TryGetValue(cellId, out var entry)) return false;
        entry.Editor.NavigateTo(start, length);
        return true;
    }

    internal bool NavigateToLine(string cellId, int line, int column, int length = 0)
    {
        if (!_editors.TryGetValue(cellId, out var entry)) return false;
        entry.Editor.NavigateToLine(line, column, length);
        return true;
    }

    internal Task<LanguageIssue[]> QueryDiagnosticsAsync(string binding, CancellationToken cancellation, bool sourceBoundary = false)
    {
        if (!TryResolveCellId(binding, out var cellId)) throw new OperationCanceledException(cancellation);
        var editor = _editors[cellId].Editor;
        editor.SynchronizeNativeInput(sourceBoundary);
        return _completionProvider.QueryDiagnosticsAsync(binding, editor.Source, cancellation);
    }

    public void SynchronizeNativeInput(string cellId)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            entry.Editor.SynchronizeNativeInput();
        }
    }

    public BrowserEditorHost? Update(string cellId, string source, IReadOnlyList<HighlightRun> highlights)
    {
        if (!_editors.TryGetValue(cellId, out var entry)) {
            return null;
        }
        if (entry.Editor.Source == source) {
            entry.Editor.UpdateHighlights(highlights);
            return entry.Editor;
        }
        var hasPosition = entry.Editor.TryGetPosition(out var position);
        var replacement = Replace(cellId, source, highlights);
        if (hasPosition) {
            replacement.RestorePosition(position);
        }
        return replacement;
    }

    public BrowserEditorHost Replace(string cellId, string source, IReadOnlyList<HighlightRun> highlights)
    {
        var container = _editors[cellId].Container;
        Remove(cellId);
        return Add(container, cellId, source, highlights);
    }

    public bool TryGetSource(string cellId, out string source)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            source = entry.Editor.Source;
            return true;
        }
        source = string.Empty;
        return false;
    }

    public bool TryGetPosition(string cellId, out EditorMemento memento)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            return entry.Editor.TryGetPosition(out memento);
        }
        memento = default!;
        return false;
    }

    internal bool TryGetCursorPosition(string cellId, out (int Line, int Column, int Selected) position)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            position = entry.Editor.CursorPosition;
            return true;
        }
        position = default;
        return false;
    }

    public void RestorePosition(string cellId, EditorMemento memento)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            entry.Editor.RestorePosition(memento);
        }
    }

    public void Remove(string cellId)
    {
        if (_editors.Remove(cellId, out var entry)) {
            entry.Editor.Dispose();
            InvalidateLanguageContext();
        }
    }

    public void Dispose()
    {
        SourceChanged = null;
        DefinitionReady = null;
        ReferencesReady = null;
        RenameReady = null;
        foreach (var entry in _editors.Values) {
            entry.Editor.Dispose();
        }
        _editors.Clear();
        _completionProvider.Dispose();
    }

    public async ValueTask DisposeAsync() { Dispose(); await _completionProvider.DisposeAsync(); }
}
