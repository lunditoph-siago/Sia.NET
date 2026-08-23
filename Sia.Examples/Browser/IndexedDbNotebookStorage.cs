#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Sia_Examples.Notebook;

namespace Sia_Examples.Browser;

public sealed partial class IndexedDbNotebookStorage(BrowserMainThread mainThread) : IWorkspaceStorage
{
    private const string Extension = ".notebook.xml";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var json = await GetAllJsonAsync();
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var records = JsonSerializer.Deserialize<RawRecord[]>(json, JsonOptions) ?? [];
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
        var record = JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!;
        return new WorkspaceFileContent(record.Xml, WorkspaceVersioning.ComputeVersion(record.Xml));
    }

    public async ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(key, content, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
        return new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content));
    }

    public async ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        if (await GetJsonAsync(key) is not null) {
            throw new WorkspaceEntryExistsException(path);
        }
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(key, content, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
    }

    public ValueTask CreateFolderAsync(string path, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The notebook store is flat and cannot hold folders.");

    public async ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var key = KeyOf(path);
        await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await RemoveAsync(key);
        mainThread.VerifyAccess();
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
        var json = await GetJsonAsync(oldKey)
            ?? throw new InvalidOperationException($"No entry named '{oldPath}' exists in the workspace.");
        if (await GetJsonAsync(newKey) is not null) {
            throw new WorkspaceEntryExistsException(newPath);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var record = JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!;
        await PutAsync(newKey, record.Xml, record.SavedAt);
        await RemoveAsync(oldKey);
        mainThread.VerifyAccess();
    }

    private async Task CheckVersionAsync(string key, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var json = await GetJsonAsync(key);
        var actual = json is null
            ? null
            : WorkspaceVersioning.ComputeVersion(JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!.Xml);
        if (actual != expected) {
            throw new WorkspaceConflictException(key + Extension, expected, actual);
        }
    }

    private static WorkspaceStorageEntry Describe(RawRecord record)
        => new(
            record.Key + Extension,
            WorkspaceEntryKind.File,
            WorkspaceVersioning.ComputeVersion(record.Xml),
            DateTimeOffset.Parse(record.SavedAt));

    private static string KeyOf(string path)
        => path.EndsWith(Extension, StringComparison.Ordinal)
            ? path[..^Extension.Length]
            : throw new ArgumentException($"'{path}' is not a notebook path.", nameof(path));

    private sealed record RawRecord(string Key, string Xml, string SavedAt);

    [JSImport("notebookGetAllJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> GetAllJsonAsync();

    [JSImport("notebookGetJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> GetJsonAsync(string key);

    [JSImport("notebookPut", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task PutAsync(string key, string xml, string savedAt);

    [JSImport("notebookRemove", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task RemoveAsync(string key);
}
#endif
