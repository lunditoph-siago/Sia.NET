using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace Sia_Examples.Notebook;

public sealed class PackageReferenceLoader : IPackageReferenceLoader
{
    private const string Base = "https://api.nuget.org/v3-flatcontainer";

    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<FetchedAssembly>>> _cache = new();

    public PackageReferenceLoader(HttpClient client) => _client = client;

    public Task<IReadOnlyList<FetchedAssembly>> LoadReferencesAsync(
        string packageId, string? version, CancellationToken ct = default)
    {
        var key = $"{packageId.ToLowerInvariant()}@{version ?? "latest"}";
        return _cache.GetOrAdd(key, _ => FetchAsync(packageId, version, ct));
    }

    private async Task<IReadOnlyList<FetchedAssembly>> FetchAsync(
        string id, string? version, CancellationToken ct)
    {
        var ver = version ?? await ResolveLatestStableAsync(id, ct);
        var url = NupkgUrl(id, ver);
        var bytes = await _client.GetByteArrayAsync(url, ct);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return NuGetAssemblyExtractor.Extract(zip, id, ver);
    }

    private async Task<string> ResolveLatestStableAsync(string id, CancellationToken ct)
    {
        var json = await _client.GetStringAsync(
                $"{Base}/{id.ToLowerInvariant()}/index.json", ct);
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions")
            .EnumerateArray().Select(v => v.GetString()!).ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException($"NuGet package '{id}' has no published versions.");
        var stable = versions.Where(v => !v.Contains('-', StringComparison.Ordinal)).ToList();
        return stable.Count > 0 ? stable[^1] : versions[^1];
    }

    private static string NupkgUrl(string id, string ver)
    {
        var i = id.ToLowerInvariant(); var v = ver.ToLowerInvariant();
        return $"{Base}/{i}/{v}/{i}.{v}.nupkg";
    }
}
