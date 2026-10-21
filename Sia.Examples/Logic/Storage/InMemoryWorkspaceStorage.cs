namespace Sia.Examples.Notebook;

public sealed class InMemoryWorkspaceStorage : IWorkspaceStorage
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<WorkspaceStorageEntry>>(
            [.. _entries
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new WorkspaceStorageEntry(
                    pair.Key, pair.Value.Kind, pair.Value.Version, pair.Value.ModifiedAt))]);

    public ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(path);
        return ValueTask.FromResult(_entries.TryGetValue(normalized, out var entry)
            && entry is { Kind: WorkspaceEntryKind.File, Content: { } content }
                ? new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content))
                : null);
    }

    public ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(path);
        CheckVersion(normalized, expectedVersion);
        var version = WorkspaceVersioning.ComputeVersion(content);
        _entries[normalized] = new(WorkspaceEntryKind.File, content, version, DateTimeOffset.UtcNow);
        return ValueTask.FromResult(new WorkspaceFileContent(content, version));
    }

    public ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(path);
        EnsureAbsent(normalized);
        _entries[normalized] = new(
            WorkspaceEntryKind.File, content, WorkspaceVersioning.ComputeVersion(content), DateTimeOffset.UtcNow);
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(path);
        EnsureAbsent(normalized);
        _entries[normalized] = new(WorkspaceEntryKind.Folder, null, null, null);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var normalized = WorkspacePath.Normalize(path);
        CheckVersion(normalized, expectedVersion);
        var paths = _entries.Keys.Where(
            key => key == normalized || WorkspacePath.IsDescendantOf(key, normalized)).ToArray();
        if (paths.Length == 0) {
            throw new InvalidOperationException($"No entry named '{normalized}' exists in the workspace.");
        }
        foreach (var removed in paths) {
            _entries.Remove(removed);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        var oldNormalized = WorkspacePath.Normalize(oldPath);
        var newNormalized = WorkspacePath.Normalize(newPath);
        if (WorkspacePath.EqualsPath(oldNormalized, newNormalized)) {
            return ValueTask.CompletedTask;
        }
        var moved = _entries.Where(pair => pair.Key == oldNormalized
            || WorkspacePath.IsDescendantOf(pair.Key, oldNormalized)).ToArray();
        if (moved.Length == 0) {
            throw new InvalidOperationException($"No entry named '{oldNormalized}' exists in the workspace.");
        }
        if (_entries.Keys.Any(key => key == newNormalized
            || WorkspacePath.IsDescendantOf(key, newNormalized))) {
            throw new WorkspaceEntryExistsException(newNormalized);
        }
        foreach (var (path, entry) in moved) {
            _entries.Remove(path);
            _entries[newNormalized + path[oldNormalized.Length..]] = entry;
        }
        return ValueTask.CompletedTask;
    }

    private void CheckVersion(string path, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var actual = _entries.TryGetValue(path, out var entry)
            && entry is { Kind: WorkspaceEntryKind.File, Content: { } content }
                ? WorkspaceVersioning.ComputeVersion(content)
                : null;
        if (actual != expected) {
            throw new WorkspaceConflictException(path, expected, actual);
        }
    }

    private void EnsureAbsent(string path)
    {
        if (_entries.ContainsKey(path)) {
            throw new WorkspaceEntryExistsException(path);
        }
    }

    private sealed record Entry(
        WorkspaceEntryKind Kind, string? Content, string? Version, DateTimeOffset? ModifiedAt);
}
