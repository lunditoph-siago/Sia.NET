#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Sia_Examples.Browser;

namespace Sia_Examples.Notebook;

public sealed partial class IndexedDbWorkspaceStorage(BrowserMainThread mainThread) : IWorkspaceStorage
{
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
        return [.. entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal)];
    }

    public async ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        var json = await GetJsonAsync(normalized);
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        if (json is null) {
            return null;
        }
        var record = JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!;
        return record.Kind == WorkspaceEntryKind.File.ToString().ToLowerInvariant()
            ? new WorkspaceFileContent(
                record.Content ?? string.Empty, WorkspaceVersioning.ComputeVersion(record.Content ?? string.Empty))
            : null;
    }

    public async ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        await CheckVersionAsync(normalized, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(normalized, "file", content, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
        return new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content));
    }

    public async ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        await EnsureAbsentAsync(normalized);
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(normalized, "file", content, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
    }

    public async ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        await EnsureAbsentAsync(normalized);
        cancellationToken.ThrowIfCancellationRequested();

        await PutAsync(normalized, "folder", string.Empty, DateTimeOffset.UtcNow.ToString("O"));
        mainThread.VerifyAccess();
    }

    public async ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        await CheckVersionAsync(normalized, expectedVersion);
        var existing = await ListRecordsAsync();
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var removed = false;
        foreach (var record in existing) {
            if (WorkspacePath.EqualsPath(record.Path, normalized)
                || WorkspacePath.IsDescendantOf(record.Path, normalized)) {
                await RemoveAsync(record.Path);
                removed = true;
            }
        }
        if (!removed) {
            throw new InvalidOperationException(
                $"No entry named '{normalized}' exists in the workspace.");
        }
        mainThread.VerifyAccess();
    }

    public async ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var oldNormalized = WorkspacePath.Normalize(oldPath);
        var newNormalized = WorkspacePath.Normalize(newPath);
        if (WorkspacePath.EqualsPath(oldNormalized, newNormalized)) {
            return;
        }
        var existing = await ListRecordsAsync();
        mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var moved = false;
        foreach (var record in existing) {
            if (WorkspacePath.EqualsPath(record.Path, newNormalized)
                || WorkspacePath.IsDescendantOf(record.Path, newNormalized)) {
                throw new WorkspaceEntryExistsException(newNormalized);
            }
            if (WorkspacePath.EqualsPath(record.Path, oldNormalized)
                || WorkspacePath.IsDescendantOf(record.Path, oldNormalized)) {
                var target = newNormalized + record.Path[oldNormalized.Length..];
                await PutAsync(
                    target, record.Kind, record.Content ?? string.Empty,
                    record.ModifiedAt ?? DateTimeOffset.UtcNow.ToString("O"));
                await RemoveAsync(record.Path);
                moved = true;
            }
        }
        if (!moved) {
            throw new InvalidOperationException(
                $"No entry named '{oldNormalized}' exists in the workspace.");
        }
        mainThread.VerifyAccess();
    }

    private async Task CheckVersionAsync(string path, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var json = await GetJsonAsync(path);
        var actual = json is null || IsFolderRecord(json)
            ? null
            : WorkspaceVersioning.ComputeVersion(
                JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!.Content ?? string.Empty);
        if (actual != expected) {
            throw new WorkspaceConflictException(path, expected, actual);
        }
    }

    private async Task EnsureAbsentAsync(string path)
    {
        if (await GetJsonAsync(path) is not null) {
            throw new WorkspaceEntryExistsException(path);
        }
    }

    private bool IsFolderRecord(string json)
        => JsonSerializer.Deserialize<RawRecord>(json, JsonOptions)!.Kind == "folder";

    private async Task<List<RawRecord>> ListRecordsAsync()
    {
        var json = await GetAllJsonAsync();
        return [.. JsonSerializer.Deserialize<RawRecord[]>(json, JsonOptions) ?? []];
    }

    private static WorkspaceStorageEntry Describe(RawRecord record)
        => record.Kind == "folder"
            ? new(record.Path, WorkspaceEntryKind.Folder, null)
            : new(
                record.Path,
                WorkspaceEntryKind.File,
                WorkspaceVersioning.ComputeVersion(record.Content ?? string.Empty),
                record.ModifiedAt is { } modifiedAt ? DateTimeOffset.Parse(modifiedAt) : null);

    private sealed record RawRecord(string Path, string Kind, string? Content, string? ModifiedAt);

    [JSImport("workspaceGetAllJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> GetAllJsonAsync();

    [JSImport("workspaceGetJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> GetJsonAsync(string path);

    [JSImport("workspacePut", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task PutAsync(string path, string kind, string content, string modifiedAt);

    [JSImport("workspaceRemove", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task RemoveAsync(string path);
}
#endif
