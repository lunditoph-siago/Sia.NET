using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Sia_Examples.Notebook;

/// <summary>Maps notebook keys/versions/conflicts onto <see cref="ResilientFile"/>'s generic,
/// already-resilient primitives — this type owns exactly the notebook-domain concerns (what a
/// valid key looks like, how a version is computed, when a write is a conflict) and nothing about
/// how to survive a racing filesystem, which is <see cref="ResilientFile"/>'s job alone.</summary>
public sealed class FileSystemNotebookStorage(string rootPath) : INotebookStorage
{
    private const string Extension = ".notebook.xml";

    /// <summary>Hex characters kept from the SHA-256 digest — 12 is 48 bits, far more than enough
    /// to make an accidental collision within one person's notebook folder a non-concern, while
    /// staying short enough to show directly in the status bar.</summary>
    private const int VersionLength = 12;

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

    /// <summary>No lock guards the check-then-write below — within one app instance there's nothing
    /// to guard against, since only one <c>NotebookWorkspace</c> is ever open at a time behind
    /// <c>DomApplication</c>'s single sequential event loop, so two saves to the same key can't race
    /// each other here. The narrow gap this leaves is across tabs/processes sharing the same folder:
    /// two writers could both read the same <paramref name="expectedVersion"/>, both pass this
    /// check, and then both write, with whichever write lands second silently winning. Closing that
    /// would need real cross-process coordination — for a race that, in this single-user
    /// local/example setting, is an accepted, low-probability gap rather than one worth the extra
    /// machinery.</summary>
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

    /// <summary>One malformed file (corrupted content, truncated write) must not take down the
    /// whole listing — every other, perfectly good notebook would disappear from the sidebar along
    /// with it. <see cref="ResilientFile.EnumerateReadableFiles"/> already handles the filesystem
    /// side of that (a vanished/unreadable file); this handles the content side (unparseable XML).</summary>
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

    private static string PeekTitle(string xml)
    {
        using var reader = new StringReader(xml);
        var document = XDocument.Load(reader, LoadOptions.None);
        return (string?)document.Root?.Attribute("Title") ?? "";
    }

    private static string ComputeVersion(string xml)
    {
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(xml));
        return Convert.ToHexStringLower(hash)[..VersionLength];
    }

    /// <summary>Runs <paramref name="body"/> on the thread pool rather than synchronously on the
    /// caller — this type's API is <see cref="ValueTask"/>-shaped precisely so callers (the browser
    /// DOM event loop included) aren't blocked while it does file I/O.</summary>
    private static ValueTask<T> RunAsync<T>(Func<T> body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));

    /// <inheritdoc cref="RunAsync{T}"/>
    private static ValueTask RunAsync(Action body, CancellationToken cancellationToken)
        => new(Task.Run(body, cancellationToken));
}
