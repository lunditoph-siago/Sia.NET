using System.IO.Compression;
using System.Text.Json;

namespace Sia_Examples.Notebook;

public sealed record FetchedAssembly(string Name, byte[] Image);

public static class NuGetPackageFetcher
{
    private const string FlatContainerBase = "https://api.nuget.org/v3-flatcontainer";

    private static readonly string[] PreferredTargetFrameworks =
    [
        "net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
        "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.3",
    ];

    public static async Task<string> ResolveVersionAsync(
        HttpClient client, string packageId, string? version, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(version)) {
            return version;
        }

        var id = packageId.ToLowerInvariant();
        using var response = await client.GetAsync(
            $"{FlatContainerBase}/{id}/index.json", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var versions = document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => v.GetString()!)
            .ToList();
        if (versions.Count == 0) {
            throw new InvalidOperationException($"NuGet package '{packageId}' has no published versions.");
        }

        var stable = versions.Where(v => !v.Contains('-', StringComparison.Ordinal)).ToList();
        return stable.Count > 0 ? stable[^1] : versions[^1];
    }

    public static async Task<IReadOnlyList<FetchedAssembly>> FetchAssembliesAsync(
        HttpClient client, string packageId, string version, CancellationToken cancellationToken = default)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();
        using var response = await client.GetAsync(
            $"{FlatContainerBase}/{id}/{ver}/{id}.{ver}.nupkg", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var packageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        using var zip = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read);

        var libEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (libEntries.Count == 0) {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} has no lib/*.dll assemblies (native-only or content-only package?).");
        }

        var chosenFolder = PickBestFolder(libEntries);
        var chosen = chosenFolder is null
            ? libEntries.Where(e => e.FullName.Count(c => c == '/') == 1)

            : libEntries.Where(e => FolderOf(e.FullName) == chosenFolder);

        List<FetchedAssembly> result = [];
        foreach (var entry in chosen) {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var name = Path.GetFileNameWithoutExtension(entry.Name);
            result.Add(new FetchedAssembly(name, buffer.ToArray()));
        }

        if (result.Count == 0) {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' {version} matched no assemblies after target-framework selection.");
        }
        return result;
    }

    private static string FolderOf(string entryFullName)
    {
        var afterLib = entryFullName["lib/".Length..];
        var slash = afterLib.IndexOf('/');
        return slash < 0 ? "" : afterLib[..slash];
    }

    private static string? PickBestFolder(IReadOnlyList<ZipArchiveEntry> libEntries)
    {
        var folders = new HashSet<string>(libEntries.Select(e => FolderOf(e.FullName)), StringComparer.OrdinalIgnoreCase);
        folders.Remove("");

        foreach (var preferred in PreferredTargetFrameworks) {
            if (folders.Contains(preferred)) return preferred;
        }

        return folders.Count > 0 ? folders.First() : null;
    }
}
