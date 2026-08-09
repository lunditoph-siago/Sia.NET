#if !BROWSER
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed partial class AssemblyLoader
{
    private readonly IUiThread _mainThread;
    private readonly IReadOnlyDictionary<string, string> _paths;
    private readonly Dictionary<string, MetadataReference> _references =
        new(StringComparer.OrdinalIgnoreCase);

    public AssemblyLoader(IUiThread mainThread)
    {
        _mainThread = mainThread;
        _paths = ResolvePlatformAssemblies();
        KnownAssemblyNames = _paths.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> KnownAssemblyNames { get; }

    public bool TryGetLoaded(string name, out MetadataReference reference)
    {
        _mainThread.VerifyAccess();
        return _references.TryGetValue(name, out reference!);
    }

    public ValueTask<MetadataReference> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_references.TryGetValue(name, out var cached)) {
            return ValueTask.FromResult(cached);
        }
        if (!_paths.TryGetValue(name, out var path)) {
            throw new KeyNotFoundException($"Framework assembly '{name}' is not available.");
        }

        MetadataReference reference = MetadataReference.CreateFromFile(path);
        _references.Add(name, reference);
        return ValueTask.FromResult(reference);
    }

    private static IReadOnlyDictionary<string, string> ResolvePlatformAssemblies()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedPlatformAssemblies)) {
            throw new InvalidOperationException("The trusted platform assembly list is unavailable.");
        }

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator)) {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name)) {
                paths.TryAdd(name, path);
            }
        }
        return paths;
    }
}
#endif
