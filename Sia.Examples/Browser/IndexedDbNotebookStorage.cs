#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Xml;
using Sia_Examples.Notebook;

namespace Sia_Examples.Browser;

public sealed partial class IndexedDbNotebookStorage(BrowserMainThread mainThread) : INotebookStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<IReadOnlyList<NotebookStorageEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var json = await GetAllJsonAsync();
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var records = JsonSerializer.Deserialize<RawRecord[]>(json, JsonOptions) ?? [];
        List<NotebookStorageEntry> entries = [];
        foreach (var record in records) {
            if (TryDescribe(record) is { } entry) {
                entries.Add(entry);
            }
        }
        return [.. entries.OrderByDescending(static entry => entry.SavedAt)];
    }

    public async ValueTask<NotebookContent?> LoadAsync(
        string key, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var json = await GetJsonAsync(key);
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        if (json is null) {
            return null;
        }
        var record = JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!;
        return new NotebookContent(record.Xml, NotebookVersioning.ComputeVersion(record.Xml));
    }

    public async ValueTask<NotebookSaveResult> SaveAsync(
        string? key, string? expectedVersion, string xml,
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        key ??= NewKey();
        await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(key, xml, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
        return new NotebookSaveResult(key, NotebookVersioning.ComputeVersion(xml));
    }

    public async ValueTask DeleteAsync(
        string key, string expectedVersion, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        await CheckVersionAsync(key, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await RemoveAsync(key);
        mainThread.VerifyAccess();
    }

    private static async Task CheckVersionAsync(string key, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var json = await GetJsonAsync(key);
        var actual = json is null
            ? null
            : NotebookVersioning.ComputeVersion(JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!.Xml);
        if (actual != expected) {
            throw new NotebookConflictException(key, expected, actual);
        }
    }

    private static NotebookStorageEntry? TryDescribe(RawRecord record)
    {
        try {
            return new NotebookStorageEntry(
                record.Key,
                NotebookVersioning.PeekTitle(record.Xml),
                DateTimeOffset.Parse(record.SavedAt),
                NotebookVersioning.ComputeVersion(record.Xml));
        }
        catch (XmlException) {
            return null;
        }
    }

    private static string NewKey() => Guid.NewGuid().ToString("N")[..12];

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
