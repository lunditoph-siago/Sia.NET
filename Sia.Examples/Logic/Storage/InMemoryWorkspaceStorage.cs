namespace Sia_Examples.Notebook;

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
        if (!_entries.Remove(normalized)) {
            throw new InvalidOperationException($"No entry named '{normalized}' exists in the workspace.");
        }
        foreach (var descendant in _entries.Keys.Where(
                 key => WorkspacePath.IsDescendantOf(key, normalized)).ToArray()) {
            _entries.Remove(descendant);
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
        if (!_entries.TryGetValue(oldNormalized, out var entry)) {
            throw new InvalidOperationException($"No entry named '{oldNormalized}' exists in the workspace.");
        }
        EnsureAbsent(newNormalized);
        _entries.Remove(oldNormalized);
        _entries[newNormalized] = entry;
        foreach (var descendant in _entries.Keys.Where(
                 key => WorkspacePath.IsDescendantOf(key, oldNormalized)).ToArray()) {
            var moved = _entries[descendant];
            _entries.Remove(descendant);
            _entries[newNormalized + descendant[oldNormalized.Length..]] = moved;
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
