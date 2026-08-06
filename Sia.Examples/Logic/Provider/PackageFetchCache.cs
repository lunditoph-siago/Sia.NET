using System.Collections.Concurrent;

namespace Sia_Examples.Notebook;

public static class PackageFetchCache
{
    private static readonly ConcurrentDictionary<string, Task<IReadOnlyList<FetchedAssembly>>> ByVersionedId =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<FetchedAssembly>> FetchAsync(
        HttpClient client, string id, string? version, CancellationToken cancellationToken = default)
    {
        var resolvedVersion = await Task.Run(
            () => NuGetPackageFetcher.ResolveVersionAsync(client, id, version, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var key = $"{id.Trim().ToLowerInvariant()}@{resolvedVersion.ToLowerInvariant()}";
        return await ByVersionedId.GetOrAdd(key, _ => Task.Run(
            () => NuGetPackageFetcher.FetchAssembliesAsync(client, id, resolvedVersion, cancellationToken),
            cancellationToken)).ConfigureAwait(false);
    }
}
