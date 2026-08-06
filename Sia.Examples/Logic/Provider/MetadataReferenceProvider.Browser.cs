#if BROWSER
using System.Collections.Concurrent;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : IMetadataReferenceProvider
{
    private static readonly HttpClient Client = new();

    private static readonly ConcurrentDictionary<string, string> UrlByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, MetadataReference> ByName = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ExcludedAssemblyNames =
    [
        "Sia.CodeGenerators",
        "Microsoft.CodeAnalysis.Workspaces.UnitTests",
    ];

    private static readonly string[] AlwaysCoreAssemblyNames =
    [
        "System.Private.CoreLib", "System.Runtime", "netstandard", "mscorlib",
    ];

    private readonly HashSet<string> _declaredNames = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<IReadOnlyList<string>> AvailableFrameworkNamesLazy =
        new(() => [.. UrlByName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)]);
    public IReadOnlyList<string> AvailableFrameworkAssemblyNames => AvailableFrameworkNamesLazy.Value;

    public async Task EnsurePackagesAsync(IReadOnlyList<PackageRef> packages, CancellationToken cancellationToken = default)
    {
        foreach (var package in packages) {
            if (package.Source == PackageSource.Framework) {
                if (!UrlByName.ContainsKey(package.Id)) {
                    throw new InvalidOperationException(
                        $"<Package Source=\"Framework\" Id=\"{package.Id}\"/> does not match any assembly this build ships.");
                }
                _declaredNames.Add(package.Id);
                continue;
            }

            var assemblies = await PackageFetchCache.FetchAsync(
                Client, package.Id, package.Version, cancellationToken).ConfigureAwait(false);

            foreach (var assembly in assemblies) {
                ByName[assembly.Name] = MetadataReference.CreateFromImage(assembly.Image, filePath: assembly.Name);
                DynamicAssemblyRegistry.Register(assembly.Name, assembly.Image);
                _declaredNames.Add(assembly.Name);
            }
        }
    }

    internal static void Initialize(string[] assemblyUrls)
    {
        if (!UrlByName.IsEmpty) return;
        foreach (var url in assemblyUrls) {
            var name = NameOf(url);
            if (IsExcluded(name)) continue;
            UrlByName.TryAdd(name, url);
        }
        Console.WriteLine($"[diag] indexed {UrlByName.Count} candidate assemblies (nothing fetched yet)");
    }

    public async ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(string source)
    {
        var namespaces = AssemblyReferenceResolver.ResolveNamespaces(source);
        var wanted = new HashSet<string>(
            AssemblyReferenceResolver.ResolveAssemblyNames(namespaces, [.. UrlByName.Keys]),
            StringComparer.OrdinalIgnoreCase);
        foreach (var always in AlwaysCoreAssemblyNames) {
            if (UrlByName.ContainsKey(always)) wanted.Add(always);
        }
        wanted.UnionWith(_declaredNames);

        await EnsureLoadedAsync(wanted).ConfigureAwait(false);
        return Snapshot(wanted);
    }

    public async ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync()
    {
        var wanted = new HashSet<string>(UrlByName.Keys, StringComparer.OrdinalIgnoreCase);
        wanted.UnionWith(_declaredNames);
        await EnsureLoadedAsync(wanted).ConfigureAwait(false);
        return Snapshot(wanted);
    }

    private static readonly ConcurrentDictionary<string, IReadOnlyList<MetadataReference>> SnapshotCache = new(StringComparer.Ordinal);

    private static IReadOnlyList<MetadataReference> Snapshot(IEnumerable<string> names)
    {
        var key = string.Join('\n', names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        return SnapshotCache.GetOrAdd(key, _ => BuildSnapshot(names));
    }

    private static IReadOnlyList<MetadataReference> BuildSnapshot(IEnumerable<string> names)
    {
        List<MetadataReference> result = [];
        foreach (var name in names) {
            if (ByName.TryGetValue(name, out var reference)) result.Add(reference);
        }
        return result;
    }

    private static async Task EnsureLoadedAsync(IEnumerable<string> names)
    {
        var missing = names
            .Where(n => UrlByName.ContainsKey(n) && !ByName.ContainsKey(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count == 0) return;

        Console.WriteLine($"[diag] fetching {missing.Count} assembl{(missing.Count == 1 ? "y" : "ies")}: "
            + string.Join(", ", missing));

        const int batchSize = 16;
        var pending = new ConcurrentDictionary<string, string>();
        var failures = new List<(string Name, Exception Error)>();

        for (var offset = 0; offset < missing.Count; offset += batchSize) {
            var batch = missing.Skip(offset).Take(batchSize).ToList();
            foreach (var name in batch) pending[name] = "queued";

            var batchTask = Task.WhenAll(batch.Select(async name => {
                try {
                    var reference = await Task.Run(
                        () => FetchReferenceAsync(name, UrlByName[name], pending)).ConfigureAwait(false);
                    ByName[name] = reference;
                    pending.TryRemove(name, out _);
                    return (Name: name, Error: (Exception?)null);
                }
                catch (Exception ex) {
                    pending.TryRemove(name, out _);
                    Console.WriteLine($"[diag] fetch FAILED: {name} -> {ex.Message}");
                    return (Name: name, Error: (Exception?)ex);
                }
            }));

            var watchdog = Task.Delay(TimeSpan.FromSeconds(10));
            var winner = await Task.WhenAny(batchTask, watchdog);
            while (winner == watchdog && !batchTask.IsCompleted) {
                Console.WriteLine($"[diag] WATCHDOG: {pending.Count} still pending:");
                foreach (var (name, stage) in pending) {
                    Console.WriteLine($"[diag]   stuck at '{stage}': {name}");
                }
                watchdog = Task.Delay(TimeSpan.FromSeconds(10));
                winner = await Task.WhenAny(batchTask, watchdog);
            }

            failures.AddRange((await batchTask).Where(a => a.Error is not null).Select(a => (a.Name, a.Error!)));
        }

        if (failures.Count > 0) {
            throw new AggregateException(
                $"Failed to load {failures.Count} of {missing.Count} reference assemblies.",
                failures.Select(f => f.Error));
        }
    }

    private static string NameOf(string url)
    {
        var fileName = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
        var lastDot = fileName.LastIndexOf('.');
        return lastDot >= 0 ? fileName[..lastDot] : fileName;
    }

    private static bool IsExcluded(string assemblyName)
        => ExcludedAssemblyNames.Contains(assemblyName, StringComparer.OrdinalIgnoreCase);

    private static async Task<MetadataReference> FetchReferenceAsync(
        string name, string url, ConcurrentDictionary<string, string> pending)
    {
        const int maxAttempts = 3;
        var stallTimeout = TimeSpan.FromSeconds(10);

        for (var attempt = 1; ; attempt++) {
            using var cts = new CancellationTokenSource(stallTimeout);
            HttpResponseMessage? response = null;
            try {
                pending[name] = "GetAsync";
                response = await Client.GetAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                pending[name] = "ReadAsStreamAsync";
                var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                pending[name] = "CreateFromStream";
                return MetadataReference.CreateFromStream(stream, filePath: url);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && attempt < maxAttempts) {
                Console.WriteLine($"[diag] fetch stalled at '{pending.GetValueOrDefault(name)}' "
                    + $"(attempt {attempt}/{maxAttempts}), retrying: {name}");
            }
            finally {
                response?.Dispose();
            }
        }
    }
}

internal static partial class BrowserNotebookInterop
{
    [JSExport]
    public static Task InitNotebookAsync(string[] assemblyUrls)
    {
        Console.WriteLine($"[diag] InitNotebookAsync (JSExport) entered, thread={Environment.CurrentManagedThreadId}");
        MetadataReferenceProvider.Initialize(assemblyUrls);
        return Task.CompletedTask;
    }
}
#endif
