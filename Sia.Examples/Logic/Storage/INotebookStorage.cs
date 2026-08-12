namespace Sia_Examples.Notebook;

public sealed record NotebookStorageEntry(string Key, string Title, DateTimeOffset SavedAt, string Version);

public sealed record NotebookContent(string Xml, string Version);

public sealed record NotebookSaveResult(string Key, string Version);

public sealed class NotebookConflictException(string key, string expectedVersion, string? actualVersion)
    : Exception(actualVersion is { } actual
        ? $"'{key}' was saved elsewhere (expected {expectedVersion}, found {actual})."
        : $"'{key}' was deleted elsewhere (expected {expectedVersion}, found nothing).")
{
    public string Key { get; } = key;
    public string ExpectedVersion { get; } = expectedVersion;
    public string? ActualVersion { get; } = actualVersion;
}

public interface INotebookStorage
{
    ValueTask<IReadOnlyList<NotebookStorageEntry>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<NotebookContent?> LoadAsync(
        string key, CancellationToken cancellationToken = default);

    ValueTask<NotebookSaveResult> SaveAsync(
        string? key, string? expectedVersion, string xml,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string key, string expectedVersion, CancellationToken cancellationToken = default);
}
