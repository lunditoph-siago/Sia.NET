#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Sia.Examples.Browser;

namespace Sia.Examples.Notebook;

public sealed partial class IndexedDbWorkspaceStorage(BrowserMainThread mainThread) : IWorkspaceStorage
{
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
        return [..entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal)];
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
        var record = JsonSerializer.Deserialize<RawRecord>(json, s_JsonOptions)!;
        return record.Kind == WorkspaceEntryKind.File.ToString().ToLowerInvariant()
            ? new WorkspaceFileContent(record.Content ?? string.Empty, VersionOf(record))
            : null;
    }

    public async ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        var previous = await CheckVersionAsync(normalized, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        var written = await PutAsync(
            normalized, "file", content, DateTimeOffset.UtcNow.ToString("O"), previous, expectedVersion is not null);
        mainThread.VerifyAccess();
        if (written is null) {
            var actual = await ReadFileAsync(normalized, cancellationToken);
            throw new WorkspaceConflictException(normalized, expectedVersion, actual?.Version);
        }
        var record = JsonSerializer.Deserialize<RawRecord>(written, s_JsonOptions)!;
        return new WorkspaceFileContent(record.Content ?? string.Empty, VersionOf(record));
    }

    public ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
        => CreateEntryAsync(path, "file", content, cancellationToken);

    public ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default)
        => CreateEntryAsync(path, "folder", string.Empty, cancellationToken);

    private async ValueTask CreateEntryAsync(
        string path, string kind, string content, CancellationToken cancellationToken)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        cancellationToken.ThrowIfCancellationRequested();
        var written = await PutAsync(normalized, kind, content, DateTimeOffset.UtcNow.ToString("O"), null, true);
        mainThread.VerifyAccess();
        if (written is null) {
            throw new WorkspaceEntryExistsException(normalized);
        }
    }

    public async ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        mainThread.VerifyAccess();
        var normalized = WorkspacePath.Normalize(path);
        var previous = await CheckVersionAsync(normalized, expectedVersion);
        cancellationToken.ThrowIfCancellationRequested();

        var result = JsonSerializer.Deserialize<RemovalResult>(
            await RemoveAsync(normalized, previous, expectedVersion is not null), s_JsonOptions)!;
        mainThread.VerifyAccess();
        if (!result.Removed) {
            if (expectedVersion is { } expected) {
                var actual = result.Current is { Kind: "file" } current ? VersionOf(current) : null;
                throw new WorkspaceConflictException(normalized, expected, actual);
            }
            throw new InvalidOperationException(
                $"No entry named '{normalized}' exists in the workspace.");
        }
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
        cancellationToken.ThrowIfCancellationRequested();
        var result = await RenameAsync(oldNormalized, newNormalized);
        mainThread.VerifyAccess();
        if (result == 1) {
            throw new InvalidOperationException(
                $"No entry named '{oldNormalized}' exists in the workspace.");
        }
        if (result == 2) {
            throw new WorkspaceEntryExistsException(newNormalized);
        }
    }

    private async Task<string?> CheckVersionAsync(string path, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return null;
        }
        var json = await GetJsonAsync(path);
        var record = json is null ? null : JsonSerializer.Deserialize<RawRecord>(json, s_JsonOptions);
        var actual = record is { Kind: "file" } ? VersionOf(record) : null;
        if (actual != expected) {
            throw new WorkspaceConflictException(path, expected, actual);
        }
        return json;
    }

    private static string VersionOf(RawRecord record)
        => record.Version ?? WorkspaceVersioning.ComputeVersion(record.Content ?? string.Empty);

    private static WorkspaceStorageEntry Describe(RawRecord record)
        => record.Kind == "folder"
            ? new(record.Path, WorkspaceEntryKind.Folder, null)
            : new(
                record.Path,
                WorkspaceEntryKind.File,
                VersionOf(record),
                record.ModifiedAt is { } modifiedAt ? DateTimeOffset.Parse(modifiedAt) : null);

    private sealed record RawRecord(string Path, string Kind, string? Content, string? ModifiedAt, string? Version = null);

    private sealed record RemovalResult(bool Removed, RawRecord? Current);

    [JSImport("workspaceGetAllJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> GetAllJsonAsync();

    [JSImport("workspaceGetJson", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> GetJsonAsync(string path);

    [JSImport("workspacePut", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> PutAsync(
        string path, string kind, string content, string modifiedAt, string? expected, bool compare);

    [JSImport("workspaceRemove", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> RemoveAsync(string path, string? expected, bool compare);

    [JSImport("workspaceRename", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<int> RenameAsync(string oldPath, string newPath);
}
#endif
