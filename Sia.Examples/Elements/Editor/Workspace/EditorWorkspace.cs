using System.Linq;
using Sia;
using Sia.Reactors;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class EditorWorkspace(World world, IWorkspaceStorage storage) : IDisposable
{
    private readonly World _world = world;
    private readonly IWorkspaceStorage _storage = storage;
    private readonly Aggregator<string> _aggregator = world.AcquireAddon<Aggregator<string>>();
    private readonly Hierarchy<WorkspaceTreeTag> _hierarchy = world.AcquireAddon<Hierarchy<WorkspaceTreeTag>>();
    private readonly HashSet<Entity> _loadedContent = [];
    private readonly List<Entity> _untitledEntities = [];

    private Entity? _previewEntity;
    private int _untitledCounter;
    private bool _initialized;

    public IReadOnlyList<Entity> RootEntries => Sort(_hierarchy.Root);

    public IReadOnlyList<Entity> ChildrenOf(Entity folder)
        => Sort(folder.Get<Node<WorkspaceTreeTag>>().Children);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) {
            return;
        }
        _initialized = true;
        await WorkspaceSeed.EnsureSeededAsync(_storage, cancellationToken);
        foreach (var entry in await _storage.ListTreeAsync(cancellationToken)) {
            if (entry.Kind == WorkspaceEntryKind.Folder) {
                EnsureFolderPath(entry.Path);
            } else {
                EnsureFileEntity(entry.Path, entry.Version);
            }
        }
    }

    public bool TryGetEntity(string path, out Entity entity)
    {
        if (_aggregator.TryGet(path, out var aggregationEntity)) {
            entity = aggregationEntity.Get<Aggregation<string>>().First;
            return true;
        }
        entity = default;
        return false;
    }

    public static bool IsFolder(Entity entity) => entity.Contains<WorkspaceFolder>();

    public static bool IsDirty(Entity fileEntity) => fileEntity.Get<WorkspaceFile>().IsDirty;

    public static string PathOf(Entity entity) => entity.Get<Sid<string>>().Value;

    public static string NameOf(Entity entity) => entity.Get<WorkspaceEntry>().Name;

    public IReadOnlyList<Entity> AllFiles()
    {
        List<Entity> result = [];
        _world.Query<TypeUnion<WorkspaceFile>>(result.Add);
        return result;
    }

    public IReadOnlyList<Entity> OpenFiles()
    {
        List<Entity> result = [];
        _world.Query<TypeUnion<OpenEditorFile>>(result.Add);
        return result;
    }

    public Entity CreateUntitled()
    {
        var number = ++_untitledCounter;
        var entity = _world.Create(HList.From(
            new WorkspaceEntry($"Untitled-{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
            new WorkspaceFile(),
            new Sid<string>(UntitledKey(number)),
            new UntitledFile()));
        entity.Add<OpenEditorFile>();
        _untitledEntities.Add(entity);
        _loadedContent.Add(entity);
        return entity;
    }

    public async ValueTask<Entity> OpenFileAsync(
        Entity fileEntity, bool pin, CancellationToken cancellationToken = default)
    {
        await EnsureContentLoadedAsync(fileEntity, cancellationToken);
        if (!fileEntity.Contains<OpenEditorFile>()) {
            fileEntity.Add<OpenEditorFile>();
        }
        if (pin) {
            Pin(fileEntity);
        } else if (!fileEntity.Contains<PreviewTab>()) {
            DemotePreview();
            fileEntity.Add<PreviewTab>();
            _previewEntity = fileEntity;
        }
        return fileEntity;
    }

    public void Pin(Entity fileEntity)
    {
        if (fileEntity.Contains<PreviewTab>()) {
            fileEntity.Remove<PreviewTab>();
        }
        if (_previewEntity == fileEntity) {
            _previewEntity = null;
        }
    }

    public void SetContent(Entity fileEntity, string content)
    {
        if (fileEntity.Get<WorkspaceFile>().Content == content) {
            return;
        }
        fileEntity.Execute(new WorkspaceFile.SetContent(content));
        Pin(fileEntity);
    }

    public bool CloseFile(Entity fileEntity)
    {
        if (_previewEntity == fileEntity) {
            _previewEntity = null;
        }
        if (fileEntity.Contains<UntitledFile>()) {
            _untitledEntities.Remove(fileEntity);
            _loadedContent.Remove(fileEntity);
            fileEntity.Destroy();
            return true;
        }
        if (fileEntity.Contains<PreviewTab>()) {
            fileEntity.Remove<PreviewTab>();
        }
        if (fileEntity.Contains<OpenEditorFile>()) {
            fileEntity.Remove<OpenEditorFile>();
        }
        return false;
    }

    public async ValueTask SaveAsync(Entity fileEntity, CancellationToken cancellationToken = default)
    {
        if (fileEntity.Contains<UntitledFile>()) {
            throw new InvalidOperationException("An untitled file must be saved with SaveAsAsync.");
        }
        var file = fileEntity.Get<WorkspaceFile>();
        var path = fileEntity.Get<Sid<string>>().Value;
        var written = await _storage.WriteFileAsync(path, file.Version, file.Content, cancellationToken);
        ref var current = ref fileEntity.Get<WorkspaceFile>();
        current.SavedContent = written.Content;
        current.Version = written.Version;
        _loadedContent.Add(fileEntity);
    }

    public async ValueTask<IReadOnlyList<(Entity Entity, Exception Error)>> SaveAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<(Entity, Exception)> failures = [];
        foreach (var entity in OpenFiles()) {
            if (entity.Contains<UntitledFile>() || !IsDirty(entity)) {
                continue;
            }
            try {
                await SaveAsync(entity, cancellationToken);
            }
            catch (Exception error) {
                failures.Add((entity, error));
            }
        }
        return failures;
    }

    public async ValueTask<Entity> SaveAsAsync(
        Entity fileEntity, string newPath, CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(newPath);
        var content = fileEntity.Get<WorkspaceFile>().Content;
        var wasUntitled = fileEntity.Contains<UntitledFile>();

        if (!wasUntitled && WorkspacePath.EqualsPath(PathOf(fileEntity), normalized)) {
            await SaveAsync(fileEntity, cancellationToken);
            return fileEntity;
        }

        await _storage.CreateFileAsync(normalized, content, cancellationToken);
        var written = await _storage.ReadFileAsync(normalized, cancellationToken)
            ?? throw new InvalidOperationException($"'{normalized}' could not be read back after being saved.");

        if (!wasUntitled) {
            return AttachOrUpdateFile(normalized, written);
        }

        _untitledEntities.Remove(fileEntity);
        _loadedContent.Remove(fileEntity);
        if (_previewEntity == fileEntity) {
            _previewEntity = null;
        }
        fileEntity.Remove<UntitledFile>();
        fileEntity.SetSid(normalized);
        fileEntity.Execute(new WorkspaceEntry.SetName(WorkspacePath.Name(normalized)));
        AttachToTree(fileEntity, normalized);
        ref var file = ref fileEntity.Get<WorkspaceFile>();
        file.SavedContent = written.Content;
        file.Version = written.Version;
        _loadedContent.Add(fileEntity);
        return fileEntity;
    }

    public async ValueTask<Entity> CreateFileAsync(
        string folderPath, string name, CancellationToken cancellationToken = default)
    {
        var path = WorkspacePath.Normalize(WorkspacePath.Combine(folderPath, name));
        await _storage.CreateFileAsync(path, string.Empty, cancellationToken);
        var entity = EnsureFileEntity(path, WorkspaceVersioning.ComputeVersion(string.Empty));
        ref var file = ref entity.Get<WorkspaceFile>();
        file.SavedContent = string.Empty;
        _loadedContent.Add(entity);
        return entity;
    }

    public async ValueTask<Entity> CreateFolderAsync(
        string folderPath, string name, CancellationToken cancellationToken = default)
    {
        var path = WorkspacePath.Normalize(WorkspacePath.Combine(folderPath, name));
        await _storage.CreateFolderAsync(path, cancellationToken);
        return EnsureFolderPath(path);
    }

    public async ValueTask RenameAsync(
        Entity entity, string newName, CancellationToken cancellationToken = default)
    {
        var oldPath = PathOf(entity);
        var parentPath = WorkspacePath.Parent(oldPath);
        var newPath = WorkspacePath.Normalize(WorkspacePath.Combine(parentPath ?? string.Empty, newName));
        if (WorkspacePath.EqualsPath(oldPath, newPath)) {
            return;
        }
        await _storage.RenameEntryAsync(oldPath, newPath, cancellationToken);
        entity.SetSid(newPath);
        entity.Execute(new WorkspaceEntry.SetName(WorkspacePath.Name(newPath)));
        if (IsFolder(entity)) {
            RenameDescendants(entity, oldPath, newPath);
        }
    }

    public async ValueTask DeleteAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        var path = PathOf(entity);
        await _storage.DeleteEntryAsync(path, cancellationToken: cancellationToken);
        if (_previewEntity == entity) {
            _previewEntity = null;
        }
        _loadedContent.Remove(entity);
        entity.Destroy();
    }

    public async ValueTask EnsureAllLoadedAsync(CancellationToken cancellationToken = default)
    {
        List<Entity> pending = [];
        foreach (var entity in AllFiles()) {
            if (!entity.Contains<UntitledFile>() && !_loadedContent.Contains(entity)) {
                pending.Add(entity);
            }
        }
        foreach (var entity in pending) {
            await EnsureContentLoadedAsync(entity, cancellationToken);
        }
    }

    public void Dispose()
    {
        foreach (var entity in _hierarchy.Root.ToArray()) {
            if (entity.IsValid) {
                entity.Destroy();
            }
        }
        foreach (var entity in _untitledEntities.ToArray()) {
            if (entity.IsValid) {
                entity.Destroy();
            }
        }
        _untitledEntities.Clear();
        _loadedContent.Clear();
        _previewEntity = null;
    }

    private async ValueTask EnsureContentLoadedAsync(Entity fileEntity, CancellationToken cancellationToken)
    {
        if (_loadedContent.Contains(fileEntity) || fileEntity.Contains<UntitledFile>()) {
            return;
        }
        var path = PathOf(fileEntity);
        var content = await _storage.ReadFileAsync(path, cancellationToken);
        if (content is null) {
            _loadedContent.Add(fileEntity);
            return;
        }
        if (fileEntity.Get<WorkspaceFile>().Content != content.Content) {
            fileEntity.Execute(new WorkspaceFile.SetContent(content.Content));
        }
        ref var file = ref fileEntity.Get<WorkspaceFile>();
        file.SavedContent = content.Content;
        file.Version = content.Version;
        _loadedContent.Add(fileEntity);
    }

    private Entity AttachOrUpdateFile(string path, WorkspaceFileContent written)
    {
        var entity = EnsureFileEntity(path, written.Version);
        if (entity.Get<WorkspaceFile>().Content != written.Content) {
            entity.Execute(new WorkspaceFile.SetContent(written.Content));
        }
        ref var file = ref entity.Get<WorkspaceFile>();
        file.SavedContent = written.Content;
        file.Version = written.Version;
        _loadedContent.Add(entity);
        return entity;
    }

    private Entity EnsureFileEntity(string path, string? version)
    {
        if (TryGetEntity(path, out var existing)) {
            return existing;
        }
        var parentPath = WorkspacePath.Parent(path);
        var parent = parentPath is null ? (Entity?)null : EnsureFolderPath(parentPath);
        var name = WorkspacePath.Name(path);
        return _world.Create(HList.From(
            new WorkspaceEntry(name),
            new WorkspaceFile { Version = version ?? string.Empty },
            parent is { } p ? new Node<WorkspaceTreeTag>(p) : new Node<WorkspaceTreeTag>(),
            new Sid<string>(path)));
    }

    private Entity EnsureFolderPath(string path)
    {
        if (TryGetEntity(path, out var existing)) {
            return existing;
        }
        var parentPath = WorkspacePath.Parent(path);
        var parent = parentPath is null ? (Entity?)null : EnsureFolderPath(parentPath);
        var name = WorkspacePath.Name(path);
        return _world.Create(HList.From(
            new WorkspaceEntry(name),
            new WorkspaceFolder(true),
            parent is { } p ? new Node<WorkspaceTreeTag>(p) : new Node<WorkspaceTreeTag>(),
            new Sid<string>(path)));
    }

    private void AttachToTree(Entity entity, string path)
    {
        var parentPath = WorkspacePath.Parent(path);
        var parent = parentPath is null ? (Entity?)null : EnsureFolderPath(parentPath);
        entity.Add<Node<WorkspaceTreeTag>>(parent is { } p ? new(p) : new());
    }

    private void RenameDescendants(Entity folder, string oldPrefix, string newPrefix)
    {
        foreach (var child in folder.Get<Node<WorkspaceTreeTag>>().Children.ToArray()) {
            var childPath = PathOf(child);
            var updated = newPrefix + childPath[oldPrefix.Length..];
            child.SetSid(updated);
            if (IsFolder(child)) {
                RenameDescendants(child, childPath, updated);
            }
        }
    }

    private void DemotePreview()
    {
        if (_previewEntity is { } current && current.IsValid && current.Contains<PreviewTab>()) {
            current.Remove<PreviewTab>();
        }
        _previewEntity = null;
    }

    private static Entity[] Sort(IEnumerable<Entity> entities)
        => [.. entities
            .OrderBy(static e => IsFolder(e) ? 0 : 1)
            .ThenBy(static e => NameOf(e), StringComparer.OrdinalIgnoreCase)];

    private static string UntitledKey(int number)
        => " untitled:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
