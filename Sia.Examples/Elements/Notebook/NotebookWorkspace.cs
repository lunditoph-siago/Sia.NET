using Sia;
using Sia.Reactive;
using Sia.Examples.Dom;
using Sia.Examples.Editor;

namespace Sia.Examples.Notebook;

public sealed partial class NotebookWorkspace : IAsyncDisposable
{
    internal void ConfigureKeyboard(EditorKeymap keymap) => _view.Editors.ConfigureKeyboard(keymap, CommandContext.Notebook);
    internal void FlushCommands() => _view.Editors.FlushCommands();
    internal event Action<EditorCommand, string> CommandInvoked {
        add => _view.Editors.CommandInvoked += value;
        remove => _view.Editors.CommandInvoked -= value;
    }

    private readonly NotebookSession _session;
    private readonly MetadataReferenceProvider _references;
    private readonly World _world;
    private readonly IWorkspaceStorage _storage;
    private readonly NotebookLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];
    private readonly BrowserNotebookView _view;
    private readonly EditorReferenceResults _referenceResults;
    private readonly EditorWorkspaceEdits _workspaceEdits;
    private readonly EditorRenamePanel _renamePanel;
    private readonly EditorNavigationPicker _navigationPicker;
    private string? _renameReturnBinding;
    private string? _referenceReturnBinding;
    private readonly ReactiveMount<NotebookViewProps> _mount;
    private NotebookSessionSnapshot _previousSnapshot;
    private NotebookDocument _previousDocument;
    private bool _disposed;
    private int _languageReferenceVersion;

    private NotebookInfo _info;

    private string? _version;
    private string _savedContent;

    public NotebookWorkspace(
        World world,
        IUiThread mainThread,
        NotebookDocument document,
        MetadataReferenceProvider references,
        NotebookInfo info,
        string? version,
        IWorkspaceStorage storage,
        NotebookLibrary library)
    {
        _world = world;
        _references = references;
        _languageReferenceVersion = references.Version;
        _storage = storage;
        _library = library;
        _info = info;
        _version = version;
        _savedContent = NotebookDocumentSerializer.Write(document);
        _session = new(mainThread, document, references);
        var cellState = NotebookCellState.Create(document);
        _view = new(world, document, references, cellState);
        _referenceResults = new("notebook-reference", NavigateEditorSource, RestoreReferenceFocus);
        _view.AttachLanguageResults(_referenceResults.Element);
        _workspaceEdits = new(CaptureWorkspaceBuffers, CommitWorkspaceBuffers, ReportRename);
        _renamePanel = new(_view.Editors, _workspaceEdits, "notebook-rename", RestoreRenameFocus);
        _view.Editors.RenameReady += (binding, target, source, position) => {
            _renameReturnBinding = binding;
            _renamePanel.Open(binding, target, source, position);
        };
        _view.Editors.LanguageContext = CaptureLanguageSnapshot;
        _navigationPicker = new("notebook-navigation-picker", CaptureFileChoices, OpenScriptChoice);
        _view.Editors.SourceChanged += HandleEditorSourceChanged;
        _view.Editors.DefinitionReady += HandleEditorDefinition;
        _view.Editors.ReferencesReady += HandleEditorReferences;
        _mount = world.Mount(
            NotebookViewComponent.Definition,
            new(_view, _session.Snapshot, cellState));
        _previousSnapshot = _session.Snapshot;
        _previousDocument = document;
        world.FlushReactive();
        _session.Changed += HandleSessionChanged;
    }

    public NotebookInfo Info => _info;

    internal bool CanExecuteCommand(EditorCommand command, string binding)
        => command.Id == EditorCommandId.QuickOpen && !_disposed
            || command.Id is EditorCommandId.DocumentSymbols or EditorCommandId.WorkspaceSymbols
                && !_disposed && _view.Editors.TryResolveCellId(binding, out _)
            || command.Context == CommandContext.Notebook && _view.Editors.TryResolveCellId(binding, out _)
            || _view.Editors.CanExecuteCommand(binding, command);

    internal bool ExecuteCommand(EditorCommand command, string binding)
    {
        if (command.Id == EditorCommandId.QuickOpen) {
            if (_disposed) return false;
            _navigationPicker.Open(() => {
                if (_view.Editors.TryResolveCellId(binding, out var origin)) _view.Editors.Focus(origin);
            });
            return true;
        }
        if (!_view.Editors.TryResolveCellId(binding, out var cellId)) return false;
        if (command.Id is EditorCommandId.DocumentSymbols or EditorCommandId.WorkspaceSymbols) {
            if (_disposed) return false;
            var workspace = command.Id == EditorCommandId.WorkspaceSymbols;
            Track(_navigationPicker.OpenSymbolsAsync(workspace, token => _view.Editors.QuerySymbolsAsync(binding, workspace, token),
                target => !_disposed && _session.ContainsScript(target.DocumentId)
                    && ReadScriptSource(target.DocumentId) == target.Source,
                NavigateEditorSource, () => _view.Editors.Focus(cellId)));
            return true;
        }
        if (command.IsEditor) {
            var handled = _view.Editors.ExecuteCommand(binding, command);
            if (handled) _view.UpdateEditorDirty(cellId);
            return handled;
        }
        switch (command.Id) {
            case EditorCommandId.NotebookRun: ToggleRun(cellId); return true;
            case EditorCommandId.NotebookSave: Save(cellId); return true;
            default: return false;
        }
    }

    public string? Version => _version;

    public bool CanDelete
        => _info.Origin == NotebookOrigin.User && _info.Key.Length > 0;

    internal (string Content, bool Dirty) CaptureNavigationState()
    {
        ThrowIfDisposed();
        var document = _session.ToDocument();
        var sections = document.Sections.Select(section => section with {
            Blocks = section.Blocks.Select(block => block switch {
                CodeCellBlock { Editable: true } cell => cell with {
                    Scripts = cell.Scripts.Select(script => {
                        _view.Editors.SynchronizeNativeInput(script.Id);
                        return script with { InitialSource = _view.Editors.GetSource(script.Id) };
                    }).ToArray(),
                },
                var other => other,
            }).ToArray(),
        }).ToArray();
        var content = NotebookDocumentSerializer.Write(document with { Sections = sections });
        return (content, content != _savedContent);
    }

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await _session.EnsurePackagesAsync(_lifetime.Token);
    }

    public void StartCompile(string cellId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        Track(_session.CompileThroughAsync(cellId, _lifetime.Token));
    }

    public void StartRun(string cellId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        Track(_session.RunThroughAsync(cellId, _lifetime.Token));
    }

    public void ToggleRun(string cellId)
    {
        ThrowIfDisposed();
        var phase = _session.GetState(cellId).Phase;
        if (phase is CellPhase.Compiling or CellPhase.Running) {
            Stop();
        }
        else {
            StartRun(cellId);
        }
    }

    public void ActivateTab(string tabId)
        => UpdateCell(state => NotebookCellLayout.OpenTabIntoHome(state, tabId));

    public void OpenWindow(string windowId)
        => UpdateCell(state => NotebookCellLayout.OpenWindow(state, windowId));

    public void CloseWindow(string windowId)
        => UpdateCell(state => NotebookCellLayout.CloseWindow(state, windowId));

    public void Cell(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 5
            || !long.TryParse(parts[0], out var revision)
            || !Enum.TryParse<CellDropPosition>(
                parts[3],
                ignoreCase: true,
                out var position)
            || !int.TryParse(parts[4], out var targetIndex)) {
            return;
        }
        UpdateCell(state => NotebookCellLayout.Cell(
            state,
            parts[1],
            parts[2],
            position,
            targetIndex),
            revision,
            acknowledge: true);
    }

    public void Detach(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 6
            || !long.TryParse(parts[0], out var revision)
            || !int.TryParse(parts[2], out var pointerX)
            || !int.TryParse(parts[3], out var pointerY)
            || !int.TryParse(parts[4], out var viewportWidth)
            || !int.TryParse(parts[5], out var viewportHeight)) {
            return;
        }
        UpdateCell(state => NotebookCellLayout.Detach(
            state,
            parts[1],
            pointerX,
            pointerY,
            viewportWidth,
            viewportHeight),
            revision,
            acknowledge: true);
    }

    public void NormalizeCells(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var viewportWidth)
            || !int.TryParse(parts[1], out var viewportHeight)) {
            return;
        }
        UpdateCell(state => NotebookCellLayout.NormalizeFloatingHosts(
            state,
            viewportWidth,
            viewportHeight));
    }

    public void ResizeCellSplit(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[0], out var revision)
            || !double.TryParse(
                parts[2],
                System.Globalization.CultureInfo.InvariantCulture,
                out var ratio)) {
            return;
        }
        UpdateCell(
            state => NotebookCellLayout.ResizeSplit(state, parts[1], ratio),
            revision,
            acknowledge: true);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _session.Interrupt();
    }

    public void Save(string cellId)
    {
        ThrowIfDisposed();
        _view.Editors.SynchronizeNativeInput(cellId);
        _session.UpdateCellSource(
            cellId,
            _view.Editors.GetSource(cellId),
            _lifetime.Token);
        _view.EndEditorEdit(cellId);
    }

    public void Discard(string cellId)
    {
        ThrowIfDisposed();
        _view.DiscardEditor(cellId);
    }

    public void SetCellScope(string cellId)
    {
        ThrowIfDisposed();
        using var input = DomElement.TryFind(NotebookElementIds.ScopeInput(cellId));
        var value = input?.Value().Trim() ?? string.Empty;
        SynchronizeEditors();
        _session.SetCellScope(cellId, value);
        _view.UpdateCellScope(cellId, value);
    }

    public void BeginEditorEdit(string bindingId)
    {
        ThrowIfDisposed();
        if (_view.Editors.TryResolveCellId(bindingId, out var cellId)) {
            _view.Editors.Activate(bindingId);
            _view.BeginEditorEdit(cellId);
        }
    }

    public void InsertCell(string afterBlockId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.InsertCell(afterBlockId);
    }

    public void AddScript(string cellId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.AddScript(cellId);
    }

    public void RemoveScript(string scriptId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.RemoveScript(scriptId);
    }

    public void RenameScript(string arguments)
    {
        ThrowIfDisposed();
        var separator = arguments.IndexOf(':');
        if (separator < 0) {
            return;
        }
        var scriptId = arguments[..separator];
        var name = Uri.UnescapeDataString(arguments[(separator + 1)..]);
        SynchronizeEditors();
        _session.RenameScript(scriptId, name);
    }

    public void InsertParagraph(string afterBlockId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.InsertParagraph(afterBlockId);
    }

    public void SaveParagraph(string blockId)
    {
        ThrowIfDisposed();
        using var textarea = DomElement.TryFind(NotebookElementIds.Paragraph(blockId));
        if (textarea is null) {
            return;
        }
        var text = textarea.Value();
        SynchronizeEditors();
        _session.SetParagraphText(blockId, text);
    }

    public void RemoveCell(string cellId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.RemoveCell(cellId);
    }

    public void MoveCell(string cellId, int offset)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.MoveCell(cellId, offset);
    }

    public void InsertSection(
        int? afterIndex, string title, NotebookBlockKind starterKind = NotebookBlockKind.Code)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.InsertSection(afterIndex, title, starterKind);
    }

    public void RemoveSection(string sectionId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.RemoveSection(sectionId);
    }

    public void RenameSection(string sectionId)
    {
        ThrowIfDisposed();
        using var input = DomElement.TryFind(NotebookElementIds.SectionTitleInput(sectionId));
        var title = input?.Value().Trim();
        if (string.IsNullOrEmpty(title)) {
            return;
        }
        SynchronizeEditors();
        _session.RenameSection(sectionId, title);
    }

    public void RenameTitle()
    {
        ThrowIfDisposed();
        using var input = DomElement.TryFind("notebook-title-input");
        var title = input?.Value().Trim();
        if (string.IsNullOrEmpty(title)) {
            return;
        }
        SynchronizeEditors();
        _session.SetTitle(title);
    }

    public async Task<(string? Version, WorkspaceConflictException? Conflict)>
        SaveToStorageAsync(bool force = false)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        var document = _session.ToDocument();
        var xml = NotebookDocumentSerializer.Write(document);

        var title = document.Title.Length > 0 ? document.Title : "(untitled)";
        var hasStorageKey = _info.Origin == NotebookOrigin.User && _info.Key.Length > 0;
        var key = hasStorageKey ? _info.Key : Guid.NewGuid().ToString("N")[..12];
        try {
            var written = await _storage.WriteFileAsync(
                NotebookLibrary.PathOf(key),
                hasStorageKey && !force ? _version : null,
                xml,
                _lifetime.Token);
            _info = new NotebookInfo(title, "", key, NotebookOrigin.User);
            _version = written.Version;
            _savedContent = xml;
            await _library.RefreshAsync(_lifetime.Token);
            return (written.Version, null);
        }
        catch (WorkspaceConflictException conflict) {
            return (null, conflict);
        }
    }

    public async Task<bool> DeleteFromStorageAsync()
    {
        ThrowIfDisposed();
        if (!CanDelete) {
            return false;
        }
        await _storage.DeleteEntryAsync(NotebookLibrary.PathOf(_info.Key), _version, _lifetime.Token);
        await _library.RefreshAsync(_lifetime.Token);
        return true;
    }

    public async Task AddPackageAsync()
    {
        ThrowIfDisposed();
        if (_view.ReadPackageAddition() is not { } package) {
            return;
        }
        await _session.AddPackageAsync(package, _lifetime.Token);
    }

    public bool RouteEditorEvent(string payload)
    {
        ThrowIfDisposed();
        var first = payload.IndexOf(':');
        var second = first < 0 ? -1 : payload.IndexOf(':', first + 1);
        var bindingId = first >= 0 && second > first
            ? payload[(first + 1)..second]
            : string.Empty;
        if (!_view.Editors.TryResolveCellId(bindingId, out var cellId)) {
            return false;
        }
        var handled = _view.Editors.Route(payload);
        if (handled && payload[..first] is "input" or "text" or "mut" or "key" or "complete") {
            _view.UpdateEditorDirty(cellId);
        }
        return handled;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _renamePanel.Dispose();
        _navigationPicker.Dispose();
        _workspaceEdits.Dispose();
        _lifetime.Cancel();
        _session.Interrupt();
        foreach (var operation in _operations.ToArray()) {
            await operation;
        }

        _session.Changed -= HandleSessionChanged;
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _session.Dispose();
        try { await _view.Editors.DisposeAsync(); }
        finally {
            _referenceResults.Dispose();
            _view.Dispose();
            _lifetime.Dispose();
        }
    }

    private void SynchronizeEditors()
    {
        foreach (var script in _session.Scripts) {
            if (!_session.IsScriptEditable(script.Id)) {
                continue;
            }
            _view.Editors.SynchronizeNativeInput(script.Id);
            var source = _view.Editors.GetSource(script.Id);
            if (source != _session.GetState(script.Id).Source) {
                _session.UpdateCellSource(
                    script.Id,
                    source,
                    _lifetime.Token);
            }
            _view.EndEditorEdit(script.Id);
        }
    }

    private void Track(Task operation)
    {
        var observed = ObserveAsync(operation);
        _operations.Add(observed);
        _ = RemoveWhenCompleteAsync(observed);
    }

    private void UpdateCell(
        Func<NotebookCellState, NotebookCellState> update,
        long? expectedRevision = null,
        bool acknowledge = false)
    {
        ThrowIfDisposed();
        var state = _mount.GetState<NotebookCellState>();
        if (expectedRevision is { } expected && expected != state.Value.Revision) {
            return;
        }
        var next = update(state.Value);
        if (next == state.Value && !acknowledge) {
            return;
        }
        if (!NotebookCellLayout.IsValid(next)) {
            throw new InvalidOperationException("The cell operation produced an invalid layout.");
        }
        next = next with { Revision = state.Value.Revision + 1 };
        state.Set(next);
        _world.FlushReactive();
    }

    private async Task ObserveAsync(Task operation)
    {
        try {
            await operation;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) {
        }
        catch (Exception error) {
            DomRuntime.ReportError(error.ToString());
        }
    }

    private async Task RemoveWhenCompleteAsync(Task operation)
    {
        await operation;
        _operations.Remove(operation);
    }

    private void HandleSessionChanged(
        NotebookSessionSnapshot snapshot,
        bool structural)
    {
        if (_disposed) {
            return;
        }
        if (structural) {
            ReconcileDocument();
        }
        if (structural || _languageReferenceVersion != _references.Version) {
            _renamePanel.Invalidate();
            _languageReferenceVersion = _references.Version;
            _view.Editors.InvalidateLanguageContext();
            _referenceResults.Clear();
        }
        SynchronizeSurfaceWindows(snapshot);
        _mount.Update(_mount.Props with { Snapshot = snapshot });
        _previousSnapshot = snapshot;
        _world.FlushReactive();
        DomRuntime.Flush();
    }

    private void ReconcileDocument()
    {
        var document = _session.ToDocument();
        var layout = _mount.GetState<NotebookCellState>();
        var nextLayout = NotebookCellLayout.ReconcileDocument(layout.Value, _previousDocument, document);
        if (!NotebookCellLayout.IsValid(nextLayout)) {
            throw new InvalidOperationException(
                "Document reconciliation produced an invalid cell layout.");
        }
        nextLayout = nextLayout with { Revision = layout.Value.Revision + 1 };
        _view.ReconcileDocument(_previousDocument, document, layout.Value, nextLayout);
        layout.Set(nextLayout);
        _previousDocument = document;
    }

    private void SynchronizeSurfaceWindows(NotebookSessionSnapshot snapshot)
    {
        var layout = _mount.GetState<NotebookCellState>();
        var next = NotebookCellLayout.SynchronizeSurfaces(layout.Value, _previousSnapshot, snapshot);
        if (next == layout.Value) return;
        if (!NotebookCellLayout.IsValid(next)) {
            throw new InvalidOperationException("Surface synchronization produced an invalid cell layout.");
        }
        layout.Set(next with { Revision = layout.Value.Revision + 1 });
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
