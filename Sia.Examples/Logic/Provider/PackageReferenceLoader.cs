using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace Sia_Examples.Notebook;

public sealed class PackageReferenceLoader
{
    private const string _baseUrl = "https://api.nuget.org/v3-flatcontainer";

    private readonly IResourceLoader _resources;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<FetchedAssembly>>>> _cache = new();

    public PackageReferenceLoader(IResourceLoader resources)
    {
        _resources = resources;
    }

    public ValueTask<IReadOnlyList<FetchedAssembly>> LoadReferencesAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var key = $"{packageId.ToLowerInvariant()}@{version ?? "latest"}";
        var entry = _cache.GetOrAdd(key, _ => new(() => FetchAndCacheAsync(key, packageId, version)));
        return new(entry.Value.WaitAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<FetchedAssembly>> FetchAndCacheAsync(
        string key,
        string packageId,
        string? version)
    {
        try {
            return await FetchAsync(packageId, version, CancellationToken.None);
        }
        catch {
            _cache.TryRemove(key, out _);
            throw;
        }
    }

    private async ValueTask<IReadOnlyList<FetchedAssembly>> FetchAsync(
        string id,
        string? version,
        CancellationToken cancellationToken)
    {
        var resolvedVersion = version
            ?? await ResolveLatestStableAsync(id, cancellationToken);
        var bytes = await _resources.FetchBytesAsync(
            GetPackageUrl(id, resolvedVersion),
            cancellationToken);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return NuGetAssemblyExtractor.Extract(zip, id, resolvedVersion);
    }

    private async ValueTask<string> ResolveLatestStableAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var json = await _resources.FetchTextAsync(
            $"{_baseUrl}/{id.ToLowerInvariant()}/index.json",
            cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(static version => version.GetString()!)
            .ToList();
        if (versions.Count == 0) {
            throw new InvalidOperationException($"NuGet package '{id}' has no published versions.");
        }
        var stable = versions.Where(v => !v.Contains('-', StringComparison.Ordinal)).ToList();
        return stable.Count > 0 ? stable[^1] : versions[^1];
    }

    private static string GetPackageUrl(string id, string version)
    {
        var normalizedId = id.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        return $"{_baseUrl}/{normalizedId}/{normalizedVersion}/{normalizedId}.{normalizedVersion}.nupkg";
    }
}
