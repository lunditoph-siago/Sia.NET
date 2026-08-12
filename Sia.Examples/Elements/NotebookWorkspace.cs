using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class NotebookWorkspace : IAsyncDisposable
{
    private readonly NotebookSession _session;
    private readonly MetadataReferenceProvider _references;
    private readonly World _world;
    private readonly INotebookStorage _storage;
    private readonly NotebookLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];
    private BrowserNotebookView _view;
    private ReactiveMount<NotebookViewProps> _mount;
    private NotebookSessionSnapshot _previousSnapshot;
    private NotebookDocument _previousDocument;
    private bool _disposed;

    private NotebookInfo _info;

    private string? _version;

    public NotebookWorkspace(
        World world,
        IUiThread mainThread,
        NotebookDocument document,
        MetadataReferenceProvider references,
        NotebookInfo info,
        string? version,
        INotebookStorage storage,
        NotebookLibrary library)
    {
        _world = world;
        _references = references;
        _storage = storage;
        _library = library;
        _info = info;
        _version = version;
        _session = new(mainThread, document, references);
        var cellState = NotebookCellState.Create(document);
        _view = new(world, document, references, cellState);
        _mount = world.Mount(
            NotebookViewComponent.Definition,
            new(_view, _session.Snapshot, cellState));
        _previousSnapshot = _session.Snapshot;
        _previousDocument = document;
        world.FlushReactive();
        _session.Changed += HandleSessionChanged;
    }

    public NotebookInfo Info => _info;

    public string? Version => _version;

    public bool CanDelete
        => _info.Origin == NotebookOrigin.User && _info.Key.Length > 0;

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
        } else {
            StartRun(cellId);
        }
    }

    public void ActivateTab(string tabId)
        => UpdateCell(state => NotebookCellLayout.Activate(state, tabId));

    public void OpenWindow(string windowId)
        => UpdateCell(state => NotebookCellLayout.OpenWindow(state, windowId));

    public void CloseWindow(string windowId)
        => UpdateCell(state => NotebookCellLayout.CloseWindow(state, windowId));

    public void Cell(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 4
            || !Enum.TryParse<CellDropPosition>(
                parts[2],
                ignoreCase: true,
                out var position)
            || !int.TryParse(parts[3], out var targetIndex)) {
            return;
        }
        UpdateCell(state => NotebookCellLayout.Cell(
            state,
            parts[0],
            parts[1],
            position,
            targetIndex));
    }

    public void Detach(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[1], out var pointerX)
            || !int.TryParse(parts[2], out var pointerY)) {
            return;
        }
        UpdateCell(state => NotebookCellLayout.Detach(
            state,
            parts[0],
            pointerX,
            pointerY));
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _session.Interrupt();
    }

    public void Save(string cellId)
    {
        ThrowIfDisposed();
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
        if (input is not null) {
            input.Attr("data-saved-value", value);
        }
    }

    public void BeginEditorEdit(string cellId)
    {
        ThrowIfDisposed();
        _view.BeginEditorEdit(cellId);
    }

    public void InsertCell(string afterBlockId)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.InsertCell(afterBlockId);
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

    public async Task<(string? Version, NotebookConflictException? Conflict)>
        SaveToStorageAsync(bool force = false)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        var document = _session.ToDocument();
        var xml = NotebookDocumentSerializer.Write(document);

        var title = document.Title.Length > 0 ? document.Title : "(untitled)";
        var hasStorageKey = _info.Origin == NotebookOrigin.User && _info.Key.Length > 0;
        try {
            var (key, version) = await _storage.SaveAsync(
                hasStorageKey ? _info.Key : null,
                hasStorageKey && !force ? _version : null,
                xml,
                _lifetime.Token);
            _info = new NotebookInfo(title, "", key, NotebookOrigin.User);
            _version = version;
            await _library.RefreshAsync(_lifetime.Token);
            return (version, null);
        }
        catch (NotebookConflictException conflict) {
            return (null, conflict);
        }
    }

    public async Task<bool> DeleteFromStorageAsync()
    {
        ThrowIfDisposed();
        if (!CanDelete) {
            return false;
        }
        await _storage.DeleteAsync(_info.Key, _version!, _lifetime.Token);
        await _library.RefreshAsync(_lifetime.Token);
        return true;
    }

    public async Task AddPackageAsync()
    {
        ThrowIfDisposed();
        using var idInput = DomElement.TryFind("package-add-id");
        var id = idInput?.Value().Trim();
        if (string.IsNullOrEmpty(id)) {
            return;
        }
        using var versionInput = DomElement.TryFind("package-add-version");
        var version = versionInput?.Value().Trim();
        await _session.AddPackageAsync(
            new(id, string.IsNullOrEmpty(version) ? null : version),
            _lifetime.Token);
    }

    public bool RouteEditorEvent(string payload)
    {
        ThrowIfDisposed();
        var first = payload.IndexOf(':');
        var second = first < 0 ? -1 : payload.IndexOf(':', first + 1);
        var cellId = first >= 0 && second > first
            ? payload[(first + 1)..second]
            : string.Empty;
        _view.Editors.TryGetSource(cellId, out var before);
        var handled = _view.Editors.Route(payload);
        if (handled
            && _view.Editors.TryGetSource(cellId, out var after)
            && before != after) {
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
        _view.Dispose();
        _lifetime.Dispose();
    }

    private void SynchronizeEditors()
    {
        foreach (var cell in _session.Cells) {
            if (!cell.Editable) {
                continue;
            }
            var source = _view.Editors.GetSource(cell.Id);
            if (source != _session.GetState(cell.Id).Source) {
                _session.UpdateCellSource(
                    cell.Id,
                    source,
                    _lifetime.Token);
            }
            _view.EndEditorEdit(cell.Id);
        }
    }

    private void Track(Task operation)
    {
        var observed = ObserveAsync(operation);
        _operations.Add(observed);
        _ = RemoveWhenCompleteAsync(observed);
    }

    private void UpdateCell(Func<NotebookCellState, NotebookCellState> update)
    {
        ThrowIfDisposed();
        var state = _mount.GetState<NotebookCellState>();
        var next = update(state.Value);
        if (next == state.Value) {
            return;
        }
        if (!NotebookCellLayout.IsValid(next)) {
            throw new InvalidOperationException("The cell operation produced an invalid layout.");
        }
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
        _view.ReconcileDocument(_previousDocument, document, layout.Value, nextLayout);
        layout.Set(nextLayout);
        _previousDocument = document;
    }

    private void SynchronizeSurfaceWindows(NotebookSessionSnapshot snapshot)
    {
        var previous = _previousSnapshot.Cells.ToDictionary(
            static cell => cell.Id,
            static cell => cell.State);
        var layout = _mount.GetState<NotebookCellState>();
        var next = layout.Value;
        foreach (var cell in snapshot.Cells) {
            var hasPrevious = previous.TryGetValue(cell.Id, out var old);
            var state = cell.State;
            var hasOutput = HasOutput(state);
            var hadOutput = hasPrevious && HasOutput(old);
            var completedRun = hasPrevious
                && old.Phase == CellPhase.Running
                && state.Phase is CellPhase.RanSuccess or CellPhase.RanError;
            if (hasOutput
                && (!hadOutput
                    || old.StandardOutput != state.StandardOutput
                    || old.StandardError != state.StandardError
                    || completedRun)) {
                next = OpenCellWindow(next, cell.Id, CellWindowKind.Output);
            } else if (!hasOutput && hadOutput) {
                next = CloseCellWindow(next, cell.Id, CellWindowKind.Output);
            }

            if (state.RenderRequested
                && (!hasPrevious
                    || !old.RenderRequested
                    || old.RenderOutput != state.RenderOutput
                    || completedRun)) {
                next = OpenCellWindow(next, cell.Id, CellWindowKind.Render);
            } else if (!state.RenderRequested && hasPrevious && old.RenderRequested) {
                next = CloseCellWindow(next, cell.Id, CellWindowKind.Render);
            }
        }
        if (next != layout.Value) {
            if (!NotebookCellLayout.IsValid(next)) {
                throw new InvalidOperationException(
                    "Surface synchronization produced an invalid cell layout.");
            }
            layout.Set(next);
        }
    }

    private static bool HasOutput(CellState state)
        => state.StandardOutput.Length > 0 || state.StandardError.Length > 0;

    private static NotebookCellState OpenCellWindow(
        NotebookCellState state,
        string cellId,
        CellWindowKind kind)
        => NotebookCellLayout.OpenWindow(
            state,
            state.GetWindow(cellId, kind).Id);

    private static NotebookCellState CloseCellWindow(
        NotebookCellState state,
        string cellId,
        CellWindowKind kind)
        => NotebookCellLayout.CloseWindow(
            state,
            state.GetWindow(cellId, kind).Id);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
