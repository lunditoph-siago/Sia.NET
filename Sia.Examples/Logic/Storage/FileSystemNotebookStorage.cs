using System.Text;
using System.Xml;

namespace Sia_Examples.Notebook;

public sealed class FileSystemNotebookStorage(string rootPath) : INotebookStorage
{
    private const string Extension = ".notebook.xml";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string RootPath { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    public ValueTask<IReadOnlyList<NotebookStorageEntry>> ListAsync(
        CancellationToken cancellationToken = default)
        => RunAsync(IReadOnlyList<NotebookStorageEntry> () => ResilientFile
            .EnumerateReadableFiles(RootPath, "*" + Extension)
            .Select(TryDescribe)
            .OfType<NotebookStorageEntry>()
            .ToList(), cancellationToken);

    public ValueTask<NotebookContent?> LoadAsync(
        string key, CancellationToken cancellationToken = default)
        => RunAsync(() => ResilientFile.TryReadText(PathOf(key)) is { } xml
            ? new NotebookContent(xml, ComputeVersion(xml))
            : null, cancellationToken);

    public ValueTask<NotebookSaveResult> SaveAsync(
        string? key, string? expectedVersion, string xml,
        CancellationToken cancellationToken = default)
        => RunAsync(() => {
            Directory.CreateDirectory(RootPath);
            key ??= NewKey();
            var path = PathOf(key);
            CheckVersion(path, expectedVersion, key);
            ResilientFile.WriteAtomic(path, xml, Utf8NoBom);
            return new NotebookSaveResult(key, ComputeVersion(xml));
        }, cancellationToken);

    public ValueTask DeleteAsync(
        string key, string expectedVersion, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var path = PathOf(key);
            CheckVersion(path, expectedVersion, key);
            ResilientFile.Delete(path);
        }, cancellationToken);

    private static void CheckVersion(string path, string? expectedVersion, string key)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var actual = ResilientFile.TryReadText(path) is { } xml ? ComputeVersion(xml) : null;
        if (actual != expected) {
            throw new NotebookConflictException(key, expected, actual);
        }
    }

    private string PathOf(string key) => Path.Combine(RootPath, ValidateKey(key) + Extension);

    private static string ValidateKey(string key)
        => string.IsNullOrEmpty(key) || key.IndexOfAny(['/', '\\']) >= 0 || key is "." or ".."
            ? throw new ArgumentException($"'{key}' is not a valid notebook key.", nameof(key))
            : key;

    private static NotebookStorageEntry? TryDescribe((string Path, string Content, DateTime LastWriteTimeUtc) file)
    {
        try {
            return new NotebookStorageEntry(
                KeyOf(file.Path), PeekTitle(file.Content), file.LastWriteTimeUtc, ComputeVersion(file.Content));
        }
        catch (XmlException) {
            return null;
        }
    }

    private static string KeyOf(string path)
        => Path.GetFileName(path)[..^Extension.Length];

    private static string NewKey() => Guid.NewGuid().ToString("N")[..12];

    private static string PeekTitle(string xml) => NotebookVersioning.PeekTitle(xml);

    private static string ComputeVersion(string xml) => NotebookVersioning.ComputeVersion(xml);

    private static ValueTask<T> RunAsync<T>(Func<T> body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));

    private static ValueTask RunAsync(Action body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));
}
