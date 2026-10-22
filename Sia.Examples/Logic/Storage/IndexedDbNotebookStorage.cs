#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Sia.Examples.Notebook;

namespace Sia.Examples.Browser;

public sealed partial class IndexedDbNotebookStorage(BrowserMainThread mainThread) : IWorkspaceStorage
{
    private const string k_Extension = ".notebook.xml";

    private static readonly JsonSerializerOptions s_JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var json = await GetAllJsonAsync();
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var records = JsonSerializer.Deserialize<RawRecord[]>(json, s_JsonOptions) ?? [];
        List<WorkspaceStorageEntry> entries = [];
        foreach (var record in records) {
            entries.Add(Describe(record));
        }
        return [.. entries.OrderByDescending(static entry => entry.ModifiedAt)];
    }

    public async ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var json = await GetJsonAsync(KeyOf(path));
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        if (json is null) {
            return null;
        }
        var record = JsonSerializer.Deserialize<RawRecord>(json, s_JsonOptions)!;
        return new WorkspaceFileContent(record.Xml, WorkspaceVersioning.ComputeVersion(record.Xml));
    }

    public async ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        var previous = await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        var written = await PutAsync(key, content, DateTimeOffset.UtcNow.ToString("O"), previous, expectedVersion is not null);
        mainThread.VerifyAccess();
        if (!written) {
            var actual = await ReadFileAsync(path, cancellationToken);
            throw new WorkspaceConflictException(path, expectedVersion, actual?.Version);
        }
        return new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content));
    }

    public async ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        cancellationToken.ThrowIfCancellationRequested();

        var written = await PutAsync(key, content, DateTimeOffset.UtcNow.ToString("O"), null, true);
        mainThread.VerifyAccess();
        if (!written) {
            throw new WorkspaceEntryExistsException(path);
        }
    }

    public ValueTask CreateFolderAsync(string path, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The notebook store is flat and cannot hold folders.");

    public async ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        var previous = await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        var result = JsonSerializer.Deserialize<RemovalResult>(
            await RemoveAsync(key, previous, expectedVersion is not null), s_JsonOptions)!;
        mainThread.VerifyAccess();
        if (!result.Removed && expectedVersion is { } expected) {
            var actual = result.Current is { } current ? WorkspaceVersioning.ComputeVersion(current.Xml) : null;
            throw new WorkspaceConflictException(path, expected, actual);
        }
    }

    public async ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var oldKey = KeyOf(oldPath);
        var newKey = KeyOf(newPath);
        if (oldKey == newKey) {
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var result = await RenameAsync(oldKey, newKey);
        mainThread.VerifyAccess();
        if (result == 1) {
            throw new InvalidOperationException($"No entry named '{oldPath}' exists in the workspace.");
        }
        if (result == 2) {
            throw new WorkspaceEntryExistsException(newPath);
        }
    }

    private async Task<string?> CheckVersionAsync(string key, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return null;
        }
        var json = await GetJsonAsync(key);
        mainThread.VerifyAccess();
        var actual = json is null
            ? null
            : WorkspaceVersioning.ComputeVersion(JsonSerializer.Deserialize<RawRecord>(json, s_JsonOptions)!.Xml);
        if (actual != expected) {
            throw new WorkspaceConflictException(key + k_Extension, expected, actual);
        }
        return json;
    }

    private static WorkspaceStorageEntry Describe(RawRecord record)
        => new(
            record.Key + k_Extension,
            WorkspaceEntryKind.File,
            WorkspaceVersioning.ComputeVersion(record.Xml),
            DateTimeOffset.Parse(record.SavedAt));

    private static string KeyOf(string path)
        => path.EndsWith(k_Extension, StringComparison.Ordinal)
            ? path[..^k_Extension.Length]
            : throw new ArgumentException($"'{path}' is not a notebook path.", nameof(path));

    private sealed record RawRecord(string Key, string Xml, string SavedAt);

    private sealed record RemovalResult(bool Removed, RawRecord? Current);

    [JSImport("notebookGetAllJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> GetAllJsonAsync();

    [JSImport("notebookGetJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> GetJsonAsync(string key);

    [JSImport("notebookPut", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    private static partial Task<bool> PutAsync(string key, string xml, string savedAt, string? expected, bool compare);

    [JSImport("notebookRemove", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> RemoveAsync(string key, string? expected, bool compare);

    [JSImport("notebookRename", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<int> RenameAsync(string oldKey, string newKey);
}
#endif
