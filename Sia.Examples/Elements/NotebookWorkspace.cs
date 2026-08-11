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
        var dockState = NotebookDockState.Create(document);
        _view = new(world, document, references, dockState);
        _mount = world.Mount(
            NotebookViewComponent.Definition,
            new(_view, _session.Snapshot, dockState));
        _previousSnapshot = _session.Snapshot;
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
        => UpdateDock(state => NotebookDockLayout.Activate(state, tabId));

    public void OpenWindow(string windowId)
        => UpdateDock(state => NotebookDockLayout.OpenWindow(state, windowId));

    public void CloseWindow(string windowId)
        => UpdateDock(state => NotebookDockLayout.CloseWindow(state, windowId));

    public void Dock(string arguments)
    {
        ThrowIfDisposed();
        var parts = arguments.Split(':');
        if (parts.Length != 4
            || !Enum.TryParse<DockDropPosition>(
                parts[2],
                ignoreCase: true,
                out var position)
            || !int.TryParse(parts[3], out var targetIndex)) {
            return;
        }
        UpdateDock(state => NotebookDockLayout.Dock(
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
        UpdateDock(state => NotebookDockLayout.Detach(
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

    public void RemoveSection(int sectionIndex)
    {
        ThrowIfDisposed();
        SynchronizeEditors();
        _session.RemoveSection(sectionIndex);
    }

    public void RenameSection(int sectionIndex)
    {
        ThrowIfDisposed();
        using var input = DomElement.TryFind($"section-title-input-{sectionIndex}");
        var title = input?.Value().Trim();
        if (string.IsNullOrEmpty(title)) {
            return;
        }
        SynchronizeEditors();
        _session.RenameSection(sectionIndex, title);
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

    public async Task AddPackageAsync(string sourceName)
    {
        ThrowIfDisposed();
        if (!Enum.TryParse<PackageSource>(sourceName, ignoreCase: true, out var source)) {
            return;
        }
        using var idInput = DomElement.TryFind("package-add-id");
        var id = idInput?.Value().Trim();
        if (string.IsNullOrEmpty(id)) {
            return;
        }
        using var versionInput = DomElement.TryFind("package-add-version");
        var version = versionInput?.Value().Trim();
        await _session.AddPackageAsync(
            new(source, id, string.IsNullOrEmpty(version) ? null : version),
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

    private void UpdateDock(Func<NotebookDockState, NotebookDockState> update)
    {
        ThrowIfDisposed();
        var state = _mount.GetState<NotebookDockState>();
        var next = update(state.Value);
        if (next == state.Value) {
            return;
        }
        if (!NotebookDockLayout.IsValid(next)) {
            throw new InvalidOperationException("The dock operation produced an invalid layout.");
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
            RebuildView(snapshot);
            DomRuntime.Flush();
            return;
        }
        SynchronizeSurfaceWindows(snapshot);
        _mount.Update(_mount.Props with { Snapshot = snapshot });
        _previousSnapshot = snapshot;
        _world.FlushReactive();
        DomRuntime.Flush();
    }

    private void SynchronizeSurfaceWindows(NotebookSessionSnapshot snapshot)
    {
        var previous = _previousSnapshot.Cells.ToDictionary(
            static cell => cell.Id,
            static cell => cell.State);
        var dock = _mount.GetState<NotebookDockState>();
        var next = dock.Value;
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
                next = OpenCellWindow(next, cell.Id, DockWindowKind.Output);
            } else if (!hasOutput && hadOutput) {
                next = CloseCellWindow(next, cell.Id, DockWindowKind.Output);
            }

            if (state.RenderRequested
                && (!hasPrevious
                    || !old.RenderRequested
                    || old.RenderOutput != state.RenderOutput
                    || completedRun)) {
                next = OpenCellWindow(next, cell.Id, DockWindowKind.Render);
            } else if (!state.RenderRequested && hasPrevious && old.RenderRequested) {
                next = CloseCellWindow(next, cell.Id, DockWindowKind.Render);
            }
        }
        if (next != dock.Value) {
            if (!NotebookDockLayout.IsValid(next)) {
                throw new InvalidOperationException(
                    "Surface synchronization produced an invalid dock layout.");
            }
            dock.Set(next);
        }
    }

    private void RebuildView(NotebookSessionSnapshot snapshot)
    {
        var document = _session.ToDocument();
        var dockState = NotebookDockState.Create(document);
        foreach (var cell in snapshot.Cells) {
            if (HasOutput(cell.State)) {
                dockState = OpenCellWindow(
                    dockState,
                    cell.Id,
                    DockWindowKind.Output);
            }
            if (cell.State.RenderRequested) {
                dockState = OpenCellWindow(
                    dockState,
                    cell.Id,
                    DockWindowKind.Render);
            }
        }
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _view.Dispose();
        _view = new(_world, document, _references, dockState);
        _mount = _world.Mount(
            NotebookViewComponent.Definition,
            new(_view, snapshot, dockState));
        _previousSnapshot = snapshot;
        _world.FlushReactive();
    }

    private static bool HasOutput(CellState state)
        => state.StandardOutput.Length > 0 || state.StandardError.Length > 0;

    private static NotebookDockState OpenCellWindow(
        NotebookDockState state,
        string cellId,
        DockWindowKind kind)
        => NotebookDockLayout.OpenWindow(
            state,
            state.GetWindow(cellId, kind).Id);

    private static NotebookDockState CloseCellWindow(
        NotebookDockState state,
        string cellId,
        DockWindowKind kind)
        => NotebookDockLayout.CloseWindow(
            state,
            state.GetWindow(cellId, kind).Id);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
