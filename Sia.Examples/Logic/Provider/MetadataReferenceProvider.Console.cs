#if !BROWSER
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : IMetadataReferenceProvider
{
    private static readonly HttpClient Client = new();

    private static readonly Lazy<IReadOnlyList<string>> FrameworkPaths = new(ResolveFrameworkPaths);
    private static readonly Lazy<IReadOnlyList<MetadataReference>> Framework = new(ResolveFramework);
    private static readonly Lazy<IReadOnlyList<string>> FrameworkNames = new(() =>
        [.. FrameworkPaths.Value
            .Select(path => Path.GetFileNameWithoutExtension(path)!)

            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]);

    public IReadOnlyList<string> AvailableFrameworkAssemblyNames => FrameworkNames.Value;

    private readonly List<MetadataReference> _packageReferences = [];

    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(string source)
        => ValueTask.FromResult(All());

    public ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync()
        => ValueTask.FromResult(All());

    private IReadOnlyList<MetadataReference> All()
        => _packageReferences.Count == 0 ? Framework.Value : [.. Framework.Value, .. _packageReferences];

    public async Task EnsurePackagesAsync(IReadOnlyList<PackageRef> packages, CancellationToken cancellationToken = default)
    {
        foreach (var package in packages) {
            if (package.Source == PackageSource.Framework) {
                continue;
            }

            var assemblies = await PackageFetchCache.FetchAsync(
                Client, package.Id, package.Version, cancellationToken).ConfigureAwait(false);

            foreach (var assembly in assemblies) {
                _packageReferences.Add(MetadataReference.CreateFromImage(assembly.Image, filePath: assembly.Name));
                DynamicAssemblyRegistry.Register(assembly.Name, assembly.Image);
            }
        }
    }

    private static IReadOnlyList<string> ResolveFrameworkPaths()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa)) {
            return [];
        }
        List<string> paths = [];
        foreach (var path in tpa.Split(Path.PathSeparator)) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName)
                || string.Equals(fileName, "Sia.CodeGenerators", StringComparison.Ordinal)) {
                continue;
            }
            paths.Add(path);
        }
        return paths;
    }

    private static IReadOnlyList<MetadataReference> ResolveFramework()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa)) {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unavailable; runtime compilation requires a framework-dependent (non-AOT, non-single-file) build.");
        }

        List<MetadataReference> references = [];
        foreach (var path in FrameworkPaths.Value) {
            try {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (IOException) {
            }
        }
        return references;
    }
}
#endif
