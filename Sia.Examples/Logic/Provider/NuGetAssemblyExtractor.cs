using System.IO.Compression;

namespace Sia_Examples.Notebook;

public static class NuGetAssemblyExtractor
{
    private static readonly string[] PreferredTfms =
    [
        "net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
        "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3",
    ];

    public static IReadOnlyList<FetchedAssembly> Extract(
        ZipArchive zip, string packageId, string version)
    {
        var libEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (libEntries.Count == 0)
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} has no lib/*.dll assemblies.");

        var folder = PickBestTfm(libEntries);
        var chosen = folder is null
            ? libEntries.Where(e => e.FullName.Count(c => c == '/') == 1)
            : libEntries.Where(e => TfmOf(e.FullName) == folder);

        List<FetchedAssembly> result = [];
        foreach (var entry in chosen) {
            using var s = entry.Open();
            using var b = new MemoryStream();
            s.CopyTo(b);
            result.Add(new FetchedAssembly(
                Path.GetFileNameWithoutExtension(entry.Name), b.ToArray()));
        }

        if (result.Count == 0)
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} matched no assemblies after TFM selection.");
        return result;
    }

    private static string TfmOf(string fullName) => fullName["lib/".Length..].Split('/')[0];

    private static string? PickBestTfm(IReadOnlyList<ZipArchiveEntry> entries)
    {
        var tfms = new HashSet<string>(
            entries.Select(e => TfmOf(e.FullName)), StringComparer.OrdinalIgnoreCase);
        tfms.Remove("");
        foreach (var p in PreferredTfms)
            if (tfms.Contains(p)) return p;
        return tfms.Count > 0 ? tfms.First() : null;
    }
}
