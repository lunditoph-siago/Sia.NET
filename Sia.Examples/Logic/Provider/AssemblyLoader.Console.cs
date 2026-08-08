#if !BROWSER
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class AssemblyLoader : IAssemblyLoader
{
    private readonly ConcurrentDictionary<string, MetadataReference> _cache = new();

    public IReadOnlySet<string> KnownAssemblyNames { get; }

    public AssemblyLoader()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is unavailable.");

        var set = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in tpa.Split(Path.PathSeparator)) {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)
                || name.Equals("Sia.CodeGenerators", StringComparison.OrdinalIgnoreCase))
                continue;
            set.Add(name);
            _cache.TryAdd(name, MetadataReference.CreateFromFile(path));
        }
        KnownAssemblyNames = set.ToImmutable();
    }

    public Task<MetadataReference> LoadAsync(string name, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(name, out var r)) return Task.FromResult(r);
        throw new KeyNotFoundException($"Assembly '{name}' is not known to this loader.");
    }
}
#endif
