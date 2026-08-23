namespace Sia_Examples.Notebook;

using System.Text;

public sealed class FileSystemWorkspaceStorage(string rootPath) : IWorkspaceStorage
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string RootPath { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    public ValueTask<IReadOnlyList<WorkspaceStorageEntry>> ListTreeAsync(
        CancellationToken cancellationToken = default)
        => RunAsync(IReadOnlyList<WorkspaceStorageEntry> () => {
            List<WorkspaceStorageEntry> entries = [];
            if (Directory.Exists(RootPath)) {
                AppendEntries(RootPath, string.Empty, entries);
            }
            return entries;
        }, cancellationToken);

    public ValueTask<WorkspaceFileContent?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default)
        => RunAsync(() => ResilientFile.TryReadText(AbsoluteOf(path)) is { } content
            ? new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content))
            : null, cancellationToken);

    public ValueTask<WorkspaceFileContent> WriteFileAsync(
        string path, string? expectedVersion, string content,
        CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var normalized = WorkspacePath.Normalize(path);
            var absolute = AbsoluteOf(normalized);
            CheckVersion(absolute, normalized, expectedVersion);
            CreateParentDirectory(absolute);
            ResilientFile.WriteAtomic(absolute, content, Utf8NoBom);
            return new WorkspaceFileContent(content, WorkspaceVersioning.ComputeVersion(content));
        }, cancellationToken);

    public ValueTask CreateFileAsync(
        string path, string content, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var normalized = WorkspacePath.Normalize(path);
            var absolute = AbsoluteOf(normalized);
            EnsureAbsent(absolute, normalized);
            CreateParentDirectory(absolute);
            ResilientFile.WriteAtomic(absolute, content, Utf8NoBom);
        }, cancellationToken);

    public ValueTask CreateFolderAsync(
        string path, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var normalized = WorkspacePath.Normalize(path);
            var absolute = AbsoluteOf(normalized);
            EnsureAbsent(absolute, normalized);
            Directory.CreateDirectory(absolute);
        }, cancellationToken);

    public ValueTask DeleteEntryAsync(
        string path, string? expectedVersion = null, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var normalized = WorkspacePath.Normalize(path);
            var absolute = AbsoluteOf(normalized);
            if (File.Exists(absolute)) {
                CheckVersion(absolute, normalized, expectedVersion);
                ResilientFile.Delete(absolute);
                return;
            }
            if (!Directory.Exists(absolute)) {
                throw new InvalidOperationException(
                    $"No entry named '{normalized}' exists in the workspace.");
            }
            Directory.Delete(absolute, recursive: true);
        }, cancellationToken);

    public ValueTask RenameEntryAsync(
        string oldPath, string newPath, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var oldNormalized = WorkspacePath.Normalize(oldPath);
            var newNormalized = WorkspacePath.Normalize(newPath);
            if (WorkspacePath.EqualsPath(oldNormalized, newNormalized)) {
                return;
            }
            var oldAbsolute = AbsoluteOf(oldNormalized);
            var newAbsolute = AbsoluteOf(newNormalized);
            if (!File.Exists(oldAbsolute) && !Directory.Exists(oldAbsolute)) {
                throw new InvalidOperationException(
                    $"No entry named '{oldNormalized}' exists in the workspace.");
            }
            EnsureAbsent(newAbsolute, newNormalized);
            CreateParentDirectory(newAbsolute);
            if (File.Exists(oldAbsolute)) {
                File.Move(oldAbsolute, newAbsolute);
                return;
            }
            Directory.Move(oldAbsolute, newAbsolute);
        }, cancellationToken);

    private static void AppendEntries(
        string absoluteDirectory, string relativeDirectory, List<WorkspaceStorageEntry> entries)
    {
        foreach (var directory in Directory.EnumerateDirectories(absoluteDirectory)) {
            var relative = WorkspacePath.Combine(relativeDirectory, Path.GetFileName(directory));
            entries.Add(new(relative, WorkspaceEntryKind.Folder, null));
            AppendEntries(directory, relative, entries);
        }
        foreach (var file in Directory.EnumerateFiles(absoluteDirectory)) {
            var content = ResilientFile.TryReadText(file);
            if (content is null) {
                continue;
            }
            DateTimeOffset? modifiedAt;
            try {
                modifiedAt = File.GetLastWriteTimeUtc(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                modifiedAt = null;
            }
            entries.Add(new(
                WorkspacePath.Combine(relativeDirectory, Path.GetFileName(file)),
                WorkspaceEntryKind.File,
                WorkspaceVersioning.ComputeVersion(content),
                modifiedAt));
        }
    }

    private string AbsoluteOf(string normalized)
        => Path.Combine([RootPath, .. normalized.Split('/')]);

    private static void CreateParentDirectory(string absolute)
    {
        var parent = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(parent)) {
            Directory.CreateDirectory(parent);
        }
    }

    private static void CheckVersion(string absolute, string path, string? expectedVersion)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var actual = ResilientFile.TryReadText(absolute) is { } content
            ? WorkspaceVersioning.ComputeVersion(content)
            : null;
        if (actual != expected) {
            throw new WorkspaceConflictException(path, expected, actual);
        }
    }

    private static void EnsureAbsent(string absolute, string path)
    {
        if (File.Exists(absolute) || Directory.Exists(absolute)) {
            throw new WorkspaceEntryExistsException(path);
        }
    }

    private static ValueTask<T> RunAsync<T>(Func<T> body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));

    private static ValueTask RunAsync(Action body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));
}
