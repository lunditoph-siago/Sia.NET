using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Sia_Examples.Notebook;

public sealed class FileSystemNotebookStorage(string rootPath) : INotebookStorage
{
    private const string Extension = ".notebook.xml";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public ValueTask<IReadOnlyList<NotebookStorageEntry>> ListAsync(
        CancellationToken cancellationToken = default)
        => RunAsync(() => {
            if (!Directory.Exists(RootPath)) {
                return (IReadOnlyList<NotebookStorageEntry>)[];
            }
            return Directory.EnumerateFiles(RootPath, "*" + Extension)
                .Select(path => {
                    var xml = File.ReadAllText(path);
                    return new NotebookStorageEntry(
                        KeyOf(path),
                        PeekTitle(xml),
                        File.GetLastWriteTimeUtc(path),
                        ComputeVersion(xml));
                })
                .ToList();
        }, cancellationToken);

    public ValueTask<(string Xml, int Version)?> LoadAsync(
        string key, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var path = PathOf(key);
            if (!File.Exists(path)) {
                return ((string Xml, int Version)?)null;
            }
            var xml = File.ReadAllText(path);
            return (xml, ComputeVersion(xml));
        }, cancellationToken);

    public ValueTask<(string Key, int Version)> SaveAsync(
        string? key, int? expectedVersion, string title, string xml,
        CancellationToken cancellationToken = default)
        => RunAsync(() => {
            Directory.CreateDirectory(RootPath);
            key ??= NewKey();
            var path = PathOf(key);

            using var @lock = AcquireLock(key);
            CheckVersion(path, expectedVersion, key);
            AtomicWrite(path, xml);
            return (key, ComputeVersion(xml));
        }, cancellationToken);

    public ValueTask DeleteAsync(
        string key, int expectedVersion, CancellationToken cancellationToken = default)
        => RunAsync(() => {
            var path = PathOf(key);
            using var @lock = AcquireLock(key);
            CheckVersion(path, expectedVersion, key);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }, cancellationToken);

    private void CheckVersion(string path, int? expectedVersion, string key)
    {
        if (expectedVersion is not { } expected) {
            return;
        }
        var actual = File.Exists(path) ? ComputeVersion(File.ReadAllText(path)) : 0;
        if (actual != expected) {
            throw new NotebookConflictException(key, expected, actual);
        }
    }

    private string PathOf(string key) => Path.Combine(RootPath, key + Extension);
    private string LockPathOf(string key) => Path.Combine(RootPath, key + Extension + ".lock");

    private static string KeyOf(string path)
        => Path.GetFileName(path)[..^Extension.Length];

    private static string NewKey() => Guid.NewGuid().ToString("N")[..12];

    private static string PeekTitle(string xml)
    {
        using var reader = new StringReader(xml);
        var document = XDocument.Load(reader, LoadOptions.None);
        return (string?)document.Root?.Attribute("Title") ?? "";
    }

    private static int ComputeVersion(string xml)
    {
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(xml));
        return BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
    }

    private FileStream AcquireLock(string key)
    {
        Directory.CreateDirectory(RootPath);
        var lockPath = LockPathOf(key);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true) {
            try {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline) {
                Thread.Sleep(25);
            }
        }
    }

    private static void AtomicWrite(string path, string content)
    {
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, Utf8NoBom)) {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private static ValueTask<T> RunAsync<T>(Func<T> body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(body());
    }

    private static ValueTask RunAsync(Action body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        body();
        return default;
    }
}
