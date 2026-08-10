using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class NotebookWorkspace : IAsyncDisposable
{
    private readonly NotebookSession _session;
    private readonly BrowserNotebookView _view;
    private readonly ReactiveMount<NotebookViewProps> _mount;
    private readonly World _world;
    private readonly INotebookStorage _storage;
    private readonly NotebookLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];
    private bool _disposed;

    private NotebookInfo _info;

    private int? _version;

    public NotebookWorkspace(
        World world,
        IUiThread mainThread,
        NotebookDocument document,
        MetadataReferenceProvider references,
        NotebookInfo info,
        int? version,
        INotebookStorage storage,
        NotebookLibrary library)
    {
        _world = world;
        _storage = storage;
        _library = library;
        _info = info;
        _version = version;
        _session = new(mainThread, document, references);
        _view = new(world, document, _session.Snapshot, references);
        _mount = world.Mount(
            NotebookViewComponent.Definition,
            new(_view, _session.Snapshot));
        world.FlushReactive();
        _session.Changed += UpdateView;
    }

    public NotebookInfo Info => _info;

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
    }

    public void StartSave()
    {
        ThrowIfDisposed();
        Track(SaveToStorageAsync(_lifetime.Token));
    }

    private async Task SaveToStorageAsync(CancellationToken cancellationToken)
    {
        SynchronizeEditors();
        var document = _session.ToDocument();
        var xml = NotebookDocumentSerializer.Write(document);

        var isUserOwned = _info.Origin == NotebookOrigin.User;
        var (key, version) = await _storage.SaveAsync(
            isUserOwned ? _info.Key : null,
            isUserOwned ? _version : null,
            document.Title,
            xml,
            cancellationToken);

        var title = document.Title.Length > 0 ? document.Title : "(untitled)";
        _info = new NotebookInfo(title, "", key, NotebookOrigin.User);
        _version = version;
        await _library.RefreshAsync(cancellationToken);
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
        return _view.Editors.Route(payload);
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

        _session.Changed -= UpdateView;
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
        }
    }

    private void Track(Task operation)
    {
        var observed = ObserveAsync(operation);
        _operations.Add(observed);
        _ = RemoveWhenCompleteAsync(observed);
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

    private void UpdateView(NotebookSessionSnapshot snapshot)
    {
        if (_disposed) {
            return;
        }
        _mount.Update(new(_view, snapshot));
        _world.FlushReactive();
        DomRuntime.Flush();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
