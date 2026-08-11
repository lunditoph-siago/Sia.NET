using System.IO.Compression;

namespace Sia_Examples.Notebook;

public static class NuGetAssemblyExtractor
{
#if BROWSER
    private static readonly string[] _preferredTfms =
    [
        "net11.0-browser1.0", "net11.0-browser",
        "net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
        "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3",
    ];
#else
    private static readonly string[] _preferredTfms =
    [
        "net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
        "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3",
    ];
#endif

    public static IReadOnlyList<FetchedAssembly> Extract(
        ZipArchive zip, string packageId, string version)
    {
        var libEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (libEntries.Count == 0) {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} has no lib/*.dll assemblies.");
        }

        var folder = PickBestTfm(libEntries);
        var chosen = folder is null
            ? libEntries.Where(e => e.FullName.Count(c => c == '/') == 1)
            : libEntries.Where(e => TfmOf(e.FullName) == folder);

        List<FetchedAssembly> result = [];
        foreach (var entry in chosen) {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            result.Add(new FetchedAssembly(
                Path.GetFileNameWithoutExtension(entry.Name),
                buffer.ToArray()));
        }

        if (result.Count == 0) {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} matched no assemblies after TFM selection.");
        }
        return result;
    }

    private static string TfmOf(string fullName) => fullName["lib/".Length..].Split('/')[0];

    private static string? PickBestTfm(IReadOnlyList<ZipArchiveEntry> entries)
    {
        var tfms = new HashSet<string>(
            entries.Select(e => TfmOf(e.FullName)), StringComparer.OrdinalIgnoreCase);
        tfms.Remove("");
        foreach (var preferredTfm in _preferredTfms) {
            if (tfms.Contains(preferredTfm)) {
                return preferredTfm;
            }
        }
        return tfms.Count > 0 ? tfms.First() : null;
    }
}
