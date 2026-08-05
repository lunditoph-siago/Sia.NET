#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : IMetadataReferenceProvider
{
    private static readonly HttpClient Client = new();
    private static readonly List<MetadataReference> Cache = [];
    private static Task? _initTask;

    internal static Task InitializeAsync(string[] assemblyUrls)
    {
        // A failed load used to be cached forever in `_initTask` — every later call
        // (e.g. a retry button, or the page reloading a stale service-worker copy)
        // would just hand back the same faulted task with no way to try again.
        if (_initTask is { IsFaulted: true }) {
            _initTask = null;
        }
        _initTask ??= LoadAsync(assemblyUrls);
        return _initTask;
    }

    private static async Task LoadAsync(string[] assemblyUrls)
    {
        var candidates = assemblyUrls
            .Where(url => !string.Equals(
                Path.GetFileName(new Uri(url).AbsolutePath),
                "Sia.CodeGenerators.dll",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Fetch every candidate independently rather than via a single Task.WhenAll:
        // previously, one failed request (a flaky network, a 404) made *all* fetches
        // count as failed and left `Cache` completely empty, even for assemblies that
        // downloaded fine — breaking every notebook's compilation, not just ones that
        // actually needed the missing reference.
        var attempts = await Task.WhenAll(candidates.Select(async url => {
            try {
                return (Url: url, Reference: await FetchReferenceAsync(url).ConfigureAwait(false), Error: (Exception?)null);
            }
            catch (Exception ex) {
                return (Url: url, Reference: (MetadataReference?)null, Error: ex);
            }
        })).ConfigureAwait(false);

        Cache.AddRange(attempts.Where(a => a.Reference is not null).Select(a => a.Reference!));

        var failures = attempts.Where(a => a.Error is not null).ToList();
        if (failures.Count > 0) {
            throw new AggregateException(
                $"Failed to load {failures.Count} of {candidates.Count} reference assemblies.",
                failures.Select(f => f.Error!));
        }
    }

    private static async Task<MetadataReference> FetchReferenceAsync(string url)
    {
        var response = await Client.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return MetadataReference.CreateFromStream(stream, filePath: url);
    }

    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync()
        => ValueTask.FromResult<IReadOnlyList<MetadataReference>>(Cache);
}

internal static partial class BrowserNotebookInterop
{
    [JSExport]
    public static Task InitNotebookAsync(string[] assemblyUrls)
        => MetadataReferenceProvider.InitializeAsync(assemblyUrls);
}
#endif
