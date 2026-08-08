#if BROWSER
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed partial class AssemblyLoader : IAssemblyLoader
{
    private static string[]? _initNames;
    private static string[]? _initUrls;

    [JSExport]
    public static Task InitNotebookAsync(string[] assemblyVirtualPaths, string[] assemblyUrls)
    {
        if (assemblyVirtualPaths.Length != assemblyUrls.Length)
            throw new ArgumentException("assemblyVirtualPaths and assemblyUrls must have the same length.");
        _initNames = assemblyVirtualPaths;
        _initUrls = assemblyUrls;
        return Task.CompletedTask;
    }

    public static (string[] VirtualPaths, string[] Urls) AssemblyManifest
        => _initNames is { } names && _initUrls is { } urls
            ? (names, urls)
            : throw new InvalidOperationException("InitNotebookAsync has not been called.");

    private readonly ConcurrentDictionary<string, string> _urls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MetadataReference> _cache = new();

    public IReadOnlySet<string> KnownAssemblyNames { get; }

    public AssemblyLoader(string[] assemblyVirtualPaths, string[] assemblyUrls)
    {
        if (assemblyVirtualPaths.Length != assemblyUrls.Length)
            throw new ArgumentException("assemblyVirtualPaths and assemblyUrls must have the same length.");
        var set = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < assemblyVirtualPaths.Length; i++)
        {
            var name = Path.GetFileNameWithoutExtension(assemblyVirtualPaths[i]);
            if (string.IsNullOrEmpty(name)) continue;
            set.Add(name);
            _urls.TryAdd(name, assemblyUrls[i]);
        }
        KnownAssemblyNames = set.ToImmutable();
    }

    public async Task PreloadAsync()
    {
        var json = JsonSerializer.Serialize(
            _urls.Select(kv => new { Name = kv.Key, Url = kv.Value }));

        var resultJson = await Bindings.FetchBatchAsync(json);
        var entries = JsonSerializer.Deserialize<Entry[]>(resultJson)!;
        foreach (var e in entries)
        {
            var bytes = Convert.FromBase64String(e.B64);
            _cache.TryAdd(e.Name, MetadataReference.CreateFromImage(bytes, filePath: e.Name));
        }
    }

    public Task<MetadataReference> LoadAsync(string name, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(name, out var cached))
            return Task.FromResult(cached);
        throw new KeyNotFoundException($"Assembly '{name}' is not preloaded.");
    }

    private sealed record Entry(string Name, string B64);

    private static partial class Bindings
    {
        [JSImport("fetchScheduler.batchBase64", "main.js")]
        public static partial Task<string> FetchBatchAsync(string jsonUrls);
    }
}
#endif
