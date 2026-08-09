#if !BROWSER
using Microsoft.CodeAnalysis;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class StaticCompilationReferenceResolver : ICompilationReferenceResolver
{
    private readonly IReadOnlyList<MetadataReference> _references;

    public StaticCompilationReferenceResolver()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies) {
            paths.UnionWith(platformAssemblies.Split(Path.PathSeparator));
        }

        paths.Add(typeof(Sia.World).Assembly.Location);
        paths.Add(typeof(Sia.Reactive.Reactive).Assembly.Location);
        _references = paths
            .Where(static path => path.Length > 0 && File.Exists(path))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_references);
    }

    public ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_references);
    }
}
#endif
