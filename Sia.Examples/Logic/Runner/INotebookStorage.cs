namespace Sia_Examples.Notebook;

public sealed record NotebookStorageEntry(string Key, string Title, DateTimeOffset SavedAt, int Version);

public sealed class NotebookConflictException(string key, int expectedVersion, int actualVersion)
    : Exception($"'{key}' was saved elsewhere (expected v{expectedVersion}, found v{actualVersion}).")
{
    public string Key { get; } = key;
    public int ExpectedVersion { get; } = expectedVersion;
    public int ActualVersion { get; } = actualVersion;
}

public interface INotebookStorage
{
    ValueTask<IReadOnlyList<NotebookStorageEntry>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<(string Xml, int Version)?> LoadAsync(
        string key, CancellationToken cancellationToken = default);

    ValueTask<(string Key, int Version)> SaveAsync(
        string? key, int? expectedVersion, string title, string xml,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string key, int expectedVersion, CancellationToken cancellationToken = default);
}
