namespace Sia_Examples.Notebook;

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
    ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default);

    ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default);

    ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default);

    ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default);

    ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default);

    ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default);
}
