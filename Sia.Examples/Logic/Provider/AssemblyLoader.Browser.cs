#if BROWSER
using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.CodeAnalysis;
using Sia_Examples.Browser;

namespace Sia_Examples.Notebook;

public sealed partial class AssemblyLoader
{
    private static ImmutableArray<AssemblyAsset> _manifest;

    private readonly BrowserMainThread _mainThread;
    private readonly BrowserResourceLoader _resources;
    private readonly Dictionary<string, string> _urls;
    private readonly Dictionary<string, MetadataReference> _references =
        new(StringComparer.OrdinalIgnoreCase);

    [JSExport]
    public static Task InitializeAsync(
        string[] assemblyVirtualPaths,
        string[] assemblyUrls)
    {
        ArgumentNullException.ThrowIfNull(assemblyVirtualPaths);
        ArgumentNullException.ThrowIfNull(assemblyUrls);
        if (assemblyVirtualPaths.Length != assemblyUrls.Length) {
            throw new ArgumentException(
                "Assembly virtual paths and URLs must contain the same number of entries.");
        }

        var assets = ImmutableArray.CreateBuilder<AssemblyAsset>(assemblyVirtualPaths.Length);
        for (var index = 0; index < assemblyVirtualPaths.Length; index++) {
            var name = Path.GetFileNameWithoutExtension(assemblyVirtualPaths[index]);
            if (!string.IsNullOrWhiteSpace(name)) {
                assets.Add(new(name, assemblyUrls[index]));
            }
        }
        _manifest = assets.MoveToImmutable();
        return Task.CompletedTask;
    }

    public IReadOnlySet<string> KnownAssemblyNames { get; }

    public AssemblyLoader(BrowserMainThread mainThread, BrowserResourceLoader resources)
    {
        if (_manifest.IsDefault) {
            throw new InvalidOperationException(
                "The browser assembly manifest has not been initialized.");
        }

        _mainThread = mainThread;
        _resources = resources;
        _urls = _manifest.ToDictionary(
            static asset => asset.Name,
            static asset => asset.Url,
            StringComparer.OrdinalIgnoreCase);
        KnownAssemblyNames = _urls.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetLoaded(string name, out MetadataReference reference)
    {
        _mainThread.VerifyAccess();
        return _references.TryGetValue(name, out reference!);
    }

    public async ValueTask<MetadataReference> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        _mainThread.VerifyAccess();
        if (_references.TryGetValue(name, out var cached)) {
            return cached;
        }
        if (!_urls.TryGetValue(name, out var url)) {
            throw new KeyNotFoundException($"Framework assembly '{name}' is not available.");
        }

        var image = await _resources.FetchBytesAsync(url, cancellationToken);
        _mainThread.VerifyAccess();

        var reference = MetadataReference.CreateFromImage(image, filePath: name);
        _references.Add(name, reference);
        return reference;
    }

    private readonly record struct AssemblyAsset(string Name, string Url);
}
#endif
