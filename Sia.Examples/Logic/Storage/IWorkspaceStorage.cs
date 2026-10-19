namespace Sia.Examples.Notebook;

public enum WorkspaceEntryKind
{
    File,
    Folder,
}

public sealed record WorkspaceStorageEntry(
    string Path, WorkspaceEntryKind Kind, string? Version, DateTimeOffset? ModifiedAt = null);

public sealed record WorkspaceFileContent(string Content, string Version);

public sealed class WorkspaceConflictException(string path, string? expectedVersion, string? actualVersion)
    : Exception(actualVersion is { } actual
        ? $"'{path}' was changed elsewhere (expected {expectedVersion}, found {actual})."
        : $"'{path}' was deleted elsewhere (expected {expectedVersion}, found nothing).")
{
    public string Path { get; } = path;

    public string? ExpectedVersion { get; } = expectedVersion;

    public string? ActualVersion { get; } = actualVersion;
}

public sealed class WorkspaceEntryExistsException(string path)
    : Exception($"An entry named '{path}' already exists in the workspace.")
{
    public string Path { get; } = path;
}

public interface IWorkspaceStorage
{
    public ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default);

    public ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default);

    public ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default);

    public ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default);

    public ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default);

    public ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default);

    public ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default);
}
