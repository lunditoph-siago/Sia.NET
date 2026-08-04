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
        _initTask ??= LoadAsync(assemblyUrls);
        return _initTask;
    }

    private static async Task LoadAsync(string[] assemblyUrls)
    {
        var references = await Task.WhenAll(assemblyUrls
            .Where(url => !string.Equals(
                Path.GetFileName(new Uri(url).AbsolutePath),
                "Sia.CodeGenerators.dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(FetchReferenceAsync)).ConfigureAwait(false);
        Cache.AddRange(references);
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
