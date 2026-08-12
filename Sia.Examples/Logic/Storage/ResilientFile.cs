using System.Text;

namespace Sia_Examples.Notebook;

internal static class ResilientFile
{
    private const int MaxRetries = 9;

    public static string? TryReadText(string path)
    {
        try {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception error) when (IsTransient(error)) {
            return null;
        }
    }

    public static void WriteAtomic(string path, string content, Encoding encoding)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding)) {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            Retry(() => File.Move(tempPath, path, overwrite: true));
        }
        catch {
            try {
                File.Delete(tempPath);
            }
            catch {
                // the exception already in flight is the one that matters
            }
            throw;
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path)) {
            Retry(() => File.Delete(path));
        }
    }

    public static IEnumerable<(string Path, string Content, DateTime LastWriteTimeUtc)> EnumerateReadableFiles(
        string directory, string searchPattern)
    {
        if (!Directory.Exists(directory)) {
            yield break;
        }
        foreach (var path in Directory.EnumerateFiles(directory, searchPattern)) {
            if (TryReadText(path) is not { } content) {
                continue;
            }
            DateTime lastWriteTimeUtc;
            try {
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception error) when (IsTransient(error)) {
                continue;
            }
            yield return (path, content, lastWriteTimeUtc);
        }
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++) {
            try {
                action();
                return;
            }
            catch (Exception error) when (attempt < MaxRetries && IsTransient(error)) {
                Thread.Sleep(5 * (attempt + 1));
            }
        }
    }

    private static bool IsTransient(Exception error)
        => error is IOException or UnauthorizedAccessException;
}
